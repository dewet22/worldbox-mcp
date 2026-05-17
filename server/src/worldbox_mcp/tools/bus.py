"""Inter-agent message bus tools — ``worldbox_send_message`` and ``worldbox_recv_messages``.

These let one AI agent talk to another (or to the whole table) through the bridge's
in-memory bus. Messages live until they age out under the bounded-inbox policy (default
200 messages / agent, drop-oldest). Nothing is persisted to disk in v0.3.

Typical usage in a PvP scenario:
- Agents periodically poll ``worldbox_recv_messages`` (e.g. every loop iteration) with the
  ``since_seq`` cursor from the previous response.
- Diplomatic moves: ``worldbox_send_message(to="other_agent", kind="diplomacy", content="...")``.
- Narrator broadcasts: ``worldbox_send_message(to="*", ...)`` — requires the send_broadcast
  permission (god / narrator roles only).
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from mcp.server.fastmcp import FastMCP

    from worldbox_mcp.client import BridgeClient


def register(server: FastMCP, client: BridgeClient) -> None:
    @server.tool(
        name="worldbox_send_message",
        description=(
            "Sends a message to another agent's inbox in the active WorldBox session, or "
            "broadcasts to everyone else with `to='*'`. Use this for coordination, "
            "diplomacy, threats, or narration between AI agents on the same world. "
            "Optional `kind` is a short categorization tag your peers can filter on (e.g. "
            "'diplomacy', 'alert', 'lore'). Broadcasts require the send_broadcast permission "
            "(god + narrator roles only); FactionPlayers can only send 1-to-1. Returns "
            "{seq, recipients, broadcast}."
        ),
    )
    async def worldbox_send_message(
        to: str, content: str, kind: str | None = None
    ) -> dict[str, Any]:
        args: dict[str, Any] = {"to": to, "content": content}
        if kind is not None:
            args["kind"] = kind
        return await client.call("send_message", args)

    @server.tool(
        name="worldbox_recv_messages",
        description=(
            "Polls this agent's inbox in the active WorldBox session. Non-destructive — "
            "messages remain until they age out of the bounded queue (default 200 per agent, "
            "drop-oldest). Pass `since_seq` (the highest `seq` from the previous response) "
            "to skip already-seen messages — that's the canonical cursor. `max` caps the "
            "batch size (default 50, hard ceiling 500). Returns {items, count, next_cursor} "
            "where each item has seq, from, to, kind, content, sent_utc."
        ),
    )
    async def worldbox_recv_messages(since_seq: int = 0, max: int = 50) -> dict[str, Any]:
        return await client.call("recv_messages", {"since_seq": since_seq, "max": max})
