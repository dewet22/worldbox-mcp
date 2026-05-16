"""Read tools: read-only inspection of the world.

Lands in Phase 2:
    * ``get_world_state``, ``get_tile``, ``get_actor``
    * ``list_kingdoms``, ``list_cities``, ``query_actors``
    * ``screenshot``
"""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from ..client import BridgeClient


def register(server: "FastMCP", client: "BridgeClient") -> None:  # noqa: ARG001
    """No-op until Phase 2."""
    return
