# Vector Notifier

A .NET 10 event-driven notification service that turns normalized AgentMail, calendar, Brainwav/OpenClaw, GitHub, and platform-health events into restrained physical Vector announcements through Wire-Pod.

Producers classify events and send safe spoken text. The API validates and suppresses them, SQLite persists dedupe/state/queue data, and one background worker serializes animation and speech commands. Producers remain responsible for mail classification, calendar intake, threshold duration (for example sustained CPU), and safe redaction.

## Endpoints

- `POST /vector/events` — normalized event intake; requires `X-Api-Key`.
- `POST /vector/say` — compatible direct speech endpoint; requires `X-Api-Key`.
- `POST /github/webhook` — compatible GitHub webhook; requires a valid `X-Hub-Signature-256`.
- `GET /health/live` — process liveness.
- `GET /health/ready` — reports API, SQLite, and current Wire-Pod reachability; degraded readiness is HTTP 503.

`/vector/events` returns 202 for persisted work and 200 for intentional suppression (`duplicate`, `expired`, `quiet_hours`, `unchanged_state`, `sensitive_content`, `event_disabled`, or `quiet_queue_full`). Invalid requests return 400 and bad API keys return 401. Secrets and metadata are never logged.

```json
{
  "source": "agentmail",
  "eventType": "email.priority",
  "severity": "warning",
  "emotion": "attention",
  "message": "Russ asked whether you can review the Canopy proposal.",
  "summary": "Reply requested from Russ",
  "dedupeKey": "agentmail-message-123",
  "occurredAt": "2026-07-12T14:30:00-05:00",
  "expiresAt": "2026-07-12T18:00:00-05:00",
  "stateKey": null,
  "state": null,
  "metadata": { "senderName": "Russ" }
}
```

Required: `source`, `eventType`, `severity`, `emotion`, `message`, and `dedupeKey`. Severities are `info`, `warning`, `critical`. Emotions are `quiet`, `attention`, `curious`, `excited`, `celebrate`, `annoyed`, `urgent`, `relieved`, and `judgmental`. `message` is limited to 500 characters. Supply `stateKey` and `state` together.

## Suppression and state

Dedupe keys survive restarts for `DedupeRetentionHours`. Expired events never speak. America/Chicago quiet hours queue noncritical events by default, up to `MaximumQueuedNotifications`; critical events bypass them. Only unexpired queued items are released. State events announce changes and suppress unchanged state, so `healthy → unhealthy` and `unhealthy → healthy` speak while repeated states do not. Cleanup runs hourly.

Choose stable state keys such as `cloudflared.service`, `github:repo:workflow:CI`, or `openclaw.model`. Dedupe keys identify one occurrence and should differ on a real transition.

AgentMail additionally blocks messages containing common high-risk phrases such as verification codes, credentials, account/routing numbers, and medical records. This is defense in depth: the producer must never send raw bodies, codes, account/loan details, credentials, medical or financial data. Use only a display name and a short safe summary.

## Supported integration contracts

Any event type is accepted unless disabled under `Notifier:Events`; these are the documented contracts:

- AgentMail: `email.priority`, `email.reply_required`, `email.security_warning`, `email.suspicious`, `package.shipped`, `package.delivered`, `appointment.confirmed`, `inbox.digest_ready`, `expected_email.arrived`, `bill_or_renewal.received`. Ordinary mail is not sent. Shipment is disabled by default.
- Calendar: `calendar.meeting_soon` (normally 10 minutes), `calendar.meeting_imminent` (normally 2 minutes), `calendar.meeting_cancelled`, `calendar.back_to_back`, `calendar.focus_time_started`, `calendar.workday_complete`, `calendar.personal_reminder`. Use meeting ID plus interval in the dedupe key. Never send confidential descriptions or attendee lists; use “your next meeting” when a title is unsafe.
- Brainwav/OpenClaw: `brainwav.task_completed`, `brainwav.approval_required`, `brainwav.priority_alert`, `brainwav.digest_ready`, `brainwav.automation_failed`, `brainwav.automation_recovered`, `brainwav.audit_problem`, `brainwav.audit_clean`, `brainwav.slipbox_backlog`, `openclaw.model_changed`, `openclaw.context_warning`, `openclaw.task_stalled`. Send Slipbox only when the external counter crosses `SlipboxBacklogThreshold`; this service neither changes notes nor enables Daily Cabinet.
- Platform health: service/tunnel, disk, sustained CPU/memory/swap, backup, internet, Wire-Pod, and Vector down/recovered events. Existing `platform-health.py` should calculate thresholds, sustained duration, and cooldown, then POST transitions with `stateKey`/`state`; do not add another monitor.
- GitHub: the signed endpoint supports ping, push, PR opened/merged/approved/changes requested, issues, workflow succeeded/failed/cancelled, deployment started/succeeded/failed, and repository vulnerability alerts. Every distinct completed workflow delivery is announced; build and deployment events are handled separately. GitHub messages are randomly selected from humorous response pools (20 each for build success and failure, and at least 5 for every other handled GitHub event). Robot work is queued before the endpoint returns.

## Producer examples

Set `URL` and use an API-key placeholder:

```bash
URL=http://127.0.0.1:5055
curl -i "$URL/vector/events" -H 'Content-Type: application/json' -H 'X-Api-Key: <VECTOR_NOTIFIER_API_KEY>' \
  -d '{"source":"agentmail","eventType":"email.reply_required","severity":"warning","emotion":"attention","message":"Russ sent an important message that needs your reply.","summary":"Reply requested from Russ","dedupeKey":"agentmail-message-123","expiresAt":"2026-07-13T00:00:00Z","metadata":{"senderName":"Russ"}}'

curl -i "$URL/vector/events" -H 'Content-Type: application/json' -H 'X-Api-Key: <VECTOR_NOTIFIER_API_KEY>' \
  -d '{"source":"calendar","eventType":"calendar.meeting_soon","severity":"info","emotion":"attention","message":"Your next meeting starts in ten minutes.","dedupeKey":"calendar-meeting-abc-10m","expiresAt":"2026-07-13T00:00:00Z"}'

curl -i "$URL/vector/events" -H 'Content-Type: application/json' -H 'X-Api-Key: <VECTOR_NOTIFIER_API_KEY>' \
  -d '{"source":"brainwav","eventType":"brainwav.approval_required","severity":"warning","emotion":"curious","message":"Brainwav needs your approval before continuing.","dedupeKey":"brainwav-approval-task-456","expiresAt":"2026-07-13T00:00:00Z"}'

curl -i "$URL/vector/events" -H 'Content-Type: application/json' -H 'X-Api-Key: <VECTOR_NOTIFIER_API_KEY>' \
  -d '{"source":"platform-health","eventType":"service.state","severity":"critical","emotion":"urgent","message":"The Cloudflare tunnel is down.","dedupeKey":"cloudflared-down-20260712T2100","stateKey":"cloudflared.service","state":"unhealthy","expiresAt":"2026-07-13T00:00:00Z"}'
```

Replace the example expirations with future timestamps. A recovery uses the same `stateKey`, a new dedupe key, `state: "healthy"`, and usually `severity: "info"`, `emotion: "relieved"`.

## Configuration

Required environment variables:

| Variable | Purpose |
|---|---|
| `VECTOR_NOTIFIER_API_KEY` | API key for Vector endpoints |
| `GITHUB_WEBHOOK_SECRET` | GitHub HMAC secret |
| `VECTOR_NOTIFIER_DB` | SQLite path; default `data/notifier.db` |
| `WIREPOD_BASE_URL` | Overrides Wire-Pod URL |
| `VECTOR_SERIAL` | Overrides robot serial |

`appsettings.json` documents typed `Notifier` settings: timezone, quiet-hour start/end and behavior (`queue` or `suppress`), critical bypass, queue maximum, retention, cooldown, event switches, Slipbox threshold, humor settings, shipment and imminent-meeting switches, Wire-Pod timings, and `EmotionAnimations`.

Animation identifiers differ between robot firmware/Wire-Pod installations. Defaults are intentionally empty, so every emotion safely falls back to speech. Populate `Notifier:EmotionAnimations:<emotion>` only with identifiers verified on this robot through its installed SDK/Wire-Pod animation list. An unsupported animation logs only the emotion and falls back to speech. No drive or lift commands are issued, which avoids table-edge movement.

Nested settings can be overridden with standard ASP.NET variables, for example `Notifier__QuietHoursBehavior=suppress`, `Notifier__Events__calendar.meeting_imminent=false`, or `Notifier__EmotionAnimations__excited=<verified-animation-name>`. Never put secrets in `appsettings.json`.

## Run, test, and deploy

```bash
export VECTOR_NOTIFIER_API_KEY='<secret>'
export GITHUB_WEBHOOK_SECRET='<secret>'
dotnet run
dotnet test tests/vector-notifier.Tests.csproj
```

For systemd, publish and install the included unit:

```bash
dotnet publish -c Release -o /opt/vector-notifier
sudo cp vector-notifier.service /etc/systemd/system/
sudo install -m 600 /dev/null /etc/vector-notifier.env
sudo systemctl daemon-reload
sudo systemctl enable --now vector-notifier
sudo systemctl status vector-notifier
```

Put required variables in `/etc/vector-notifier.env` as `NAME=value`. The unit runs as `vector-notifier`; create that system user and ensure `/opt/vector-notifier` is readable. `StateDirectory=vector-notifier` creates `/var/lib/vector-notifier` for SQLite.

Troubleshoot with `curl -i http://127.0.0.1:5055/health/ready` and `journalctl -u vector-notifier -n 100 --no-pager`. A 503 with `storage:false` indicates path/permission trouble; `vector:false` means Wire-Pod/Vector is unreachable while the API remains alive and persisted events remain queued. GitHub 401 means the configured secret, raw request body, or signature differs.
