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

    // Set to 1 via Interlocked when the UI requests an immediate reschedule.
    private int  _rescheduleRequested;
    private long _scheduleVersion;

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

            // Honour a manual reschedule request from the UI.
            if (Interlocked.CompareExchange(ref _rescheduleRequested, 0, 1) == 1)
            {
                lastScheduleDate = now.Date;
                await LoadScheduleForTodayAsync(ct);
            }
            // Refresh the schedule at midnight for the new day.
            else if (now.Date != lastScheduleDate)
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
    // Public API (used by the Blazor UI)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Signals the worker to rebuild today's schedule on the next tick.
    /// Safe to call from any thread.
    /// </summary>
    public void RequestReschedule() =>
        Interlocked.Exchange(ref _rescheduleRequested, 1);

    /// <summary>
    /// A monotonically increasing counter that the worker increments every time
    /// <see cref="LoadScheduleForTodayAsync"/> completes. The UI can poll this
    /// value to detect when a force-reschedule has been processed.
    /// </summary>
    public long ScheduleVersion => Interlocked.Read(ref _scheduleVersion);

    /// <summary>
    /// Returns a snapshot of the currently queued replay events, sorted by
    /// <see cref="ScheduledReplay.FireAt"/>. Safe to call from any thread.
    /// </summary>
    public IReadOnlyList<ScheduledReplay> PeekSchedule() =>
        [.. _queue.OrderBy(r => r.FireAt)];

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
            Interlocked.Increment(ref _scheduleVersion);
            return;
        }

        if (cfg.Entities.Count == 0)
        {
            _log.LogWarning("Vacation mode is enabled but no entities are configured.");
            Interlocked.Increment(ref _scheduleVersion);
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
            Interlocked.Increment(ref _scheduleVersion);
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

        Interlocked.Increment(ref _scheduleVersion);
    }

    // -------------------------------------------------------------------------
    // Event execution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Drains all events from the queue whose <see cref="ScheduledReplay.FireAt"/>
    /// is at or before <paramref name="now"/> and calls the HA service for each.
    /// Events are dequeued immediately; any event that is not yet due is re-enqueued
    /// and the loop stops.
    /// </summary>
    private async Task FireDueEventsAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Dequeue directly and check the timestamp afterward to avoid a TryPeek/TryDequeue race.
        while (_queue.TryDequeue(out var replay))
        {
            if (replay.FireAt > now)
            {
                // Not due yet — put it back and stop.
                _queue.Enqueue(replay);
                break;
            }

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
