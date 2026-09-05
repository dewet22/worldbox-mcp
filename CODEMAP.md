<!-- contexte:convention
profil: monorepo (C# game mod + Python MCP server)
piliers: CODEMAP.md, TODOS.md, CHANGELOG.md
docs: docs/
archives: archives/
langue: en
notes:
  - CHANGELOG.md is generated and owned by release-please. Never hand-edit it. Write the
    intent into the Conventional Commit instead. Dated-entry convention does not apply.
  - docs/ is the published MkDocs site (fullya99.github.io/worldbox-mcp/), not an internal
    knowledge base. Pages carry no status headers because they render to the public web.
    Its index is docs/index.md, per mkdocs.yml, not docs/README.md.
  - Long-lived operational knowledge lives in CLAUDE.md, which is loaded every session.
    This file points at it rather than duplicating it.
-->

# CODEMAP, worldbox-mcp

> Where things live, and what touches what.
> Last updated: 2026-09-05 (v0.4.0)

**In one sentence**: a two-piece bridge that lets any MCP client drive the live Unity game
WorldBox, made of a BepInEx C# plugin that opens a loopback HTTP API into the game and a Python
MCP server that proxies tool calls to it.

---

## Tree

```
worldbox-mcp/
├── mod/            BepInEx 5 plugin, net462, injected into worldbox.exe
├── server/         Python 3.11+ MCP server, published to PyPI as worldbox-mcp
├── docs/           MkDocs Material site, published to GitHub Pages
├── examples/       Client configs, demo prompts, runnable end-to-end scenarios
├── scripts/        Installers (ps1 + sh) and dev bootstrap
└── mkdocs.yml      Site config. Lives at the root on purpose, see CLAUDE.md
```

`scratch/` is gitignored and holds decompiled game types.

---

## Modules

| Module | Path | Role |
|---|---|---|
| Plugin entry | `mod/src/WorldBoxBridge/Plugin.cs` | Wires config, dispatcher, session, command registry, HTTP listener |
| HTTP layer | `mod/src/WorldBoxBridge/Http/` | Hand-rolled HTTP/1.1 over TcpListener, auth, error envelope, turn gating |
| Commands | `mod/src/WorldBoxBridge/Commands/` | 28 `ICommand` implementations across six categories |
| Reflection | `mod/src/WorldBoxBridge/Reflection/` | Cached, fail-soft access to game internals. No compile-time reference to the game |
| Session | `mod/src/WorldBoxBridge/Session/` | Multi-agent identity, permissions, fog of war, turn order, message bus |
| Threading | `mod/src/WorldBoxBridge/Threading/` | Main-thread dispatcher injected into Unity's PlayerLoop |
| MCP server | `server/src/worldbox_mcp/` | FastMCP-style server on the mcp 2.x SDK, one module per tool category |
| Bridge client | `server/src/worldbox_mcp/client.py` | httpx wrapper, bearer auth, per-call token override |

Command categories map one to one onto `mod/src/WorldBoxBridge/Commands/<Category>/` and onto
`server/src/worldbox_mcp/tools/<category>.py`.

---

## Entry points

| Entry | File | Triggered by |
|---|---|---|
| Plugin load | `mod/src/WorldBoxBridge/Plugin.cs:34` (`Awake`) | BepInEx chainloader at game start |
| HTTP request | `mod/src/WorldBoxBridge/Http/HttpBridge.cs` | Any call to `127.0.0.1:8723` |
| Introspection | `mod/src/WorldBoxBridge/Http/HttpBridge.cs:419` | `GET /capabilities`, not an `ICommand` |
| Server CLI | `server/src/worldbox_mcp/__main__.py:75` (`main`) | `uvx worldbox-mcp`, console script `worldbox-mcp` |

---

## Main flow

An MCP client calls `worldbox_<tool>`. The Python tool in `tools/<category>.py` calls
`BridgeClient.call("<command>", args)`, which POSTs JSON to the loopback bridge with a bearer
token. `HttpBridge` authenticates the token against the `AgentRegistry`, builds a
`RequestContext`, applies turn gating for Action and Control categories, then hands the command
to `MainThreadDispatcher`. The command runs inside Unity's PlayerLoop, reaches game state through
`GameRefs` / `WorldAccess` / `GameUiAccess`, and returns a plain object that becomes the JSON
envelope.

Two registry families that are easy to confuse, asset libraries versus live entity managers, are
explained in CLAUDE.md. Read that before touching anything reflective.

---

## Counting the surface

29 MCP tools. That is 28 registered `ICommand` implementations plus the `/capabilities`
endpoint, which the bridge serves directly. Note `PauseCommand.cs` declares two commands,
`pause` and `resume`, so file count and command count differ.

Every number in the docs must agree with `docs/command-reference.md`, which is the reference
list. Tool counts have drifted three times now. Roadmap item 4 in CLAUDE.md exists to generate
that table instead of maintaining it by hand.

---

## External dependencies that constrain the build

| Dependency | Pinned where | Why it is pinned |
|---|---|---|
| `UnityEngine.Modules` 2022.3.60 | `mod/Directory.Packages.props` | Must match the engine the game ships. Comes from the BepInEx feed, mapped by exact id in `mod/NuGet.config` |
| `Newtonsoft.Json` 13.0.2 | same | The game's bundled copy wins at runtime. A newer nuget throws `MissingMethodException` |
| `Microsoft.NETFramework.ReferenceAssemblies` | same | Explicit so the restore graph is identical on Windows, Linux and macOS |
| `BepInEx.Core` 5.4.21 | same | Manual bumps only. Pulls HarmonyX 2.7.0 transitively, which is what the game expects |
| csharpier 0.30.6 | `.github/workflows/ci.yml` | 1.x changed the CLI and the XML formatting defaults |

`packages.lock.json` is committed for both mod projects. Regenerate with a force-evaluate
restore after any deliberate version change, otherwise CI fails with NU1004.

---

## Commands that actually work

Full cheat sheet in CLAUDE.md. The short version, from the repo root:

```bash
# Mod. No game install needed since v0.4.0.
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
dotnet restore mod/WorldBoxBridge.sln --locked-mode
dotnet build mod/WorldBoxBridge.sln -c Release --no-restore -warnaserror
dotnet test mod/tests/WorldBoxBridge.Tests/WorldBoxBridge.Tests.csproj -c Release --no-build
dotnet csharpier --check mod

# Server
cd server && uv sync --all-extras && uv run pytest tests/unit tests/integration
cd server && uv run ruff check . && uv run mypy --strict src
cd server && uv run worldbox-mcp --self-check --no-bridge-required
```

104 xUnit cases and 25 pytest cases as of v0.4.0, all verified on Linux with no game installed.

---

## Sensitive zones

The hard-won game API lessons are in CLAUDE.md under "Game API gotchas", nine of them, each one
a bug that was paid for once. The two that bite most often:

- Reflection lookups without explicit argument types hit `AmbiguousMatchException`. `GameRefs.Method`
  now resolves by enumeration, but pass argument types anyway when the method has overloads.
- Every Unity API call goes through `MainThreadDispatcher.RunOnMainThreadAsync`. Anything else
  corrupts game state without saying so.

What cannot be verified from a machine without the game: live `/health`, power invocation,
screenshots, save and load round trips. Everything else runs locally, including the whole C# side.
