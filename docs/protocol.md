# Protocol specification

The mod exposes a minimal HTTP/JSON API on `http://127.0.0.1:8723` (default port; configurable). Stable across the `0.x` series.

## Endpoints

| Method + path | Purpose |
|---|---|
| `GET /health` | Liveness + version metadata. Auth required. |
| `POST /cmd` | Execute a named command. Auth required. |
| `GET /capabilities` | Introspect registered commands. Auth required. |

## Authentication

Every request must include the `X-WB-Token` header. The token is generated on first launch and stored at:

```
<worldbox>/BepInEx/config/WorldBoxBridge.cfg
```

Requests without a valid token return `401 Unauthorized` immediately, before any work is queued.

## `GET /health`

Returns a small JSON object useful for connection checks.

```http
GET /health HTTP/1.1
X-WB-Token: <token>
```

```json
{
  "ok": true,
  "mod_version": "0.1.0",
  "worldbox_version": "0.x.x",
  "unity_version": "2022.3.60f1",
  "assembly_csharp_sha256": "…",
  "tick": 12345,
  "enabled": true
}
```

## `POST /cmd`

```http
POST /cmd HTTP/1.1
X-WB-Token: <token>
Content-Type: application/json

{
  "name": "<command-name>",
  "args": { "<arg>": <value>, ... }
}
```

### Success response

```json
{
  "ok": true,
  "result": { /* command-specific */ },
  "tick": 12345
}
```

### Error response

```json
{
  "ok": false,
  "error": {
    "code": "GAME_REJECTED",
    "message": "spawn 'dragon_red' at (12, 8) failed: tile is water and actor.water_walking = false",
    "command": "spawn",
    "args": { "entity_id": "dragon_red", "x": 12, "y": 8 },
    "exception": {
      "type": "System.InvalidOperationException",
      "message": "Cannot place water-incompatible actor on water tile",
      "stack_top": "WorldBoxBridge.Commands.Action.SpawnCommand.Execute (line 47)"
    }
  }
}
```

### Error codes

| Code | HTTP | Meaning |
|---|---|---|
| `UNKNOWN_COMMAND` | 404 | The `name` field doesn't match a registered command. |
| `BAD_ARGS` | 400 | Args fail JSON-schema validation. |
| `UNKNOWN_ASSET` | 400 | An asset id (`tile_id` / `entity_id` / `power_id`) doesn't exist in this WorldBox build. Response includes `did_you_mean: [...]` (Levenshtein top-5). |
| `OUT_OF_BOUNDS` | 400 | Coordinates are outside the current map. |
| `GAME_REJECTED` | 422 | The game accepted the dispatch but the action was logically refused. |
| `GAME_CRASH` | 500 | An exception bubbled up from `Assembly-CSharp`. Full type + message + stack top in response. |
| `MAIN_THREAD_TIMEOUT` | 504 | Command exceeded 30s on the main thread; it was abandoned. |
| `UNAUTHORIZED` | 401 | Missing or wrong `X-WB-Token`. |
| `DISABLED` | 503 | `enabled = false` in `WorldBoxBridge.cfg`. The kill-switch is active. |

## `GET /capabilities`

Returns the list of registered commands with their JSON Schemas. The Python server consumes this on startup to construct its MCP `tools/list` response.

```json
{
  "mod_version": "0.1.0",
  "worldbox_version": "0.x.x",
  "commands": [
    {
      "name": "spawn",
      "category": "action",
      "description": "Spawn one or more actors at (x, y).",
      "schema": { "$schema": "https://json-schema.org/draft/2020-12/schema", ... }
    },
    …
  ]
}
```

## Kill-switch

Edit `<worldbox>/BepInEx/config/WorldBoxBridge.cfg` and set `enabled = false`. The mod hot-reloads the file every 2 seconds and stops accepting commands. New requests get `503 DISABLED`. Set back to `true` to resume — no restart required.

## Stability promise

- Endpoint paths, error codes, and the JSON envelope shape are part of the `0.x` SemVer contract.
- Command **inputs and outputs** are versioned per command via `capabilities()`. Adding a new optional input field is non-breaking; removing or renaming a field is a major bump.
- New commands may be added at any minor release.
- `did_you_mean` suggestions and exception detail formatting are best-effort and may evolve.
