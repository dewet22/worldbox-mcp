"""End-to-end smoke tests against a real running WorldBox + mod.

Skipped by default. Pass ``--run-e2e`` to enable. Requires:

* WorldBox launched with the WorldBoxBridge plugin loaded (`BepInEx/LogOutput.log` shows
  "listening on 127.0.0.1:<port>").
* Either auto-discovery succeeds or you export ``WORLDBOX_MCP_TOKEN``.
"""

from __future__ import annotations

import pytest

from worldbox_mcp.client import BridgeClient
from worldbox_mcp.config import load_settings


@pytest.mark.e2e
async def test_health_against_real_bridge() -> None:
    settings = load_settings()
    async with BridgeClient(settings.bridge) as client:
        health = await client.health()
    assert health["mod_version"]
    assert health["unity_version"].startswith("2022.3")
    assert health["assembly_csharp_sha256"]
    assert isinstance(health["tick"], int)


@pytest.mark.e2e
async def test_capabilities_reports_health_command() -> None:
    settings = load_settings()
    async with BridgeClient(settings.bridge) as client:
        caps = await client.capabilities()
    names = {c["name"] for c in caps["commands"]}
    assert "health" in names
