"""Async HTTP client for talking to the WorldBoxBridge mod.

Wraps :mod:`httpx` with the bridge's auth header and unified error decoding. One instance
per server lifetime; the underlying httpx client is reused for connection pooling.
"""

from __future__ import annotations

from typing import Any, Final

import httpx

from .config import BridgeAddress
from .errors import BridgeError, BridgeErrorEnvelope, TransportError

TOKEN_HEADER: Final[str] = "X-WB-Token"
DEFAULT_TIMEOUT: Final[float] = 35.0  # Bridge has a 30s per-action timeout; leave room.


class BridgeClient:
    """Lightweight typed wrapper over the bridge's HTTP surface.

    Use as an async context manager so the underlying httpx client is closed cleanly:

    .. code-block:: python

        async with BridgeClient(address) as client:
            await client.health()
    """

    def __init__(
        self,
        address: BridgeAddress,
        *,
        timeout: float = DEFAULT_TIMEOUT,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        self._address = address
        self._timeout = timeout
        self._owns_client = client is None
        self._client = client or httpx.AsyncClient(
            base_url=address.base_url,
            timeout=timeout,
            headers={TOKEN_HEADER: address.token},
        )

    async def __aenter__(self) -> "BridgeClient":
        return self

    async def __aexit__(self, *_exc: object) -> None:
        await self.aclose()

    async def aclose(self) -> None:
        if self._owns_client:
            await self._client.aclose()

    async def health(self) -> dict[str, Any]:
        """GET /health → bridge metadata."""
        return await self._request("GET", "/health")

    async def capabilities(self) -> dict[str, Any]:
        """GET /capabilities → list of registered commands + schemas."""
        return await self._request("GET", "/capabilities", envelope=False)

    async def call(self, command: str, args: dict[str, Any] | None = None) -> dict[str, Any]:
        """POST /cmd → execute a named command."""
        payload = {"name": command, "args": args or {}}
        return await self._request("POST", "/cmd", json=payload)

    async def _request(
        self,
        method: str,
        path: str,
        *,
        json: dict[str, Any] | None = None,
        envelope: bool = True,
    ) -> dict[str, Any]:
        try:
            response = await self._client.request(method, path, json=json)
        except httpx.TimeoutException as exc:
            msg = f"Bridge did not respond within {self._timeout}s ({method} {path})."
            raise TransportError(msg, cause=exc) from exc
        except httpx.HTTPError as exc:
            msg = f"Bridge unreachable at {self._address.base_url}{path}: {exc!s}"
            raise TransportError(msg, cause=exc) from exc

        try:
            data: dict[str, Any] = response.json()
        except ValueError as exc:
            msg = f"Bridge returned non-JSON (status {response.status_code}): {response.text[:200]!r}"
            raise TransportError(msg, cause=exc) from exc

        if not envelope:
            return data

        if data.get("ok") is True:
            result = data.get("result")
            # /health is special: there's no separate `result` — the whole envelope is the result.
            if result is None and path == "/health":
                return data
            return result if isinstance(result, dict) else {"value": result}

        raise BridgeErrorEnvelope.from_payload(data.get("error", {})).as_bridge_error()
