# TODOS, worldbox-mcp

> What to do next. This file keeps only the future. Anything done leaves here and lives in the
> CHANGELOG, which release-please generates from the commits.

## 🔄 Pick up here, 2026-09-05, third session of the day

**Where things stand**: `main` is at `ec3dbab` and **`v0.6.0` is out**. #63 closed the previous
session, then #60 cut the release, squashed because it is release-please's own PR and that is the
single exception to the merge-commit rule. Its changelog landed six entries with no duplicates,
one `feat`, four `fix`, one `refactor`, which is the merge-body convention holding across five
merges. Both post-release chores are done: `uv lock` moved the project to 0.6.0, and
[compatibility.md](docs/compatibility.md) carries a 🔵 row for it. `gen-docs.py --check` is clean
and the tool count is still 29.

**The one thing owed to a running game, and it now costs more**: two releases in a row, 0.5.0 and
0.6.0, ship on static evidence alone. 213 xUnit and 80 pytest are green on this machine and not
one of them can see the game. The machine these sessions run on has no WorldBox install, so the
live pass is blocked on hardware, not forgotten. The Debt section leads with what a single live
run would settle, and the ZIP to install now downloads straight off the 0.6.0 release instead of
needing a rebuild.

**Do not redo these three arguments**, each cost a review round.

- `load_world` reads off the main thread and marshals only `loadMapFromBytes`. The reason is
  that the dispatcher's 30s deadline is a *queueing* deadline, tested before the action runs,
  so it stops nothing that has started. Gotcha 11 in
  [game-api-notes](docs/game-api-notes.md) is the canonical statement.
- **An `await` inside a command that reports `RequiresMainThread => true` does not resume on a
  pool thread.** Unity installs `UnityEngine.UnitySynchronizationContext` on the main thread, it
  is in the UnityEngine.Modules reference assembly the mod compiles against, and the engine
  pumps it from the player loop. The continuation comes back to the main thread but outside the
  dispatcher, so it escapes the deadline and the `maxPerFrame` bound. #62 first shipped the
  opposite claim and an adversarial pass caught it. Reaching for `ConfigureAwait(false)` or
  `Task.Run` to get back onto the main thread is what leaves it.
- **`BrushAccess` deliberately does not trust the id the game hands back.** Preferring it over
  the constructed `circ_<radius>` swaps a value that is correct on stock builds for one whose
  provenance nobody has verified, because `Brush.get(int, string)`'s return type is recorded
  nowhere. It logs disagreement instead. The Debt item says what one live call turns that into.

**Know before you touch anything**

- `packages.lock.json` is committed for both mod projects, and `mod/Directory.Build.props` sets
  `RestorePackagesWithLockFile`, so this is real: change a package version and CI fails with
  NU1004 until you regenerate with `dotnet restore mod/WorldBoxBridge.sln --force-evaluate`.
- Merge PRs with a merge commit, never a squash, and give that merge a prose body that is not a
  Conventional Commit. Both halves matter. #60's clean changelog is the evidence.
- Prose, comments and commits are all in English, and the repo is deliberately free of em dashes
  outside code blocks and table notation.
- Every stated tool count is generated. Run `uv run python ../scripts/gen-docs.py --write` from
  `server/`, never edit a count by hand.
- Every release owes three chores, all done for 0.6.0 and all worth remembering for 0.7.0: run
  `uv lock` from `server/` and commit it, write the new row in
  [compatibility.md](docs/compatibility.md), and bump the `mod_version` shown in the two sample
  responses under `docs/install/`. Skip any of them and the next ordinary PR fails on the
  lockfile step or on `gen-docs.py --check`, which is the design. The third one is new: that
  sample said 0.3.0 for three releases running, which is the family whose DLLs never load.
- `.NET` on this box: `export DOTNET_ROOT=$HOME/.dotnet` and
  `PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"`.
- There is no `CODEMAP.md` and no `archives/` here, on purpose. A context audit will flag both,
  plus a missing `docs/README.md`, the CHANGELOG date format, missing status headers on the
  `docs/` pages, a PowerShell snippet in `multi-agent.md` read as dead links, and two test
  tokens one of which is literally named `test-token-do-not-use`. All expected, none real.

**Next step**: the live pass, and nothing else is close in value. Install the 0.6.0 ZIP from the
GitHub release on a machine that has WorldBox and run the four checks the Blocked section names.
One session there closes four Debt items and turns both 🔵 rows into ✅ or into bug reports. The
next thing after that, if the live pass has to wait, is the `SemaphoreSlim` in `HandleClientAsync`,
which needs no game and closes two Debt items at once.

---

## 🔴 Blocked

- **Every live verification, on hardware rather than on a decision.** The machine these sessions
  run on has no WorldBox install, so nothing in the Debt list that needs a running game can be
  closed from here. Four items are waiting on one session at a machine that has the game: the
  `load_world` load path, the `IsWorldLoading` pre-flight, what `Brush.get(int, string)` returns,
  and dewet22's two untested guards in `invoke_power`. The ZIP no longer needs rebuilding:
  `WorldBoxBridge-v0.6.0.zip` and its `.sha256` hang off the
  [v0.6.0 release](https://github.com/fullya99/worldbox-mcp/releases/tag/v0.6.0), built by CI the
  way `release.yml` does it, Release plus `-warnaserror` plus `restore --locked-mode`, with the
  DLL staged by `scripts/install-mod.ps1` alongside the README and the LICENSE.

## 🎯 Next up

1. **The live pass on a machine that has the game.** Everything else in this file is either
   blocked behind it or worth less than it. See the Blocked section for the four checks and the
   install recipe.
2. **Bound the in-flight work**, the one Debt item that needs no game and closes two: a
   `SemaphoreSlim` in `HttpBridge.HandleClientAsync` caps concurrent requests, and the per-frame
   job registry wants the same bound or one of its own.
3. Then the rest of the Debt section, which is the natural queue.

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
