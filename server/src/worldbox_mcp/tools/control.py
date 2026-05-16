"""Control tools — simulation flow + world lifecycle.

Six tools that let an agent manage the simulation as a whole: pause/resume + speed
control for time management, generate/save/load for world lifecycle.
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

    @server.tool(
        name="worldbox_generate_world",
        description=(
            "Regenerates the world map. All kingdoms / cities / actors are wiped. Optional "
            "zone_x and zone_y set the map size in 64-tile zones (default 4x4 = 256x256, "
            "max 16x16 = 1024x1024). Generation runs asynchronously over many frames; the "
            "response means 'scheduled', not 'ready' — poll worldbox_get_world_state until "
            "tick advances to know when it's done."
        ),
    )
    async def worldbox_generate_world(
        zone_x: int | None = None,
        zone_y: int | None = None,
    ) -> dict[str, Any]:
        args: dict[str, Any] = {}
        if zone_x is not None:
            args["zone_x"] = zone_x
        if zone_y is not None:
            args["zone_y"] = zone_y
        return await client.call("generate_world", args)

    @server.tool(
        name="worldbox_save_world",
        description=(
            "Saves the current world to disk via the game's native save format. `folder` is "
            "required (absolute path). Save files are compatible with the in-game load UI. "
            "Fails with GAME_REJECTED if no world is currently loaded."
        ),
    )
    async def worldbox_save_world(folder: str, compress: bool = True) -> dict[str, Any]:
        return await client.call("save_world", {"folder": folder, "compress": compress})

    @server.tool(
        name="worldbox_load_world",
        description=(
            "Loads a previously-saved world. Provide either `path` (absolute path to a save "
            "file on disk) or `bytes_b64` (base64-encoded zipped save bytes). Like "
            "generate_world the load runs asynchronously — poll worldbox_get_world_state "
            "until tick advances."
        ),
    )
    async def worldbox_load_world(
        path: str | None = None,
        bytes_b64: str | None = None,
    ) -> dict[str, Any]:
        args: dict[str, Any] = {}
        if path is not None:
            args["path"] = path
        if bytes_b64 is not None:
            args["bytes_b64"] = bytes_b64
        return await client.call("load_world", args)
