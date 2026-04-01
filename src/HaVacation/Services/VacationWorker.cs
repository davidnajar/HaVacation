using HaVacation.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace HaVacation.Services;

/// <summary>
/// The heart of HaVacation.
///
/// How it works
/// ─────────────
/// 1. On startup (and every night at midnight) the worker looks up the history
///    of all configured entities for the same calendar day N days ago.
/// 2. Each recorded state-change is re-scheduled for today at the same time-of-day,
///    offset by a small random jitter so the pattern is never identical.
/// 3. Every second the worker checks the queue and fires any events whose
///    scheduled time has arrived.
///
/// Enable / disable via the "Vacation:Enabled" config flag without restarting:
///   • appsettings.json
///   • environment variable VACATION__ENABLED=true
///   • docker-compose environment section
/// </summary>
public sealed class VacationWorker : BackgroundService
{
    // The pending replay queue is sorted by FireAt; a ConcurrentQueue gives us
    // lock-free access from the single worker loop.
    private readonly ConcurrentQueue<ScheduledReplay> _queue = new();

    private readonly HomeAssistantClient _ha;
    private readonly IOptionsMonitor<VacationConfig> _vacationCfg;
    private readonly ILogger<VacationWorker> _log;
    private readonly Random _rng = Random.Shared;

    public VacationWorker(
        HomeAssistantClient ha,
        IOptionsMonitor<VacationConfig> vacationCfg,
        ILogger<VacationWorker> log)
    {
        _ha          = ha;
        _vacationCfg = vacationCfg;
        _log         = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("HaVacation worker started.");

        // Build today's schedule immediately on startup.
        await LoadScheduleForTodayAsync(ct);

        var lastScheduleDate = DateTimeOffset.Now.Date;

        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;

            // Refresh the schedule at midnight for the new day.
            if (now.Date != lastScheduleDate)
            {
                lastScheduleDate = now.Date;
                await LoadScheduleForTodayAsync(ct);
            }

            await FireDueEventsAsync(now, ct);

            // Tick every second – fine-grained enough to respect jitter precision.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    // -------------------------------------------------------------------------
    // Schedule loading
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fetches history for the reference day (today − LookbackDays) and
    /// populates the queue with today's replay times.
    /// </summary>
    private async Task LoadScheduleForTodayAsync(CancellationToken ct)
    {
        var cfg = _vacationCfg.CurrentValue;

        if (!cfg.Enabled)
        {
            _log.LogInformation("Vacation mode is disabled – no schedule loaded.");
            ClearQueue();
            return;
        }

        if (cfg.Entities.Count == 0)
        {
            _log.LogWarning("Vacation mode is enabled but no entities are configured.");
            return;
        }

        // Reference window: the full calendar day N days ago.
        var referenceDate = DateTimeOffset.Now.Date.AddDays(-cfg.LookbackDays);
        var from = new DateTimeOffset(referenceDate, TimeSpan.Zero);
        var to   = from.AddDays(1);

        _log.LogInformation(
            "Loading vacation schedule – reference day: {Date}, entities: {Count}",
            referenceDate.ToShortDateString(),
            cfg.Entities.Count);

        List<EntityStateEntry> history;
        try
        {
            history = await _ha.GetHistoryAsync(cfg.Entities, from, to, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to fetch history from Home Assistant.");
            return;
        }

        ClearQueue();

        var today     = DateTimeOffset.Now.Date;
        var jitterMax = cfg.RandomJitterSeconds;
        var count     = 0;

        foreach (var entry in history)
        {
            // Translate reference-day time → today's equivalent time.
            var timeOfDay = entry.LastChanged - entry.LastChanged.Date;
            var fireAt    = new DateTimeOffset(today, entry.LastChanged.Offset) + timeOfDay;

            // Apply random jitter (±jitterMax seconds).
            var jitterSec = _rng.Next(-jitterMax, jitterMax + 1);
            fireAt = fireAt.AddSeconds(jitterSec);

            // Skip events that are already in the past (e.g. service restarted mid-day).
            if (fireAt < DateTimeOffset.Now)
                continue;

            _queue.Enqueue(new ScheduledReplay
            {
                EntityId   = entry.EntityId,
                State      = entry.State,
                Attributes = entry.Attributes,
                FireAt     = fireAt
            });
            count++;
        }

        _log.LogInformation(
            "Schedule loaded: {Scheduled} events queued ({Skipped} past events skipped).",
            count,
            history.Count - count);
    }

    // -------------------------------------------------------------------------
    // Event execution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Drains all events from the queue whose <see cref="ScheduledReplay.FireAt"/>
    /// is at or before <paramref name="now"/> and calls the HA service for each.
    /// Uses TryPeek before TryDequeue so that not-yet-due events are never removed
    /// from the queue, preserving the FireAt-sorted order at all times.
    /// </summary>
    private async Task FireDueEventsAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Single-consumer invariant: only the ExecuteAsync loop calls this method,
        // so TryPeek followed by TryDequeue is race-free here.
        // Peek first — if the front event is not yet due we stop without touching the queue.
        while (_queue.TryPeek(out var replay))
        {
            if (replay.FireAt > now)
                break; // Next event is not yet due; queue order is preserved.

            // The event is due — remove it definitively.
            if (!_queue.TryDequeue(out replay))
                break;

            try
            {
                await _ha.ReplayStateAsync(
                    new EntityStateEntry
                    {
                        EntityId    = replay.EntityId,
                        State       = replay.State,
                        Attributes  = replay.Attributes,
                        LastChanged = replay.FireAt
                    },
                    ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to replay {EntityId} → {State}.", replay.EntityId, replay.State);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void ClearQueue()
    {
        while (_queue.TryDequeue(out _)) { }
    }
}
