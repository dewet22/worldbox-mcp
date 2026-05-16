# Continue

[Continue](https://continue.dev) is an open-source IDE assistant for VS Code and JetBrains. MCP servers go in its YAML config.

## Add the server

Edit `~/.continue/config.yaml`:

```yaml
mcpServers:
  - name: worldbox
    command: uvx
    args:
      - worldbox-mcp
```

Reload Continue (Cmd/Ctrl + Shift + P → "Continue: Reload").

## Verify

In a chat tab, list tools and call `worldbox_health`.
