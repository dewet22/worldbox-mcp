"""Async HTTP client for talking to the WorldBoxBridge mod.

Wraps :mod:`httpx` with the bridge's auth header and unified error decoding. One instance
per server lifetime; the underlying httpx client is reused for connection pooling.

Auth — v0.3 multi-agent: each request sends ``Authorization: Bearer <token>``. The token
is taken from :attr:`BridgeAddress.token` by default (single-process / stdio mode) but
can be overridden per-call via the ``token`` kwarg (multi-tenant front-ends in Phase 2.5).
The legacy ``X-WB-Token`` header is **not** sent — the C# bridge accepts both, but the
unified Authorization path keeps the wire format consistent with the broader MCP / HTTP
ecosystem.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any, Final

import httpx

from .errors import BridgeErrorEnvelope, TransportError

if TYPE_CHECKING:
    from .config import BridgeAddress

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
        # No baked-in Authorization header — every request injects one explicitly so
        # callers can override per-call (Phase 2.5 multi-tenant) without rebuilding
        # the httpx client.
        self._client = client or httpx.AsyncClient(
            base_url=address.base_url,
            timeout=timeout,
        )

    async def __aenter__(self) -> BridgeClient:
        return self

    async def __aexit__(self, *_exc: object) -> None:
        await self.aclose()

    async def aclose(self) -> None:
        if self._owns_client:
            await self._client.aclose()

    async def health(self, *, token: str | None = None) -> dict[str, Any]:
        """GET /health → bridge metadata."""
        return await self._request("GET", "/health", token=token)

    async def capabilities(self, *, token: str | None = None) -> dict[str, Any]:
        """GET /capabilities → list of registered commands + schemas."""
        return await self._request("GET", "/capabilities", envelope=False, token=token)

    async def call(
        self,
        command: str,
        args: dict[str, Any] | None = None,
        *,
        token: str | None = None,
    ) -> dict[str, Any]:
        """POST /cmd → execute a named command.

        ``token`` overrides the agent credential for this single call. Used by the
        Phase 2.5 multi-tenant front-end so one shared :class:`BridgeClient` can carry
        traffic from several distinct agents (each agent's bearer is extracted from its
        own MCP client connection and forwarded here).
        """
        payload = {"name": command, "args": args or {}}
        return await self._request("POST", "/cmd", json=payload, token=token)

    async def _request(
        self,
        method: str,
        path: str,
        *,
        json: dict[str, Any] | None = None,
        envelope: bool = True,
        token: str | None = None,
    ) -> dict[str, Any]:
        effective_token = token or self._address.token
        headers = {"Authorization": f"Bearer {effective_token}"}
        try:
            response = await self._client.request(method, path, json=json, headers=headers)
        except httpx.TimeoutException as exc:
            msg = f"Bridge did not respond within {self._timeout}s ({method} {path})."
            raise TransportError(msg, cause=exc) from exc
        except httpx.HTTPError as exc:
            msg = f"Bridge unreachable at {self._address.base_url}{path}: {exc!s}"
            raise TransportError(msg, cause=exc) from exc

        try:
            data: dict[str, Any] = response.json()
        except ValueError as exc:
            preview = response.text[:200]
            msg = f"Bridge returned non-JSON (status {response.status_code}): {preview!r}"
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
