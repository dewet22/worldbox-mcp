"""End-to-end smoke test for the v0.3 multi-agent layer.

Spins up two :class:`BridgeClient` instances (one per agent), each authenticating with its
own bearer token, against a single running ``WorldBoxBridge``. Walks through the canonical
multi-agent flow: identity, session introspection, fog-of-war, message bus, scoreboard.
Prints a tight report so you can see at a glance whether anything regressed.

Prerequisite: the game is running with ``BepInEx/config/WorldBoxBridge.agents.json``
defining at least the two tokens declared below. The simplest setup is to deploy
``examples/scenarios/multi-agent/pvp.json`` and replace the placeholder tokens with the
constants used here. (Don't ship those constants -- they're fixed only so this smoke is
reproducible from a clean checkout.)
"""

from __future__ import annotations

import asyncio
import json
import sys
from typing import Any

# Project root is two parents up (examples/scenarios/multi-agent/pvp_smoke.py -> repo root).
import os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "server", "src"))

from worldbox_mcp.client import BridgeClient  # noqa: E402
from worldbox_mcp.config import BridgeAddress  # noqa: E402
from worldbox_mcp.errors import BridgeError  # noqa: E402

# Fixed test tokens -- match the agents.json you deploy. Replace before sharing.
ATHENA_TOKEN = "athena-test-token-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
ARES_TOKEN = "ares-test-token-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"

BRIDGE = BridgeAddress(host="127.0.0.1", port=8723, token="<unused-baseline>")


def _line(label: str, payload: Any) -> None:
    pretty = json.dumps(payload, indent=2, default=str) if isinstance(payload, (dict, list)) else str(payload)
    print(f"\n-- {label}")
    for ln in pretty.splitlines():
        print(f"    {ln}")


async def _expect_denied(client: BridgeClient, command: str, agent_label: str, **args: Any) -> None:
    try:
        await client.call(command, args)
    except BridgeError as exc:
        print(f"  [OK] {agent_label} -> {command}: rejected with {exc.code}")
        return
    print(f"  [FAIL] {agent_label} -> {command} unexpectedly SUCCEEDED - expected a permission rejection")
    sys.exit(1)


async def main() -> int:
    print("=" * 72)
    print(" worldbox-mcp v0.3 multi-agent end-to-end smoke (PvP scenario)")
    print("=" * 72)

    # Each client wraps the SAME bridge but with a different token -- that's the whole
    # multi-tenant story right there.
    async with BridgeClient(BridgeAddress(BRIDGE.host, BRIDGE.port, ATHENA_TOKEN)) as athena, \
               BridgeClient(BridgeAddress(BRIDGE.host, BRIDGE.port, ARES_TOKEN))   as ares:

        # 1. Identity
        ath_who = await athena.call("whoami")
        ar_who = await ares.call("whoami")
        _line("athena.whoami", ath_who)
        _line("ares.whoami",   ar_who)
        assert ath_who["agent_id"] == "athena", f"expected agent_id=athena, got {ath_who['agent_id']!r}"
        assert ar_who["agent_id"] == "ares",    f"expected agent_id=ares,   got {ar_who['agent_id']!r}"
        assert ath_who["role"] == "faction_player"
        assert ar_who["role"] == "faction_player"

        # 2. Session
        session = await athena.call("session_info")
        _line("session_info (via athena)", session)
        assert session["scenario"] == "pvp"
        assert session["partial_intel"] is True
        assert {a["id"] for a in session["agents"]} >= {"athena", "ares"}

        # 3. Inter-agent message -- idempotent against earlier runs by scoping recv to seqs
        # produced AFTER this turn's send. The MessageBus is in-memory and accumulates
        # across runs of the smoke until the game restarts.
        ack = await ares.call("recv_messages", {"max": 1})
        baseline_seq = ack.get("next_cursor", 0)
        snd = await athena.call("send_message", {"to": "ares", "kind": "diplomacy", "content": "i propose a non-aggression pact"})
        _line("athena sends to ares", snd)
        assert snd["seq"] > baseline_seq

        recv = await ares.call("recv_messages", {"since_seq": baseline_seq})
        _line(f"ares.recv_messages (since_seq={baseline_seq})", recv)
        assert recv["count"] >= 1
        latest = recv["items"][-1]
        assert latest["from"] == "athena", f"latest from {latest['from']!r}, expected 'athena'"
        assert "non-aggression" in latest["content"]

        # 4. Permission boundaries -- these MUST be denied
        print("\n-- permission boundaries (athena = FactionPlayer):")
        await _expect_denied(athena, "screenshot",  "athena")
        await _expect_denied(athena, "pause",       "athena")
        await _expect_denied(athena, "paint_tile",  "athena", x=0, y=0, tile_id="sand")
        await _expect_denied(athena, "send_message", "athena", to="*", content="propaganda")  # broadcast denied

        # 5. Scoreboard
        scoreboard = await athena.call("objective_status")
        _line("objective_status (via athena)", scoreboard)
        assert scoreboard["scenario"] == "pvp"
        assert any(a["agent_id"] == "athena" for a in scoreboard["agents"])

    print("\n" + "=" * 72)
    print(" all checks passed -- multi-agent v0.3 is wired correctly")
    print("=" * 72)
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
