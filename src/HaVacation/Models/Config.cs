namespace HaVacation.Models;

/// <summary>Connection details for the Home Assistant instance.</summary>
public sealed class HomeAssistantConfig
{
    public string Url { get; set; } = "";
    public string Token { get; set; } = "";
}

/// <summary>Vacation-mode behaviour settings.</summary>
public sealed class VacationConfig
{
    /// <summary>Set to true when you leave home to start replaying history.</summary>
    public bool Enabled { get; set; }

    /// <summary>How many days back to look for the reference pattern (default: 7).</summary>
    public int LookbackDays { get; set; } = 7;

    /// <summary>
    /// Maximum random delay (in seconds) added to every replayed event so the
    /// pattern is never perfectly identical night after night.
    /// </summary>
    public int RandomJitterSeconds { get; set; } = 120;

    /// <summary>Entity IDs to replay (lights, covers, media_players, …).</summary>
    public List<string> Entities { get; set; } = [];
}
