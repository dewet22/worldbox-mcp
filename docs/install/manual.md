# Any other MCP-compatible client

`worldbox-mcp` follows the [MCP specification](https://modelcontextprotocol.io/specification) and supports both standard transports.

## Transport: stdio (recommended)

| Field | Value |
|---|---|
| Command | `uvx` |
| Args | `["worldbox-mcp"]` |
| Working directory | any |
| Environment | none required |

## Transport: Streamable HTTP

Launch the server manually:

```bash
uvx worldbox-mcp --http --host 127.0.0.1 --port 7800
```

Point your client at `http://127.0.0.1:7800/mcp`.

## Environment overrides

| Variable | Default | Purpose |
|---|---|---|
| `WORLDBOX_MCP_BRIDGE_HOST` | `127.0.0.1` | Mod HTTP host |
| `WORLDBOX_MCP_BRIDGE_PORT` | `8723` | Mod HTTP port |
| `WORLDBOX_MCP_TOKEN` | _(auto-discover)_ | Bearer token sent to the bridge. In multi-agent setups, set this per-process to each agent's token (one `worldbox-mcp` per agent). |
| `WORLDBOX_MCP_WORLDBOX_DIR` | _(auto-discover)_ | Manually point to your WorldBox install |
| `WORLDBOX_MCP_LOG` | `info` | `debug`, `info`, `warning`, `error` |

## Capability discovery

After connecting, the first thing your client should do is call the `capabilities()` tool. It returns:

```json
{
  "mod_version": "0.6.0",
  "worldbox_version": "0.x.x",
  "unity_version": "2022.3.60f1",
  "assembly_csharp_sha256": "…",
  "commands": [ { "name": "...", "category": "...", "description": "...", "schema": { ... } }, ... ]
}
```

This lets you handle WorldBox updates gracefully: commands that lose backing support disappear from the list. The Python MCP server already calls this at startup and turns each command into a typed MCP tool.
