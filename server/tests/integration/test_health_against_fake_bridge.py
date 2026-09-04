"""Integration test: spin up a fake mod (aiohttp) on a free port, point a real BridgeClient at
it, and exercise the full /health path.

No MCP framework is needed at this level — the contract under test is the HTTP boundary.
The MCP-layer wiring is covered by :mod:`tests.unit.test_client` plus the self-check at
``--self-check`` runtime.
"""

from __future__ import annotations

import asyncio
import socket
from typing import TYPE_CHECKING

import pytest
from aiohttp import web

from worldbox_mcp.client import BridgeClient
from worldbox_mcp.config import BridgeAddress

if TYPE_CHECKING:
    from collections.abc import AsyncIterator


def _free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


# A valid 1x1 transparent PNG; what the mod would hand back after encoding.
_ONE_PIXEL_PNG_B64 = (
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
    "AAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="
)


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
        if body.get("name") == "set_speed":
            # Mirror the real bridge's UNKNOWN_ASSET envelope, including did_you_mean.
            return web.json_response(
                {
                    "ok": False,
                    "error": {
                        "code": "UNKNOWN_ASSET",
                        "message": "speed_id 'bogus' is not a registered WorldTimeScaleAsset.",
                        "command": "set_speed",
                        "args": body.get("args"),
                        "did_you_mean": ["x1", "x10", "x15", "x2", "x20"],
                    },
                },
                status=400,
            )
        if body.get("name") == "screenshot":
            return web.json_response(
                {
                    "ok": True,
                    "result": {
                        "format": "png",
                        "width": 1,
                        "height": 1,
                        "source_width": 2,
                        "source_height": 2,
                        "base64": _ONE_PIXEL_PNG_B64,
                        "bytes": 67,
                    },
                    "tick": 125,
                }
            )
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
    _bridge, address = fake_bridge
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
    assert method == "POST"
    assert path == "/cmd"
    assert body == {"name": "noop", "args": {"a": 1, "b": "two"}}


async def test_concurrent_requests_share_client(
    fake_bridge: tuple[FakeBridge, BridgeAddress],
) -> None:
    _, address = fake_bridge
    async with BridgeClient(address) as client:
        results = await asyncio.gather(*[client.health() for _ in range(10)])
    assert all(r["mod_version"] == "0.1.0" for r in results)


async def test_bridge_error_reaches_mcp_client_with_hints(
    fake_bridge: tuple[FakeBridge, BridgeAddress],
) -> None:
    """mcp 2.x only forwards ToolError subclasses to the model; anything else is masked as
    'Error executing tool <name>'. A bridge rejection must therefore surface as a ToolError
    carrying the code, the message and the did_you_mean hints."""
    from mcp.server.mcpserver.exceptions import ToolError, UnexpectedToolError

    from worldbox_mcp.config import Settings
    from worldbox_mcp.server import build_server

    _bridge, address = fake_bridge
    server, client = build_server(Settings(bridge=address, worldbox_dir=None))
    try:
        with pytest.raises(ToolError) as info:
            await server.call_tool("worldbox_set_speed", {"speed_id": "bogus"})
    finally:
        await client.aclose()
    assert not isinstance(info.value, UnexpectedToolError)
    text = str(info.value)
    assert "UNKNOWN_ASSET" in text
    assert "not a registered WorldTimeScaleAsset" in text
    assert "x10" in text  # did_you_mean survives the trip


async def test_transport_error_reaches_mcp_client(
    bridge_address: BridgeAddress,
) -> None:
    """Nothing listening on the port: the model should read 'unreachable', not a masked crash."""
    from mcp.server.mcpserver.exceptions import ToolError, UnexpectedToolError

    from worldbox_mcp.config import Settings
    from worldbox_mcp.server import build_server

    server, client = build_server(Settings(bridge=bridge_address, worldbox_dir=None))
    try:
        with pytest.raises(ToolError) as info:
            await server.call_tool("worldbox_health", {})
    finally:
        await client.aclose()
    assert not isinstance(info.value, UnexpectedToolError)
    assert "unreachable" in str(info.value).lower()


async def test_screenshot_returns_image_block_and_metadata(
    fake_bridge: tuple[FakeBridge, BridgeAddress],
) -> None:
    """The model should receive the picture as an MCP image block (so vision-capable clients
    render it) plus a small JSON block with the dimensions -- never base64 inside JSON text."""
    import json

    from mcp.types import ImageContent, TextContent

    from worldbox_mcp.config import Settings
    from worldbox_mcp.server import build_server

    bridge, address = fake_bridge
    server, client = build_server(Settings(bridge=address, worldbox_dir=None))
    try:
        result = await server.call_tool("worldbox_screenshot", {"max_dimension": 64})
    finally:
        await client.aclose()

    assert not result.is_error
    images = [c for c in result.content if isinstance(c, ImageContent)]
    texts = [c for c in result.content if isinstance(c, TextContent)]
    assert len(images) == 1
    assert images[0].mime_type == "image/png"
    assert images[0].data == _ONE_PIXEL_PNG_B64
    assert len(texts) == 1
    meta = json.loads(texts[0].text)
    assert meta["width"] == 1
    assert meta["source_width"] == 2
    assert "base64" not in meta

    _method, _path, body = bridge.calls[-1]
    assert body["name"] == "screenshot"
    assert body["args"] == {"max_dimension": 64, "format": "jpg", "quality": 80}


@pytest.mark.parametrize(
    ("tool", "command"),
    [
        ("worldbox_get_ui_state", "get_ui_state"),
        ("worldbox_dismiss_window", "dismiss_window"),
        ("worldbox_list_speeds", "list_speeds"),
    ],
)
async def test_ui_tools_forward_to_bridge_commands(
    fake_bridge: tuple[FakeBridge, BridgeAddress], tool: str, command: str
) -> None:
    from worldbox_mcp.config import Settings
    from worldbox_mcp.server import build_server

    bridge, address = fake_bridge
    server, client = build_server(Settings(bridge=address, worldbox_dir=None))
    try:
        result = await server.call_tool(tool, {})
    finally:
        await client.aclose()
    assert not result.is_error
    _method, _path, body = bridge.calls[-1]
    assert body == {"name": command, "args": {}}
