"""Runtime configuration for the MCP server.

Resolution order:

    1. Explicit environment variables (`WORLDBOX_MCP_*`).
    2. Auto-discovered ``BepInEx/config/WorldBoxBridge.cfg`` inside a detected WorldBox install.
    3. Built-in defaults (host 127.0.0.1, port 8723).

The token is **never** defaulted, if neither env nor config provides one, startup fails fast
with a clear error rather than producing 401 storms at runtime.
"""

from __future__ import annotations

import configparser
import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Iterable

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8723

# Steam library + custom paths where a WorldBox install might live.
# Order matters: explicit env var beats heuristics. On Windows, every fixed drive is checked.
_WINDOWS_RELATIVE_CANDIDATES: tuple[str, ...] = (
    r"SteamLibrary\steamapps\common\worldbox",
    r"Steam\steamapps\common\worldbox",
    r"GAMES\steamapps\common\worldbox",
    r"Program Files (x86)\Steam\steamapps\common\worldbox",
    r"Program Files\Steam\steamapps\common\worldbox",
)


@dataclass(frozen=True, slots=True)
class BridgeAddress:
    """Where to reach the in-game HTTP bridge."""

    host: str
    port: int
    token: str

    @property
    def base_url(self) -> str:
        return f"http://{self.host}:{self.port}"


@dataclass(frozen=True, slots=True)
class Settings:
    """All runtime configuration in one immutable bundle."""

    bridge: BridgeAddress
    worldbox_dir: Path | None
    log_level: str = "info"
    extra: dict[str, str] = field(default_factory=dict)


class ConfigError(RuntimeError):
    """Raised when configuration cannot be assembled."""


def load_settings(env: dict[str, str] | None = None) -> Settings:
    """Build :class:`Settings` from the environment + auto-discovery.

    Pass ``env`` explicitly in tests; in production it falls back to ``os.environ``.
    """
    env = dict(os.environ if env is None else env)
    log_level = env.get("WORLDBOX_MCP_LOG", "info").lower()

    worldbox_dir = _resolve_worldbox_dir(env)
    cfg_values: dict[str, str] = {}
    if worldbox_dir is not None:
        cfg_path = worldbox_dir / "BepInEx" / "config" / "WorldBoxBridge.cfg"
        cfg_values = _parse_bepinex_cfg(cfg_path)

    host = env.get("WORLDBOX_MCP_BRIDGE_HOST") or cfg_values.get("host") or DEFAULT_HOST
    port_str = env.get("WORLDBOX_MCP_BRIDGE_PORT") or cfg_values.get("port") or str(DEFAULT_PORT)
    try:
        port = int(port_str)
    except ValueError as exc:
        msg = f"Invalid port value {port_str!r}: {exc}"
        raise ConfigError(msg) from exc

    token = env.get("WORLDBOX_MCP_TOKEN") or cfg_values.get("token") or ""
    if not token:
        searched = (
            f"\n  - env WORLDBOX_MCP_TOKEN"
            f"\n  - {worldbox_dir / 'BepInEx' / 'config' / 'WorldBoxBridge.cfg'}"
            if worldbox_dir
            else "\n  - env WORLDBOX_MCP_TOKEN (no WorldBox install auto-discovered)"
        )
        msg = (
            "WorldBoxBridge auth token not found. Searched:" + searched + "\n"
            "Either launch WorldBox once (the mod generates the token on first run), "
            "or export WORLDBOX_MCP_TOKEN=<value>."
        )
        raise ConfigError(msg)

    return Settings(
        bridge=BridgeAddress(host=host, port=port, token=token),
        worldbox_dir=worldbox_dir,
        log_level=log_level,
    )


def _resolve_worldbox_dir(env: dict[str, str]) -> Path | None:
    """Find the WorldBox install root, or ``None`` if it can't be located."""
    explicit = env.get("WORLDBOX_MCP_WORLDBOX_DIR") or env.get("WORLDBOX_DIR")
    if explicit:
        path = Path(explicit)
        return path if _is_worldbox_install(path) else None

    for candidate in _enumerate_default_paths():
        if _is_worldbox_install(candidate):
            return candidate
    return None


def _enumerate_default_paths() -> Iterable[Path]:
    """Yield plausible WorldBox install directories for the current OS."""
    if os.name == "nt":
        try:
            import string

            drives = [
                f"{letter}:\\" for letter in string.ascii_uppercase if Path(f"{letter}:\\").exists()
            ]
        except Exception:  # pragma: no cover, defensive
            drives = ["C:\\"]
        for drive in drives:
            for relative in _WINDOWS_RELATIVE_CANDIDATES:
                yield Path(drive) / relative
    else:
        home = Path.home()
        yield home / ".steam" / "steam" / "steamapps" / "common" / "worldbox"
        yield home / ".local" / "share" / "Steam" / "steamapps" / "common" / "worldbox"
        yield (
            home / "Library" / "Application Support" / "Steam" / "steamapps" / "common" / "worldbox"
        )


def _is_worldbox_install(path: Path) -> bool:
    """A directory counts as a WorldBox install iff it contains the executable."""
    if not path.is_dir():
        return False
    for binary in ("worldbox.exe", "worldbox", "worldbox.x86_64"):
        if (path / binary).is_file():
            return True
    return bool((path / "worldbox.app").is_dir())


def _parse_bepinex_cfg(cfg_path: Path) -> dict[str, str]:
    """Parse a BepInEx-style INI config file into a flat dict (keys lower-cased).

    BepInEx config files use ``##`` for descriptions which configparser doesn't accept as
    inline comments, we pre-strip them.
    """
    if not cfg_path.is_file():
        return {}
    text = cfg_path.read_text(encoding="utf-8")
    parser = configparser.ConfigParser(
        comment_prefixes=("#", ";"),
        inline_comment_prefixes=None,
        strict=False,
    )
    try:
        parser.read_string(text)
    except configparser.Error:
        return {}
    if "Bridge" not in parser:
        return {}
    return {k.lower(): v.strip() for k, v in parser["Bridge"].items()}
