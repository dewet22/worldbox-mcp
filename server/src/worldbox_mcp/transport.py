"""Transport selection: stdio (default) or Streamable HTTP.

stdio is the MCP standard for desktop AI clients (Claude Code, Cursor, Codex, …). HTTP
exists for web clients, agents that can't spawn subprocesses, or one-off curl debugging.
"""

from __future__ import annotations

import asyncio
from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from .client import BridgeClient


@dataclass(frozen=True, slots=True)
class TransportConfig:
    kind: str = "stdio"  # "stdio" or "http"
    host: str = "127.0.0.1"
    port: int = 7800


async def run(
    server: FastMCP,
    client: BridgeClient,
    transport: TransportConfig,
) -> None:
    """Run the server until shutdown, then close the bridge client."""
    try:
        if transport.kind == "stdio":
            await server.run_stdio_async()
        elif transport.kind == "http":
            # FastMCP's HTTP transport is exposed via run_async on a Starlette app under
            # the hood. The exact entrypoint name has churned between MCP SDK versions;
            # we feature-detect to stay compatible across them.
            runner = getattr(server, "run_streamable_http_async", None) or getattr(
                server, "run_sse_async", None
            )
            if runner is None:
                msg = (
                    "This version of the mcp Python SDK does not expose a Streamable HTTP "
                    "transport. Upgrade `mcp` or use the default stdio transport."
                )
                raise RuntimeError(msg)
            # Most SDK variants accept (host, port) kwargs; if the signature is different,
            # fall back to setting them on the instance.
            try:
                await runner(host=transport.host, port=transport.port)
            except TypeError:
                # Older SDKs exposed FastMCP.settings.host/.port for transport config.
                # Newer SDKs may not -- the type: ignore covers both shapes.
                server.settings.host = transport.host
                server.settings.port = transport.port
                await runner()
        else:
            msg = f"Unknown transport kind: {transport.kind!r}"
            raise ValueError(msg)
    finally:
        await asyncio.shield(client.aclose())
