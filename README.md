# HaVacation

A lightweight C# background service that makes your home **look occupied while you're away** by replaying the previous week's entity history through Home Assistant — with a small random jitter so the pattern is never perfectly identical.

## How it works

1. When `Vacation.Enabled` is `true`, the service fetches the state-change history of all configured entities for the same calendar day **N days ago** (default: 7, i.e. the same weekday).
2. Each historical event is re-scheduled to fire today at the same time-of-day, ± a random offset (`RandomJitterSeconds`).
3. A background loop checks the queue every second and calls the appropriate HA service to replay the state (turn lights on/off, open/close covers, etc.).
4. At midnight the schedule is automatically refreshed for the new day.

Supported entity domains: `light`, `cover`, `media_player`, `switch`, `input_boolean`, `fan`, and any other domain that uses `turn_on` / `turn_off` services.

---

## Quick start

### Option A – Docker Compose (recommended)

```bash
# 1. Clone the repo
git clone https://github.com/davidnajar/HaVacation.git
cd HaVacation/src/HaVacation

# 2. Edit appsettings.json with your HA URL, token, and entity list
#    (see Configuration section below)

# 3. Enable vacation mode
#    Set "Enabled": true in appsettings.json

# 4. Build and run
docker compose up -d
```

### Option B – dotnet run

```bash
cd HaVacation/src/HaVacation
dotnet run
```

### Option C – run as a systemd service

```bash
dotnet publish -c Release -o /opt/havacation
# create /etc/systemd/system/havacation.service pointing to /opt/havacation/HaVacation
systemctl enable --now havacation
```

---

## Configuration

Edit `appsettings.json` (or use environment variables when running with Docker):

```json
{
  "HomeAssistant": {
    "Url": "http://homeassistant.local:8123",
    "Token": "YOUR_LONG_LIVED_ACCESS_TOKEN"
  },
  "Vacation": {
    "Enabled": false,
    "LookbackDays": 7,
    "RandomJitterSeconds": 120,
    "Entities": [
      "light.living_room",
      "light.bedroom",
      "cover.living_room_blind",
      "media_player.tv"
    ]
  }
}
```

> **Token:** HA → Profile → Long-Lived Access Tokens  
> **Enabled:** flip to `true` when you leave, `false` when you return  
> **LookbackDays:** `7` = same weekday last week  
> **RandomJitterSeconds:** `120` = ±2 min random delay per event; `0` = exact replay

### Environment variable overrides (Docker)

```yaml
environment:
  - HomeAssistant__Url=http://homeassistant.local:8123
  - HomeAssistant__Token=your_token
  - Vacation__Enabled=true
  - Vacation__LookbackDays=7
  - Vacation__RandomJitterSeconds=120
  - Vacation__Entities__0=light.living_room
  - Vacation__Entities__1=cover.living_room_blind
```

> **Note:** Changes to `appsettings.json` are picked up at runtime without a restart (the schedule is refreshed at the next midnight).

---

## Home Assistant integration tips

### Toggle vacation mode from HA

Create an `input_boolean.vacation_mode` in HA and use an **automation** to call this service's REST endpoint — or simply update the config file / environment variable from an HA script using the `shell_command` integration.

### Recommended entity history settings

Ensure HA is recording the entities you care about. In `configuration.yaml`:

```yaml
recorder:
  include:
    entities:
      - light.living_room
      - cover.living_room_blind
      - media_player.tv
```

---

## Architecture

```
Program.cs
└── VacationWorker (BackgroundService, ticks every 1 s)
    ├── LoadScheduleForTodayAsync()  ← calls HA history API, applies jitter, fills queue
    └── FireDueEventsAsync()         ← dequeues and calls HA service API

HomeAssistantClient
├── GetHistoryAsync()   GET  /api/history/period
└── ReplayStateAsync()  POST /api/services/{domain}/{service}
```

All configuration is in `Models/Config.cs`; all HA communication is in `Services/HomeAssistantClient.cs`.
