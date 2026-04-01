using System.Text.Json;
using System.Text.Json.Serialization;

namespace HaVacation.Models;

/// <summary>
/// Minimal HA state entry used for entity discovery (GET /api/states).
/// </summary>
public sealed class HaStateEntry
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = "";

    [JsonPropertyName("attributes")]
    public JsonElement Attributes { get; set; }

    /// <summary>Returns the friendly_name attribute, falling back to the entity ID.</summary>
    public string FriendlyName =>
        Attributes.ValueKind == JsonValueKind.Object &&
        Attributes.TryGetProperty("friendly_name", out var fn) &&
        fn.ValueKind == JsonValueKind.String
            ? fn.GetString() ?? EntityId
            : EntityId;
}

/// <summary>A Home Assistant entity available for monitoring.</summary>
public sealed record HaEntityInfo(string EntityId, string FriendlyName);

/// <summary>
/// A single entity-state snapshot returned by the HA history API.
/// </summary>
public sealed class EntityStateEntry
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    /// <summary>Raw attribute bag – kept as JsonElement so we can forward it to service calls.</summary>
    [JsonPropertyName("attributes")]
    public JsonElement Attributes { get; set; }

    [JsonPropertyName("last_changed")]
    public DateTimeOffset LastChanged { get; set; }
}

/// <summary>
/// An event that has been scheduled for replay today (after jitter is applied).
/// </summary>
public sealed class ScheduledReplay
{
    public string EntityId { get; set; } = "";
    public string State { get; set; } = "";
    public JsonElement Attributes { get; set; }

    /// <summary>The exact local time at which this replay should fire.</summary>
    public DateTimeOffset FireAt { get; set; }
}
