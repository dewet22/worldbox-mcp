"""Integration test: spin up a fake mod (aiohttp) on a free port, point a real BridgeClient at
it, and exercise the full /health path.

No MCP framework is needed at this level — the contract under test is the HTTP boundary.
The MCP-layer wiring is covered by :mod:`tests.unit.test_client` plus the self-check at
``--self-check`` runtime.
"""

from __future__ import annotations

import asyncio
import socket
from collections.abc import AsyncIterator

import pytest
from aiohttp import web

from worldbox_mcp.client import BridgeClient
from worldbox_mcp.config import BridgeAddress


def _free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


class FakeBridge:
    def __init__(self, *, token: str = "fake-token") -> None:
        self.token = token
        self.calls: list[tuple[str, str, dict | None]] = []
        self.app = web.Application()
        self.app.router.add_get("/health", self._health)
        self.app.router.add_post("/cmd", self._cmd)

    async def _check_auth(self, request: web.Request) -> web.Response | None:
        # Mirror the real C# bridge: accept Authorization: Bearer <token> (v0.3 path)
        # OR the legacy X-WB-Token header (kept for v0.1/v0.2 single-tenant clients).
        presented: str | None = None
        auth = request.headers.get("Authorization", "")
        if auth.lower().startswith("bearer "):
            presented = auth[7:].strip()
        elif legacy := request.headers.get("X-WB-Token"):
            presented = legacy
        if presented != self.token:
            return web.json_response(
                {"ok": False, "error": {"code": "UNAUTHORIZED", "message": "bad token"}},
                status=401,
            )
        return None

    async def _health(self, request: web.Request) -> web.Response:
        bad = await self._check_auth(request)
        if bad is not None:
            return bad
        self.calls.append(("GET", "/health", None))
        return web.json_response(
            {
                "ok": True,
                "mod_version": "0.1.0",
                "worldbox_version": "test",
                "unity_version": "2022.3.60f1",
                "assembly_csharp_sha256": "deadbeef" * 8,
                "tick": 123,
                "enabled": True,
            }
        )

    async def _cmd(self, request: web.Request) -> web.Response:
        bad = await self._check_auth(request)
        if bad is not None:
            return bad
        body = await request.json()
        self.calls.append(("POST", "/cmd", body))
        return web.json_response({"ok": True, "result": {"echo": body}, "tick": 124})


@pytest.fixture
async def fake_bridge() -> AsyncIterator[tuple[FakeBridge, BridgeAddress]]:
    bridge = FakeBridge()
    port = _free_port()
    runner = web.AppRunner(bridge.app)
    await runner.setup()
    site = web.TCPSite(runner, host="127.0.0.1", port=port)
    await site.start()
    try:
        yield bridge, BridgeAddress(host="127.0.0.1", port=port, token=bridge.token)
    finally:
        await runner.cleanup()


async def test_health_round_trip(
    fake_bridge: tuple[FakeBridge, BridgeAddress],
) -> None:
    bridge, address = fake_bridge
    async with BridgeClient(address) as client:
        result = await client.health()
        assert result["mod_version"] == "0.1.0"
        assert result["tick"] == 123
    assert ("GET", "/health", None) in bridge.calls


async def test_auth_rejected(fake_bridge: tuple[FakeBridge, BridgeAddress]) -> None:
    bridge, address = fake_bridge
    wrong = BridgeAddress(host=address.host, port=address.port, token="WRONG")
    async with BridgeClient(wrong) as client:
        from worldbox_mcp.errors import BridgeError

        with pytest.raises(BridgeError) as info:
            await client.health()
        assert info.value.code == "UNAUTHORIZED"


async def test_command_payload_forwarded(
    fake_bridge: tuple[FakeBridge, BridgeAddress],
) -> None:
    bridge, address = fake_bridge
    async with BridgeClient(address) as client:
        out = await client.call("noop", {"a": 1, "b": "two"})
        assert out == {"echo": {"name": "noop", "args": {"a": 1, "b": "two"}}}
    method, path, body = bridge.calls[-1]
    assert method == "POST" and path == "/cmd"
    assert body == {"name": "noop", "args": {"a": 1, "b": "two"}}


async def test_concurrent_requests_share_client(
    fake_bridge: tuple[FakeBridge, BridgeAddress],
) -> None:
    _, address = fake_bridge
    async with BridgeClient(address) as client:
        results = await asyncio.gather(*[client.health() for _ in range(10)])
    assert all(r["mod_version"] == "0.1.0" for r in results)
