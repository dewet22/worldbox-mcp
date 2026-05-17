# Generic MCP client configuration

If your MCP client isn't listed in [`docs/install/`](../../docs/install/), use these baseline settings.

## Transport: stdio (recommended)

| Field | Value |
|---|---|
| Command | `uvx` |
| Args | `["worldbox-mcp"]` |
| Working directory | any |
| Environment | none required |

The server auto-discovers WorldBox at common Steam library paths and reads the auth token from `<worldbox>/BepInEx/config/WorldBoxBridge.cfg`.

## Transport: Streamable HTTP

Launch the server manually:

```bash
uvx worldbox-mcp --http --host 127.0.0.1 --port 7800
```

Then point your client to `http://127.0.0.1:7800/mcp`.

## Environment overrides

| Var | Default | Purpose |
|---|---|---|
| `WORLDBOX_MCP_BRIDGE_HOST` | `127.0.0.1` | Mod HTTP host |
| `WORLDBOX_MCP_BRIDGE_PORT` | `8723` | Mod HTTP port |
| `WORLDBOX_MCP_TOKEN` | _(auto-discover)_ | Bearer token sent to the bridge. In multi-agent mode, run one `worldbox-mcp` process per agent and set this to each agent's token. |
| `WORLDBOX_MCP_LOG` | `info` | `debug`, `info`, `warning`, `error` |
| `WORLDBOX_MCP_WORLDBOX_DIR` | _(auto-discover)_ | Manually point to your WorldBox install |
