"""Unit tests for ``scripts/gen-docs.py``.

The script is the guard that keeps the documented tool surface honest, so it needs a guard of
its own: a check that stays silent when the docs drift is worse than no check at all. These
tests drive it against a throwaway tree rather than the repository, which is why every entry
point takes a ``root``.
"""

from __future__ import annotations

import importlib.util
import re
import sys
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from types import ModuleType

REPO_ROOT = Path(__file__).resolve().parents[3]
SCRIPT = REPO_ROOT / "scripts" / "gen-docs.py"


def _load() -> ModuleType:
    # Not importable as a package: it is a hyphenated script outside the distribution.
    spec = importlib.util.spec_from_file_location("gen_docs", SCRIPT)
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


gen_docs = _load()


@pytest.fixture
def surface() -> object:
    return gen_docs.Surface(
        by_category={"Meta": ["worldbox_health"], "Read": ["worldbox_get_tile"]},
        bridge_commands=1,
    )


@pytest.fixture
def docs(tmp_path: Path) -> Path:
    """A tree whose docs agree with the two-tool surface above."""
    (tmp_path / "docs").mkdir()
    body = (
        "# Title\n\n"
        "<!-- gen-docs:begin total -->2<!-- gen-docs:end total --> tools, spelled "
        "<!-- gen-docs:begin total-words -->Two<!-- gen-docs:end total-words -->, over "
        "<!-- gen-docs:begin bridge-commands -->1<!-- gen-docs:end bridge-commands --> command.\n\n"
        "`worldbox_health` and `worldbox_get_tile`.\n"
    )
    for name in ("README.md", "docs/index.md", "docs/multi-agent.md", "docs/command-reference.md"):
        (tmp_path / name).write_text(body, encoding="utf-8")
    return tmp_path


def test_a_consistent_tree_reports_nothing(surface: object, docs: Path) -> None:
    report = gen_docs.run(surface, docs, write=False)
    assert report.problems == []


def test_stale_count_is_reported(surface: object, docs: Path) -> None:
    index = docs / "docs/index.md"
    index.write_text(index.read_text().replace(">2<", ">26<"), encoding="utf-8")

    report = gen_docs.run(surface, docs, write=False)

    assert any("total: '26' should be '2'" in p for p in report.problems)


def test_missing_tool_in_an_inventory_file_is_reported(surface: object, docs: Path) -> None:
    index = docs / "docs/index.md"
    index.write_text(index.read_text().replace("`worldbox_get_tile`", ""), encoding="utf-8")

    report = gen_docs.run(surface, docs, write=False)

    assert any("never mentions worldbox_get_tile" in p for p in report.problems)


def test_tool_that_does_not_exist_is_reported(surface: object, docs: Path) -> None:
    (docs / "docs/protocol.md").write_text("Call `worldbox_teleport`.\n", encoding="utf-8")

    report = gen_docs.run(surface, docs, write=False)

    assert any("worldbox_teleport" in p for p in report.problems)


def test_known_non_tools_are_not_reported(surface: object, docs: Path) -> None:
    (docs / "docs/protocol.md").write_text(
        "The `worldbox_mcp` package reads `worldbox_version` from `worldbox_dir`.\n",
        encoding="utf-8",
    )

    report = gen_docs.run(surface, docs, write=False)

    assert report.problems == []


def test_command_added_on_one_side_only_is_reported(docs: Path) -> None:
    lopsided = gen_docs.Surface(
        by_category={"Meta": ["worldbox_health"], "Read": ["worldbox_get_tile"]},
        bridge_commands=5,
    )

    report = gen_docs.run(lopsided, docs, write=False)

    assert any("one side of the bridge only" in p for p in report.problems)


def test_write_repairs_a_stale_region(surface: object, docs: Path) -> None:
    index = docs / "docs/index.md"
    index.write_text(index.read_text().replace(">Two<", ">Twenty-six<"), encoding="utf-8")

    report = gen_docs.run(surface, docs, write=True)

    assert report.problems == []
    assert ">Two<" in index.read_text()


def test_a_region_nobody_uses_is_reported(surface: object, tmp_path: Path) -> None:
    (tmp_path / "docs").mkdir()
    for name in ("README.md", "docs/index.md", "docs/multi-agent.md", "docs/command-reference.md"):
        (tmp_path / name).write_text("`worldbox_health` `worldbox_get_tile`\n", encoding="utf-8")

    report = gen_docs.run(surface, tmp_path, write=False)

    assert any("region 'total' is generated but no file uses it" in p for p in report.problems)


@pytest.mark.parametrize(
    ("number", "spelled"),
    [
        (0, "Zero"),
        (9, "Nine"),
        (13, "Thirteen"),
        (20, "Twenty"),
        (29, "Twenty-nine"),
        (99, "Ninety-nine"),
    ],
)
def test_spelling(number: int, spelled: str) -> None:
    assert gen_docs.spell(number) == spelled


def test_spelling_refuses_what_it_cannot_write() -> None:
    with pytest.raises(ValueError, match="extend spell"):
        gen_docs.spell(100)


def test_unclosed_region_is_reported(surface: object, docs: Path) -> None:
    index = docs / "docs/index.md"
    index.write_text(index.read_text().replace("<!-- gen-docs:end total -->", ""), encoding="utf-8")

    report = gen_docs.run(surface, docs, write=False)

    assert any("begin marker(s) but" in p for p in report.problems)


def test_mismatched_end_marker_is_reported(surface: object, docs: Path) -> None:
    index = docs / "docs/index.md"
    index.write_text(
        index.read_text().replace(
            "<!-- gen-docs:end bridge-commands -->", "<!-- gen-docs:end total -->"
        ),
        encoding="utf-8",
    )

    report = gen_docs.run(surface, docs, write=False)

    assert any("begin marker(s) but" in p for p in report.problems)


# ─── Screenshot defaults ──────────────────────────────────────────────────
#
# The defaults are stated on both sides of the bridge: the MCP schema tells the model what it
# will get, ScreenshotScaler applies it when the caller says nothing. Two statements of one
# value, so the check compares them. Both entry points take their inputs so these run against
# a throwaway file rather than the repository.

SCALER_SOURCE = """
internal static class ScreenshotScaler
{
    public const int DefaultMaxDimension = 1280;
    public const int DefaultQuality = 80;
    public const string Jpg = "jpg";
    public const string Png = "png";
    public const string DefaultFormat = Jpg;
}
"""

MATCHING_PYTHON = {
    "SCREENSHOT_MAX_DIMENSION": "1280",
    "SCREENSHOT_QUALITY": "80",
    "SCREENSHOT_FORMAT": "jpg",
}


REFERENCE_ROW = (
    "| `worldbox_screenshot` | Args {max_dimension=1280, "
    'format="jpg"(default)|"png", quality=80}. |\n'
)


@pytest.fixture
def scaler(tmp_path: Path) -> Path:
    path = tmp_path / "ScreenshotScaler.cs"
    path.write_text(SCALER_SOURCE, encoding="utf-8")
    return path


@pytest.fixture
def reference(tmp_path: Path) -> Path:
    path = tmp_path / "command-reference.md"
    path.write_text(REFERENCE_ROW, encoding="utf-8")
    return path


def test_matching_screenshot_defaults_report_nothing(scaler: Path, reference: Path) -> None:
    report = gen_docs.Report()
    gen_docs.check_screenshot_defaults(
        report, scaler=scaler, python_values=MATCHING_PYTHON, reference=reference
    )

    assert report.problems == []


def test_screenshot_default_drift_is_reported(scaler: Path, reference: Path) -> None:
    report = gen_docs.Report()
    drifted = {**MATCHING_PYTHON, "SCREENSHOT_QUALITY": "75"}
    gen_docs.check_screenshot_defaults(
        report, scaler=scaler, python_values=drifted, reference=reference
    )

    assert any("DefaultQuality" in p and "75" in p for p in report.problems)


def test_screenshot_default_renamed_on_the_csharp_side_is_reported(
    tmp_path: Path, reference: Path
) -> None:
    path = tmp_path / "ScreenshotScaler.cs"
    path.write_text(SCALER_SOURCE.replace("DefaultQuality", "DefaultJpegQuality"), encoding="utf-8")

    report = gen_docs.Report()
    gen_docs.check_screenshot_defaults(
        report, scaler=path, python_values=MATCHING_PYTHON, reference=reference
    )

    assert any("no longer declares" in p for p in report.problems)


def test_screenshot_default_renamed_on_the_python_side_is_reported(
    scaler: Path, reference: Path
) -> None:
    # The C# side has its own message for this. Without the matching branch the Python side
    # reported the constant as having drifted to the string "None", which sends the reader
    # looking for a number that was set wrong rather than for a constant that is gone.
    gone = {k: v for k, v in MATCHING_PYTHON.items() if k != "SCREENSHOT_QUALITY"}

    report = gen_docs.Report()
    gen_docs.check_screenshot_defaults(
        report, scaler=scaler, python_values=gone, reference=reference
    )

    assert any("no longer declares SCREENSHOT_QUALITY" in p for p in report.problems)
    assert not any("'None'" in p for p in report.problems)


def test_missing_scaler_is_reported_rather_than_passing_quietly(
    tmp_path: Path, reference: Path
) -> None:
    report = gen_docs.Report()
    gen_docs.check_screenshot_defaults(
        report, scaler=tmp_path / "gone.cs", python_values=MATCHING_PYTHON, reference=reference
    )

    assert any("is missing" in p for p in report.problems)


def test_csharp_const_alias_is_resolved(scaler: Path) -> None:
    # `DefaultFormat = Jpg` is how the bridge names its default without restating "jpg". A
    # checker that could not follow the alias reported the constant as missing.
    values = gen_docs.csharp_screenshot_defaults(scaler)

    assert values["DefaultFormat"] == "jpg"


def test_command_reference_row_drift_is_reported(scaler: Path, tmp_path: Path) -> None:
    # The defaults are stated a third time, in prose, in the command reference.
    drifted = tmp_path / "drifted.md"
    drifted.write_text(REFERENCE_ROW.replace("quality=80", "quality=75"), encoding="utf-8")

    report = gen_docs.Report()
    gen_docs.check_screenshot_defaults(
        report, scaler=scaler, python_values=MATCHING_PYTHON, reference=drifted
    )

    assert any("quality=75" in p and "SCREENSHOT_QUALITY" in p for p in report.problems)


def test_command_reference_row_reworded_away_is_reported(scaler: Path, tmp_path: Path) -> None:
    # A reworded row must fail loudly rather than pass because there is nothing left to match.
    reworded = tmp_path / "reworded.md"
    reworded.write_text("| `worldbox_screenshot` | See the guide. |\n", encoding="utf-8")

    report = gen_docs.Report()
    gen_docs.check_screenshot_defaults(
        report, scaler=scaler, python_values=MATCHING_PYTHON, reference=reworded
    )

    assert any("no longer states 'max_dimension=<value>'" in p for p in report.problems)


def test_the_real_tree_states_the_same_screenshot_defaults_on_both_sides() -> None:
    report = gen_docs.Report()
    gen_docs.check_screenshot_defaults(report)

    assert report.problems == []


def test_run_against_a_throwaway_tree_does_not_check_the_real_repo(
    surface: object, docs: Path
) -> None:
    # The screenshot check is repo-global. If run() applied it to every root, a real-repo
    # drift would fail every test in this file with a problem about files the throwaway tree
    # never created, which is exactly the coupling this file's fixtures exist to avoid.
    report = gen_docs.run(surface, docs, write=False)

    assert not any("screenshot" in p for p in report.problems)


def test_run_checks_the_screenshot_defaults_for_the_real_repository(
    surface: object, monkeypatch: pytest.MonkeyPatch
) -> None:
    # Only the negative side of run()'s root gate was pinned. If the gate ever became
    # always-false, or CI started passing `--root .`, the check would stop running and every
    # test here would still pass.
    calls: list[int] = []
    monkeypatch.setattr(gen_docs, "check_screenshot_defaults", lambda report: calls.append(1))

    # write=False matters: `surface` is a fake two-tool surface, and a write pass against the
    # real root would rewrite every generated region in the repository from it. Do not copy
    # this call with write=True.
    gen_docs.run(surface, gen_docs.REPO_ROOT, write=False)

    assert calls == [1]


def test_command_reference_row_without_a_default_format_is_reported(
    scaler: Path, tmp_path: Path
) -> None:
    # Listing `format="jpg"|"png"` says which values are legal, not which one you get, so the
    # row could have claimed PNG and nothing would have noticed.
    vague = tmp_path / "vague.md"
    vague.write_text(
        REFERENCE_ROW.replace('format="jpg"(default)', 'format="jpg"'), encoding="utf-8"
    )

    report = gen_docs.Report()
    gen_docs.check_screenshot_defaults(
        report, scaler=scaler, python_values=MATCHING_PYTHON, reference=vague
    )

    assert any("SCREENSHOT_FORMAT" in p for p in report.problems)


# ─── Release version and the compatibility matrix ─────────────────────────
#
# Four files state the release version and release-please bumps all four, so they are checked
# against each other. The matrix is then checked for a row covering that version: it is the one
# document that records whether a release actually works, it is written by hand, and a matrix a
# release behind reads exactly like a release nobody has reported a problem with.

MATRIX = """# Compatibility matrix

| WorldBox version | Unity | Scripting backend | Mod version | Status | Notes |
|---|---|---|---|---|---|
| **0.51.2** | 2022.3.60f1 | Mono | **0.5.0** | 🔵 | Shipped, unverified. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.3.0 to 0.3.3 | ❌ | DLLs never loaded. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.2.x | ✅ | Baseline. |
"""


def _tree(root: Path, version: str, matrix: str = MATRIX) -> Path:
    """A throwaway tree stating `version` in all four places release-please bumps."""
    for relative, body in [
        ("server/pyproject.toml", f'[project]\nversion = "{version}"\n'),
        ("server/src/worldbox_mcp/__init__.py", f'__version__ = "{version}"\n'),
        (
            "mod/src/WorldBoxBridge/WorldBoxBridge.csproj",
            f"<Project><PropertyGroup><Version>{version}</Version></PropertyGroup></Project>\n",
        ),
        (
            "mod/src/WorldBoxBridge/PluginInfo.cs",
            f'    public const string Version = "{version}";\n',
        ),
    ]:
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(body, encoding="utf-8")
    compatibility = root / "docs" / "compatibility.md"
    compatibility.parent.mkdir(parents=True, exist_ok=True)
    compatibility.write_text(matrix, encoding="utf-8")
    return root


def test_a_version_with_a_matrix_row_reports_nothing(tmp_path: Path) -> None:
    report = gen_docs.Report()
    gen_docs.check_release_version(report, root=_tree(tmp_path, "0.5.0"))

    assert report.problems == []


def test_a_version_the_matrix_never_mentions_is_reported(tmp_path: Path) -> None:
    report = gen_docs.Report()
    gen_docs.check_release_version(report, root=_tree(tmp_path, "0.6.0"))

    assert any("no row for 0.6.0" in p for p in report.problems)


def test_version_files_that_disagree_are_reported(tmp_path: Path) -> None:
    root = _tree(tmp_path, "0.5.0")
    (root / "server" / "pyproject.toml").write_text('[project]\nversion = "0.4.0"\n')

    report = gen_docs.Report()
    gen_docs.check_release_version(report, root=root)

    assert any("disagree on the version" in p for p in report.problems)


def test_a_version_declaration_that_moved_is_reported_rather_than_skipped(
    tmp_path: Path,
) -> None:
    # The dangerous failure is a pattern that stops matching: the file is still bumped, the
    # check just stops seeing it, and everything goes green.
    root = _tree(tmp_path, "0.5.0")
    (root / "mod/src/WorldBoxBridge/PluginInfo.cs").write_text("// nothing here\n")

    report = gen_docs.Report()
    gen_docs.check_release_version(report, root=root)

    assert any("no longer states a version" in p for p in report.problems)


def test_a_missing_matrix_is_reported(tmp_path: Path) -> None:
    root = _tree(tmp_path, "0.5.0")
    (root / "docs" / "compatibility.md").unlink()

    report = gen_docs.Report()
    gen_docs.check_release_version(report, root=root)

    assert any("compatibility.md is missing" in p for p in report.problems)


def test_a_matrix_whose_header_moved_is_reported_not_read_as_empty(tmp_path: Path) -> None:
    # The dangerous shape: returning [] here would read as "no row for this version" and send
    # the reader hunting a missing row rather than a broken parser.
    root = _tree(tmp_path, "0.5.0", matrix=MATRIX.replace("Scripting backend", "Backend"))

    report = gen_docs.Report()
    gen_docs.check_release_version(report, root=root)

    assert any("no row matching the expected matrix header" in p for p in report.problems)


def test_a_second_table_in_the_file_cannot_satisfy_the_check(tmp_path: Path) -> None:
    # A fourth column elsewhere on the page used to count. That is the silent green this whole
    # check exists to prevent, so it must not be how the check itself fails.
    decoy = MATRIX + (
        "\n## Steam builds\n\n"
        "| Build | Channel | Published | Note | Extra |\n"
        "|---|---|---|---|---|\n"
        "| 19962337 | public | 2025-09-13 | 0.6.0 | none |\n"
    )
    root = _tree(tmp_path, "0.6.0", matrix=decoy)

    report = gen_docs.Report()
    gen_docs.check_release_version(report, root=root)

    assert any("no row for 0.6.0" in p for p in report.problems)


def test_the_matrix_is_read_by_column_name_not_by_position(tmp_path: Path) -> None:
    # Every row here puts a plausible version in a column that is not "Mod version". Only the
    # real column may satisfy the check.
    shifted = MATRIX.replace(
        "| **0.51.2** | 2022.3.60f1 | Mono | **0.5.0** | 🔵 | Shipped, unverified. |",
        "| **0.51.2** | 0.9.9 | Mono | **0.5.0** | 🔵 | Shipped, unverified. |",
    )
    report = gen_docs.Report()
    gen_docs.check_release_version(report, root=_tree(tmp_path, "0.9.9", matrix=shifted))

    assert any("no row for 0.9.9" in p for p in report.problems)


@pytest.mark.parametrize(
    ("cell", "version", "covered"),
    [
        ("**0.5.0**", "0.5.0", True),
        ("0.5.0", "0.5.1", False),
        ("0.3.0 to 0.3.3", "0.3.2", True),
        ("0.3.0 to 0.3.3", "0.3.4", False),
        ("0.2.x", "0.2.7", True),
        ("0.2.x", "0.2.10", True),
        ("0.2.x", "0.3.0", False),
        # A wildcard that fixes only the major covered every 0.y.z release ever made.
        ("0.x", "0.9.9", False),
        ("", "0.5.0", False),
        ("(unreleased)", "0.5.0", False),
        ("`0.5.0`", "0.5.0", False),
        ("0.3.0 (upstream's own validation, Windows)", "0.3.0", True),
        ("see the notes", "0.5.0", False),
    ],
)
def test_matrix_cell_notations(cell: str, version: str, covered: bool) -> None:
    assert gen_docs.version_covers(cell, version) is covered


def test_the_real_tree_states_one_version_in_all_four_files() -> None:
    """The half of the real-tree check that is safe to assert from pytest.

    Deliberately not asserting the matrix row as well. release-please's PR bumps the four files
    to a version compatibility.md has no row for yet, and the pytest job carries no skip for
    that branch, so asserting the row here would turn the one PR the release process has to
    merge red on all nine matrix legs. The row is enforced by the ``--check`` CI step, which
    does carry the skip. The four-way agreement has no such problem: release-please moves all
    four together, so they agree on every branch including its own.
    """
    versions = {
        relative: re.search(pattern, (gen_docs.REPO_ROOT / relative).read_text()).group(1)  # type: ignore[union-attr]
        for relative, pattern in gen_docs.VERSION_SOURCES
    }

    assert len(set(versions.values())) == 1, versions


def test_run_checks_the_release_version_for_the_real_repository(
    surface: object, monkeypatch: pytest.MonkeyPatch
) -> None:
    calls: list[int] = []
    monkeypatch.setattr(gen_docs, "check_release_version", lambda report: calls.append(1))

    gen_docs.run(surface, gen_docs.REPO_ROOT, write=False)

    assert calls == [1]


def _install_pages(root: Path, version: str) -> Path:
    """The two install pages, each printing one sample response stating `version`."""
    for relative in gen_docs.HEALTH_SAMPLE_SOURCES:
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            '```json\n{\n  "ok": true,\n'
            f'  "mod_version": "{version}",\n'
            '  "worldbox_version": "0.51.2",\n'
            '  "unity_version": "2022.3.60f1"\n}\n```\n',
            encoding="utf-8",
        )
    return root


def test_install_samples_that_state_the_declared_version_report_nothing(tmp_path: Path) -> None:
    report = gen_docs.Report()
    root = _install_pages(_tree(tmp_path, "0.5.0"), "0.5.0")

    gen_docs.check_release_version(report, root=root)

    assert report.problems == []


def test_an_install_sample_a_release_behind_is_reported(tmp_path: Path) -> None:
    """The drift that went unnoticed across three releases, and why it matters.

    0.3.0 is the one family whose DLLs never load, so a reader with a dead mod comparing their
    own output against the sample saw the number they expected.
    """
    report = gen_docs.Report()
    root = _install_pages(_tree(tmp_path, "0.5.0"), "0.3.0")

    gen_docs.check_release_version(report, root=root)

    assert len(report.problems) == len(gen_docs.HEALTH_SAMPLE_SOURCES)
    assert all("0.3.0" in problem and "0.5.0" in problem for problem in report.problems)


def test_an_install_page_that_lost_its_sample_is_reported(tmp_path: Path) -> None:
    root = _install_pages(_tree(tmp_path, "0.5.0"), "0.5.0")
    (root / gen_docs.HEALTH_SAMPLE_SOURCES[0]).write_text("no sample here\n", encoding="utf-8")
    report = gen_docs.Report()

    gen_docs.check_release_version(report, root=root)

    assert len(report.problems) == 1
    assert "no longer shows a mod_version" in report.problems[0]


def test_a_tree_without_install_pages_is_not_a_failure(tmp_path: Path) -> None:
    """run() drives the release check against throwaway trees that never create the pages."""
    report = gen_docs.Report()

    gen_docs.check_release_version(report, root=_tree(tmp_path, "0.5.0"))

    assert report.problems == []
