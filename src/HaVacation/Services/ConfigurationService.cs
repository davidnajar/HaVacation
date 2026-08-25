using System.Text.Json;
using HaVacation.Models;

namespace HaVacation.Services;

/// <summary>Thread-safe configuration store persisted in Home Assistant's /data volume.</summary>
public sealed class ConfigurationService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private PersistedConfig _config;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConfigurationService(IHostEnvironment env, ILogger<ConfigurationService> log)
    {
        var configuredDir = Environment.GetEnvironmentVariable("HAVACATION_DATA_DIR");
        var dataDir = !string.IsNullOrWhiteSpace(configuredDir)
            ? configuredDir
            : Path.Combine(env.ContentRootPath, "data");
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
        catch (Exception ex)
        {
            log.LogError(ex, "Could not load {Path}; defaults will be used", _path);
        }
        return new PersistedConfig();
    }

    public HomeAssistantConfig GetHomeAssistantConfig() => new()
    {
        Url = _config.HomeAssistant.Url,
        Token = _config.HomeAssistant.Token
    };

    public VacationConfig GetVacationConfig() => new()
    {
        Enabled = _config.Vacation.Enabled,
        LookbackDays = _config.Vacation.LookbackDays,
        RandomJitterSeconds = _config.Vacation.RandomJitterSeconds,
        TimeZone = _config.Vacation.TimeZone,
        Entities = [.. _config.Vacation.Entities]
    };

    public async Task SaveAsync(HomeAssistantConfig ha, VacationConfig vacation)
    {
        await _gate.WaitAsync();
        try
        {
            vacation.LookbackDays = Math.Clamp(vacation.LookbackDays, 1, 365);
            vacation.RandomJitterSeconds = Math.Clamp(vacation.RandomJitterSeconds, 0, 3600);
            vacation.Entities = vacation.Entities
                .Select(e => e.Trim()).Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
                    Entities = [.. vacation.Entities]
                }
            };

            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(_config, JsonOptions));
            File.Move(tmp, _path, true);
        }
        finally { _gate.Release(); }
    }
}
