# Claude Code

[Claude Code](https://www.anthropic.com/claude-code) is Anthropic's official CLI agent. It supports MCP servers natively over stdio.

## Add the server

```bash
claude mcp add worldbox -- uvx worldbox-mcp
```

Or edit `~/.claude.json` manually:

```json
{
  "mcpServers": {
    "worldbox": {
      "command": "uvx",
      "args": ["worldbox-mcp"]
    }
  }
}
```

## Verify

In a Claude Code session:

```
> /mcp
```

You should see `worldbox` listed with status `connected`. Then:

```
> Call worldbox_health() and tell me the current tick.
```

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `worldbox` status `failed` | `uvx` not on PATH | `winget install astral-sh.uv` then restart your shell |
| `connection refused 127.0.0.1:8723` | Mod not loaded or game not running | See [install/index.md](index.md) — verify `BepInEx/LogOutput.log` |
| `401 Unauthorized` | Token mismatch | The server auto-discovers the token; check `WorldBoxBridge.cfg` exists |
