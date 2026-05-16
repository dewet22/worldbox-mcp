"""Read tools — observe the world.

The agent needs to look before it acts. These tools cover dimensions, snapshots,
queries, and screenshots so the agent can plan actions based on real state instead of
guesswork.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from ..client import BridgeClient


def register(server: "FastMCP", client: "BridgeClient") -> None:
    @server.tool(
        name="worldbox_get_world_state",
        description=(
            "Returns the world snapshot: dimensions (width, height), seed, current tick, "
            "paused flag, populations (alive + lifetime), kingdoms_alive, cities_alive. "
            "Call this first to size other queries and to detect whether the simulation "
            "is running."
        ),
    )
    async def worldbox_get_world_state() -> dict[str, Any]:
        return await client.call("get_world_state")

    @server.tool(
        name="worldbox_get_tile",
        description=(
            "Returns the tile at (x, y): tile_id, top_id, height, and the names of actors "
            "standing on it. Returns OUT_OF_BOUNDS when (x, y) is off the map."
        ),
    )
    async def worldbox_get_tile(x: int, y: int) -> dict[str, Any]:
        return await client.call("get_tile", {"x": x, "y": y})

    @server.tool(
        name="worldbox_list_kingdoms",
        description=(
            "Returns every kingdom currently alive: id, name, race, king name, capital "
            "city id, cities_count, units_count. By default wild kingdoms (animal packs, "
            "sea monsters) are filtered out — pass include_wild=true to see them."
        ),
    )
    async def worldbox_list_kingdoms(include_wild: bool = False) -> dict[str, Any]:
        return await client.call("list_kingdoms", {"include_wild": include_wild})

    @server.tool(
        name="worldbox_list_cities",
        description=(
            "Returns every city alive: id, name, kingdom_id, kingdom_name, leader_name, "
            "building_count, unit_count. Optionally filter by kingdom_id."
        ),
    )
    async def worldbox_list_cities(kingdom_id: int | None = None) -> dict[str, Any]:
        args: dict[str, Any] = {}
        if kingdom_id is not None:
            args["kingdom_id"] = kingdom_id
        return await client.call("list_cities", args)

    @server.tool(
        name="worldbox_query_actors",
        description=(
            "Walks every Actor in the simulation and filters by race / kingdom_id / "
            "bounding rect / alive status, with pagination. Use this to count or sample "
            "populations without dumping the whole world. Default limit=500, max=5000. "
            "Pass offset for pagination. Returns `{items, matched, returned, has_more}`."
        ),
    )
    async def worldbox_query_actors(
        race: str | None = None,
        kingdom_id: int | None = None,
        in_rect: dict[str, int] | None = None,
        alive: bool = True,
        limit: int = 500,
        offset: int = 0,
    ) -> dict[str, Any]:
        args: dict[str, Any] = {"alive": alive, "limit": limit, "offset": offset}
        if race is not None:
            args["race"] = race
        if kingdom_id is not None:
            args["kingdom_id"] = kingdom_id
        if in_rect is not None:
            args["in_rect"] = in_rect
        return await client.call("query_actors", args)

    @server.tool(
        name="worldbox_screenshot",
        description=(
            "Captures the current game framebuffer as a base64-encoded PNG. Useful so "
            "the agent can see what it just did before deciding the next move. Returns "
            "`{format, width, height, base64, bytes}`. The image is the most recently "
            "completed frame."
        ),
    )
    async def worldbox_screenshot() -> dict[str, Any]:
        return await client.call("screenshot")
