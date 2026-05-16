"""Meta tools: ``worldbox_health`` and ``worldbox_capabilities``.

Phase 1 only exposes ``worldbox_health``. The discovery/action/read/control tools land in
later phases as the corresponding mod-side commands ship.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from ..client import BridgeClient


def register(server: "FastMCP", client: "BridgeClient") -> None:
    """Register meta tools onto ``server``.

    Tool names use the ``worldbox_*`` prefix so they don't collide with tools from other
    MCP servers that an agent may also have connected.
    """

    @server.tool(
        name="worldbox_health",
        description=(
            "Probe the WorldBoxBridge mod. Returns plugin liveness, mod version, "
            "WorldBox version, Unity version, the SHA256 of Assembly-CSharp.dll, "
            "and the most recent main-thread tick. Call this first — its return value "
            "tells you whether the bridge is reachable and what game build you're "
            "talking to."
        ),
    )
    async def worldbox_health() -> dict[str, Any]:
        return await client.health()

    @server.tool(
        name="worldbox_capabilities",
        description=(
            "Returns the full list of commands the WorldBoxBridge mod currently exposes, "
            "with their JSON-Schema. The mod publishes this list dynamically — when "
            "WorldBox is updated, commands that lose backing support disappear from here. "
            "Use this when an agent wants to discover what's actually available rather "
            "than assuming."
        ),
    )
    async def worldbox_capabilities() -> dict[str, Any]:
        return await client.capabilities()

    @server.tool(
        name="worldbox_whoami",
        description=(
            "Returns this client's identity in the current WorldBox session: agent_id, "
            "role (god / faction_player / observer / narrator), the kingdom id this agent "
            "controls (or null in sandbox mode), the permission flags it holds, the active "
            "scenario preset, and whether partial_intel / fog-of-war is enabled. Call "
            "after worldbox_health to discover what this client is allowed to do. In "
            "legacy single-token deployments returns role='god' with no kingdom claim."
        ),
    )
    async def worldbox_whoami() -> dict[str, Any]:
        return await client.call("whoami")

    @server.tool(
        name="worldbox_session_info",
        description=(
            "Returns the live multi-agent session: scenario preset (pvp / coop / "
            "hierarchical / sandbox), partial_intel + turn_based flags, and the list of "
            "all registered agents (id, role, claimed kingdom, last_seen). Tokens are "
            "never exposed. Use this to discover the other agents on the same world."
        ),
    )
    async def worldbox_session_info() -> dict[str, Any]:
        return await client.call("session_info")
