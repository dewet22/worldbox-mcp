# TODOS, worldbox-mcp

> What to do next. This file keeps only the future. Anything done leaves here and lives in the
> CHANGELOG, which release-please generates from the commits.

## 🔄 Pick up here, 2026-09-05

**Where things stand**: v0.4.0 shipped and the four items that were queued after it are done.
PyPI has `worldbox-mcp 0.4.0`, the GitHub release carries `WorldBoxBridge-v0.4.0.zip` plus its
`.sha256`, and CI attached them on its own for the first time. The mod builds and tests from a
bare checkout with no game installed, on Windows, Linux and macOS.

Anyone still running a 0.3.x mod DLL has a plugin that silently fails to load. Tell them to
upgrade, because `LogOutput.log` looks perfectly normal in that state and the exception only
shows up in Unity's own `Player.log`.

What landed after the release:

- `compat-check.yml` works again (#48). It had failed on every scheduled run since at least
  2026-08-24, and the missing `wb-update` label was only the outermost of three faults. Steam's
  `UpToDateCheck` endpoint does not know appid 1206560 and answers with an error body, which the
  workflow stored as the current version, then compared against a file that never existed. It now
  reads the `public` branch build id from `api.steamcmd.net` and compares it against
  `.github/worldbox-build-baseline.txt`, seeded with build `19962337`, which is 0.51.2. The
  `wb-update` and `needs-triage` labels exist now, and `needs-triage` is referenced by both issue
  templates, so every bug report filed so far had silently lost it.
- `xunit.runner.visualstudio` 4.0.0 (#46), reviewed and merged. The major is an alignment with
  the core framework, not a break: same target frameworks, still runs xunit v1/v2/v3, and 104
  tests were discovered before and after. Worth knowing for later: upstream says the package
  will probably be deprecated once the third-party VSTest runners move to Microsoft Testing
  Platform.
- `dismiss_window` is no longer turn-gated (#50). An open window freezes the simulation for the
  whole session, so clearing it is a shared unblock, not a move. The decision moved into a
  `TurnGate` class the test project can link, which `HttpBridge` cannot.
- `scripts/gen-docs.py` (#52) generates the tool counts and verifies the inventories, with
  `--check` wired into CI. See [development.md](docs/development.md) for how it works.

**In flight**: nothing on the working tree. release-please has #49 open, `chore(main): release
0.5.0`, because the `dismiss_window` change landed as a `feat`. Merge it when you want 0.5.0 cut,
and squash that one, it is the single exception to the merge-commit rule.

**Next step**: nothing is blocking. Cut 0.5.0, then pick from the Debt section.

**Know before you touch anything**

- `packages.lock.json` is committed for both mod projects. Change a package version, restore
  normally, and CI fails with NU1004. Regenerate with
  `dotnet restore mod/WorldBoxBridge.sln --force-evaluate` and commit the result.
- Merge PRs with a merge commit, never a squash. The repo takes the PR title as the squash
  subject, so squashing hides the `feat:` commits inside and release-please skips the minor bump.
- Prose, comments and commits are all in English, and the repo is deliberately free of em dashes
  outside code blocks and table notation. Keep it that way.
- Every stated tool count is generated. Run `uv run python ../scripts/gen-docs.py --write` from
  `server/` after adding or removing a tool, do not edit the numbers by hand.

---

## 🔴 Blocked

Nothing.

## 🎯 Next up

Cut 0.5.0. See the header block.

## 🧹 Debt

- [ ] Roadmap item 9: `fix(ci):` commits land under "Dependencies" in the generated changelog.
      Cosmetic, but easier to fix before the next minor than after.
- [ ] `RequestContext.RequireKingdomAccess` has no call site anywhere in the mod. The method is
      documented as the guard for "who may act on which kingdom" and nothing invokes it, so the
      per-kingdom action scope is not enforced at all. Either wire it into `spawn` (the one
      Action command a FactionPlayer can still reach) or delete it and say plainly in
      [multi-agent.md](docs/multi-agent.md) that claims scope reads, not writes.
- [ ] `docs/compatibility.md` is still updated by hand after a release, and nothing checks it.

## 💡 Not committed to

Carried over from the CLAUDE.md roadmap. Read that section for the reasoning behind each.

- Single multi-tenant MCP server, so N agents no longer means N server processes.
- Auto-resolve `kingdom_claim: "auto:N"` on first world load, which would make PvP scoping real
  rather than best-effort.
- The remaining power delegates: `click_brush_action`, `toggle_action`, `click_special_action`.
- `get_actor(name_or_id)`, and `terraform(action_id, x, y, radius)`.
- Opt-in JSONL message log for replay and post-mortem.
