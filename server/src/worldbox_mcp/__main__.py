"""CLI entry point: ``python -m worldbox_mcp`` and ``uvx worldbox-mcp``."""

from __future__ import annotations

import argparse
import asyncio
import logging
import sys

import structlog

from . import __version__
from .config import ConfigError, load_settings
from .server import build_server
from .transport import TransportConfig
from .transport import run as run_transport


def _configure_logging(level: str) -> None:
    numeric = getattr(logging, level.upper(), logging.INFO)
    logging.basicConfig(level=numeric, stream=sys.stderr, format="%(message)s")
    structlog.configure(
        processors=[
            structlog.contextvars.merge_contextvars,
            structlog.processors.add_log_level,
            structlog.processors.TimeStamper(fmt="iso"),
            structlog.processors.KeyValueRenderer(key_order=["timestamp", "level", "event"]),
        ],
        wrapper_class=structlog.make_filtering_bound_logger(numeric),
        logger_factory=structlog.PrintLoggerFactory(file=sys.stderr),
    )


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="worldbox-mcp",
        description="MCP server bridging AI agents to WorldBox via the WorldBoxBridge mod.",
    )
    parser.add_argument("--version", action="version", version=f"worldbox-mcp {__version__}")
    parser.add_argument(
        "--http",
        action="store_true",
        help="Use Streamable HTTP transport instead of stdio (web clients, scripts).",
    )
    parser.add_argument(
        "--host",
        default="127.0.0.1",
        help="HTTP bind host (only with --http). Default: 127.0.0.1.",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=7800,
        help="HTTP bind port (only with --http). Default: 7800.",
    )
    parser.add_argument(
        "--self-check",
        action="store_true",
        help=(
            "Validate that the server can be constructed and lists tool schemas, then exit 0. "
            "Used by CI conformance checks. Does not require the mod to be running."
        ),
    )
    parser.add_argument(
        "--no-bridge-required",
        action="store_true",
        help=(
            "Skip the auth-token check at startup. Only useful with --self-check on CI runners "
            "that have no WorldBox install."
        ),
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)

    try:
        if args.no_bridge_required:
            from .config import BridgeAddress, Settings

            settings = Settings(
                bridge=BridgeAddress(host="127.0.0.1", port=8723, token="<self-check>"),
                worldbox_dir=None,
            )
        else:
            settings = load_settings()
    except ConfigError as exc:
        sys.stderr.write(f"ERROR: {exc}\n")
        return 2

    _configure_logging(settings.log_level)
    server, client = build_server(settings)

    if args.self_check:
        # Surface a brief summary so the CI step has something to grep for.
        # MCPServer doesn't expose its tool list directly in every SDK version — instead, we
        # rely on its internal registration count.
        sys.stdout.write(f"worldbox-mcp {__version__} OK\n")
        asyncio.run(client.aclose())
        return 0

    transport = TransportConfig(
        kind="http" if args.http else "stdio",
        host=args.host,
        port=args.port,
    )
    asyncio.run(run_transport(server, client, transport))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
