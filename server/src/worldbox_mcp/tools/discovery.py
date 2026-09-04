"""Discovery tools: enumerate the in-game asset registry.

The mod-side ``list_*`` commands introspect ``AssetManager`` at runtime, so the
returned ids stay correct across WorldBox updates without us having to recompile.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from mcp.server.mcpserver import MCPServer

    from worldbox_mcp.client import BridgeClient


def register(server: MCPServer, client: BridgeClient) -> None:
    """Register the discovery tools onto ``server``."""

    @server.tool(
        name="worldbox_list_tiles",
        description=(
            "Lists every TileType currently registered in the running WorldBox build. "
            "Returns `{items: [{id, color_hex?, has_biome_tags?, ...}], count}`. "
            "Tile ids are the valid inputs for `worldbox_paint_tile` (Phase 3). "
            "Call this once per session to learn what's available — the catalog can "
            "change with WorldBox updates and modder-added tiles."
        ),
    )
    async def worldbox_list_tiles() -> dict[str, Any]:
        return await client.call("list_tiles")

    @server.tool(
        name="worldbox_list_actors",
        description=(
            "Lists every ActorAsset (creature / race / animal / monster / mythical) "
            "currently registered. Returns `{items: [{id, race?, asset_type?, ...}], "
            "count}`. Actor ids are the valid inputs for `worldbox_spawn` (Phase 3). "
            "Examples on stock WorldBox 0.51.x include `human`, `elf`, `orc`, `dwarf`, "
            "`wolf`, `bear`, `dragon_red`, `cthulhu`, plus ~300 more."
        ),
    )
    async def worldbox_list_actors() -> dict[str, Any]:
        return await client.call("list_actors")

    @server.tool(
        name="worldbox_list_speeds",
        description=(
            "Lists every simulation speed (WorldTimeScaleAsset) in the running build with its "
            "`multiplier`, and reports the active one as `current`. Returns `{items: [{id, "
            "multiplier, ticks?, ...}], count, current}`. The ids are the only valid inputs "
            "for `worldbox_set_speed`; on stock WorldBox 0.51.x they are `slow_mo`, `x1`, "
            "`x2`, `x3`, `x4`, `x5`, `x10`, `x15`, `x20`, `x40`."
        ),
    )
    async def worldbox_list_speeds() -> dict[str, Any]:
        return await client.call("list_speeds")

    @server.tool(
        name="worldbox_list_powers",
        description=(
            "Lists every PowerAsset (god-mode action) registered in the running game. "
            "Returns `{items: [{id, tab_id?, target_type?, ...}], count}`. Powers cover "
            "spawn buttons, disasters (meteor, nuke, plague, …), toggles "
            "(toggle_peace, toggle_civ, …), and modifiers. Power ids are the valid "
            "inputs for `worldbox_invoke_power` (Phase 3)."
        ),
    )
    async def worldbox_list_powers() -> dict[str, Any]:
        return await client.call("list_powers")
