# HaVacation

HaVacation makes your home look occupied while you're away by replaying Home Assistant entity activity from a previous day, shifted by configurable random jitter.

## Home Assistant add-on

When installed as a Home Assistant add-on, HaVacation uses the Supervisor API automatically. You do not need to create a Long-Lived Access Token. Runtime configuration is stored persistently under `/data/havacation.json`.

The timezone defaults to `auto`: HaVacation reads Home Assistant's configured `time_zone`, so replay times follow the same local timezone and DST rules as Home Assistant.

### Home Assistant entities

HaVacation publishes `sensor.havacation_next_event` directly through the Home Assistant Core API. Its state describes the next replay, with `entity_id`, `action`, `scheduled_at`, and `vacation_mode` attributes.

If an MQTT broker service is available, HaVacation also uses MQTT Discovery to expose a real controllable Vacation Mode switch. MQTT is optional; replay functionality does not depend on it.

## Automations

House-wide arrival/departure actions belong in Home Assistant automations rather than inside HaVacation. This keeps HaVacation focused on presence simulation while Home Assistant remains the orchestration layer.

Use the HaVacation Vacation Mode switch as an automation trigger. When it turns on you can disable a TV smart plug, set a refrigerator vacation mode, change climate presets, arm an alarm, or perform any other Home Assistant action. A second automation can restore the desired state when vacation mode turns off.

Example:

```yaml
alias: Vacation mode enabled
triggers:
  - trigger: state
    entity_id: switch.havacation_vacation_mode
    to: "on"
actions:
  - action: switch.turn_off
    target:
      entity_id: switch.tv_plug
  # Add refrigerator/climate/alarm actions here.
```

## Standalone Docker

Standalone Docker remains supported. Persist `/app/data`, and configure Home Assistant using the web UI or bootstrap environment variables (`HomeAssistant__Url`, `HomeAssistant__Token`, `Vacation__TimeZone`, etc.).

## Replay support

Current replay mappings include lights, covers, media players, switches, input booleans and fans. Light brightness/color information is preserved where Home Assistant history exposes it. The scheduler uses a priority queue after applying random jitter so events always fire in actual chronological order.
