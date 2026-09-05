# TODOS, worldbox-mcp

> What to do next. This file keeps only the future. Anything done leaves here and lives in the
> CHANGELOG, which release-please generates from the commits.

## 🔄 Pick up here, 2026-09-05

**Where things stand**: `v0.5.0` is out. PyPI has `worldbox-mcp 0.5.0`, the GitHub release carries
`WorldBoxBridge-v0.5.0.zip` plus its `.sha256`, and `uv.lock` was refreshed after the bump. Nobody
has run it against a game yet, so [compatibility.md](docs/compatibility.md) lists it 🔵 rather than
✅. That marker is new: the matrix previously had no way to say "released, unverified", which is the
state every release sits in for a while.

**Breaking in 0.5.0**: a `FactionPlayer` agent calling `invoke_power` now gets `PERMISSION_DENIED`.
God powers are map-wide and WorldBox scopes none of them to a kingdom, so they carry the same gate
as `paint_tile`. Nothing legitimate is lost, `spawn` covers all 322 actor assets and still accepts
either scope. The alternative, classifying per power off `GodPower.tab_id`, was rejected because it
fails open: the game owns that field, no version documents it, and a new disaster in an unknown tab
would be allowed by default.

What landed today, across #54, #55 and #56:

- The bridge stopped lying about what it did. `load_world` reported `source: "path"` for a load that
  read nothing but the base64 payload, and every argument error came back as 500 `GAME_CRASH`.
  Commands raise `BridgeRejectionException(BadArgs)` directly now, so a 500 means something actually
  broke. Do not throw `ArgumentException` from a command, the router has no arm for it.
- Save path containment is decided by resolving, not by the shape of the string. Two rounds of
  prefix rules leaked in opposite directions before that landed, and the story is in the commit
  message of `a26ac03` if you are ever tempted to go back to prefix matching.
- `actionlint` runs in CI, pinned to 1.7.7 and verified by content hash. `uv lock --check` guards
  the lockfile and skips release-please's own branch.
- `gen-docs.py` also checks the screenshot defaults on both sides of the bridge, plus the third copy
  in the command reference row.
- Six Debt items cleared, three new ones recorded below.

**Two things that cost real time, both now documented**

- `gh pr merge --merge` puts the PR title in the merge commit body, and PR titles here are
  Conventional Commits, so release-please counts the work twice. Every entry in the 0.5.0 changelog
  that predates the fix is duplicated. Pass `--body` with a prose review note.
- release-please read the `BREAKING CHANGE` footer and proposed `1.0.0`. That was forced back to
  0.5.0 with a `Release-As:` footer, because the per-kingdom action scope is enforced nowhere and
  `load_world` can still hang the game. Expect the same on the next breaking change.

**In flight**: nothing. Working tree clean, `main` at the lockfile refresh, all 16 CI checks green.

**Next step**: the `load_world` main-thread hazard in the Debt section is the only item that can
hurt a user today. It wants its own PR and a manual check against a running WorldBox.

**Know before you touch anything**

- `packages.lock.json` is committed for both mod projects. Change a package version, restore
  normally, and CI fails with NU1004. Regenerate with
  `dotnet restore mod/WorldBoxBridge.sln --force-evaluate` and commit the result.
- Merge PRs with a merge commit, never a squash, and give that merge a prose body. Both halves
  matter, and only one of them used to be written down.
- Prose, comments and commits are all in English, and the repo is deliberately free of em dashes
  outside code blocks and table notation. Keep it that way.
- Every stated tool count is generated. Run `uv run python ../scripts/gen-docs.py --write` from
  `server/` after adding or removing a tool, do not edit the numbers by hand.
- After a release lands, run `uv lock` from `server/` and commit it. Skip it and the next ordinary
  PR fails on the lockfile check, which is the design rather than a bug.

---

## 🔴 Blocked

Nothing.

## 🎯 Next up

Nothing queued. The Debt section is the natural queue.

## 🧹 Debt

- [ ] **Verify the `load_world` threading fix against a running game.** The fix is in:
      `LoadWorldCommand` now reports `RequiresMainThread => false`, resolves the path and reads
      the file on the pool thread, and marshals nothing but the `loadMapFromBytes` call. Two
      things about it cannot be exercised on a bare machine and want a live check. First that a
      load still works at all, by `path` and by `bytes_b64`, since the command changed which
      thread it starts on. Second that `GameSavePaths.Capture()` really does run before any
      request: it samples `Application.persistentDataPath` in `Plugin.Awake` because that
      property is main-thread only, and if it is ever missed, every `load_world` by name fails
      with `GAME_CRASH` instead of resolving. A bare `save1` load proves both at once.
- [ ] **`save_world` can still stall a frame, and the load fix does not transfer.** The write
      that blocks is the game's own `SaveManager.saveWorldToDirectory`, which serializes the
      live world and writes it in one call, so it has to hold the main thread. Our own
      pre-invoke work there is `Path.GetFullPath` and two `MapBox` reads, no filesystem call, so
      there is nothing to move off-thread the way `load_world` moved its read. The residual is
      real but smaller: it needs an attacker who can plant a non-regular `map.wbox` at the
      destination, where `load_world` only needed a bad `path` argument. Fixing it properly
      means bounding the game's call, which nothing in net462 can do, or reimplementing the
      save format. Recorded rather than attempted.
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
