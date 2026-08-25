namespace HaVacation.Models;

public sealed class HomeAssistantConfig
{
    // Empty values mean: use the Home Assistant Supervisor API automatically.
    public string Url { get; set; } = "";
    public string Token { get; set; } = "";
}

public sealed class VacationConfig
{
    public bool Enabled { get; set; }
    public int LookbackDays { get; set; } = 7;
    public int RandomJitterSeconds { get; set; } = 120;
    public string TimeZone { get; set; } = "Europe/Madrid";
    public List<string> Entities { get; set; } = [];
}

public sealed class PersistedConfig
{
    public HomeAssistantConfig HomeAssistant { get; set; } = new();
    public VacationConfig Vacation { get; set; } = new();
}
