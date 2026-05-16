# Architecture

`worldbox-mcp` is split across three address spaces that communicate through two well-defined boundaries.

```
┌─────────────┐  MCP        ┌──────────────────┐  HTTP loopback   ┌──────────────────────┐
│  AI client  ├────────────►│ worldbox-mcp     ├─────────────────►│ WorldBoxBridge       │
│   (any)     │  stdio/HTTP │ (Python, PyPI)   │  127.0.0.1:8723  │ BepInEx C# plugin    │
└─────────────┘             └──────────────────┘                  │ inside worldbox.exe  │
                                                                  │                      │
                                                                  │  ┌────────────────┐  │
                                                                  │  │ HTTP listener  │  │
                                                                  │  │ Auth + routing │  │
                                                                  │  └────────┬───────┘  │
                                                                  │           │          │
                                                                  │     ConcurrentQueue  │
                                                                  │           │          │
                                                                  │  ┌────────▼───────┐  │
                                                                  │  │ Main thread    │  │
                                                                  │  │ dispatcher     │  │
                                                                  │  │ (Update loop)  │  │
                                                                  │  └────────┬───────┘  │
                                                                  │           │          │
                                                                  │  ┌────────▼───────┐  │
                                                                  │  │ Command via    │  │
                                                                  │  │ reflection on  │  │
                                                                  │  │ Assembly-CSharp│  │
                                                                  │  └────────────────┘  │
                                                                  └──────────────────────┘
```

## Why this layout

| Boundary | Why a separate process |
|---|---|
| AI client ↔ MCP server | The MCP spec dictates this; lets any client reuse the same server. |
| MCP server ↔ Mod | The mod must live inside `worldbox.exe` to access game internals. The MCP server stays a normal Python process — easy to ship via PyPI, runs on any OS, no Unity baggage. |

## Component responsibilities

### `worldbox-mcp` (Python server)

- Speaks the MCP wire protocol (stdio + Streamable HTTP).
- Exposes a curated tool surface to AI clients (see [command-reference.md](command-reference.md)).
- Owns the contract: input validation via Pydantic, error mapping, retries on transient HTTP failures.
- Auto-discovers the mod's auth token by reading `<worldbox>/BepInEx/config/WorldBoxBridge.cfg`.
- **Does not** know anything about WorldBox internals. It is a thin, typed façade over the HTTP bridge.

### `WorldBoxBridge` (BepInEx C# plugin)

- Hosts a `System.Net.HttpListener` bound to `127.0.0.1`, authenticated with a per-install token.
- Dispatches incoming JSON commands onto Unity's main thread via a `ConcurrentQueue<Action>` drained from `MonoBehaviour.Update()`.
- Resolves all WorldBox types via cached reflection — never `using WorldBox.*` directly — so the mod survives game updates as long as core types keep their names.
- Maps every command to game APIs that live inside `Assembly-CSharp.dll`.

## Critical invariants

1. **Unity API calls happen on the main thread.** Period. The dispatcher is the only legal way for HTTP handlers to touch the game.
2. **Auth is checked before any work.** The HTTP middleware short-circuits on a bad token before queueing anything onto the main thread.
3. **Loopback only.** `HttpListener` bound to `127.0.0.1`. Refused at startup if config tries `0.0.0.0`.
4. **No static binding to game types.** A reflection lookup that fails logs a warning and disables only the affected command — the rest of the bridge keeps working.

## Data flow for a tool call

1. AI client emits `tools/call` over MCP.
2. Python server validates args with Pydantic, builds a JSON command envelope, sends `POST /cmd` with `X-WB-Token`.
3. Mod's HTTP handler verifies token, parses JSON, enqueues an `Action` on the main-thread dispatcher with a `TaskCompletionSource`.
4. Next Unity frame: dispatcher pops the action, runs the command, sets the TCS result.
5. HTTP handler awaits the TCS, serializes the result, returns `200 OK`.
6. Python server returns the result to the MCP client.

For long-running commands the dispatcher enforces a 30-second timeout to keep the game from freezing if a reflection call goes pathological — see [protocol.md](protocol.md).

## Threading model summary

| Thread | Owns |
|---|---|
| .NET thread pool | HTTP socket I/O, JSON parsing, command queueing |
| Unity main thread | All game state reads/writes, all `MapBox`/`World`/`Actor` access |
| Logger | Thread-safe via `BepInEx.Logging.ManualLogSource` |
