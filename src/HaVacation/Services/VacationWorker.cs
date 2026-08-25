using HaVacation.Models;

namespace HaVacation.Services;

public sealed class VacationWorker : BackgroundService
{
    private readonly PriorityQueue<ScheduledReplay, DateTimeOffset> _queue = new();
    private readonly object _queueLock = new();
    private int _rescheduleRequested;
    private long _scheduleVersion;
    private readonly HomeAssistantClient _ha;
    private readonly ConfigurationService _config;
    private readonly ILogger<VacationWorker> _log;

    public VacationWorker(HomeAssistantClient ha, ConfigurationService config, ILogger<VacationWorker> log)
        => (_ha, _config, _log) = (ha, config, log);

    public long ScheduleVersion => Interlocked.Read(ref _scheduleVersion);
    public void RequestReschedule() => Interlocked.Exchange(ref _rescheduleRequested, 1);

    public IReadOnlyList<ScheduledReplay> PeekSchedule()
    {
        lock (_queueLock) return [.. _queue.UnorderedItems.Select(x => x.Element).OrderBy(x => x.FireAt)];
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await LoadScheduleForTodayAsync(ct);
        var lastLocalDate = GetLocalNow(_config.GetVacationConfig()).Date;
        while (!ct.IsCancellationRequested)
        {
            var cfg = _config.GetVacationConfig();
            var now = GetLocalNow(cfg);
            if (Interlocked.Exchange(ref _rescheduleRequested, 0) == 1 || now.Date != lastLocalDate)
            {
                lastLocalDate = now.Date;
                await LoadScheduleForTodayAsync(ct);
            }
            await FireDueEventsAsync(now, ct);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task LoadScheduleForTodayAsync(CancellationToken ct)
    {
        var cfg = _config.GetVacationConfig();
        ClearQueue();
        if (!cfg.Enabled || cfg.Entities.Count == 0) { Interlocked.Increment(ref _scheduleVersion); return; }

        try
        {
            var tz = ResolveTimeZone(cfg.TimeZone);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var referenceDate = localNow.Date.AddDays(-cfg.LookbackDays);
            var from = LocalBoundary(referenceDate, tz);
            var to = LocalBoundary(referenceDate.AddDays(1), tz);
            var history = await _ha.GetHistoryAsync(cfg.Entities, from, to, ct);

            foreach (var entry in history)
            {
                var historicalLocal = TimeZoneInfo.ConvertTime(entry.LastChanged, tz);
                var targetLocal = localNow.Date + historicalLocal.TimeOfDay;
                var fireAt = LocalBoundary(targetLocal, tz).AddSeconds(Random.Shared.Next(-cfg.RandomJitterSeconds, cfg.RandomJitterSeconds + 1));
                var fireAtLocal = TimeZoneInfo.ConvertTime(fireAt, tz);
                if (fireAtLocal <= localNow) continue;
                var replay = new ScheduledReplay { EntityId = entry.EntityId, State = entry.State, Attributes = entry.Attributes, FireAt = fireAtLocal };
                lock (_queueLock) _queue.Enqueue(replay, fireAtLocal);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Failed to build vacation schedule");
        }
        finally { Interlocked.Increment(ref _scheduleVersion); }
    }

    private async Task FireDueEventsAsync(DateTimeOffset now, CancellationToken ct)
    {
        while (true)
        {
            ScheduledReplay? replay;
            lock (_queueLock)
            {
                if (!_queue.TryPeek(out replay, out var due) || due > now) return;
                _queue.Dequeue();
            }
            try
            {
                await _ha.ReplayStateAsync(new EntityStateEntry { EntityId = replay!.EntityId, State = replay.State, Attributes = replay.Attributes, LastChanged = replay.FireAt }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Failed to replay {Entity} -> {State}", replay!.EntityId, replay.State);
            }
        }
    }

    private void ClearQueue() { lock (_queueLock) _queue.Clear(); }
    private static TimeZoneInfo ResolveTimeZone(string id) => TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "UTC" : id);
    private static DateTimeOffset GetLocalNow(VacationConfig cfg) => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ResolveTimeZone(cfg.TimeZone));
    private static DateTimeOffset LocalBoundary(DateTime local, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified));
    }
}
