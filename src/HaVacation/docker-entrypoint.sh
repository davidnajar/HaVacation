#!/bin/bash
# docker-entrypoint.sh
#
# When running as a Home Assistant add-on the Supervisor writes the user's
# configured options to /data/options.json.  This script translates those
# JSON values into the .NET environment-variable configuration keys that
# HaVacation already understands, then hands off to the application.
#
# In standalone Docker / docker-compose usage the file does not exist and
# the script is a transparent pass-through – env vars / appsettings.json
# work exactly as before.

set -e

OPTIONS="/data/options.json"

if [ -f "$OPTIONS" ]; then
    echo "[entrypoint] Home Assistant add-on mode detected – loading options from $OPTIONS"

    HA_URL=$(jq -r '.homeassistant_url // empty' "$OPTIONS")
    HA_TOKEN=$(jq -r '.homeassistant_token // empty' "$OPTIONS")
    VACATION_ENABLED=$(jq -r '.vacation_enabled // empty' "$OPTIONS")
    LOOKBACK_DAYS=$(jq -r '.lookback_days // empty' "$OPTIONS")
    JITTER=$(jq -r '.random_jitter_seconds // empty' "$OPTIONS")

    [ -n "$HA_URL" ]            && export HomeAssistant__Url="$HA_URL"
    [ -n "$HA_TOKEN" ]          && export HomeAssistant__Token="$HA_TOKEN"
    [ -n "$VACATION_ENABLED" ]  && export Vacation__Enabled="$VACATION_ENABLED"
    [ -n "$LOOKBACK_DAYS" ]     && export Vacation__LookbackDays="$LOOKBACK_DAYS"
    [ -n "$JITTER" ]            && export Vacation__RandomJitterSeconds="$JITTER"

    # Map the JSON array  ["light.x", "cover.y", …]  →  Vacation__Entities__0, __1, …
    COUNT=$(jq '.entities | length' "$OPTIONS")
    if [ "$COUNT" -gt 0 ]; then
        for i in $(seq 0 $((COUNT - 1))); do
            ENTITY=$(jq -r ".entities[$i]" "$OPTIONS")
            export "Vacation__Entities__${i}=${ENTITY}"
        done
    fi
fi

exec dotnet HaVacation.dll
