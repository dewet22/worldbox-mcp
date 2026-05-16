"""Control tools — simulation flow.

These three are the minimum an agent needs to run multi-step scenarios reliably:
pause the world while you set things up, then resume at the speed you want.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from ..client import BridgeClient


def register(server: "FastMCP", client: "BridgeClient") -> None:
    @server.tool(
        name="worldbox_pause",
        description=(
            "Pauses the simulation. Call this before building a complex scenario so the "
            "world doesn't drift while you set things up. Returns the previous paused "
            "state so you can detect whether you actually changed anything."
        ),
    )
    async def worldbox_pause() -> dict[str, Any]:
        return await client.call("pause")

    @server.tool(
        name="worldbox_resume",
        description=(
            "Resumes the simulation. Pair with worldbox_set_speed if you want the world "
            "to run faster than real-time (x2/x3/x5)."
        ),
    )
    async def worldbox_resume() -> dict[str, Any]:
        return await client.call("resume")

    @server.tool(
        name="worldbox_set_speed",
        description=(
            "Sets the simulation tick rate by WorldTimeScaleAsset id. Typical values: "
            "'slow_mo', 'x1', 'x2', 'x3', 'x5'. Higher values run the simulation faster "
            "so longer experiments take less wall-clock time. Wrong ids return "
            "UNKNOWN_ASSET with did_you_mean suggestions."
        ),
    )
    async def worldbox_set_speed(speed_id: str) -> dict[str, Any]:
        return await client.call("set_speed", {"speed_id": speed_id})
