"""Unit tests for :mod:`worldbox_mcp.config`."""

from __future__ import annotations

from pathlib import Path

import pytest

from worldbox_mcp.config import (
    ConfigError,
    _parse_bepinex_cfg,
    load_settings,
)


def test_parse_bepinex_cfg_extracts_known_keys(tmp_path: Path, cfg_text: str) -> None:
    cfg = tmp_path / "WorldBoxBridge.cfg"
    cfg.write_text(cfg_text, encoding="utf-8")
    parsed = _parse_bepinex_cfg(cfg)
    assert parsed["enabled"] == "true"
    assert parsed["host"] == "127.0.0.1"
    assert parsed["port"] == "8723"
    assert parsed["token"].startswith("AbCdEf")


def test_parse_bepinex_cfg_missing_file_returns_empty(tmp_path: Path) -> None:
    assert _parse_bepinex_cfg(tmp_path / "does-not-exist.cfg") == {}


def test_parse_bepinex_cfg_handles_garbage(tmp_path: Path) -> None:
    cfg = tmp_path / "broken.cfg"
    cfg.write_text("\x00\x01garbage without any sections\n", encoding="utf-8")
    parsed = _parse_bepinex_cfg(cfg)
    assert parsed == {} or "token" not in parsed


def test_load_settings_explicit_env_overrides_everything(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = load_settings(
        env={
            "WORLDBOX_MCP_BRIDGE_HOST": "127.0.0.1",
            "WORLDBOX_MCP_BRIDGE_PORT": "9999",
            "WORLDBOX_MCP_TOKEN": "explicit-token-value",
        }
    )
    assert settings.bridge.host == "127.0.0.1"
    assert settings.bridge.port == 9999
    assert settings.bridge.token == "explicit-token-value"


def test_load_settings_token_required() -> None:
    with pytest.raises(ConfigError, match="token not found"):
        load_settings(env={})


def test_load_settings_invalid_port_raises() -> None:
    with pytest.raises(ConfigError, match="Invalid port"):
        load_settings(
            env={
                "WORLDBOX_MCP_BRIDGE_PORT": "not-a-number",
                "WORLDBOX_MCP_TOKEN": "x",
            }
        )


def test_load_settings_discovers_worldbox_dir(tmp_path: Path) -> None:
    # Simulate a WorldBox install: a directory with a worldbox executable and a config file.
    install = tmp_path / "worldbox"
    install.mkdir()
    # Use the OS-appropriate binary name so _is_worldbox_install accepts it.
    import os

    binary_name = "worldbox.exe" if os.name == "nt" else "worldbox"
    (install / binary_name).write_bytes(b"")

    cfg_dir = install / "BepInEx" / "config"
    cfg_dir.mkdir(parents=True)
    (cfg_dir / "WorldBoxBridge.cfg").write_text(
        "[Bridge]\nenabled = true\nhost = 127.0.0.1\nport = 1234\ntoken = cfg-token\n",
        encoding="utf-8",
    )

    settings = load_settings(env={"WORLDBOX_DIR": str(install)})
    assert settings.bridge.host == "127.0.0.1"
    assert settings.bridge.port == 1234
    assert settings.bridge.token == "cfg-token"
    assert settings.worldbox_dir == install
