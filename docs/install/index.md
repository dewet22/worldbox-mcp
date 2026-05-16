# Install

Installing `worldbox-mcp` is a two-step process:

1. **Install the in-game mod once.** The mod runs inside WorldBox itself.
2. **Plug the MCP server into your AI client.** The server is launched on demand by your client via `uvx worldbox-mcp` — no preinstall needed.

## Step 1 — Install the mod

### Prerequisites

- WorldBox on Steam (Windows/Linux/macOS).
- **Experimental Mode enabled** in-game: open WorldBox → Settings → Experimental Mode → ON.

### Windows

```powershell
iex (irm https://raw.githubusercontent.com/fullya99/worldbox-mcp/main/scripts/install-mod.ps1)
```

### Linux / macOS

```bash
curl -fsSL https://raw.githubusercontent.com/fullya99/worldbox-mcp/main/scripts/install-mod.sh | bash
```

The script:

- Downloads the appropriate [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for your platform.
- Downloads the latest `WorldBoxBridge.dll` from the GitHub Release.
- Installs both into your WorldBox install directory.
- Generates a random per-install auth token at `<worldbox>/BepInEx/config/WorldBoxBridge.cfg`.

Launch WorldBox once. `BepInEx/LogOutput.log` should contain a line like:

```
[Info: WorldBoxBridge] listening on 127.0.0.1:8723
```

### Verify

```bash
curl http://127.0.0.1:8723/health \
  -H "X-WB-Token: $(cat '<worldbox>/BepInEx/config/WorldBoxBridge.cfg' | grep token | cut -d'=' -f2)"
```

You should see:

```json
{ "ok": true, "unity_version": "2022.3.60f1", "tick": 1234 }
```

## Step 2 — Plug into your AI client

Pick your client:

- [Claude Code](claude-code.md)
- [OpenCode](opencode.md)
- [Codex CLI](codex.md)
- [Cursor](cursor.md)
- [Continue](continue.md)
- [Any other MCP-compatible client](manual.md)
