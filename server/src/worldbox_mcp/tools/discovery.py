"""Discovery tools: ``list_tiles``, ``list_actors``, ``list_powers``.

These tools introspect the in-game asset registry at runtime, so an agent never needs to
hardcode asset ids that may differ between WorldBox versions.

Lands in Phase 2 — for Phase 1 this module is intentionally empty.
"""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from ..client import BridgeClient


def register(server: "FastMCP", client: "BridgeClient") -> None:  # noqa: ARG001
    """No-op until Phase 2 ships the mod-side discovery commands."""
    return
