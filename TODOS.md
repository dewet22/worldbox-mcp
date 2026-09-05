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

**In flight**: three open PRs, and none of them is yours to forget.

- **#58, the `load_world` fix.** Green, reviewed twice, waiting on one thing only: a check against
  a running WorldBox. Do not redo this work, read the Debt item below first.
- **#60, release-please proposing 0.5.1.** Cut from the single `refactor:` commit in #59, which
  is the useful discovery: `refactor:` bumps the patch here, while the `ci:` commits in #61
  contributed nothing to it, which is what the new convention is for. Its base is one merge
  behind `main`, so it regenerates on the next push. Its branch name starts with
  `release-please--`, so the compatibility-matrix check stands down on it as designed.
- **#57, from dewet22**, `feat:` adding radius, pulses and drag to `invoke_power` by driving the
  brush and toggle delegates. Green, unreviewed. It reaches for the three power delegates listed
  under "Not committed to" below, so read that entry before reviewing it.

**Next step**: run the live check for #58 and merge it. Then review #57.

**Known false on `main` right now**: `docs/architecture.md` line 141, the
`MAIN_THREAD_TIMEOUT` rows in `docs/protocol.md` and `docs/command-reference.md`. All three
describe the dispatcher's 30-second deadline as a watchdog that keeps the game from freezing. It
is a queueing deadline: `MainThreadDispatcher.Tick` tests it before calling `pending.Run()`, so it
drops an action that waited too long for a frame and does nothing at all about one that started.
Believing otherwise is what let the `load_world` hazard sit there. The correction is written and
sits in #58, so it lands with it. Until then, do not trust those three sentences.

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

- [ ] **Run the live check for #58, the `load_world` fix.** On `main` today, one `load_world`
      call still wedges the game for good: the command declares `RequiresMainThread => true` and
      then calls `File.ReadAllBytes` on a caller-supplied path, so a FIFO or a huge file blocks
      inside a frame until the process is killed. #58 fixes it and is green. What it cannot prove
      on a bare machine is that a load still works now that the command starts on a different
      thread. One `load_world` with `path: "save1"`, one with `bytes_b64`. The first also proves
      `GameSavePaths.Capture` ran, since a save name cannot resolve without it. Optionally point
      it at a FIFO: it should answer `BAD_ARGS` at once rather than blocking, which is the second
      half of the fix.
- [ ] **Two residuals #58 records and does not fix**, both needing the game to verify. There is no
      cap on in-flight requests, so N parallel loads allocate N saves at once, and a read
      blocking on a dead network mount still leaks a thread-pool thread with no deadline
      anywhere. One `SemaphoreSlim` in `HandleClientAsync` bounds both. And `load_world` has no
      `IsWorldLoading` pre-flight where `save_world` does, so two loads can queue two
      `loadMapFromBytes` invokes the dispatcher drains in the same frame.

## 💡 Not committed to

Ideas nobody has signed up for. The pointer that used to sit here, to a roadmap section in
CLAUDE.md, was dead: no such section exists, so the reasoning lives in each line below and in
the docs each one names.

- Single multi-tenant MCP server, so N agents no longer means N server processes.
- Auto-resolve `kingdom_claim: "auto:N"` on first world load. On its own this buys nothing: #59
  established that a kingdom claim scopes reads and not writes, and that no Action command a
  FactionPlayer can reach even names a kingdom. Real PvP write scoping needs both this and a
  command that takes a kingdom. See the section in [multi-agent.md](docs/multi-agent.md).
- `get_actor(name_or_id)`, and `terraform(action_id, x, y, radius)`.
- Opt-in JSONL message log for replay and post-mortem.

The remaining power delegates (`click_brush_action`, `toggle_action`, `click_special_action`)
have left this list: PR #57 implements the brush and toggle ones. Review it against gotcha 7 in
[game-api-notes.md](docs/game-api-notes.md), which is where the delegate families are written up.
