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
  0.5.0 with a `Release-As:` footer, because at the time the per-kingdom action scope was enforced
  nowhere and `load_world` could hang the game. Both have since been dealt with, but expect the
  same proposal on the next breaking change.

**In flight**: this PR plus two others, and none of them is yours to forget.

- **This one, #58.** `load_world` no longer reads the save from the Unity main thread, and a path
  that is not a regular file is refused before it is opened. One thing is still owed on it, a
  check against a running WorldBox. The Debt item below says exactly what to run.
- **#60, release-please proposing 0.5.1.** Cut from the single `refactor:` commit in #59, which
  is the useful discovery: `refactor:` bumps the patch here, while the `ci:` commits in #61
  contributed nothing to it, which is what the new convention is for. Its branch name starts with
  `release-please--`, so the compatibility-matrix check stands down on it as designed.
- **#57, from dewet22**, `feat:` adding radius, pulses and drag to `invoke_power` by driving the
  brush and toggle delegates. Green, unreviewed. Read the note at the end of "Not committed to"
  before reviewing it.

**Next step**: run the live check below. Then review #57.

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

- [ ] **One live `load_world` is still owed.** The fix landed in #58 without it, on static
      evidence, and the merge commit of `ccf4261` records exactly what that evidence was. A probe
      harness linked `SaveFileReader`, `SavePathResolver` and `GameSavePaths` out of the branch
      and ran them against real special files under a watchdog: a FIFO with no writer is refused
      in 2 ms where `File.ReadAllBytes` on that same FIFO was still blocked after 3 seconds,
      `/dev/zero` goes the same way, a 300 MB file is refused after allocating 1232 bytes, and a
      save name resolves under a sampled `persistentDataPath` and reads a real zip back whole,
      which is also the proof that the `Capture` handoff works. So the refusal half is settled.
      What is not: that a load still completes now that the command starts on a pool thread,
      which needs one `load_world` with `path: "save1"` and one with `bytes_b64`. Note also that
      the probe ran on Linux under .NET 8, and the zero-length signal for special files is a
      measurement on the wrong runtime for a plugin that ships against Mono net462.
- [ ] **No cap on in-flight requests, and no timeout on the read.** `HttpBridge` hands every
      accepted client to `Task.Run` with nothing bounding concurrency, so N parallel `load_world`
      calls allocate N saves at once. Worse, a regular file on a dead network mount still blocks
      in the read with no deadline anywhere: socket timeouts are spent once the request is parsed
      and the dispatcher's deadline is not on this path. Each such call costs a thread-pool
      thread, a descriptor and a socket for the life of the process. One `SemaphoreSlim` in
      `HandleClientAsync` bounds both. The FIFO and character-device cases, which were the easy
      way to trigger this, are now refused before the open.
- [ ] **`load_world` has no `IsWorldLoading` pre-flight, where `save_world` does.** Two loads can
      now do their reads in parallel and queue two `loadMapFromBytes` invokes that the dispatcher
      can drain in the same frame, the second landing on a load the game just started. Mirroring
      the `save_world` guard means checking `_world.IsWorldLoading` inside the marshalled
      delegate, so the check and the invoke share a frame, and injecting `WorldAccess` into the
      command. Not verifiable without the game, so it wants the same live pass as the item above.
- [ ] **Nobody has written down what `Brush.get(int, string)` returns.** The brush-machinery
      section of [game-api-notes](docs/game-api-notes.md) is headed "verified against the 0.51.2
      decompile" and records that this overload clones `circ_1` as `circ_N`, but not its return
      type, while the same section says the `Config.current_brush` setter fills
      `current_brush_data` "via `Brush.get(id)`". So at least one overload of that name answers
      with brush data rather than with the library asset, and `BrushAccess` cannot safely prefer
      the returned asset's `id` over the name it constructs. It now logs, once per build, when
      the two disagree, which is what a single live `invoke_power` with a radius turns into an
      answer. Write the return type down, then let `TryEnsureCircleBrush` prefer the real id and
      drop the guess. Same live pass as the `load_world` item above.
- [ ] **`GameRefs` caches members without their binding flags, and claims a consequence it
      cannot know.** `Field`, `Property` and `Method` all key on `$"{owner.FullName}.{name}"`
      with the flags left out, so two call sites asking for the same type and member under
      different flags silently share the first one's answer, including a cached null.
      `owner.FullName` is also null for some constructed types, which collapses every such
      lookup onto `".id"`. Nothing collides today, every live `Field` call site passes `Static`.
      Include the flags and a non-null identity in the key. While there, drop "Dependent
      commands disabled." from the three warnings: a missing member sometimes disables nothing,
      and the message is read by whoever is already debugging the wrong thing.
- [ ] **Per-frame jobs have no cap, where the action queue has one.** `MainThreadDispatcher`
      drains at most 32 queued actions per frame, but `ActiveJobs` steps every registered
      per-frame job every frame with nothing bounding how many there are. N concurrent
      `invoke_power` runs with `pulses` therefore cost N delegate invocations per frame for up to
      25 seconds each, which is a longer-lived version of the in-flight-request item above and
      wants the same `SemaphoreSlim` or a cap of its own. Found reviewing #57, not introduced by
      it: the primitive is new, the missing bound is the same one.
- [ ] **A cancelled request can be reported as a main-thread timeout.** The 60-second backstop in
      `HttpBridge` builds its timer from a token linked to the request token, so when that token
      is cancelled the timer task completes as cancelled, can win the `Task.WhenAny`, and the
      handler throws `TimeoutException` with a message about 60 seconds that did not elapse.
      Nothing in the handler catches `OperationCanceledException` either, so before #57 the same
      path produced a 500 `GAME_CRASH`. Both labels are wrong for "the bridge is shutting down".
      Cheap to fix by checking the token before deciding it was a timeout, and worth doing
      because the wrong label lands in the log of whoever is debugging a hang.
- [ ] **`save_world` can still stall a frame, and the load fix does not transfer.** The write
      that blocks is the game's own `SaveManager.saveWorldToDirectory`, which serializes the
      live world and writes it in one call, so it has to hold the main thread. Our own
      pre-invoke work there is `Path.GetFullPath` and two `MapBox` reads, no filesystem call, so
      there is nothing to move off-thread the way `load_world` moved its read. The residual is
      real but smaller: it needs an attacker who can plant a non-regular `map.wbox` at the
      destination, where `load_world` only needed a bad `path` argument. Fixing it properly
      means bounding the game's call, which nothing in net462 can do, or reimplementing the
      save format. Recorded rather than attempted.


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
