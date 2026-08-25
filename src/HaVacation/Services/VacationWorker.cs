using System.Text.RegularExpressions;
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
    private string? _detectedTimeZone;

    public VacationWorker(HomeAssistantClient ha, ConfigurationService config, ILogger<VacationWorker> log)
        => (_ha, _config, _log) = (ha, config, log);

    public long ScheduleVersion => Interlocked.Read(ref _scheduleVersion);
    public void RequestReschedule() => Interlocked.Exchange(ref _rescheduleRequested, 1);

    public IReadOnlyList<ScheduledReplay> PeekSchedule()
    {
        lock (_queueLock) return [.. _queue.UnorderedItems.Select(x => x.Element).OrderBy(x => x.FireAt)];
    }

    public async Task<IReadOnlyList<ScheduledReplay>> BuildPreviewAsync(VacationConfig config, CancellationToken ct = default)
        => await BuildPlanAsync(config, ct);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await LoadScheduleForTodayAsync(ct);
        var lastLocalDate = (await GetLocalNowAsync(_config.GetVacationConfig(), ct)).Date;
        while (!ct.IsCancellationRequested)
        {
            var cfg = _config.GetVacationConfig();
            var now = await GetLocalNowAsync(cfg, ct);
            if (Interlocked.Exchange(ref _rescheduleRequested, 0) == 1 || now.Date != lastLocalDate)
            {
                lastLocalDate = now.Date;
                await LoadScheduleForTodayAsync(ct);
            }
            await FireDueEventsAsync(now, ct);
            await PublishStatusAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task LoadScheduleForTodayAsync(CancellationToken ct)
    {
        var cfg = _config.GetVacationConfig();
        ClearQueue();
        if (!cfg.Enabled)
        {
            Interlocked.Increment(ref _scheduleVersion);
            await PublishStatusAsync(ct);
            return;
        }

        try
        {
            foreach (var replay in await BuildPlanAsync(cfg, ct))
            {
                lock (_queueLock) _queue.Enqueue(replay, replay.FireAt);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Failed to build vacation schedule");
        }
        finally
        {
            Interlocked.Increment(ref _scheduleVersion);
            await PublishStatusAsync(ct);
        }
    }

    private async Task<IReadOnlyList<ScheduledReplay>> BuildPlanAsync(VacationConfig cfg, CancellationToken ct)
    {
        var includedEntities = cfg.Entities
            .Where(entity => !IsExcluded(entity, cfg))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (includedEntities.Count == 0) return [];

        var tz = await ResolveTimeZoneAsync(cfg, ct);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var referenceDate = localNow.Date.AddDays(-cfg.LookbackDays);
        var from = LocalBoundary(referenceDate, tz);
        var to = LocalBoundary(referenceDate.AddDays(1), tz);
        var history = await _ha.GetHistoryAsync(includedEntities, from, to, ct);
        var result = new List<ScheduledReplay>();

        foreach (var entry in history)
        {
            if (IsExcluded(entry.EntityId, cfg)) continue;

            var historicalLocal = TimeZoneInfo.ConvertTime(entry.LastChanged, tz);
            var targetLocal = localNow.Date + historicalLocal.TimeOfDay;
            var fireAt = LocalBoundary(targetLocal, tz)
                .AddSeconds(Random.Shared.Next(-cfg.RandomJitterSeconds, cfg.RandomJitterSeconds + 1));
            var fireAtLocal = TimeZoneInfo.ConvertTime(fireAt, tz);

            if (fireAtLocal <= localNow) continue;

            result.Add(new ScheduledReplay
            {
                EntityId = entry.EntityId,
                State = entry.State,
                Attributes = entry.Attributes,
                FireAt = fireAtLocal
            });
        }

        return [.. result.OrderBy(x => x.FireAt)];
    }

    private static bool IsExcluded(string entityId, VacationConfig cfg)
    {
        if (cfg.ExcludedEntities.Contains(entityId, StringComparer.OrdinalIgnoreCase)) return true;

        foreach (var pattern in cfg.ExcludedPatterns.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var regex = "^" + Regex.Escape(pattern.Trim())
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            if (Regex.IsMatch(entityId, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
        }

        return false;
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
                await _ha.ReplayStateAsync(new EntityStateEntry
                {
                    EntityId = replay!.EntityId,
                    State = replay.State,
                    Attributes = replay.Attributes,
                    LastChanged = replay.FireAt
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Failed to replay {Entity} -> {State}", replay!.EntityId, replay.State);
            }
        }
    }

    private async Task PublishStatusAsync(CancellationToken ct)
    {
        try
        {
            ScheduledReplay? next;
            lock (_queueLock) next = _queue.TryPeek(out var item, out _) ? item : null;
            await _ha.PublishNextEventSensorAsync(next, _config.GetVacationConfig().Enabled, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Could not publish Home Assistant status sensor");
        }
    }

    private async Task<TimeZoneInfo> ResolveTimeZoneAsync(VacationConfig cfg, CancellationToken ct)
    {
        var id = cfg.TimeZone;
        if (string.IsNullOrWhiteSpace(id) || id.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            _detectedTimeZone ??= await _ha.GetTimeZoneAsync(ct);
            id = _detectedTimeZone;
        }
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    private async Task<DateTimeOffset> GetLocalNowAsync(VacationConfig cfg, CancellationToken ct)
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, await ResolveTimeZoneAsync(cfg, ct));

    private void ClearQueue() { lock (_queueLock) _queue.Clear(); }

    private static DateTimeOffset LocalBoundary(DateTime local, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified));
    }
}
