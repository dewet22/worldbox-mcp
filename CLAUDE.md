# CLAUDE.md — context for future Claude Code sessions

Auto-loaded by Claude Code when working in this repo. Read this first; it'll save you 30+ minutes of re-discovery.

---

## What this project is

**`worldbox-mcp`** is a two-piece bridge that lets any MCP-compatible AI client (Claude Code, OpenCode, Codex, Cursor, Continue, …) directly control the live game **[WorldBox](https://www.superworldbox.com/)** (Unity 2022.3.60f1, Mono backend).

Two components, shipped from this monorepo:

1. **`WorldBoxBridge`** (`mod/`): C# BepInEx 5 plugin (.NET Framework 4.6.2) injected into `worldbox.exe`. Exposes a local-only authenticated HTTP API on `127.0.0.1:8723` to the game's internals via reflection.
2. **`worldbox-mcp`** (`server/`): Python 3.11+ MCP server, distributed on **[PyPI](https://pypi.org/project/worldbox-mcp/)** as `worldbox-mcp`. Auto-discovers the mod's auth token, proxies MCP tool calls to the bridge.

End user runs `claude mcp add worldbox -- uvx worldbox-mcp` (or equivalent for their client). The server spawns on demand via `uvx`, talks HTTP to the bridge, no manual install or Python virtualenv needed.

**29 MCP tools** across meta / discovery / action / read / control / bus — see `docs/command-reference.md` for the live list. Multi-agent layer (v0.3+) is documented in `docs/multi-agent.md`: same architecture supports four scenarios (PvP / coop / hierarchical / sandbox) configured via `BepInEx/config/WorldBoxBridge.agents.json`. If that file is absent, the bridge runs in legacy single-tenant mode.

---

## Status snapshot (2026-05-17)

- **Latest tag**: `v0.3.1` (PyPI `worldbox-mcp 0.3.1`, GitHub Release Latest, mod ZIP attached).
- **Branch**: `main` is the shipping branch; release-please continuously maintains a `chore(main): release X.Y.Z` PR with the next bump as commits land.
- **Docs site**: `https://fullya99.github.io/worldbox-mcp/` (default GitHub Pages URL — the user-level `fullya99/fullya99.github.io` repo had a stale CNAME to a dead `fullya.me` domain; removed 2026-05-17 so all project sites default to `<user>.github.io/<project>/`).
- **CI**: the mod builds on bare runners — UnityEngine references come from the `UnityEngine.Modules` NuGet package (BepInEx feed), so `Build mod` / `Test mod` are hard-failing jobs and `build-and-attach-mod` attaches the ZIP automatically.
- **Recently shipped on this branch**: full multi-agent session layer (Phases 1–7) + `AdvanceTime` permission split + docs refresh + CI cleanup. See `~/.claude/plans/ok-j-aimeraisd-que-tu-purrfect-pearl.md` for the execution log.

Memory files for this project live at `C:\Users\fullya\.claude\projects\C--worldbox-mcp\memory\` — load `MEMORY.md` for the index.

---

## Repo layout (the parts that matter)

```
mod/                                   BepInEx C# plugin
├── WorldBoxBridge.sln
├── Directory.Build.props              Nullable + warnings-as-errors + WorldBoxManagedDir resolution
├── Directory.Packages.props           Central NuGet versions
├── NuGet.config                       Explicit nuget.org + bepinex.dev feeds
├── src/WorldBoxBridge/
│   ├── Plugin.cs                      BepInEx entry — wires config, session, dispatcher, registry, HTTP
│   ├── BridgeConfig.cs                Token + host + port + enabled + suppress_startup_window
│   ├── PluginInfo.cs                  Version constant — tracked by release-please via the
│   │                                    `// x-release-please-version` marker, do NOT edit by hand
│   ├── Http/
│   │   ├── HttpBridge.cs              TcpListener-based HTTP/1.1 (NOT HttpListener — see Gotchas).
│   │   │                                Accepts both Authorization: Bearer (v0.3+) and X-WB-Token (legacy)
│   │   ├── ErrorCode.cs               String constants for public error codes (linkable from tests)
│   │   └── ErrorEnvelope.cs           Unified JSON success / error shape (Newtonsoft.Json bound)
│   ├── Session/                       (v0.3) Multi-agent layer
│   │   ├── AgentRole.cs               Enum: God / FactionPlayer / Observer / Narrator
│   │   ├── Permission.cs              Bitflag perms + PermissionDefaults per role
│   │   ├── Agent.cs                   Per-agent record (id, token, role, claim, perms, objectives)
│   │   ├── AgentRegistry.cs           Constant-time token → Agent lookup
│   │   ├── RequestContext.cs          Per-request identity threaded into every ICommand call
│   │   ├── Session.cs                 Singleton: scenario preset, partial_intel + turn_based flags
│   │   ├── SessionLoader.cs           Parses agents.json (JSON, not TOML — Newtonsoft already loaded)
│   │   ├── TurnOrder.cs               Thread-safe round-robin rotation
│   │   ├── MessageBus.cs              In-memory pub-sub, bounded per-agent inboxes
│   │   └── Objective.cs               Free-form per-agent goal metadata (scoreboard primitive)
│   ├── Threading/
│   │   └── MainThreadDispatcher.cs    Injects callback into Unity PlayerLoop.Update
│   ├── Reflection/
│   │   ├── GameRefs.cs                Cached Type.GetType lookups, fail-soft
│   │   ├── AssetCatalog.cs            Generic enumeration of AssetManager.* libraries
│   │   ├── WorldAccess.cs             MapBox.instance + units/kingdoms/cities accessors
│   │   └── VersionDetector.cs         Game version + Assembly-CSharp SHA256
│   ├── Commands/
│   │   ├── ICommand.cs                Command interface (takes RequestContext) + CommandCategory enum
│   │   ├── CommandRegistry.cs         name → ICommand registry
│   │   ├── HealthCommand.cs           Meta — also reports multi_agent / scenario / agent_count
│   │   ├── Meta/                      whoami, session_info, turn_advance, objective_status (v0.3+)
│   │   ├── Discovery/                 list_tiles, list_actors, list_powers, list_speeds
│   │   ├── Action/                    invoke_power, spawn, paint_tile (+ BridgeRejectionException
│   │   │                                in its own file, linkable from tests)
│   │   ├── Read/                      get_world_state, get_tile, list_kingdoms, list_cities,
│   │   │                                query_actors (faction-filtered), screenshot, get_ui_state
│   │   ├── Control/                   pause, resume, set_speed, dismiss_window, generate_world,
│   │   │                                save_world, load_world
│   │   └── Bus/                       send_message, recv_messages (v0.3+)
│   └── AssetSuggester.cs              Levenshtein for did_you_mean
└── tests/WorldBoxBridge.Tests/        xUnit, net8, linked-sources (no Unity dep).
                                       69 cases incl. AgentRegistry / RequestContext / TurnOrder
                                       / MessageBus matrix coverage.

server/                                Python MCP server
├── pyproject.toml                     Hatchling, deps: mcp, httpx, pydantic, structlog
├── src/worldbox_mcp/
│   ├── __init__.py                    __version__ (tracked by release-please via marker)
│   ├── __main__.py                    CLI: stdio (default), --http, --self-check
│   ├── server.py                      FastMCP factory + tool registration
│   ├── transport.py                   stdio / Streamable HTTP selector
│   ├── client.py                      httpx wrapper, sends Authorization: Bearer (per-call token
│   │                                    override available for multi-tenant front-ends)
│   ├── config.py                      Env + auto-discover BepInEx config
│   ├── errors.py                      BridgeError + TransportError
│   └── tools/
│       ├── meta.py                    health / capabilities / whoami / session_info / turn_advance
│       │                                / objective_status
│       ├── discovery.py               worldbox_list_*
│       ├── action.py                  worldbox_invoke_power / spawn / paint_tile
│       ├── read.py                    worldbox_get_* / list_* / query_actors / screenshot
│       ├── control.py                 worldbox_pause / resume / set_speed / generate / save / load
│       └── bus.py                     (v0.3) worldbox_send_message / recv_messages
└── tests/                             pytest (unit + integration with fake bridge + e2e). 18 cases.

docs/                                  MkDocs Material — published at fullya99.github.io/worldbox-mcp/
├── index.md
├── architecture.md                    Component layout, thread model, session layer
├── multi-agent.md                     (v0.3) Multi-agent walkthrough: roles, perms, fog, bus, presets
├── protocol.md                        HTTP/JSON spec, both Authorization: Bearer + legacy X-WB-Token
├── command-reference.md               29 tools + error codes
├── game-api-notes.md                  ★ verified reflection paths into WorldBox internals
├── compatibility.md                   WorldBox × mod version matrix
├── development.md                     local dev + testing
└── install/                           One page per client (claude-code, opencode, codex, cursor, continue, manual)

examples/
├── client-configs/                    JSON snippets to paste into each client
├── prompts/                           Demo prompts (godmode-ecology, build-civilization, …)
└── scenarios/
    ├── ecology_smoke.py               Single-agent end-to-end agentic loop (legacy mode)
    └── multi-agent/                   (v0.3) Four scenario presets + README + pvp_smoke.py e2e
        ├── pvp.json / coop.json / hierarchical.json / sandbox.json
        ├── README.md                  Side-by-side comparison + token-generation snippet
        └── pvp_smoke.py               Two-agent PvP end-to-end smoke (BridgeClient × 2)

scratch/                               Decompiled Assembly-CSharp.dll types — gitignored
                                       Regenerate via the ilspycmd one-liners below.

scripts/
├── install-mod.ps1 / .sh              Downloads BepInEx + DLL release, generates token
├── dev-setup.ps1                      winget .NET SDK 8 + uv (Windows dev bootstrap)
└── verify-install.ps1                 Post-install sanity check
```

---

## Day-to-day commands

All assume the repo root as cwd.

**Pre-flight for any PowerShell session on Windows** (dotnet isn't on PATH by default here):

```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"
$env:PATH="$env:USERPROFILE\.dotnet;$env:USERPROFILE\.dotnet\tools;"+$env:PATH
```

No game install is needed to build: UnityEngine references come from NuGet (`UnityEngine.Modules`, pinned to the game's engine version in `Directory.Packages.props`) and the mod touches `Assembly-CSharp` only via reflection.

```powershell
# Build mod (Release)
dotnet build C:\worldbox-mcp\mod\WorldBoxBridge.sln --configuration Release

# Test mod (xUnit, ~70 cases, no game needed — linked-source pattern)
dotnet test C:\worldbox-mcp\mod\tests\WorldBoxBridge.Tests\WorldBoxBridge.Tests.csproj --configuration Release

# Format check + auto-format (csharpier 0.30.6 pinned — see CI ops notes below)
dotnet csharpier --check C:\worldbox-mcp\mod
dotnet csharpier C:\worldbox-mcp\mod

# Deploy mod to running install
Get-Process worldbox -EA SilentlyContinue | Stop-Process -Force
Copy-Item C:\worldbox-mcp\mod\src\WorldBoxBridge\bin\Release\WorldBoxBridge.dll `
          'X:\GAMES\steamapps\common\worldbox\BepInEx\plugins\WorldBoxBridge.dll' -Force
Start-Process 'X:\GAMES\steamapps\common\worldbox\worldbox.exe'

# Liveness probe (use Authorization: Bearer; X-WB-Token still works as legacy fallback)
Invoke-WebRequest -Uri 'http://127.0.0.1:8723/health' `
  -Headers @{ 'Authorization' = "Bearer $(Get-Content 'X:\GAMES\steamapps\common\worldbox\BepInEx\config\WorldBoxBridge.cfg' | Select-String '^token = ' | ForEach-Object { $_ -replace '^token = ','' })" } `
  -TimeoutSec 5 -SkipHttpErrorCheck
```

**macOS dev loop** (the mod builds and runs on macOS too; the Steam install is an app bundle):

```bash
# .NET SDK via Homebrew formula (the cask needs an interactive sudo). SDK 10 builds the
# net462 mod fine; the net8.0 test project and 0.x csharpier need a runtime roll-forward.
brew install dotnet
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec DOTNET_ROLL_FORWARD=Major PATH="$HOME/.dotnet/tools:$PATH"

# Build + test. No game install needed: Unity refs come from the UnityEngine.Modules NuGet
# package, and the mod reaches Assembly-CSharp only through reflection.
dotnet build mod/WorldBoxBridge.sln --configuration Release
dotnet test mod/tests/WorldBoxBridge.Tests/WorldBoxBridge.Tests.csproj --configuration Release
dotnet tool install -g csharpier --version 0.30.6 && dotnet csharpier --check mod

# Deploy. The game must be started through run_bepinex.sh (Steam launch option
# "<worldbox>/run_bepinex.sh" %command%), otherwise BepInEx is not loaded at all.
WB="$HOME/Library/Application Support/Steam/steamapps/common/worldbox"
osascript -e 'tell application "WorldBox" to quit'
cp mod/src/WorldBoxBridge/bin/Release/WorldBoxBridge.dll "$WB/BepInEx/plugins/"
(cd "$WB" && sh ./run_bepinex.sh "$WB/worldbox.app/Contents/MacOS/WorldBox" &)

# Logs: BepInEx/LogOutput.log for the plugin; plugin *load* exceptions only appear in Unity's
# own log at ~/Library/Logs/mkarpenko/WorldBox/Player.log.
```

```bash
# Python (works the same in bash + PowerShell since uv handles env vars)
cd server && uv sync --all-extras && uv run pytest tests/unit tests/integration
cd server && uv run ruff check . && uv run ruff format --check . && uv run mypy --strict src
cd server && uv run worldbox-mcp --self-check --no-bridge-required

# Run the multi-agent e2e PvP smoke against a live game (after deploying the mod + agents.json)
cd C:\worldbox-mcp && uv --project server run python examples/scenarios/multi-agent/pvp_smoke.py

# Single-agent legacy demo
cd server && uv run python ../examples/scenarios/ecology_smoke.py
```

```powershell
# Decompile a game type — drop output into scratch/ for grepping (gitignored)
ilspycmd -t MapBox -r 'X:\GAMES\steamapps\common\worldbox\worldbox_Data\Managed' `
  'X:\GAMES\steamapps\common\worldbox\worldbox_Data\Managed\Assembly-CSharp.dll' > scratch\MapBox.cs
# Generic types use backtick-arity, e.g. ilspycmd -t 'AssetLibrary`1' ...
```

---

## Conventions

- **Commits**: [Conventional Commits](https://www.conventionalcommits.org/) **in English**. `feat:` / `fix:` / `docs:` / `chore:` / `ci:` / `test:` / `refactor:` / `perf:`. release-please reads them to bump SemVer + write CHANGELOG.
- **C# style**: `<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Format via `csharpier`.
- **Python style**: `ruff` (format + lint) + `mypy --strict`. Type-annotate everything; `Any` only at MCP boundaries.
- **No `[email protected]` in workflows**: use real action refs like `pre-commit/[email protected]`. Cloudflare email-obfuscation has bitten us — `actionlint` catches it.
- **No `System.ValueTuple`**: not always loadable under Unity Mono on net462. Use plain `readonly struct` for multi-value returns / dict keys.
- **All Unity API calls go through `MainThreadDispatcher.RunOnMainThreadAsync`**. Anything else corrupts game state silently.

---

## CI/CD operational notes (v0.3.1 cleanup, deliberate quirks)

These choices are baked into `.github/workflows/` + `.github/dependabot.yml`. Each one fixes a real issue surfaced during the v0.3 release cycle — please don't "tidy them up" without reading why.

- **csharpier is pinned to 0.30.6** (`ci.yml#Install csharpier`). csharpier 1.x changed the CLI (`csharpier check .` instead of `dotnet csharpier --check .`) and added new XML formatting defaults that would reformat the whole csproj/props tree. Upgrading is a deliberate decision — track it in a PR by itself.
- **`mkdocs.yml` lives at the repo root**, not `docs/`. mkdocs 1.x rejects configs where `docs_dir` is the parent of the config file. Layout: `mkdocs.yml` at root with `docs_dir: docs`, `site_dir: site`.
- **Unity references come from `UnityEngine.Modules` on the BepInEx NuGet feed** (`NuGet.config` maps that exact id there, not the `UnityEngine.*` wildcard; nuget.org's copy stops at 2021.3). The version must match the game's engine (`/health` → `unity_version`); dependabot ignores it. `Assembly-CSharp.dll` is not referenced at all — everything game-specific is reflection — which is what makes bare-runner builds possible.
- **`packages.lock.json` is committed** for both mod projects, via `RestorePackagesWithLockFile` in `Directory.Build.props`. The workflows have always run `dotnet restore --locked-mode`, but with no lock file that flag silently verifies nothing. It matters here because the Unity refs come from a third-party feed and a restore-time `.targets`/`.props` executes during `dotnet build`. After a deliberate version bump, regenerate with `dotnet restore mod/WorldBoxBridge.sln --force-evaluate` and commit the result, otherwise CI fails with NU1004.
- **`release.yml` scopes `permissions:` per job, not workflow-wide.** `build-and-attach-mod` restores and builds NuGet packages, so it gets `contents: write` and deliberately no `id-token: write` — only `publish-pypi` holds the PyPI trusted-publishing token. Don't hoist those back to the top of the file.
- **GitHub Pages was bootstrapped via `gh api repos/.../pages -X POST -F build_type=workflow`** (one-time). The default `GITHUB_TOKEN` lacks the admin scope to *create* the Pages site, but `actions/configure-pages@v5 with: enablement: true` is idempotent afterward.
- **No user-level CNAME**: `fullya99/fullya99.github.io` used to carry a `CNAME` file pointing at `fullya.me`, which silently 301-redirected *every* project site under the user — including this one — to a dead domain. Removed 2026-05-17 (commit `6af015a` in that repo). If a project ever needs a custom domain again, prefer a per-project `docs/CNAME` instead of the user-level one so the blast radius stays bounded.
- **FluentAssertions is capped at 6.x** in `.github/dependabot.yml`. v7+ ships under a paid Xceed commercial license (~$130/dev/year). v6 is the last MIT/Apache release. Migration alternatives if needed: AwesomeAssertions or Shouldly.
- **The Pre-commit job does not run csharpier** (no .NET SDK on `pre-commit/[email protected]`'s ubuntu-latest runner). The dedicated `lint-mod` job covers it.
- **MCP conformance check runs `worldbox-mcp --self-check --no-bridge-required`** directly — *not* the MCP Inspector CLI, which now requires `--method <jsonrpc>` to be useful.

---

## Adding a new MCP tool — 5-minute checklist

1. **Mod side** (`mod/src/WorldBoxBridge/Commands/<Category>/<Name>Command.cs`):
   - Implement `ICommand`. Pick a `CommandCategory` (Meta, Discovery, Action, Read, Control, Bus). Set `RequiresMainThread`.
   - Signature is `Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken)`. Call `ctx.Require(Permission.X)` at the top to gate the command; use `ctx.CanSeeKingdom` / `ctx.RequireKingdomAccess` for fog-of-war / faction binding.
   - Reuse `AssetCatalog.Resolve` for any asset-id input (gives free `did_you_mean`).
   - Reuse `WorldAccess` for `MapBox` / `units` / `kingdoms` / `cities` access.
   - Throw `BridgeRejectionException` (in `Commands.Action` namespace, its own file) for structured errors. HttpBridge maps it to the right HTTP status + envelope.
   - Be mindful of category semantics: **Action/Control are turn-gated** in turn_based sessions; **Meta/Discovery/Read/Bus are not** (so `turn_advance` lives in Meta, not Control, to avoid permanent deadlock).
2. **Register** in `mod/src/WorldBoxBridge/Plugin.cs#RegisterCommands` (one line).
3. **Python side** (`server/src/worldbox_mcp/tools/<category>.py`):
   - Add a `@server.tool(name="worldbox_<your_name>", description=...)` function.
   - **Description matters**: it's what Claude reads to decide when to call your tool. Be concrete about inputs/outputs and edge cases.
4. **Update** `docs/command-reference.md` with the new tool in its category table. If it's multi-agent-related, also update `docs/multi-agent.md`.
5. **Build + deploy + smoke-test** with the cheat-sheet commands above.

---

## Game API gotchas (the hard-won lessons)

These are bugs / mismatches we hit and fixed. **If something in the reflection layer breaks, check these first.**

1. **`System.Net.HttpListener` silently doesn't bind** under Unity 2022.3 Mono. `IsListening == true` but `netstat` shows no port. Use `TcpListener` + a hand-rolled HTTP/1.1 parser — that's why `HttpBridge.cs` looks like it does. See [Unity Discussions #755558](https://discussions.unity.com/t/httplistener-ignores-port-on-some-windows-platform-s/755558).

2. **`new TcpListener(IPAddress.Parse("127.0.0.1"), port)` silently fails to bind**. The `Parse` path produces an `IPAddress` instance Mono treats differently from the static constant. Always use `IPAddress.Loopback` (or `IPAddress.IPv6Loopback` / `IPAddress.Any` if you actually want those). `BridgeConfig.AssertLoopbackOnly` + `HttpBridge`'s host-to-IPAddress switch enforces this.

3. **BepInEx `MonoBehaviour` GameObjects get destroyed shortly after Awake** on this game. Our `MainThreadDispatcher` does NOT live on a MonoBehaviour — it injects a delegate directly into Unity's `PlayerLoop` Update phase (`UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop`). The `PlayerLoop` entry is part of the engine's tick table and survives lifecycle quirks.

4. **`SimSystemManager<,>` has `getSimpleList()`; `MetaSystemManager<,>` does NOT.** Both manager families inherit from `CoreSystemManager<,>` which implements `IEnumerable<T>`. To iterate any manager (Actor / Kingdom / City) **use the `IEnumerable` interface**, not `getSimpleList` reflection — that's the `WorldAccess.GetSimpleList` body. Same goes for `Count` — it's a property on `CoreSystemManager`.

5. **`System.ValueTuple` isn't always loadable under Unity Mono** (out-of-band on net462). Tuple syntax in method signatures, field types, or dictionary keys can cause `TypeLoadException` at first JIT. Replace with `readonly struct`. We have `WorldAccess.MapDimensions`, `AssetCatalog.TypeFieldKey`, `HttpBridge.HeaderReadResult` for that reason.

6. **`Type.GetMethod(name, flags)` without explicit arg types throws `AmbiguousMatchException`** as soon as the named method has overloads. `Actor.getName` and `WorldTile.setTileType` both have multiple overloads. `WorldAccess.CachedMethod` enumerates `GetMethods()` and filters manually instead of using the convenience overload.

7. **Powers use different click delegates.** Most `GodPower`s set `click_action` (`(WorldTile, string)`), but the drops/bombs/drop-building templates (`rain`, `fire`, `bomb`, `volcano`, …) set `click_power_action` (`(WorldTile, GodPower)`); `invoke_power` tries both. Still not drivable: brush-only (`click_brush_action`), `toggle_action`, and powers reading live pointer state (`finger` → NRE, mapped to `GAME_REJECTED`).

8. **`SaveManager.saveWorldToDirectory` NREs if no world is loaded** (calls deep into `World.world.items.diagnostic()`). `SaveWorldCommand` pre-flights with `_world.Width > 0` and returns `GAME_REJECTED` with a clear message.

9. **`Application.unityVersion` reports `"2022.3.60f1"` but the build is `2022.3.60.6251517` (per BepInEx log)**. The mod uses `Application.unityVersion` (the public-facing string) in `/health`.

10. **Dependency bumps can break the plugin at load or call time without touching game code.** The plugin binds at runtime to whatever BepInEx and the game bundle, not to what NuGet restored. Two real cases from the v0.3.0 dependabot sweep: HarmonyX 2.16 pulled in `MonoMod.Backports`, which BepInEx 5.4.23 doesn't ship, so `Chainloader.Start` threw `FileNotFoundException` and `Awake` never ran (only visible in Unity's `Player.log`, not `LogOutput.log`). Newtonsoft.Json 13.0.4 added `JToken.ToString(Formatting)`, the compiler preferred it over the `params` overload, and the game's bundled Newtonsoft.Json-for-Unity 13.0.2 threw `MissingMethodException` on `/capabilities`. Rule: keep Newtonsoft.Json pinned to the game's version (`Directory.Packages.props`), don't reference Harmony unless you use it, and after any mod dependency change verify with `strings WorldBoxBridge.dll | grep -i monomod` plus a live `/capabilities` call.

---

## Live entity vs asset library — two registry families

Don't confuse them.

| Asset library | Live entity manager |
|---|---|
| `AssetManager.tiles` — TileType templates | `MapBox.instance.tiles_map[x,y]` — actual WorldTile instances |
| `AssetManager.actor_library` — ActorAsset templates | `MapBox.instance.units` — ActorManager of live Actor instances |
| `AssetManager.kingdoms` — KingdomAsset templates (race definitions) | `MapBox.instance.kingdoms` — KingdomManager of live Kingdom instances |
| Iterated via `AssetLibrary<T>.list` field | Iterated via `IEnumerable<T>` (CoreSystemManager) |

`list_tiles` / `list_actors` / `list_powers` query the asset library side.
`list_kingdoms` / `list_cities` / `query_actors` / `get_world_state` query the live entity side.

---

## Release process

1. Land work on `main` with Conventional Commits.
2. **release-please** ([googleapis/release-please-action](https://github.com/googleapis/release-please)) runs on every push to `main`. It opens (or updates) a PR titled `chore(main): release X.Y.Z` containing the version bumps in `pyproject.toml` / `csproj` / `PluginInfo.cs` / `__init__.py` / `.release-please-manifest.json` plus the auto-generated `CHANGELOG.md` section. `feat:` bumps minor; `fix:` bumps patch; `feat!:` bumps major. **All four version files are tracked** via `extra-files` in `release-please-config.json` — the C#/Python ones use the inline `// x-release-please-version` / `# x-release-please-version` marker.
3. **Merge that PR**. release-please then:
   - Tags `vX.Y.Z`
   - Creates a GitHub Release with the CHANGELOG body
   - Sets `release_created=true` on the action outputs
   - Immediately opens the next `chore(main): release X.Y.Z+1` PR if any `fix:`/`feat:` commits already landed on main since this release — don't be surprised, that's working as intended.
4. The same `release.yml` workflow has dependent jobs gated on `release_created==true`:
   - `publish-pypi` — builds wheel/sdist with `uv build` and publishes via [PyPI trusted publisher](https://docs.pypi.org/trusted-publishers/). Environment `pypi` (configured both in repo and on pypi.org).
   - `build-and-attach-mod` — Windows runner, `dotnet build`, ZIP + SHA256 + GH release upload.
5. `build-and-attach-mod` builds the mod on the runner and attaches `WorldBoxBridge-vX.Y.Z.zip` + `.sha256` to the release. If it ever fails, the manual fallback is:
   ```powershell
   # 1. Sync local to the merge commit (which has the bumped version files)
   git pull --ff-only origin main

   # 2. Pre-flight env (same as Day-to-day commands)
   $env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"
   $env:PATH="$env:USERPROFILE\.dotnet;$env:USERPROFILE\.dotnet\tools;"+$env:PATH

   # 3. Build + stage
   dotnet build C:\worldbox-mcp\mod --configuration Release
   $version = "X.Y.Z"   # set to the just-released tag
   $stage = "C:\worldbox-mcp\release-stage\WorldBoxBridge"
   if (Test-Path 'C:\worldbox-mcp\release-stage') { Remove-Item -Recurse -Force 'C:\worldbox-mcp\release-stage' }
   New-Item -ItemType Directory -Force -Path $stage | Out-Null
   Copy-Item 'C:\worldbox-mcp\mod\src\WorldBoxBridge\bin\Release\WorldBoxBridge.dll' "$stage\"
   Copy-Item C:\worldbox-mcp\scripts\install-mod.ps1, C:\worldbox-mcp\LICENSE, C:\worldbox-mcp\README.md $stage\

   # 4. ZIP + SHA256 + upload
   $zip = "C:\worldbox-mcp\release-stage\WorldBoxBridge-v$version.zip"
   Compress-Archive -Path $stage -DestinationPath $zip -Force
   (Get-FileHash $zip -Algorithm SHA256).Hash | Out-File ($zip + '.sha256') -Encoding ASCII -NoNewline
   gh release upload "v$version" $zip ($zip + '.sha256') --clobber
   ```
6. **Verify**: `gh release view vX.Y.Z --json assets` shows the ZIP + .sha256; PyPI `curl -s https://pypi.org/pypi/worldbox-mcp/json | python -c "import json,sys; print(json.load(sys.stdin)['info']['version'])"` reports the new version.

---

## When something breaks — diagnostic flow

| Symptom | First thing to check |
|---|---|
| Mod doesn't load on launch | `<worldbox>/BepInEx/LogOutput.log`. Look for `WorldBoxBridge vX.Y.Z starting up...` line. If missing, BepInEx didn't pick up the DLL — wrong folder. |
| Bridge listening but `/health` connection refused | `Get-Process worldbox` + `netstat -ano \| Select-String 8723` — confirm the port is bound. If `[diag] after Start(): IsBound=True` in the log but netstat is empty, you've hit Mono Unity bug #1 or #2 (see Gotchas). |
| All commands timeout after 30s | The `MainThreadDispatcher` isn't running. Check the log for `[dispatcher] injected into Unity PlayerLoop Update phase`. If absent, gotcha #3 — re-verify the PlayerLoop injection survived. |
| Asset id rejected with `UNKNOWN_ASSET` but you're sure it exists | Call `list_*` from the same session — game might have renamed it. Use the `did_you_mean` suggestions. |
| `list_kingdoms` / `list_cities` return 0 with kingdoms alive | Gotcha #4 — re-verify `WorldAccess.GetSimpleList` is using `IEnumerable` not `getSimpleList`. Bug pre-v0.1.1. |
| `release.yml` PyPI publish fails | Trusted publisher config drift. Verify <https://pypi.org/manage/project/worldbox-mcp/settings/publishing/> has the GitHub provider with `release.yml` + `pypi` environment. |
| `dotnet restore` fails on `UnityEngine.Modules` | The BepInEx NuGet feed (`nuget.bepinex.dev`) is unreachable or `NuGet.config`'s `UnityEngine.*` source mapping was removed. |
| CI `Lint mod (csharpier)` fails with "dotnet-csharpier does not exist" | csharpier 1.x got installed instead of 0.30.6. Check `ci.yml#Install csharpier` is pinned. See CI/CD operational notes. |
| CI `Docs` fails with "docs_dir should not be the parent directory of the config file" | Someone moved `mkdocs.yml` back into `docs/`. Move it to the repo root with `docs_dir: docs`. |
| CI `Docs` fails with "Get Pages site failed / Resource not accessible by integration" | GitHub Pages was disabled on the repo. Re-bootstrap: `gh api repos/fullya99/worldbox-mcp/pages -X POST -F build_type=workflow`. |
| CI `Build mod` fails after a WorldBox update | The game moved to a new Unity version: bump `UnityEngine.Modules` in `Directory.Packages.props` to match `/health` → `unity_version`. |
| Dependabot reopens a FluentAssertions major bump PR | Verify `.github/dependabot.yml` still has the FluentAssertions `version-update:semver-major` ignore. v7+ is paid commercial license. |

---

## What I'd build next (roadmap notes)

The v0.3 multi-agent layer (identity, permissions, fog-of-war, turn-based, message bus,
objectives, four scenario presets, pvp_smoke) shipped on `main` 2026-05-17 as `v0.3.0` / `v0.3.1`.
Pending items, roughly in priority order:

1. ~~CI mod build~~ — done via the `UnityEngine.Modules` NuGet package; `build-and-attach-mod` now attaches the ZIP itself.
2. **Single multi-tenant MCP server (Phase 2.5)** — currently "N agents on one world" = N `worldbox-mcp` processes each with their own `WORLDBOX_MCP_TOKEN`. The bridge already supports it; what's missing is one MCP server that accepts multiple MCP clients with distinct bearer headers and forwards `ctx.request.headers["authorization"]` per-call. `BridgeClient.call(token=...)` is already plumbed; the work is in `tools/*.py` to take `ctx: Context` and extract the bearer.
3. **Auto-resolve `kingdom_claim: "auto:N"`** — currently parked as `null` until claimed; need a hook on first world-load to bind the Nth alive kingdom to the agent. Today `RequireKingdomAccess` is permissive on null claims, so PvP scoping is partly best-effort.
4. **`scripts/gen-docs.py`** — calls `worldbox_capabilities` against a running game and regenerates `docs/command-reference.md` from the JSON Schema. Removes the drift risk between code and doc tool counts (which bit us during the v0.3 docs sweep — "20 tools" appeared in 6 places).
5. **Remaining power delegates** — `invoke_power` now drives `click_action` and `click_power_action` (drops/bombs/plague/volcano all work). Still uncovered: `click_brush_action` (needs `Config.current_brush_data`), `toggle_action` (peace/civ toggles) and `click_special_action`.
6. **`get_actor(name_or_id)`** — single-actor lookup so the agent can drill into a specific Actor's stats without scanning the whole `query_actors` output.
7. **`terraform(action_id, x, y, radius)`** — wrap `AssetManager.terraform` for non-paint terrain mutations (raise/lower terrain, river carving, etc.).
8. **Persistent message log** (v0.3.2 candidate) — opt-in JSONL on disk for replay / post-mortem.
9. **`changelog-sections` refinement** in `release-please-config.json` — `fix(ci):` commits currently land under "Dependencies" in the generated CHANGELOG. Cosmetic, but worth tuning before the next minor.

Earlier long plan (the original 600-line spec) at `~/.claude/plans/option-b-fait-un-gentle-pancake.md`; the v0.3 implementation plan at `~/.claude/plans/ok-j-aimeraisd-que-tu-purrfect-pearl.md` documents what shipped and the remaining gaps. Project memory index at `~/.claude/projects/C--worldbox-mcp/memory/MEMORY.md` distills the recurring gotchas + user preferences for next-session pickup.
