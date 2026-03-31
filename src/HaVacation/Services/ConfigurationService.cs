using HaVacation.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HaVacation.Services;

/// <summary>
/// Reads current vacation/HA settings and persists changes back to appsettings.json.
/// Because IOptionsMonitor watches the file, the worker picks up changes automatically.
/// </summary>
public sealed class ConfigurationService
{
    private readonly IOptionsMonitor<HomeAssistantConfig> _haCfg;
    private readonly IOptionsMonitor<VacationConfig> _vacationCfg;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ConfigurationService(
        IOptionsMonitor<HomeAssistantConfig> haCfg,
        IOptionsMonitor<VacationConfig> vacationCfg,
        IHostEnvironment env)
    {
        _haCfg       = haCfg;
        _vacationCfg = vacationCfg;
        _settingsPath = Path.Combine(env.ContentRootPath, "appsettings.json");
    }

    /// <summary>Returns a detached copy of the current Home Assistant config.</summary>
    public HomeAssistantConfig GetHomeAssistantConfig() => new()
    {
        Url   = _haCfg.CurrentValue.Url,
        Token = _haCfg.CurrentValue.Token
    };

    /// <summary>Returns a detached copy of the current vacation config.</summary>
    public VacationConfig GetVacationConfig() => new()
    {
        Enabled             = _vacationCfg.CurrentValue.Enabled,
        LookbackDays        = _vacationCfg.CurrentValue.LookbackDays,
        RandomJitterSeconds = _vacationCfg.CurrentValue.RandomJitterSeconds,
        Entities            = [.. _vacationCfg.CurrentValue.Entities]
    };

    /// <summary>
    /// Persists <paramref name="haConfig"/> and <paramref name="vacationConfig"/> to
    /// appsettings.json, preserving any other sections already in the file.
    /// A semaphore ensures only one write occurs at a time.
    /// </summary>
    public async Task SaveAsync(HomeAssistantConfig haConfig, VacationConfig vacationConfig)
    {
        await _writeLock.WaitAsync();
        try
        {
            string raw = File.Exists(_settingsPath)
                ? await File.ReadAllTextAsync(_settingsPath)
                : "{}";

            // Parse allowing the JSON comments that may exist in the default file.
            var documentOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
            var root            = JsonNode.Parse(raw, documentOptions: documentOptions) as JsonObject ?? new JsonObject();

            // Build and replace the HomeAssistant section.
            root["HomeAssistant"] = new JsonObject
            {
                ["Url"]   = haConfig.Url,
                ["Token"] = haConfig.Token
            };

            // Build and replace the Vacation section.
            var entitiesArray = new JsonArray();
            foreach (var entity in vacationConfig.Entities.Where(e => !string.IsNullOrWhiteSpace(e)))
                entitiesArray.Add(JsonValue.Create(entity.Trim()));

            root["Vacation"] = new JsonObject
            {
                ["Enabled"]             = vacationConfig.Enabled,
                ["LookbackDays"]        = vacationConfig.LookbackDays,
                ["RandomJitterSeconds"] = vacationConfig.RandomJitterSeconds,
                ["Entities"]            = entitiesArray
            };

            // Write to a temp file first, then atomically replace the target to avoid
            // partial writes corrupting the config if the process is interrupted.
            var tempPath     = _settingsPath + ".tmp";
            var writeOptions = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(tempPath, root.ToJsonString(writeOptions));
            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
