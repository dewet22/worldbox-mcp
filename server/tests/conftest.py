"""Shared pytest fixtures."""

from __future__ import annotations

import pytest

from worldbox_mcp.config import BridgeAddress


def pytest_addoption(parser: pytest.Parser) -> None:
    parser.addoption(
        "--run-e2e",
        action="store_true",
        default=False,
        help="Run end-to-end tests that require a real WorldBox + mod running.",
    )


def pytest_collection_modifyitems(config: pytest.Config, items: list[pytest.Item]) -> None:
    if config.getoption("--run-e2e"):
        return
    skip_e2e = pytest.mark.skip(reason="e2e suite needs WorldBox running (--run-e2e to enable)")
    for item in items:
        if "e2e" in item.keywords:
            item.add_marker(skip_e2e)


@pytest.fixture
def bridge_address() -> BridgeAddress:
    return BridgeAddress(host="127.0.0.1", port=18723, token="test-token-do-not-use")


@pytest.fixture
def cfg_text() -> str:
    return (
        "## WorldBoxBridge configuration\n"
        "## Generated 2026-01-01T00:00:00\n"
        "\n"
        "[Bridge]\n"
        "## Whether the bridge accepts requests.\n"
        "# Setting type: bool\n"
        "enabled = true\n"
        "host = 127.0.0.1\n"
        "port = 8723\n"
        "token = AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-randomtokenvalue\n"
    )
