# HaVacation

HaVacation makes your home look occupied while you're away by replaying Home Assistant entity activity from a previous day, shifted by configurable random jitter.

## Home Assistant add-on

When installed as a Home Assistant add-on, HaVacation uses the Supervisor API automatically. You do not need to create a Long-Lived Access Token. Runtime configuration is stored persistently under `/data/havacation.json`.

The timezone defaults to `auto`: HaVacation reads Home Assistant's configured `time_zone` from Core, so replay times follow the same local timezone and DST rules as Home Assistant.

## Preview / Dry Run

Before enabling Vacation Mode, use **Preview today** in the HaVacation UI. The preview uses the same planning engine as the live scheduler, including lookback day, exclusions, timezone handling and random jitter, but it never calls a Home Assistant service.

Only events that would still happen later today are shown. This makes it possible to inspect the exact entities, times and target states before activating Vacation Mode.

## Included entities and exclusions

The included list defines which entities HaVacation may replay. Exclusions always win over the included list.

You can exclude exact entity IDs, for example:

- `switch.fridge`
- `media_player.living_room_tv`

You can also exclude groups using shell-like patterns:

- `media_player.*`
- `switch.fridge_*`
- `light.guest_?`

`*` matches any sequence of characters and `?` matches a single character. Excluded entities are filtered before history is requested and are also checked again before events are planned.

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

Standalone Docker remains supported. Persist `/app/data`, and configure Home Assistant using the web UI or bootstrap environment variables (`HomeAssistant__Url`, `HomeAssistant__Token`, `Vacation__TimeZone`, `Vacation__ExcludedEntities`, `Vacation__ExcludedPatterns`, etc.). Comma-separated values are supported for the entity lists when bootstrapping from environment variables.

## Replay support

Current replay mappings include lights, covers, media players, switches, input booleans and fans. Light brightness/color information is preserved where Home Assistant history exposes it. The scheduler uses a priority queue after applying random jitter so events always fire in actual chronological order.
