# TODOS, worldbox-mcp

> What to do next. This file keeps only the future. Anything done leaves here and lives in the
> CHANGELOG, which release-please generates from the commits.

## 🔄 Pick up here, 2026-09-05

**Where things stand**: v0.4.0 shipped. PyPI has `worldbox-mcp 0.4.0`, the GitHub release carries
`WorldBoxBridge-v0.4.0.zip` plus its `.sha256`, and CI attached them on its own for the first
time. All 15 CI jobs are green on `main`, including `Build mod` and `Test mod`, which used to be
`continue-on-error: true` because the runner had no Unity DLLs. That hack is gone. The mod builds
from a bare checkout with no game installed, on Windows, Linux and macOS.

Anyone still running a 0.3.x mod DLL has a plugin that silently fails to load. Tell them to
upgrade, because `LogOutput.log` looks perfectly normal in that state and the exception only
shows up in Unity's own `Player.log`.

The repo was also cleaned up for public reading after the release. Maintainer knowledge that used
to sit only in `CLAUDE.md` now lives in `docs/`, where contributors actually look: the ten game
API gotchas in `game-api-notes.md`, the build and release material in `development.md`, the tree
and the pinned versions in `architecture.md`. `CLAUDE.md` went from 437 lines to 124 and carries
no machine-specific paths any more. There is no `CODEMAP.md`: it duplicated `architecture.md` and
was removed rather than kept in sync.

**In flight**: nothing. Working tree is clean, `main` is at `2b690a2`.

**Next step**: fix `compat-check.yml`, see the blocked section below. One repo-level change, and
it has failed on every scheduled run since at least 2026-08-31, so nobody is being told when
WorldBox ships an update.

**Know before you touch anything**

- `packages.lock.json` is committed for both mod projects. Change a package version, restore
  normally, and CI fails with NU1004. Regenerate with
  `dotnet restore mod/WorldBoxBridge.sln --force-evaluate` and commit the result.
- Merge PRs with a merge commit, never a squash. The repo takes the PR title as the squash
  subject, so squashing hides the `feat:` commits inside and release-please skips the minor bump.
- Prose, comments and commits are all in English, and the repo is deliberately free of em dashes
  outside code blocks and table notation. Keep it that way.

---

## 🔴 Blocked

- [ ] `compat-check.yml` has failed on every scheduled run since at least 2026-08-31 with
      `could not add label: 'wb-update' not found`. The workflow opens an issue when a new
      WorldBox version appears and tags it `wb-update`, but that label does not exist in the
      repo. Nothing warns you about game updates until it is fixed. Create the label
      (`gh label create wb-update`) or drop the `--label` argument from the workflow.

## 🎯 Next up

- [ ] Review #46, `xunit.runner.visualstudio` 3.1.5 → 4.0.0. A new major, opened after the v0.4.0
      merges, not looked at yet.
- [ ] Decide whether `dismiss_window` should stay turn-gated. It is a `Control` command, so in a
      `turn_based` session only the agent whose turn it is can clear a window that is freezing
      the simulation for everyone. Other agents can see the block through `get_ui_state` but
      cannot act on it. Mostly theoretical while `suppress_startup_window` defaults to true, but
      it is a real asymmetry: closing a blocking window is a shared unblock, not a competitive
      move. See `HttpBridge` category gating and `Commands/Control/DismissWindowCommand.cs`.
- [ ] Roadmap item 4, `scripts/gen-docs.py`. Generate `docs/command-reference.md` from
      `worldbox_capabilities` instead of maintaining it by hand. Every count is correct right now,
      but they have drifted three separate times: the v0.4.0 review caught `docs/index.md` still
      claiming twenty-six tools and missing three of them outright. The number is stated in five
      places, so it will drift again unless it is generated.

## 🧹 Debt

Found during the pre-merge review of #37 to #42. None of these are regressions from that batch,
they are pre-existing and were surfaced, not introduced.

- [ ] `Commands/Control/LoadWorldCommand.cs:140` reports `source: "path"` and echoes the caller's
      raw path whenever `path` is non-empty, even when `bytes_b64` was supplied and actually used.
      The guard only rejects the case where both are empty. The response lies about what was read.
- [ ] `invoke_power` gates on `RequireAny(ActionFaction, ActionGlobal)`, while its sibling
      `paint_tile` requires `ActionGlobal` with an explicit comment about not letting a
      FactionPlayer reshape an opponent's territory. #42 widened which powers actually fire, so a
      FactionPlayer in a PvP session can now drop a volcano anywhere on the map. Either match
      `paint_tile`, or classify per power.
- [ ] `Commands/Control/SavePathResolver.cs` documents that a relative name can never escape the
      saves directory. That is not quite true: `Path.IsPathRooted` returns true for Windows
      drive-relative forms like `C:foo` and `\foo`, which skip the `..` check entirely. Behaviour
      is no worse than before the helper existed, but the stated invariant is false.
- [ ] `Commands/Control/SetSpeedCommand.cs:129` duplicates `ListSpeedsCommand.CurrentSpeedId`
      almost line for line, and its copy bypasses the `GameRefs` cache by calling `GetField`
      directly on every read. Extract one helper.
- [ ] `server/src/worldbox_mcp/tools/read.py` repeats the screenshot defaults (1280, jpg, 80) as
      bare literals that must track `ScreenshotScaler`'s constants. Nothing catches the drift.
      Either comment the coupling or pass `None` and let the bridge decide.
- [ ] `LoadWorldCommand.ResolveMapFile` has three untested branches. It cannot be linked into the
      test project today because it reads `GameSavePaths.SavesRoot`, which touches
      `Application.persistentDataPath`. Parameterise it the way `SavePathResolver.ResolveFolder`
      already takes `savesRoot`, then test it.
- [ ] `GameUiAccess` has no interface seam, so the branch logic in `DismissWindowCommand` and
      `GetUiStateCommand` cannot be unit tested even though it is Unity-type-free at the surface.
- [ ] Roadmap item 9: `fix(ci):` commits land under "Dependencies" in the generated changelog.
      Cosmetic, but easier to fix before the next minor than after.

## 💡 Not committed to

Carried over from the CLAUDE.md roadmap. Read that section for the reasoning behind each.

- Single multi-tenant MCP server, so N agents no longer means N server processes.
- Auto-resolve `kingdom_claim: "auto:N"` on first world load, which would make PvP scoping real
  rather than best-effort.
- The remaining power delegates: `click_brush_action`, `toggle_action`, `click_special_action`.
- `get_actor(name_or_id)`, and `terraform(action_id, x, y, radius)`.
- Opt-in JSONL message log for replay and post-mortem.
