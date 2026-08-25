using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HaVacation.Models;

namespace HaVacation.Services;

public sealed class HomeAssistantClient
{
    private readonly HttpClient _http;
    private readonly ConfigurationService _config;
    private readonly ILogger<HomeAssistantClient> _log;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] SupportedDomains = ["light", "cover", "media_player", "switch", "input_boolean", "fan"];

    public HomeAssistantClient(HttpClient http, ConfigurationService config, ILogger<HomeAssistantClient> log)
        => (_http, _config, _log) = (http, config, log);

    public async Task<List<HaEntityInfo>> GetEntitiesAsync(CancellationToken ct = default)
    {
        Configure();
        using var response = await _http.GetAsync("states", ct);
        response.EnsureSuccessStatusCode();
        var states = JsonSerializer.Deserialize<List<HaStateEntry>>(await response.Content.ReadAsStringAsync(ct), JsonOpts) ?? [];
        return [.. states.Where(s => s.EntityId.Contains('.') && SupportedDomains.Contains(s.EntityId.Split('.')[0]))
            .Select(s => new HaEntityInfo(s.EntityId, s.FriendlyName)).OrderBy(e => e.EntityId)];
    }

    public async Task<List<EntityStateEntry>> GetHistoryAsync(IEnumerable<string> entityIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        Configure();
        var ids = Uri.EscapeDataString(string.Join(",", entityIds));
        var start = Uri.EscapeDataString(from.ToString("o"));
        var end = Uri.EscapeDataString(to.ToString("o"));
        var url = $"history/period/{start}?end_time={end}&filter_entity_id={ids}&minimal_response=true&no_attributes=false";
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var perEntity = JsonSerializer.Deserialize<List<List<EntityStateEntry>>>(await response.Content.ReadAsStringAsync(ct), JsonOpts) ?? [];
        return [.. perEntity.Where(h => h.Count > 1).SelectMany(h => h.Skip(1)).OrderBy(e => e.LastChanged)];
    }

    public async Task ReplayStateAsync(EntityStateEntry entry, CancellationToken ct = default)
    {
        var domain = entry.EntityId.Split('.')[0];
        var (service, payload) = BuildServiceCall(domain, entry);
        if (service is null) return;
        Configure();
        using var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"services/{domain}/{service}", body, ct);
        if (!response.IsSuccessStatusCode)
            _log.LogWarning("Service call for {Entity} failed ({Status}): {Body}", entry.EntityId, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    private void Configure()
    {
        var cfg = _config.GetHomeAssistantConfig();
        var supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
        var useSupervisor = !string.IsNullOrWhiteSpace(supervisorToken) && string.IsNullOrWhiteSpace(cfg.Url);
        var baseUrl = useSupervisor ? "http://supervisor/core/api/" : cfg.Url.TrimEnd('/') + "/api/";
        var token = useSupervisor ? supervisorToken! : cfg.Token;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Home Assistant connection is not configured and Supervisor API access is unavailable.");
        _http.BaseAddress = new Uri(baseUrl);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static (string? service, object payload) BuildServiceCall(string domain, EntityStateEntry entry)
    {
        var id = new { entity_id = entry.EntityId };
        return domain switch
        {
            "light" when entry.State == "on" => ("turn_on", BuildLightPayload(entry.EntityId, entry.Attributes)),
            "light" when entry.State == "off" => ("turn_off", id),
            "cover" when entry.State == "open" => ("open_cover", id),
            "cover" when entry.State == "closed" => ("close_cover", id),
            "media_player" when entry.State == "off" => ("turn_off", id),
            "media_player" when entry.State is "on" or "idle" or "playing" or "paused" => ("turn_on", id),
            _ when entry.State == "on" => ("turn_on", id),
            _ when entry.State == "off" => ("turn_off", id),
            _ => (null, id)
        };
    }

    private static object BuildLightPayload(string entityId, JsonElement attrs)
    {
        var payload = new Dictionary<string, object> { ["entity_id"] = entityId };
        if (attrs.TryGetProperty("brightness", out var b) && b.ValueKind == JsonValueKind.Number) payload["brightness"] = b.GetInt32();
        // color_temp_kelvin is the current HA attribute; retain mired color_temp as a fallback for older histories.
        if (attrs.TryGetProperty("color_temp_kelvin", out var k) && k.ValueKind == JsonValueKind.Number) payload["color_temp_kelvin"] = k.GetInt32();
        else if (attrs.TryGetProperty("color_temp", out var t) && t.ValueKind == JsonValueKind.Number) payload["color_temp"] = t.GetInt32();
        if (attrs.TryGetProperty("rgb_color", out var rgb) && rgb.ValueKind == JsonValueKind.Array) payload["rgb_color"] = rgb.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        return payload;
    }
}
