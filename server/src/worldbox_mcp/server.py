"""Server factory.

Builds a fully wired :class:`MCPServer` instance: configuration, HTTP client, all registered
tools. The factory is decoupled from transport selection so tests (and the ``--self-check``
mode) can instantiate the server without binding any I/O.
"""

from __future__ import annotations

from typing import TYPE_CHECKING

import structlog

from .client import BridgeClient
from .tools import action, bus, control, discovery, meta, read

if TYPE_CHECKING:
    from mcp.server.mcpserver import MCPServer

    from .config import Settings

logger = structlog.get_logger(__name__)


def build_server(settings: Settings) -> tuple[MCPServer, BridgeClient]:
    """Construct the MCP server and the bridge client it owns.

    Returns both so the caller can keep the client lifecycle tied to the server's
    transport loop. Closing the client after ``server.run()`` returns is the caller's
    responsibility.
    """
    # Lazy import to keep `--self-check` fast (no MCP framework import unless we actually
    # need to register tools).
    from mcp.server.mcpserver import MCPServer

    server = MCPServer(
        name="worldbox-mcp",
        instructions=(
            "Tools for controlling and inspecting a running WorldBox game. Call "
            "`worldbox_health` first to verify the bridge is reachable, then "
            "`worldbox_capabilities` to discover what commands the current mod build "
            "supports. Asset identifiers (tile/actor/power ids) come from the in-game "
            "registry — never assume them; enumerate via the discovery tools."
        ),
    )

    client = BridgeClient(settings.bridge)
    logger.info(
        "server.build",
        bridge_url=settings.bridge.base_url,
        worldbox_dir=str(settings.worldbox_dir) if settings.worldbox_dir else None,
    )

    meta.register(server, client)
    discovery.register(server, client)
    action.register(server, client)
    read.register(server, client)
    control.register(server, client)
    bus.register(server, client)

    return server, client
