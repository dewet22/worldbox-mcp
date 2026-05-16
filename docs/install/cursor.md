# Cursor

[Cursor](https://cursor.com) supports MCP servers via per-project or global config.

## Add the server

### Per-project

Create `.cursor/mcp.json` in your project root:

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

### Global

Create `~/.cursor/mcp.json` with the same content.

## Verify

Open Cursor settings → MCP. The `worldbox` server should appear as connected.

In the agent chat, type:

```
> Show available worldbox tools and call worldbox_health.
```
