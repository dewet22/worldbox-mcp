# worldbox-mcp (Python MCP server)

The Python half of [worldbox-mcp](https://github.com/fullya99/worldbox-mcp). Distributed on PyPI.

```bash
uvx worldbox-mcp           # stdio transport (default)
uvx worldbox-mcp --http    # Streamable HTTP transport
uvx worldbox-mcp --help
```

See the [top-level README](../README.md) and the [docs site](https://fullya.me/worldbox-mcp/) for full installation instructions and client configuration recipes.

## Development

```bash
uv sync --all-extras
uv run pytest
uv run ruff check .
uv run mypy --strict src
```

## Architecture

This package is a thin, typed façade over the [`WorldBoxBridge`](../mod) HTTP API. It:

1. Speaks the [MCP protocol](https://modelcontextprotocol.io) to AI clients.
2. Validates tool inputs with Pydantic.
3. Translates each MCP `tools/call` into a `POST /cmd` request against the local mod.
4. Maps bridge error codes to MCP errors with full preservation of detail (no swallowing).

See [`docs/architecture.md`](../docs/architecture.md) for the full picture.
