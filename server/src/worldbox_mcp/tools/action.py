"""Action tools — modify the world.

The single ``invoke_power`` primitive covers most of WorldBox's action surface because the
game itself uses a unified `GodPower` model: spawning a race, triggering a meteor, toggling
peace and so on are all just powers in the game's UI. We expose that uniform layer to the
agent.

For Phase 3 we also provide convenience wrappers ``paint_tile`` and ``spawn`` that funnel
into the same primitive but with explicit, schema-validated arguments. They make agent code
clearer than passing an arbitrary power id around.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from ..client import BridgeClient


def register(server: "FastMCP", client: "BridgeClient") -> None:
    @server.tool(
        name="worldbox_invoke_power",
        description=(
            "Invokes any GodPower on a tile. Universal action: covers every spawn "
            "(by passing a race id), every disaster (meteor, nuke, plague, lightning, "
            "tsunami, earthquake, …), every toggle (peace, civilization, …) and every "
            "modifier the in-game god-mode UI exposes. Discover valid power_id values "
            "via `worldbox_list_powers`. x/y are tile coordinates within the map "
            "(0 ≤ x < width, 0 ≤ y < height). Returns `{power_id, x, y, accepted}` "
            "where `accepted=false` means the game's logic refused the action "
            "(e.g. you can't paint lava on the world edge)."
        ),
    )
    async def worldbox_invoke_power(power_id: str, x: int, y: int) -> dict[str, Any]:
        return await client.call("invoke_power", {"power_id": power_id, "x": x, "y": y})
