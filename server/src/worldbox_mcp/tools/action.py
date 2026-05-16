"""Action tools — the three generic primitives covering 100% of WorldBox actions.

Lands in Phase 3:
    * ``paint_tile(x, y, tile_id, radius=0)``
    * ``spawn(entity_id, x, y, count=1, kingdom_id=None)``
    * ``invoke_power(power_id, x=None, y=None, args=None)``
"""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from ..client import BridgeClient


def register(server: "FastMCP", client: "BridgeClient") -> None:  # noqa: ARG001
    """No-op until Phase 3."""
    return
