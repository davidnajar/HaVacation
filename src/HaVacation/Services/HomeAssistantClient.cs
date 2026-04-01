using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HaVacation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HaVacation.Services;

/// <summary>
/// Thin wrapper around the Home Assistant REST API.
/// Only the two endpoints needed for vacation mode are implemented:
///   • GET  /api/history/period  – fetch historical state changes
///   • POST /api/services/{domain}/{service} – replay a state change
/// </summary>
public sealed class HomeAssistantClient
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<HomeAssistantConfig> _config;
    private readonly ILogger<HomeAssistantClient> _log;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HomeAssistantClient(
        HttpClient http,
        IOptionsMonitor<HomeAssistantConfig> config,
        ILogger<HomeAssistantClient> log)
    {
        _http   = http;
        _config = config;
        _log    = log;
    }

    private static readonly string[] SupportedDomains =
        ["light", "cover", "media_player", "switch", "input_boolean", "fan"];

    // -------------------------------------------------------------------------
    // Entity discovery
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all HA entities whose domain is supported by vacation mode,
    /// sorted by entity ID. Used to populate the entity picker in the UI.
    /// </summary>
    public async Task<List<HaEntityInfo>> GetEntitiesAsync(CancellationToken ct = default)
    {
        ConfigureHttpClient();

        var response = await _http.GetAsync("api/states", ct);
        response.EnsureSuccessStatusCode();

        var json   = await response.Content.ReadAsStringAsync(ct);
        var states = JsonSerializer.Deserialize<List<HaStateEntry>>(json, _jsonOpts) ?? [];

        return [.. states
            .Where(s => s.EntityId.Contains('.') &&
                        SupportedDomains.Contains(s.EntityId.Split('.')[0]))
            .Select(s => new HaEntityInfo(s.EntityId, s.FriendlyName))
            .OrderBy(e => e.EntityId)];
    }

    // -------------------------------------------------------------------------
    // History
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all state changes for the given entities between <paramref name="from"/>
    /// and <paramref name="to"/>, ordered by <see cref="EntityStateEntry.LastChanged"/>.
    /// The first entry for each entity is the state at <paramref name="from"/> (not a
    /// change), so it is skipped automatically.
    /// </summary>
    public async Task<List<EntityStateEntry>> GetHistoryAsync(
        IEnumerable<string> entityIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        ConfigureHttpClient();

        var ids    = string.Join(",", entityIds);
        var start  = Uri.EscapeDataString(from.ToString("o"));
        var end    = Uri.EscapeDataString(to.ToString("o"));
        var url    = $"api/history/period/{start}?end_time={end}&filter_entity_id={ids}&minimal_response=true&no_attributes=false";

        _log.LogDebug("Fetching history: {Url}", url);

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        // The API returns: List<List<EntityStateEntry>> (one inner list per entity).
        var perEntity = JsonSerializer.Deserialize<List<List<EntityStateEntry>>>(json, _jsonOpts)
                        ?? [];

        var all = new List<EntityStateEntry>();
        foreach (var entityHistory in perEntity)
        {
            if (entityHistory.Count > 1)
            {
                // First element = state at query start time (not an actual change) – skip it.
                all.AddRange(entityHistory.Skip(1));
            }
        }

        return [.. all.OrderBy(e => e.LastChanged)];
    }

    // -------------------------------------------------------------------------
    // Service calls
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calls a HA service to replay an entity state.
    /// Handles lights (brightness / color), covers (position), and generic
    /// on/off entities (switches, input_booleans, scripts, media players, …).
    /// </summary>
    public async Task ReplayStateAsync(EntityStateEntry entry, CancellationToken ct = default)
    {
        var domain  = entry.EntityId.Split('.')[0];
        var (svc, payload) = BuildServiceCall(domain, entry);

        if (svc is null)
        {
            _log.LogDebug("Skipping {EntityId} (state={State}, no service mapping)", entry.EntityId, entry.State);
            return;
        }

        ConfigureHttpClient();

        var url  = $"api/services/{domain}/{svc}";
        var body = JsonSerializer.Serialize(payload);
        var req  = new StringContent(body, Encoding.UTF8, "application/json");

        _log.LogInformation("Replaying {EntityId} → {State} (POST {Url})", entry.EntityId, entry.State, url);

        var response = await _http.PostAsync(url, req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Service call failed ({Status}): {Body}", response.StatusCode, err);
        }
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private void ConfigureHttpClient()
    {
        var cfg = _config.CurrentValue;

        // BaseAddress and auth header may change when config is reloaded at runtime.
        _http.BaseAddress = new Uri(cfg.Url.TrimEnd('/') + '/');
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", cfg.Token);
    }

    /// <summary>
    /// Determines the HA service name and data payload for the given entity + state.
    /// Returns (null, _) when the state should be skipped (e.g. transient cover movement).
    /// </summary>
    private static (string? service, object payload) BuildServiceCall(string domain, EntityStateEntry entry)
    {
        var attrs = entry.Attributes;
        var id    = new { entity_id = entry.EntityId };

        return domain switch
        {
            "light" when entry.State == "on" => (
                "turn_on",
                BuildLightPayload(entry.EntityId, attrs)
            ),

            "light" => (
                "turn_off",
                id
            ),

            "cover" when entry.State == "open" => (
                "open_cover",
                id
            ),

            "cover" when entry.State == "closed" => (
                "close_cover",
                id
            ),

            // Skip transient cover states (opening / closing / stopped).
            "cover" => (null, id),

            "media_player" when entry.State is "off" or "unavailable" => (
                "turn_off",
                id
            ),

            "media_player" => (
                "turn_on",
                id
            ),

            // Generic entities: switches, input_booleans, scripts, fans, etc.
            _ when entry.State == "on" => (
                "turn_on",
                id
            ),

            _ when entry.State == "off" => (
                "turn_off",
                id
            ),

            // Unknown state – skip.
            _ => (null, id)
        };
    }

    private static object BuildLightPayload(string entityId, JsonElement attrs)
    {
        // Forward the most common light attributes so color/brightness is preserved.
        var payload = new Dictionary<string, object> { ["entity_id"] = entityId };

        if (attrs.TryGetProperty("brightness", out var brightness) &&
            brightness.ValueKind == JsonValueKind.Number)
        {
            payload["brightness"] = brightness.GetInt32();
        }

        if (attrs.TryGetProperty("color_temp", out var colorTemp) &&
            colorTemp.ValueKind == JsonValueKind.Number)
        {
            payload["color_temp"] = colorTemp.GetInt32();
        }

        if (attrs.TryGetProperty("rgb_color", out var rgb) &&
            rgb.ValueKind == JsonValueKind.Array)
        {
            payload["rgb_color"] = rgb.EnumerateArray()
                                      .Select(v => v.GetInt32())
                                      .ToArray();
        }

        return payload;
    }
}
