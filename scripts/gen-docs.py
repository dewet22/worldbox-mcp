#!/usr/bin/env python3
"""Keep the documented tool surface in step with the tools the server actually registers.

The tool count is stated in six files and has drifted three times, most recently when
``docs/index.md`` still claimed twenty-six tools and was missing three of them outright.
This script removes the drift in two ways.

**Generated regions.** Anything mechanical lives between markers and is rewritten by
``--write``::

    <!-- gen-docs:begin total -->29<!-- gen-docs:end total -->

Everything outside the markers is hand-written prose and is never touched, which is why the
per-version asset counts, the argument columns and the error model survive. The count in
``docs/compatibility.md`` is deliberately not marked: that row records what a released
version shipped, and it must not move when the surface grows.

**Inventory checks.** The category tables carry editorial columns, so rewriting them would
cost more than it saves. They are verified instead: a file listed in :data:`INVENTORY_FILES`
must name every registered tool. Any ``worldbox_*`` identifier anywhere in the docs must
also resolve to a real tool.

Source of truth is the MCP server itself, imported and queried in-process, so no game and no
network are involved. The C# side is counted from source and cross-checked against it, which
catches a command added on one side of the bridge only.

Usage::

    python scripts/gen-docs.py --check   # report drift, exit 1 if any (CI)
    python scripts/gen-docs.py --write   # rewrite the generated regions
"""

from __future__ import annotations

import argparse
import asyncio
import importlib
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SERVER_SRC = REPO_ROOT / "server" / "src"
COMMANDS_DIR = REPO_ROOT / "mod" / "src" / "WorldBoxBridge" / "Commands"

# Category name and the tools submodule that registers it, in the order build_server calls
# them. A new category means a new module, so this list is the one place to extend.
CATEGORY_MODULES: list[tuple[str, str]] = [
    ("Meta", "meta"),
    ("Discovery", "discovery"),
    ("Action", "action"),
    ("Read", "read"),
    ("Control", "control"),
    ("Bus", "bus"),
]

# Files whose tables are meant to be a complete inventory of the tool surface.
INVENTORY_FILES: list[str] = [
    "README.md",
    "docs/index.md",
    "docs/multi-agent.md",
    "docs/command-reference.md",
]

SKIP_DIRS = frozenset({".git", ".venv", "archives", "node_modules", "site"})

# The screenshot defaults are stated on both sides of the bridge: the MCP schema has to tell
# the model what it will get, and the mod has to apply it when the caller says nothing. Two
# statements of one value, so they are checked against each other. Maps a `const` in
# ScreenshotScaler.cs to the module constant in the Python tool.
SCREENSHOT_SCALER = (
    REPO_ROOT / "mod" / "src" / "WorldBoxBridge" / "Commands" / "Read" / "ScreenshotScaler.cs"
)
COMMAND_REFERENCE = REPO_ROOT / "docs" / "command-reference.md"
COMPATIBILITY = REPO_ROOT / "docs" / "compatibility.md"

# The release version is stated in four files, all bumped by release-please through the
# `extra-files` entries in release-please-config.json. They are checked against each other
# because a broken updater entry is silent: the release ships with one of the four a version
# behind, and nobody notices until they read the plugin banner in a log.
VERSION_SOURCES: list[tuple[str, str]] = [
    ("server/pyproject.toml", r'(?m)^version\s*=\s*"([^"]+)"'),
    ("server/src/worldbox_mcp/__init__.py", r'(?m)^__version__\s*=\s*"([^"]+)"'),
    ("mod/src/WorldBoxBridge/WorldBoxBridge.csproj", r"<Version>([^<]+)</Version>"),
    (
        "mod/src/WorldBoxBridge/PluginInfo.cs",
        r'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"',
    ),
]

# The command reference states the same three values a third time, in the worldbox_screenshot
# row. Matched by token so a reworded row fails loudly rather than passing silently. The
# format entry needs the "(default)" marker in the row for the same reason: listing
# `format="jpg"|"png"` says which values are legal but not which one you get, so the row could
# have claimed PNG and nothing would have noticed.
SCREENSHOT_ROW = re.compile(r"^\|\s*`worldbox_screenshot`\s*\|.*$", re.MULTILINE)
SCREENSHOT_ROW_TOKENS: list[tuple[str, str, str]] = [
    ("SCREENSHOT_MAX_DIMENSION", "max_dimension=", r"max_dimension=(\d+)"),
    ("SCREENSHOT_QUALITY", "quality=", r"quality=(\d+)"),
    ("SCREENSHOT_FORMAT", 'format="', r'format="(\w+)"\(default\)'),
]

SCREENSHOT_DEFAULTS: list[tuple[str, str]] = [
    ("DefaultMaxDimension", "SCREENSHOT_MAX_DIMENSION"),
    ("DefaultQuality", "SCREENSHOT_QUALITY"),
    ("DefaultFormat", "SCREENSHOT_FORMAT"),
]

# A const is a number, a string literal, or another const in the same class. The last form
# matters: `DefaultFormat = Jpg` is how the bridge names its default without restating "jpg",
# and a checker that could not follow it would report the constant as missing.
CSHARP_CONST = re.compile(
    r"public\s+const\s+(?:int|string)\s+(?P<name>\w+)\s*=\s*"
    r"(?P<value>\d+|\"[^\"]*\"|[A-Za-z_]\w*)\s*;"
)

# Identifiers that match the tool naming pattern without being tools. Add to this set when a
# new one appears, the alternative is a check that lets a renamed tool slip through.
NOT_A_TOOL: frozenset[str] = frozenset(
    {
        "worldbox_mcp",  # the Python package
        "worldbox_version",  # a field of the /health payload
        "worldbox_dir",  # a server setting
    }
)

# Every command declares its wire name with a `Name =>` property. PauseCommand.cs holds two
# of them, which is why counting files gives the wrong answer.
CSHARP_NAME = re.compile(r"public\s+(?:override\s+|sealed\s+override\s+)?string\s+Name\s*=>")

REGION = re.compile(
    r"(?P<open><!-- gen-docs:begin (?P<name>[a-z][a-z0-9-]*) -->)"
    r"(?P<body>.*?)"
    r"(?P<close><!-- gen-docs:end (?P=name) -->)",
    re.DOTALL,
)

TOOL_MENTION = re.compile(r"\bworldbox_[a-z0-9_]+")

ONES = [
    "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
    "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
    "eighteen", "nineteen",
]
TENS = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"]


def spell(n: int) -> str:
    """Spell out 0 to 99, capitalised, the way the headings do ('Twenty-nine')."""
    if not 0 <= n < 100:
        raise ValueError(f"no spelling for {n}, extend spell() if the surface grew that much")
    word = ONES[n] if n < 20 else TENS[n // 10] + ("-" + ONES[n % 10] if n % 10 else "")
    return word.capitalize()


@dataclass
class Surface:
    """The tool surface as the code defines it."""

    by_category: dict[str, list[str]]
    bridge_commands: int

    @property
    def names(self) -> set[str]:
        return {name for names in self.by_category.values() for name in names}

    @property
    def total(self) -> int:
        return sum(len(names) for names in self.by_category.values())


@dataclass
class Report:
    """What drifted. No problems means the docs agree with the code."""

    problems: list[str] = field(default_factory=list)
    rewrites: list[str] = field(default_factory=list)

    def fail(self, message: str) -> None:
        self.problems.append(message)


def markdown_files(root: Path) -> list[Path]:
    return sorted(
        path
        for path in root.rglob("*.md")
        if not SKIP_DIRS.intersection(path.relative_to(root).parts)
    )


async def _collect_tools() -> dict[str, list[str]]:
    from mcp.server.mcpserver import MCPServer

    from worldbox_mcp.client import BridgeClient
    from worldbox_mcp.config import BridgeAddress

    # The client is never called: registration only needs something to close over.
    client = BridgeClient(BridgeAddress(host="127.0.0.1", port=8723, token="gen-docs"))
    try:
        by_category: dict[str, list[str]] = {}
        seen: dict[str, str] = {}
        for category, module_name in CATEGORY_MODULES:
            server = MCPServer(name="gen-docs")
            module = importlib.import_module(f"worldbox_mcp.tools.{module_name}")
            module.register(server, client)
            names = sorted(tool.name for tool in await server.list_tools())
            for name in names:
                if name in seen:
                    raise RuntimeError(f"{name} registered by both {seen[name]} and {category}")
                seen[name] = category
            by_category[category] = names
        return by_category
    finally:
        await client.aclose()


def read_surface(commands_dir: Path = COMMANDS_DIR) -> Surface:
    """Import the server, register every tool module, and count the C# side too."""
    if str(SERVER_SRC) not in sys.path:
        sys.path.insert(0, str(SERVER_SRC))
    by_category = asyncio.run(_collect_tools())
    bridge_commands = sum(
        len(CSHARP_NAME.findall(path.read_text(encoding="utf-8")))
        for path in sorted(commands_dir.rglob("*.cs"))
    )
    return Surface(by_category=by_category, bridge_commands=bridge_commands)


def region_values(surface: Surface) -> dict[str, str]:
    """What each named region should contain."""
    return {
        "total": str(surface.total),
        "total-words": spell(surface.total),
        "bridge-commands": str(surface.bridge_commands),
    }


def sync_regions(surface: Surface, root: Path, *, write: bool, report: Report) -> None:
    values = region_values(surface)
    seen: set[str] = set()

    for path in markdown_files(root):
        text = path.read_text(encoding="utf-8")
        if "gen-docs:begin" not in text:
            continue
        rel = path.relative_to(root)
        stale: list[str] = []

        def replace(match: re.Match[str]) -> str:
            name = match.group("name")
            seen.add(name)
            if name not in values:
                report.fail(f"{rel}: unknown region '{name}', expected one of {sorted(values)}")
                return match.group(0)
            wanted = values[name]
            if match.group("body") != wanted:
                stale.append(f"{name}: '{match.group('body')}' should be '{wanted}'")
            return f"{match.group('open')}{wanted}{match.group('close')}"

        updated, matched = REGION.subn(replace, text)
        # A begin marker whose end is missing, or misspelt, matches nothing. Without this the
        # region would be skipped in silence, which is the failure mode the script exists to
        # prevent.
        opened = text.count("<!-- gen-docs:begin ")
        if matched != opened:
            report.fail(
                f"{rel}: {opened} begin marker(s) but {matched} complete region(s). One is "
                f"unclosed or its end marker names a different region."
            )
        if not stale:
            continue
        if write:
            path.write_text(updated, encoding="utf-8")
            report.rewrites.append(f"{rel}: {', '.join(stale)}")
        else:
            for detail in stale:
                report.fail(f"{rel}: {detail}")

    for name in sorted(set(values) - seen):
        report.fail(f"region '{name}' is generated but no file uses it")


def check_inventories(surface: Surface, root: Path, report: Report) -> None:
    known = surface.names
    for rel in INVENTORY_FILES:
        path = root / rel
        if not path.is_file():
            report.fail(f"{rel}: listed as an inventory file but missing")
            continue
        mentioned = set(TOOL_MENTION.findall(path.read_text(encoding="utf-8"))) - NOT_A_TOOL
        missing = sorted(known - mentioned)
        if missing:
            report.fail(f"{rel}: never mentions {', '.join(missing)}")


def check_mentions(surface: Surface, root: Path, report: Report) -> None:
    allowed = surface.names | NOT_A_TOOL
    for path in markdown_files(root):
        unknown = set(TOOL_MENTION.findall(path.read_text(encoding="utf-8"))) - allowed
        if unknown:
            report.fail(
                f"{path.relative_to(root)}: mentions {', '.join(sorted(unknown))}, which no tool "
                f"is named. Either the docs are stale, or add it to NOT_A_TOOL in this script."
            )


def check_parity(surface: Surface, report: Report) -> None:
    # /capabilities is served by the HTTP layer, not by an ICommand, so the mod declares
    # exactly one command fewer than the server exposes tools.
    expected = surface.bridge_commands + 1
    if expected != surface.total:
        report.fail(
            f"the mod declares {surface.bridge_commands} commands, so the server should expose "
            f"{expected} tools, but it registers {surface.total}. A command was probably added "
            f"on one side of the bridge only."
        )


def csharp_screenshot_defaults(scaler: Path) -> dict[str, str]:
    """The `const` values ScreenshotScaler declares, as strings, aliases resolved."""
    raw = {
        m.group("name"): m.group("value")
        for m in CSHARP_CONST.finditer(scaler.read_text(encoding="utf-8"))
    }
    resolved: dict[str, str] = {}
    for name, value in raw.items():
        seen: set[str] = set()
        # `A = B; B = "jpg"` resolves to "jpg". `seen` stops a cycle, which the C# compiler
        # would reject anyway, from spinning here.
        while value in raw and value not in seen:
            seen.add(value)
            value = raw[value]
        resolved[name] = value.strip('"')
    return resolved


def python_screenshot_defaults() -> dict[str, str]:
    """The defaults the MCP schema advertises, read off the tool module itself."""
    module = importlib.import_module("worldbox_mcp.tools.read")
    # Absent stays absent rather than becoming the string "None", which would be reported as a
    # value drift and send the reader looking for a constant set to the wrong number.
    return {
        py: str(getattr(module, py))
        for _, py in SCREENSHOT_DEFAULTS
        if hasattr(module, py)
    }


def check_screenshot_defaults(
    report: Report,
    scaler: Path = SCREENSHOT_SCALER,
    python_values: dict[str, str] | None = None,
    reference: Path = COMMAND_REFERENCE,
) -> None:
    """The screenshot defaults the MCP schema promises must be what the bridge applies."""
    if not scaler.exists():
        report.fail(f"{scaler} is missing; the screenshot defaults cannot be checked.")
        return
    csharp = csharp_screenshot_defaults(scaler)
    python = python_screenshot_defaults() if python_values is None else python_values
    for cs_name, py_name in SCREENSHOT_DEFAULTS:
        if cs_name not in csharp:
            report.fail(
                f"ScreenshotScaler no longer declares `const ... {cs_name}`, so the Python "
                f"default {py_name} in tools/read.py has nothing to be checked against."
            )
            continue
        if py_name not in python:
            report.fail(
                f"tools/read.py no longer declares {py_name}, so ScreenshotScaler.{cs_name} "
                f"has nothing to be checked against."
            )
            continue
        if csharp[cs_name] != python[py_name]:
            report.fail(
                f"screenshot default drift: ScreenshotScaler.{cs_name} is {csharp[cs_name]!r} "
                f"but tools/read.py {py_name} is {python[py_name]!r}. The schema would "
                f"promise the model something the bridge does not do."
            )
    check_screenshot_row(report, python, reference=reference)


def check_screenshot_row(
    report: Report, python: dict[str, str], reference: Path = COMMAND_REFERENCE
) -> None:
    """The command reference restates the same defaults in prose. Keep it honest too."""
    if not reference.exists():
        report.fail(f"{reference} is missing; the documented screenshot row cannot be checked.")
        return
    match = SCREENSHOT_ROW.search(reference.read_text(encoding="utf-8"))
    if match is None:
        report.fail(
            f"{reference.name} has no `worldbox_screenshot` row, so its copy of the screenshot "
            f"defaults cannot be checked. Restore the row or drop SCREENSHOT_ROW_TOKENS."
        )
        return
    row = match.group(0)
    for py_name, token, pattern in SCREENSHOT_ROW_TOKENS:
        found = re.search(pattern, row)
        if found is None:
            report.fail(
                f"{reference.name}: the worldbox_screenshot row no longer states "
                f"'{token}<value>', so {py_name} is documented somewhere this check cannot see."
            )
        elif found.group(1) != python.get(py_name):
            report.fail(
                f"{reference.name}: the worldbox_screenshot row says "
                f"{found.group(0)!r} but {py_name} is {python.get(py_name)!r}."
            )


def matrix_mod_versions(compatibility: Path) -> list[str]:
    """The "Mod version" cell of every data row in the compatibility matrix, verbatim."""
    cells: list[str] = []
    for line in compatibility.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped.startswith("|"):
            continue
        parts = [cell.strip() for cell in stripped.strip("|").split("|")]
        if len(parts) < 5:
            continue
        if parts[0] == "WorldBox version" or set(parts[0]) <= set("-: "):
            continue  # header, or the separator row under it
        cells.append(parts[3])
    return cells


def version_covers(cell: str, version: str) -> bool:
    """Whether a matrix cell claims to cover `version`.

    Three forms are in use and all three have to be read: a plain version, possibly bold or
    trailed by a parenthetical ("0.3.0 (upstream's own validation, Windows)"); a wildcard
    ("0.2.x"); and a range ("0.3.0 to 0.3.3"). Anything else is treated as not covering, so a
    new notation fails loudly rather than being silently accepted as a match.
    """
    cell = re.sub(r"\(.*?\)", "", cell.replace("**", "")).strip()
    if cell == version:
        return True
    if cell.endswith(".x"):
        return version.startswith(cell[:-1])
    span = re.fullmatch(r"(\S+)\s+to\s+(\S+)", cell)
    if span is None:
        return False
    try:
        low = _version_parts(span.group(1))
        high = _version_parts(span.group(2))
        here = _version_parts(version)
    except ValueError:
        return False
    return low <= here <= high


def _version_parts(version: str) -> tuple[int, ...]:
    return tuple(int(part) for part in version.split("."))


def check_release_version(
    report: Report, root: Path = REPO_ROOT, compatibility: Path | None = None
) -> None:
    """The four version files must agree, and the matrix must have a row for what they say.

    The matrix is the one document that records whether a released version actually works, and
    it is written by hand: 0.3.0 to 0.3.3 shipped DLLs that never loaded, and the row saying so
    was added afterwards. Nothing checked that the row existed at all, so the failure mode is a
    matrix quietly a release behind, which reads exactly like a release nobody has reported a
    problem with.

    Deliberately not run on release-please's own branch, see the CI step: that PR bumps the four
    files and the row is written by hand once the release is out. The next ordinary PR fails
    here until it is, which is the point.
    """
    found: dict[str, str] = {}
    for relative, pattern in VERSION_SOURCES:
        path = root / relative
        if not path.exists():
            report.fail(f"{relative} is missing; the release version cannot be checked.")
            return
        match = re.search(pattern, path.read_text(encoding="utf-8"))
        if match is None:
            report.fail(
                f"{relative} no longer states a version this check can find, while "
                f"release-please still bumps it. Fix the pattern in VERSION_SOURCES or restore "
                f"the declaration, otherwise the four files can drift unnoticed."
            )
            return
        found[relative] = match.group(1)

    distinct = sorted(set(found.values()))
    if len(distinct) > 1:
        detail = ", ".join(f"{rel} says {value}" for rel, value in sorted(found.items()))
        report.fail(
            f"the four files release-please bumps disagree on the version ({detail}). One of "
            f"the `extra-files` entries in release-please-config.json has stopped matching."
        )
        return

    version = distinct[0]
    path = compatibility if compatibility is not None else root / "docs" / "compatibility.md"
    if not path.exists():
        report.fail(f"{path} is missing; the compatibility matrix cannot be checked.")
        return
    if not any(version_covers(cell, version) for cell in matrix_mod_versions(path)):
        report.fail(
            f"docs/compatibility.md has no row for {version}, the version this tree declares. "
            f"Add one with the status the release actually has: 🔵 until the e2e suite has run "
            f"against a real install, ✅ only after."
        )


def run(surface: Surface, root: Path, *, write: bool) -> Report:
    report = Report()
    sync_regions(surface, root, write=write, report=report)
    check_inventories(surface, root, report)
    check_mentions(surface, root, report)
    check_parity(surface, report)
    # The screenshot defaults are repo-global rather than root-relative, so they are only
    # checked when run() is pointed at this repository. The unit tests drive run() against a
    # throwaway tree, which would otherwise inherit a failure about files it never created.
    if root.resolve() == REPO_ROOT:
        check_screenshot_defaults(report)
        check_release_version(report)
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Keep the documented tool surface in step with the registered tools."
    )
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="report drift without writing")
    mode.add_argument("--write", action="store_true", help="rewrite the generated regions")
    parser.add_argument(
        "--root", type=Path, default=REPO_ROOT, help="repository root (defaults to this one)"
    )
    args = parser.parse_args(argv)

    surface = read_surface()
    report = run(surface, args.root, write=args.write)

    for line in report.rewrites:
        print(f"updated {line}")

    if report.problems:
        print(f"\n{len(report.problems)} problem(s):", file=sys.stderr)
        for problem in report.problems:
            print(f"  {problem}", file=sys.stderr)
        if not args.write:
            print(
                "\nRun `python scripts/gen-docs.py --write` to refresh the generated regions.",
                file=sys.stderr,
            )
        return 1

    categories = ", ".join(f"{c} {len(n)}" for c, n in surface.by_category.items())
    print(f"{surface.total} tools ({categories}); {surface.bridge_commands} bridge commands. Docs agree.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
