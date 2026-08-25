using System.Text.Json;
using HaVacation.Models;

namespace HaVacation.Services;

public sealed class ConfigurationService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private PersistedConfig _config;
    private long _revision;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public long Revision => Interlocked.Read(ref _revision);

    public ConfigurationService(IHostEnvironment env, ILogger<ConfigurationService> log)
    {
        var configuredDir = Environment.GetEnvironmentVariable("HAVACATION_DATA_DIR");
        var dataDir = !string.IsNullOrWhiteSpace(configuredDir) ? configuredDir : Path.Combine(env.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "havacation.json");
        _config = Load(log);
    }

    private PersistedConfig Load(ILogger log)
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<PersistedConfig>(File.ReadAllText(_path), JsonOptions) ?? new();
        }
        catch (Exception ex) { log.LogError(ex, "Could not load {Path}; defaults will be used", _path); }

        return new PersistedConfig
        {
            HomeAssistant = new()
            {
                Url = Environment.GetEnvironmentVariable("HomeAssistant__Url") ?? "",
                Token = Environment.GetEnvironmentVariable("HomeAssistant__Token") ?? ""
            },
            Vacation = new()
            {
                Enabled = bool.TryParse(Environment.GetEnvironmentVariable("Vacation__Enabled"), out var enabled) && enabled,
                LookbackDays = int.TryParse(Environment.GetEnvironmentVariable("Vacation__LookbackDays"), out var days) ? days : 7,
                RandomJitterSeconds = int.TryParse(Environment.GetEnvironmentVariable("Vacation__RandomJitterSeconds"), out var jitter) ? jitter : 120,
                TimeZone = Environment.GetEnvironmentVariable("Vacation__TimeZone") ?? "auto",
                Entities = SplitCsv("Vacation__Entities"),
                ExcludedEntities = SplitCsv("Vacation__ExcludedEntities"),
                ExcludedPatterns = SplitCsv("Vacation__ExcludedPatterns")
            }
        };
    }

    private static List<string> SplitCsv(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    public HomeAssistantConfig GetHomeAssistantConfig() => new() { Url = _config.HomeAssistant.Url, Token = _config.HomeAssistant.Token };

    public VacationConfig GetVacationConfig() => new()
    {
        Enabled = _config.Vacation.Enabled,
        LookbackDays = _config.Vacation.LookbackDays,
        RandomJitterSeconds = _config.Vacation.RandomJitterSeconds,
        TimeZone = string.IsNullOrWhiteSpace(_config.Vacation.TimeZone) ? "auto" : _config.Vacation.TimeZone,
        Entities = [.. _config.Vacation.Entities],
        ExcludedEntities = [.. _config.Vacation.ExcludedEntities],
        ExcludedPatterns = [.. _config.Vacation.ExcludedPatterns]
    };

    public async Task SaveAsync(HomeAssistantConfig ha, VacationConfig vacation)
    {
        await _gate.WaitAsync();
        try
        {
            vacation.LookbackDays = Math.Clamp(vacation.LookbackDays, 1, 365);
            vacation.RandomJitterSeconds = Math.Clamp(vacation.RandomJitterSeconds, 0, 3600);
            vacation.Entities = Normalize(vacation.Entities);
            vacation.ExcludedEntities = Normalize(vacation.ExcludedEntities);
            vacation.ExcludedPatterns = Normalize(vacation.ExcludedPatterns);
            vacation.TimeZone = string.IsNullOrWhiteSpace(vacation.TimeZone) ? "auto" : vacation.TimeZone.Trim();
            if (!vacation.TimeZone.Equals("auto", StringComparison.OrdinalIgnoreCase))
                _ = TimeZoneInfo.FindSystemTimeZoneById(vacation.TimeZone);

            _config = new PersistedConfig
            {
                HomeAssistant = new() { Url = ha.Url.Trim(), Token = ha.Token.Trim() },
                Vacation = new()
                {
                    Enabled = vacation.Enabled,
                    LookbackDays = vacation.LookbackDays,
                    RandomJitterSeconds = vacation.RandomJitterSeconds,
                    TimeZone = vacation.TimeZone,
                    Entities = [.. vacation.Entities],
                    ExcludedEntities = [.. vacation.ExcludedEntities],
                    ExcludedPatterns = [.. vacation.ExcludedPatterns]
                }
            };

            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(_config, JsonOptions));
            File.Move(tmp, _path, true);
            Interlocked.Increment(ref _revision);
        }
        finally { _gate.Release(); }
    }

    private static List<string> Normalize(IEnumerable<string> values) => values
        .Select(e => e.Trim())
        .Where(e => !string.IsNullOrWhiteSpace(e))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
