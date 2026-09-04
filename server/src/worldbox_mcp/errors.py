"""Error mapping between the bridge's JSON envelope and the MCP boundary.

The bridge returns errors in a stable shape (see ``docs/protocol.md``). We surface those
errors as Python exceptions, attaching the full envelope on the exception instance.

Both exception types derive from the MCP SDK's :class:`ToolError`. Since mcp 2.x the server
only forwards ``ToolError`` subclasses to the model verbatim; any other exception raised by
a tool is masked as ``Error executing tool <name>``. A bridge rejection (unknown asset, bad
args, game refused) is an anticipated failure the agent must be able to read and act on,
so it has to travel as a ``ToolError`` -- including the ``did_you_mean`` hints.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

from mcp.server.mcpserver.exceptions import ToolError


class BridgeError(ToolError):
    """Raised when the bridge returns a non-OK envelope."""

    def __init__(self, code: str, message: str, *, detail: dict[str, Any] | None = None) -> None:
        self.code = code
        self.message = message
        self.detail: dict[str, Any] = detail or {}
        text = f"[{code}] {message}"
        hints = self.detail.get("did_you_mean")
        if isinstance(hints, list) and hints:
            text += " Did you mean: " + ", ".join(str(h) for h in hints) + "?"
        super().__init__(text)


class TransportError(ToolError):
    """Raised when the HTTP transport itself fails (timeout, connection refused, etc.)."""

    def __init__(self, message: str, *, cause: Exception | None = None) -> None:
        super().__init__(message)
        self.cause = cause


@dataclass(frozen=True, slots=True)
class BridgeErrorEnvelope:
    """Strongly-typed view over the bridge's ``error`` object."""

    code: str
    message: str
    command: str | None = None
    args: dict[str, Any] | None = None
    did_you_mean: list[str] | None = None
    exception: dict[str, Any] | None = None
    extras: dict[str, Any] = field(default_factory=dict)

    @classmethod
    def from_payload(cls, payload: dict[str, Any]) -> BridgeErrorEnvelope:
        known = {"code", "message", "command", "args", "did_you_mean", "exception"}
        return cls(
            code=str(payload.get("code", "INTERNAL")),
            message=str(payload.get("message", "(no message)")),
            command=payload.get("command"),
            args=payload.get("args"),
            did_you_mean=payload.get("did_you_mean"),
            exception=payload.get("exception"),
            extras={k: v for k, v in payload.items() if k not in known},
        )

    def as_bridge_error(self) -> BridgeError:
        detail = {
            "command": self.command,
            "args": self.args,
            "did_you_mean": self.did_you_mean,
            "exception": self.exception,
            **self.extras,
        }
        detail = {k: v for k, v in detail.items() if v is not None}
        return BridgeError(self.code, self.message, detail=detail)
