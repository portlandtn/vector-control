# vector-control

An ASP.NET 10 minimal API that makes a [Vector robot](https://www.digitaldreamlabs.com/pages/vector) speak GitHub event notifications. Webhooks from GitHub are verified and translated into spoken announcements delivered through [wire-pod](https://github.com/kercre123/wire-pod).

## Endpoints

### `POST /vector/say`

Speaks arbitrary text through Vector.

**Header:** `X-Api-Key: <your key>`

```json
{ "text": "Hello from Vector." }
```

### `POST /github/webhook`

Receives GitHub webhook events and speaks them. Supports `ping`, `push`, `pull_request`, `issues`, and `workflow_run`.

Signature is verified via HMAC-SHA256 (`X-Hub-Signature-256`).

## Configuration

Set these environment variables before running:

| Variable | Description |
|---|---|
| `VECTOR_NOTIFIER_API_KEY` | API key required for `/vector/say` |
| `GITHUB_WEBHOOK_SECRET` | Secret used to verify GitHub webhook signatures |

The wirepod server address and Vector serial number are hardcoded in `Program.cs` — adjust them to match your setup.

## Running

```bash
dotnet run
```

## GitHub Webhook Setup

In your GitHub repo settings, add a webhook pointing to `https://<your-host>/github/webhook` with:
- Content type: `application/json`
- Secret: the value of `GITHUB_WEBHOOK_SECRET`
- Events: Push, Pull requests, Issues, Workflow runs (or "Send me everything")
