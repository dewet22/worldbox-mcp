# OpenCode

[OpenCode](https://opencode.ai) is an open-source AI coding agent. It loads MCP servers from its config file.

## Add the server

Edit `~/.config/opencode/config.json` (Linux/macOS) or `%APPDATA%\opencode\config.json` (Windows):

```json
{
  "mcp": {
    "worldbox": {
      "type": "local",
      "command": ["uvx", "worldbox-mcp"],
      "enabled": true
    }
  }
}
```

Restart OpenCode.

## Verify

In a session, ask:

```
> List MCP servers, then call worldbox_health().
```
