"""Transport selection: stdio (default) or Streamable HTTP.

stdio is the MCP standard for desktop AI clients (Claude Code, Cursor, Codex, …). HTTP
exists for web clients, agents that can't spawn subprocesses, or one-off curl debugging.
"""

from __future__ import annotations

import asyncio
from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from mcp.server.mcpserver import MCPServer

    from .client import BridgeClient


@dataclass(frozen=True, slots=True)
class TransportConfig:
    kind: str = "stdio"  # "stdio" or "http"
    host: str = "127.0.0.1"
    port: int = 7800


async def run(
    server: MCPServer,
    client: BridgeClient,
    transport: TransportConfig,
) -> None:
    """Run the server until shutdown, then close the bridge client."""
    try:
        if transport.kind == "stdio":
            await server.run_stdio_async()
        elif transport.kind == "http":
            await server.run_streamable_http_async(host=transport.host, port=transport.port)
        else:
            msg = f"Unknown transport kind: {transport.kind!r}"
            raise ValueError(msg)
    finally:
        await asyncio.shield(client.aclose())
