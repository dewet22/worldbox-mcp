"""Unit tests for :class:`worldbox_mcp.client.BridgeClient` using respx."""

from __future__ import annotations

import httpx
import pytest
import respx

from worldbox_mcp.client import BridgeClient
from worldbox_mcp.config import BridgeAddress
from worldbox_mcp.errors import BridgeError, TransportError


@pytest.fixture
def address() -> BridgeAddress:
    return BridgeAddress(host="127.0.0.1", port=18723, token="t")


async def test_health_returns_envelope(address: BridgeAddress) -> None:
    async with BridgeClient(address) as client:
        with respx.mock(base_url=address.base_url, assert_all_called=True) as mock:
            route = mock.get("/health").mock(
                return_value=httpx.Response(
                    200,
                    json={
                        "ok": True,
                        "mod_version": "0.1.0",
                        "worldbox_version": "0.x.x",
                        "unity_version": "2022.3.60f1",
                        "assembly_csharp_sha256": "deadbeef",
                        "tick": 42,
                        "enabled": True,
                    },
                )
            )
            health = await client.health()
            assert health["mod_version"] == "0.1.0"
            assert health["tick"] == 42
            # Token must be forwarded as X-WB-Token header.
            assert route.calls.last.request.headers["X-WB-Token"] == "t"


async def test_call_unwraps_result(address: BridgeAddress) -> None:
    async with BridgeClient(address) as client:
        with respx.mock(base_url=address.base_url) as mock:
            mock.post("/cmd").mock(
                return_value=httpx.Response(
                    200,
                    json={"ok": True, "result": {"foo": "bar"}, "tick": 7},
                )
            )
            result = await client.call("anything", {"x": 1})
            assert result == {"foo": "bar"}


async def test_call_scalar_result_wrapped(address: BridgeAddress) -> None:
    async with BridgeClient(address) as client:
        with respx.mock(base_url=address.base_url) as mock:
            mock.post("/cmd").mock(
                return_value=httpx.Response(200, json={"ok": True, "result": 42, "tick": 7})
            )
            assert (await client.call("anything"))["value"] == 42


async def test_error_envelope_raises_bridge_error(address: BridgeAddress) -> None:
    async with BridgeClient(address) as client:
        with respx.mock(base_url=address.base_url) as mock:
            mock.post("/cmd").mock(
                return_value=httpx.Response(
                    400,
                    json={
                        "ok": False,
                        "error": {
                            "code": "UNKNOWN_ASSET",
                            "message": "no such tile",
                            "command": "paint_tile",
                            "args": {"tile_id": "grass_xtra"},
                            "did_you_mean": ["grass", "grass_dry"],
                        },
                    },
                )
            )
            with pytest.raises(BridgeError) as info:
                await client.call("paint_tile", {"tile_id": "grass_xtra"})
            assert info.value.code == "UNKNOWN_ASSET"
            assert info.value.detail["did_you_mean"] == ["grass", "grass_dry"]


async def test_connection_refused_raises_transport_error(address: BridgeAddress) -> None:
    async with BridgeClient(address, timeout=0.5) as client:
        with respx.mock(base_url=address.base_url) as mock:
            mock.get("/health").mock(side_effect=httpx.ConnectError("refused"))
            with pytest.raises(TransportError):
                await client.health()


async def test_non_json_response_raises_transport_error(address: BridgeAddress) -> None:
    async with BridgeClient(address) as client:
        with respx.mock(base_url=address.base_url) as mock:
            mock.get("/health").mock(return_value=httpx.Response(500, text="<html>"))
            with pytest.raises(TransportError, match="non-JSON"):
                await client.health()
