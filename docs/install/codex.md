# Codex CLI

[Codex CLI](https://github.com/openai/codex) is OpenAI's open-source terminal agent. MCP servers are configured in TOML.

## Add the server

Edit `~/.codex/config.toml`:

```toml
[mcp_servers.worldbox]
command = "uvx"
args = ["worldbox-mcp"]
```

## Verify

```
codex
> /tools
```

You should see `worldbox_*` tools registered. Try:

```
> Call worldbox_health.
```
