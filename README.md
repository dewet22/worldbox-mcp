# worldbox-mcp

> _<!-- TODO(pitch): one-sentence tagline that hooks. Example: "Give your AI agent god-mode in WorldBox." Keep it under 80 chars. -->_

[![CI](https://github.com/fullya99/worldbox-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/fullya99/worldbox-mcp/actions/workflows/ci.yml)
[![PyPI version](https://img.shields.io/pypi/v/worldbox-mcp.svg)](https://pypi.org/project/worldbox-mcp/)
[![PyPI downloads](https://img.shields.io/pypi/dm/worldbox-mcp.svg)](https://pypi.org/project/worldbox-mcp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Code style: ruff](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/astral-sh/ruff/main/assets/badge/v2.json)](https://github.com/astral-sh/ruff)

**worldbox-mcp** lets any [MCP](https://modelcontextprotocol.io)-compatible AI client (Claude Code, OpenCode, Codex, Cursor, Continue, …) directly control the game [WorldBox](https://www.superworldbox.com/). Spawn dragons, paint terrain, trigger meteors, query civilizations — all from a conversation.

<!-- TODO(demo): replace with real 15s demo GIF once Phase 1 works -->
![demo](examples/demo.gif)

## How it works

```
┌─────────────┐  MCP stdio  ┌──────────────────┐  HTTP localhost  ┌─────────────────────┐
│  AI Client  ├────────────►│ worldbox-mcp     ├─────────────────►│ WorldBoxBridge      │
│ (any MCP)   │             │ (Python server)  │                  │ BepInEx C# plugin   │
└─────────────┘             └──────────────────┘                  │ inside WorldBox     │
                                                                  └─────────────────────┘
```

Two components, both open source:
1. **`WorldBoxBridge`** — a BepInEx plugin (C#) injected into WorldBox that exposes a local HTTP API to the game's internals.
2. **`worldbox-mcp`** — a Python MCP server distributed via PyPI that translates MCP tool calls into HTTP requests.

## Quickstart

### 1. Install the in-game mod

Requires WorldBox installed and **Experimental Mode** enabled in-game (Settings → Experimental Mode).

```powershell
# Windows
iex (irm https://raw.githubusercontent.com/fullya99/worldbox-mcp/main/scripts/install-mod.ps1)
```

```bash
# Linux / macOS
curl -fsSL https://raw.githubusercontent.com/fullya99/worldbox-mcp/main/scripts/install-mod.sh | bash
```

Launch WorldBox once. The mod auto-generates a config + token at `<worldbox>/BepInEx/config/WorldBoxBridge.cfg`.

### 2. Plug it into your AI client

The MCP server runs via `uvx` — no install required.

<details>
<summary><strong>Claude Code</strong></summary>

```bash
claude mcp add worldbox -- uvx worldbox-mcp
```
</details>

<details>
<summary><strong>OpenCode</strong></summary>

```jsonc
// ~/.config/opencode/config.json
{
  "mcp": {
    "worldbox": { "type": "local", "command": ["uvx", "worldbox-mcp"] }
  }
}
```
</details>

<details>
<summary><strong>Codex CLI</strong></summary>

```toml
# ~/.codex/config.toml
[mcp_servers.worldbox]
command = "uvx"
args = ["worldbox-mcp"]
```
</details>

<details>
<summary><strong>Cursor</strong></summary>

```json
// .cursor/mcp.json
{
  "mcpServers": {
    "worldbox": { "command": "uvx", "args": ["worldbox-mcp"] }
  }
}
```
</details>

<details>
<summary><strong>Continue</strong></summary>

```yaml
# ~/.continue/config.yaml
mcpServers:
  - name: worldbox
    command: uvx
    args: [worldbox-mcp]
```
</details>

See [docs/install/](docs/install/) for any other MCP-compatible client.

### 3. Try it

```
> Build me a Roman empire surrounded by hostile dragons and let evolution run for 50 years.
```

## What can it do?

Three generic primitives cover **100% of WorldBox actions** through the game's own asset registry:

| Tool | Covers |
|---|---|
| `paint_tile(x, y, tile_id, radius?)` | Every terrain type (water, lava, sand, grass variants, forests, roads, …) |
| `spawn(entity_id, x, y, count?, kingdom_id?)` | Every actor (humans, elves, orcs, dragons, demons, titans, kraken, cthulhu, all animals…) |
| `invoke_power(power_id, x?, y?, args?)` | Every disaster + global toggle (meteor, nuke, volcano, tsunami, plague, peace, civ on/off, …) |

Plus discovery (`list_tiles`, `list_actors`, `list_powers`), read-only inspection (`get_world_state`, `query_actors`, `screenshot`), and global control (`pause`, `set_speed`, `time_skip`, `generate_world`, `save_world`, `load_world`, `camera_goto`).

→ Full reference: [docs/command-reference.md](docs/command-reference.md) (auto-generated from `capabilities()`)

## Compatibility

| WorldBox version | Mod version | Status |
|---|---|---|
| 0.x (Unity 2022.3.60f1, Mono) | 0.1.x | ✅ Tested |

See [docs/compatibility.md](docs/compatibility.md) for the full matrix.

## Documentation

Full docs: **<https://fullya99.github.io/worldbox-mcp/>**

- [Architecture](docs/architecture.md)
- [Protocol spec](docs/protocol.md)
- [Command reference](docs/command-reference.md)
- [Game API notes](docs/game-api-notes.md)
- [Contributing](CONTRIBUTING.md)

## Contributing

Issues, PRs, and adding new commands are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE) © 2026 fullya99

> _worldbox-mcp is an unofficial community project and is not affiliated with or endorsed by Maxim Karpenko or Superworldbox._
