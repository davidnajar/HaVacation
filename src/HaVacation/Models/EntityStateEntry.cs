using System.Text.Json;
using System.Text.Json.Serialization;

namespace HaVacation.Models;

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
