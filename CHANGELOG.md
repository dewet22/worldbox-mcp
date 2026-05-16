# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This file is maintained automatically by [release-please](https://github.com/googleapis/release-please)
from Conventional Commits.

## [0.1.1] — 2026-05-16

### Fixed
- `worldbox_list_kingdoms` and `worldbox_list_cities` now return live entries instead of
  always an empty list. Root cause: the reflection helper looked for `getSimpleList()` on
  the manager, which only exists on `SimSystemManager<,>` (the actor side) — never on
  `MetaSystemManager<,>` (the kingdom / city side). Both manager hierarchies share a common
  `CoreSystemManager<,>` base that implements `IEnumerable<T>`, so the fix iterates via
  the C# interface instead of a specific method name.
- `worldbox_get_world_state.kingdoms_alive` / `cities_alive` now report the real counts
  (same root cause, same fix — replaced the manual list count with the `Count` property
  inherited from `CoreSystemManager`).

### Changed
- `docs/game-api-notes.md` updated with verified reflection paths for every command
  (spawn, paint_tile, invoke_power, pause/resume/set_speed, generate/save/load world,
  screenshot), the `WorldTimeScaleAsset` ids actually accepted (including the undocumented
  `x10`, `x15`, `x20`), and the `CoreSystemManager` iteration contract.
- `docs/command-reference.md` rewritten as a real reference (was a Phase-3 stub). Covers
  the 20 tools, their args, and the full error envelope.
- `docs/index.md` and `README.md` reconciled: tool count is **20** in both places (was
  inconsistently 19 and 20).
- `docs/install/claude-code.md`: simplified — `uvx worldbox-mcp` is now the primary path
  (v0.1.0 is published on PyPI as of this release cycle); local-clone path kept as a
  fallback for testing unreleased commits.
- `docs/compatibility.md` upgraded with a real entry: WorldBox 0.51.2 × mod 0.1.1 is
  marked ✅ validated end-to-end.

## [0.1.0] — 2026-05-16

### Added
- First public release. 19 mod commands surfaced as 20 MCP tools (+ `worldbox_capabilities`
  meta tool):
  - **Meta**: `health`, `capabilities`
  - **Discovery**: `list_tiles`, `list_actors`, `list_powers`
  - **Action**: `invoke_power`, `spawn`, `paint_tile`
  - **Read**: `get_world_state`, `get_tile`, `list_kingdoms`, `list_cities`, `query_actors`,
    `screenshot`
  - **Control**: `pause`, `resume`, `set_speed`, `generate_world`, `save_world`,
    `load_world`
- BepInEx 5.x C# plugin (`WorldBoxBridge`) with HTTP API on `127.0.0.1:8723` and per-install
  auth token.
- Python MCP server published on PyPI as `worldbox-mcp` — `uvx worldbox-mcp` for instant
  use from any MCP client.
- Universal reflection-based discovery: `AssetCatalog` enumerates any of the ~150 typed
  asset libraries on `AssetManager` via the uniform `AssetLibrary<T>` contract.
- `MainThreadDispatcher` injected into Unity's `PlayerLoop` Update phase (rather than a
  `MonoBehaviour.Update()` — that gets destroyed shortly after Awake on this game).
- Levenshtein-based `did_you_mean` suggestions on every `UNKNOWN_ASSET` error.
- End-to-end ecology demo at `examples/scenarios/ecology_smoke.py`.
- Per-client wiring recipes for Claude Code / OpenCode / Codex / Cursor / Continue.

### Known issues
- `list_kingdoms` / `list_cities` / `get_world_state.{kingdoms,cities}_alive` always return
  0 even when kingdoms exist — **fixed in 0.1.1**.
