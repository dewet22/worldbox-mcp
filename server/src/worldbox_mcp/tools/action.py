"""Action tools, modify the world.

Three primitives cover the full action surface:

* ``worldbox_invoke_power``, universal GodPower trigger (most spawns + every disaster).
* ``worldbox_spawn``, actor spawn for entries that aren't in the PowerLibrary
  (dragons, kraken, cthulhu, ...).
* ``worldbox_paint_tile``, direct tile-type modification with optional radius.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from mcp.server.mcpserver import MCPServer

    from worldbox_mcp.client import BridgeClient


def register(server: MCPServer, client: BridgeClient) -> None:
    @server.tool(
        name="worldbox_invoke_power",
        description=(
            "Invokes any GodPower on a tile. Universal action: covers every spawn-by-race "
            "(by passing a race id like 'human'), every disaster (meteorite, nuke, plague, "
            "lightning, tsunami, earthquake, ...), area drops (rain, fire, lava, ...), every "
            "toggle (peace, civilization, ...) and every modifier the in-game god-mode UI "
            "exposes. Discover valid power_id values via `worldbox_list_powers`. x/y are tile "
            "coordinates within the map. Optional radius (1-50) applies the power over a "
            "circular brush of that radius, only for powers flagged `supports_radius` in "
            "list_powers; radius on any other power is rejected with GAME_REJECTED. Without "
            "radius, brush-only powers run at a minimal radius-1 brush, and powers flagged "
            "`is_toggle` flip their global state (x/y ignored but must be in-bounds). Returns "
            "`{power_id, x, y, accepted, via}` plus `{radius, brush}` when a brush was used. "
            "Optional pulses (1-200) applies the power once per game frame that many times, "
            "the equivalent of holding the mouse button (~60 pulses/s, so one click of rain "
            "is barely a drizzle; a storm is pulses=60+); with x2/y2 the pulses sweep from "
            "(x, y) to (x2, y2) like a click-hold-drag. Multi-pulse calls return "
            "`{pulses, accepted_count, pulses_applied}` instead of `accepted` and take "
            "pulses/60 seconds; a run stops early with `stopped: turn_ended | world_changed "
            "| deadline | error` when the caller's turn ends, the world is replaced mid-run, "
            "the 25s budget runs out, or a pulse throws (error_code/error_message then carry "
            "the failure). "
            "`accepted=false` means the game declined this time (drop-style powers such as "
            "rain or bombs roll a chance, so retry). Powers that need live mouse/drag state "
            "(e.g. 'finger') are rejected with GAME_REJECTED and a reason, use "
            "worldbox_paint_tile / worldbox_spawn instead. In a multi-agent session this "
            "needs the global action scope (God role), same as worldbox_paint_tile: god "
            "powers are map-wide and cannot be scoped to a faction. A FactionPlayer places "
            "creatures with worldbox_spawn instead."
        ),
    )
    async def worldbox_invoke_power(
        power_id: str,
        x: int,
        y: int,
        radius: int | None = None,
        pulses: int | None = None,
        x2: int | None = None,
        y2: int | None = None,
    ) -> dict[str, Any]:
        args: dict[str, Any] = {"power_id": power_id, "x": x, "y": y}
        if radius is not None:
            args["radius"] = radius
        if pulses is not None:
            args["pulses"] = pulses
        if x2 is not None:
            args["x2"] = x2
        if y2 is not None:
            args["y2"] = y2
        return await client.call("invoke_power", args)

    @server.tool(
        name="worldbox_spawn",
        description=(
            "Spawns one or more actors of a given asset id at (x, y). Use this for "
            "creatures NOT exposed as GodPowers, dragons, kraken, cthulhu, demons, "
            "titans, specific animals. Discover ids via `worldbox_list_actors`. The game "
            "auto-assigns the actor's wild kingdom from ActorAsset.kingdom_id_wild, no "
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
            "type (water, lava, sand, soil_low, ...); optional top_id sets the top "
            "decoration (forests, roads, wasteland, ...). Discover valid ids via "
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
