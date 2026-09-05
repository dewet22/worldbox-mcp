# Development

> See also [CONTRIBUTING.md](contributing.md) for code style, commit conventions, and PR flow.

## Local setup

You need the .NET SDK 8 and [uv](https://docs.astral.sh/uv/). **No WorldBox install is required to
build or test**. Unity references come from the `UnityEngine.Modules` NuGet package, and the mod
reaches the game's own code only through reflection.

```powershell
# Windows
.\scripts\dev-setup.ps1          # or: winget install Microsoft.DotNet.SDK.8 astral-sh.uv
```

```bash
# macOS. The Homebrew cask wants an interactive sudo, the formula does not.
brew install dotnet uv
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec DOTNET_ROLL_FORWARD=Major

# Linux
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir ~/.dotnet
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
```

SDK 10 builds the net462 mod fine. The net8.0 test project and csharpier 0.x need
`DOTNET_ROLL_FORWARD=Major` on that setup.

```bash
git clone https://github.com/fullya99/worldbox-mcp.git
cd worldbox-mcp
```

## Working on the mod

```bash
dotnet restore mod/WorldBoxBridge.sln --locked-mode
dotnet build mod/WorldBoxBridge.sln --configuration Release -warnaserror
dotnet tool install -g csharpier --version 0.30.6
dotnet csharpier --check mod
```

Build output: `mod/src/WorldBoxBridge/bin/Release/WorldBoxBridge.dll`. Note there is no
target-framework folder in that path: `AppendTargetFrameworkToOutputPath` is off.

`--locked-mode` is real here, `packages.lock.json` is committed for both projects. If you change
a package version, regenerate it or the build fails with NU1004:

```bash
dotnet restore mod/WorldBoxBridge.sln --force-evaluate
```

### Deploying to a local game install

Throughout, `<worldbox>` is your Steam install directory.

```powershell
# Windows
.\scripts\install-mod.ps1 -Local
```

```bash
# macOS / Linux, by hand
WB="<worldbox>"
cp mod/src/WorldBoxBridge/bin/Release/WorldBoxBridge.dll "$WB/BepInEx/plugins/"
```

Then **fully close and relaunch WorldBox**, BepInEx loads plugins once at startup. On macOS and
Linux the game must be started through `run_bepinex.sh`, otherwise BepInEx never loads and nothing
tells you why. Set the Steam launch option to `"<worldbox>/run_bepinex.sh" %command%`.

Liveness probe, with the token the mod generated into its config:

```bash
TOKEN=$(sed -n 's/^token = //p' "<worldbox>/BepInEx/config/WorldBoxBridge.cfg")
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8723/health
```

### Tests

```bash
cd mod
dotnet test
```

The mod test suite (xUnit, ~70 cases) covers the suggester, the agent registry, the request-context permission/fog-of-war helpers, the turn-order rotation (incl. concurrency), and the message bus (delivery, broadcast fan-out, cursoring, bounded-inbox drop-oldest) — **without the game**. The pattern is "linked sources": pure-logic files from the mod project are referenced as `<Compile Include="..\..\src\..." Link="..." />` in the test csproj so they compile under net8 without Unity. Anything that genuinely needs WorldBox to be running lives in the server-side e2e suite instead.

### Decompiling the game

Open `<worldbox>/worldbox_Data/Managed/Assembly-CSharp.dll` (macOS: `worldbox.app/Contents/Resources/Data/Managed/`) in ILSpy. The mod itself never references this assembly — everything game-specific goes through reflection (`GameRefs`), which is what lets it build on a bare CI runner from the `UnityEngine.Modules` NuGet package alone. Record findings in [game-api-notes.md](game-api-notes.md).

## Working on the server

```bash
cd server
uv sync --all-extras
uv run worldbox-mcp --self-check
```

`--self-check` validates that the server can be loaded and emits its tool schemas without needing the mod online.

### Tests

```bash
cd server
uv run pytest tests/unit tests/integration
```

The integration suite spins up a fake bridge in pure Python (`aiohttp`) that mimics the mod's HTTP contract — no game required.

### End-to-end smoke tests

```bash
cd server
uv run pytest tests/e2e --run-e2e
```

These need:

1. WorldBox running with the latest mod installed.
2. `WORLDBOX_MCP_TOKEN` exported (or auto-discoverable).

CI skips this suite by default.

## Adding a new MCP tool

1. **Mod side**, `mod/src/WorldBoxBridge/Commands/<Category>/<Name>Command.cs`:
   - Implement `ICommand`, pick a `CommandCategory` (Meta, Discovery, Action, Read, Control, Bus)
     and set `RequiresMainThread`.
   - The signature is `Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken)`.
     Call `ctx.Require(Permission.X)` first to gate it, and `ctx.CanSeeKingdom` or
     `ctx.RequireKingdomAccess` for fog-of-war and faction binding.
   - Reuse `AssetCatalog.Resolve` for any asset id, which gives you `did_you_mean` for free, and
     `WorldAccess` for `MapBox`, units, kingdoms and cities.
   - Throw `BridgeRejectionException` for structured errors. `HttpBridge` maps it to the right
     status and envelope.
   - Category semantics matter: **Action and Control are turn-gated** in `turn_based` sessions,
     Meta, Discovery, Read and Bus are not. That is why `turn_advance` lives in Meta rather than
     Control, otherwise a session could deadlock permanently.
2. **Register it** in `Plugin.cs#RegisterCommands`, one line.
3. **Server side**, `server/src/worldbox_mcp/tools/<category>.py`: add a
   `@server.tool(name="worldbox_<your_name>", description=...)` function. The description is what
   the model reads to decide when to call your tool, so be concrete about inputs, outputs and
   edge cases.
4. **Update [command-reference.md](command-reference.md)**, and [multi-agent.md](multi-agent.md)
   if the tool is session-aware. Keep every stated tool count in agreement.
5. Build, deploy and smoke-test against a running game.

## When something breaks

| Symptom | First thing to check |
|---|---|
| Mod doesn't load on launch | `<worldbox>/BepInEx/LogOutput.log`, look for `WorldBoxBridge vX.Y.Z starting up...`. If the line is missing, BepInEx never picked up the DLL, so it is in the wrong folder. On macOS and Linux, check you launched through `run_bepinex.sh`. |
| Log looks normal but the plugin never runs | Plugin *load* exceptions do not reach `LogOutput.log`. They only appear in Unity's own `Player.log` (macOS: `~/Library/Logs/mkarpenko/WorldBox/Player.log`). This is how a bad dependency bump hides. See gotcha 10 in [game-api-notes.md](game-api-notes.md). |
| Bridge says it is listening but `/health` refuses the connection | Confirm the port is really bound (`netstat`). If the log says `IsBound=True` and the OS disagrees, you hit gotcha 1 or 2. |
| Every command times out after 30s | The `MainThreadDispatcher` is not running. Look for `[dispatcher] injected into Unity PlayerLoop Update phase`. If absent, gotcha 3. |
| Asset id rejected with `UNKNOWN_ASSET` when you know it exists | Call the matching `list_*` in the same session, the game may have renamed it, and use the `did_you_mean` suggestions. |
| `list_kingdoms` / `list_cities` return 0 with kingdoms alive | Gotcha 4. Check `WorldAccess.GetSimpleList` still iterates through `IEnumerable`. |
| `dotnet restore` fails on `UnityEngine.Modules` | The BepInEx feed is unreachable, or the exact-id source mapping was removed from `mod/NuGet.config`. |
| `dotnet restore` fails with NU1004 | `packages.lock.json` is stale after a version change. Run a `--force-evaluate` restore and commit the result. |
| CI `Lint mod` says `dotnet-csharpier does not exist` | csharpier 1.x got installed instead of 0.30.6. The pin is deliberate. |
| CI `Build mod` fails after a WorldBox update | The game moved to a new Unity version. Bump `UnityEngine.Modules` in `Directory.Packages.props` to match `/health` → `unity_version`. |

## Releasing (maintainers)

Land work on `main` with Conventional Commits. `release-please` runs on every push and maintains a
`chore(main): release X.Y.Z` PR carrying the version bumps and the generated changelog. `feat:`
bumps the minor, `fix:` the patch, `feat!:` the major. Four version files are kept in sync through
`extra-files` in `release-please-config.json`.

**Merge PRs with a merge commit, not a squash.** The repo takes the PR title as the squash subject,
so squashing a PR titled `deps: ...` hides the `feat:` commits inside it and release-please skips
the minor bump.

Merging the release PR tags the version, creates the GitHub Release, and triggers two jobs:

- `publish-pypi` publishes the wheel and sdist through [PyPI trusted publishing](https://docs.pypi.org/trusted-publishers/).
- `build-and-attach-mod` builds the DLL on the runner and attaches `WorldBoxBridge-vX.Y.Z.zip`
  plus its `.sha256` to the release.

Verify with `gh release view vX.Y.Z --json assets` and by checking the version on PyPI.

If `build-and-attach-mod` ever fails, the manual fallback is to build locally, stage
`WorldBoxBridge.dll` with `install-mod.ps1`, `LICENSE` and `README.md` into a `WorldBoxBridge/`
folder, zip it as `WorldBoxBridge-v<version>.zip`, write the SHA256 next to it, and
`gh release upload "v<version>" <zip> <zip>.sha256 --clobber`.

`docs/compatibility.md` is still updated by hand after a release.
