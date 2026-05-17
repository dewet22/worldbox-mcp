"""Action tools — modify the world.

Three primitives cover the full action surface:

* ``worldbox_invoke_power`` — universal GodPower trigger (most spawns + every disaster).
* ``worldbox_spawn`` — actor spawn for entries that aren't in the PowerLibrary
  (dragons, kraken, cthulhu, …).
* ``worldbox_paint_tile`` — direct tile-type modification with optional radius.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from worldbox_mcp.client import BridgeClient


def register(server: FastMCP, client: BridgeClient) -> None:
    @server.tool(
        name="worldbox_invoke_power",
        description=(
            "Invokes any GodPower on a tile. Universal action: covers every spawn-by-race "
            "(by passing a race id like 'human'), every disaster (meteorite, nuke, plague, "
            "lightning, tsunami, earthquake, …), every toggle (peace, civilization, …) and "
            "every modifier the in-game god-mode UI exposes. Discover valid power_id values "
            "via `worldbox_list_powers`. x/y are tile coordinates within the map. Returns "
            "`{power_id, x, y, accepted}` where `accepted=false` means the game's logic "
            "refused the action."
        ),
    )
    async def worldbox_invoke_power(power_id: str, x: int, y: int) -> dict[str, Any]:
        return await client.call("invoke_power", {"power_id": power_id, "x": x, "y": y})

    @server.tool(
        name="worldbox_spawn",
        description=(
            "Spawns one or more actors of a given asset id at (x, y). Use this for "
            "creatures NOT exposed as GodPowers — dragons, kraken, cthulhu, demons, "
            "titans, specific animals. Discover ids via `worldbox_list_actors`. The game "
            "auto-assigns the actor's wild kingdom from ActorAsset.kingdom_id_wild — no "
            "kingdom argument needed. count must be 1..100. If the actor can't survive the "
            "terrain (e.g. land animal on water), the game silently refuses and the call "
            "reports failed > 0."
        ),
    )
    async def worldbox_spawn(
        entity_id: str,
        x: int,
        y: int,
        count: int = 1,
        adult: bool = False,
        spawn_height: float = 6.0,
    ) -> dict[str, Any]:
        return await client.call(
            "spawn",
            {
                "entity_id": entity_id,
                "x": x,
                "y": y,
                "count": count,
                "adult": adult,
                "spawn_height": spawn_height,
            },
        )

    @server.tool(
        name="worldbox_paint_tile",
        description=(
            "Paints a single tile or a disc of tiles. tile_id changes the main ground "
            "type (water, lava, sand, soil_low, …); optional top_id sets the top "
            "decoration (forests, roads, wasteland, …). Discover valid ids via "
            "`worldbox_list_tiles`. With radius > 0, paints a Euclidean disc of `radius` "
            "cells. Out-of-map cells are skipped silently. Returns `{painted, skipped}`."
        ),
    )
    async def worldbox_paint_tile(
        x: int,
        y: int,
        tile_id: str | None = None,
        top_id: str | None = None,
        radius: int = 0,
    ) -> dict[str, Any]:
        args: dict[str, Any] = {"x": x, "y": y, "radius": radius}
        if tile_id is not None:
            args["tile_id"] = tile_id
        if top_id is not None:
            args["top_id"] = top_id
        return await client.call("paint_tile", args)
