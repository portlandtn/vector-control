#!/usr/bin/env bash
set -euo pipefail

: "${WIREPOD_BASE_URL:?Set WIREPOD_BASE_URL, for example http://192.168.50.205:8080}"
: "${VECTOR_NOTIFIER_CALLBACK_URL:?Set VECTOR_NOTIFIER_CALLBACK_URL to a URL Wire-Pod can reach}"
: "${VECTOR_PLAY_INTENT_TOKEN:?Set VECTOR_PLAY_INTENT_TOKEN to the same secret used by vector-notifier}"

command -v curl >/dev/null || { echo "curl is required" >&2; exit 1; }
command -v jq >/dev/null || { echo "jq is required" >&2; exit 1; }

wirepod="${WIREPOD_BASE_URL%/}"
callback="${VECTOR_NOTIFIER_CALLBACK_URL%/}"
existing="$(curl -fsS "$wirepod/api/get_custom_intents_json" 2>/dev/null || printf '[]')"

upsert_intent() {
  local name="$1"
  local description="$2"
  local utterances="$3"
  local endpoint="$4"
  local lua payload number

  lua="postHTTPRequest(\"$callback$endpoint\", \"application/json\", \"{\\\"token\\\":\\\"$VECTOR_PLAY_INTENT_TOKEN\\\"}\", 5)"
  payload="$(jq -n \
    --arg name "$name" \
    --arg description "$description" \
    --argjson utterances "$utterances" \
    --arg lua "$lua" \
    '{name:$name,description:$description,utterances:$utterances,intent:"intent_system_noaudio",params:{paramname:"",paramvalue:""},exec:"/bin/true",execargs:[],luascript:$lua}')"
  number="$(jq -r --arg name "$name" 'to_entries[] | select(.value.name==$name) | .key+1' <<<"$existing" | head -n 1)"

  if [[ -n "$number" ]]; then
    payload="$(jq --argjson number "$number" '. + {number:$number}' <<<"$payload")"
    curl -fsS -X POST "$wirepod/api/edit_custom_intent" -H 'Content-Type: application/json' --data "$payload"
  else
    curl -fsS -X POST "$wirepod/api/add_custom_intent" -H 'Content-Type: application/json' --data "$payload"
    existing="$(jq --argjson item "$payload" '. + [$item]' <<<"$existing")"
  fi
  printf '\n'
}

upsert_intent "Izzy play mode" "Pause notifications and start a curated Izzy play session" '["go play with izzy","go play with easy","go play with isabella"]' "/vector/mode/play"
upsert_intent "Vector work mode" "Stop the Izzy play session and resume notifications" '["enter work mode"]' "/vector/mode/work"

echo "Wire-Pod play/work intents are configured."
