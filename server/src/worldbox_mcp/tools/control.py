"""Control tools: simulation flow + world ops.

Lands in Phase 3:
    * ``pause``, ``resume``, ``set_speed``, ``time_skip``
    * ``generate_world``, ``save_world``, ``load_world``
    * ``camera_goto``
"""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from ..client import BridgeClient


def register(server: "FastMCP", client: "BridgeClient") -> None:  # noqa: ARG001
    """No-op until Phase 3."""
    return
