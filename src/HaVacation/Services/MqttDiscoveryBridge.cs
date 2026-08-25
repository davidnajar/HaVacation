using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;

namespace HaVacation.Services;

public sealed class MqttDiscoveryBridge : BackgroundService
{
    private readonly ConfigurationService _config;
    private readonly VacationWorker _worker;
    private readonly ILogger<MqttDiscoveryBridge> _log;
    private IMqttClient? _client;

    public MqttDiscoveryBridge(ConfigurationService config, VacationWorker worker, ILogger<MqttDiscoveryBridge> log)
        => (_config, _worker, _log) = (config, worker, log);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_client is null || !_client.IsConnected)
                    await ConnectAsync(stoppingToken);

                if (_client?.IsConnected == true)
                    await PublishStateAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogDebug(ex, "MQTT integration unavailable; continuing without MQTT entities");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var token = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
        if (string.IsNullOrWhiteSpace(token)) return;

        using var http = new HttpClient { BaseAddress = new Uri("http://supervisor/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.GetAsync("services/mqtt", ct);
        if (!response.IsSuccessStatusCode) return;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;
        if (!root.TryGetProperty("host", out var hostEl) || string.IsNullOrWhiteSpace(hostEl.GetString())) return;

        var host = hostEl.GetString()!;
        var port = root.TryGetProperty("port", out var portEl) && int.TryParse(portEl.ToString(), out var p) ? p : 1883;
        var ssl = root.TryGetProperty("ssl", out var sslEl) && sslEl.ValueKind == JsonValueKind.True;
        if (ssl)
        {
            _log.LogWarning("MQTT service requires TLS; automatic HaVacation MQTT discovery currently uses the Supervisor internal non-TLS service only.");
            return;
        }

        var username = root.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = root.TryGetProperty("password", out var pw) ? pw.GetString() : null;

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += async args =>
        {
            if (args.ApplicationMessage.Topic != "havacation/vacation_mode/set") return;
            var payload = args.ApplicationMessage.ConvertPayloadToString();
            var cfg = _config.GetVacationConfig();
            cfg.Enabled = payload.Equals("ON", StringComparison.OrdinalIgnoreCase);
            await _config.SaveAsync(_config.GetHomeAssistantConfig(), cfg);
            _worker.RequestReschedule();
            await PublishStateAsync(CancellationToken.None);
        };

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId("havacation")
            .WithTcpServer(host, port);
        if (!string.IsNullOrWhiteSpace(username)) optionsBuilder = optionsBuilder.WithCredentials(username, password ?? "");

        await _client.ConnectAsync(optionsBuilder.Build(), ct);
        var subscribe = factory.CreateSubscribeOptionsBuilder().WithTopicFilter("havacation/vacation_mode/set").Build();
        await _client.SubscribeAsync(subscribe, ct);
        await PublishDiscoveryAsync(ct);
        _log.LogInformation("MQTT discovery enabled for HaVacation");
    }

    private async Task PublishDiscoveryAsync(CancellationToken ct)
    {
        var device = new { identifiers = new[] { "havacation" }, name = "HaVacation", manufacturer = "HaVacation" };
        await PublishAsync("homeassistant/switch/havacation/vacation_mode/config", JsonSerializer.Serialize(new
        {
            name = "Vacation Mode", unique_id = "havacation_vacation_mode",
            command_topic = "havacation/vacation_mode/set", state_topic = "havacation/vacation_mode/state",
            payload_on = "ON", payload_off = "OFF", icon = "mdi:beach", device
        }), true, ct);

        await PublishAsync("homeassistant/sensor/havacation/next_event/config", JsonSerializer.Serialize(new
        {
            name = "Next Event", unique_id = "havacation_next_event",
            state_topic = "havacation/next_event/state",
            value_template = "{{ value_json.summary }}",
            json_attributes_topic = "havacation/next_event/state",
            icon = "mdi:calendar-clock", device
        }), true, ct);
    }

    private async Task PublishStateAsync(CancellationToken ct)
    {
        if (_client?.IsConnected != true) return;
        var cfg = _config.GetVacationConfig();
        var next = _worker.PeekSchedule().FirstOrDefault();
        await PublishAsync("havacation/vacation_mode/state", cfg.Enabled ? "ON" : "OFF", true, ct);
        await PublishAsync("havacation/next_event/state", JsonSerializer.Serialize(new
        {
            summary = next is null ? (cfg.Enabled ? "No upcoming event" : "Vacation mode disabled") : $"{next.EntityId} → {next.State}",
            entity_id = next?.EntityId,
            action = next?.State,
            scheduled_at = next?.FireAt.ToString("o")
        }), true, ct);
    }

    private async Task PublishAsync(string topic, string payload, bool retain, CancellationToken ct)
    {
        if (_client is null) return;
        var message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag(retain).Build();
        await _client.PublishAsync(message, ct);
    }
}
