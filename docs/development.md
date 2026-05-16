# Development

> See also [CONTRIBUTING.md](contributing.md) for code style, commit conventions, and PR flow.

## Local setup

```powershell
# Windows
.\scripts\dev-setup.ps1
```

Manual equivalent:

```powershell
winget install Microsoft.DotNet.SDK.8
winget install astral-sh.uv
```

Then:

```bash
git clone https://github.com/fullya99/worldbox-mcp.git
cd worldbox-mcp
```

## Working on the mod

```bash
cd mod
dotnet restore
dotnet build --configuration Release
```

Build output: `mod/src/WorldBoxBridge/bin/Release/net462/WorldBoxBridge.dll`.

Deploy to your local WorldBox install:

```powershell
.\scripts\install-mod.ps1 -Local
```

Then **fully close and relaunch WorldBox** (BepInEx loads plugins once at startup).

### Tests

```bash
cd mod
dotnet test
```

The mod test suite (xUnit) covers parsers, the dispatcher, the suggester, and protocol envelopes — **without the game**. Anything that genuinely needs WorldBox to be running lives in the server-side e2e suite instead.

### Decompiling the game

Open `<worldbox>/worldbox_Data/Managed/Assembly-CSharp.dll` in ILSpy. Record findings in [game-api-notes.md](game-api-notes.md).

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

## Releasing (maintainers)

Tags are cut by `release-please` automatically once Conventional-Commit PRs land on `main`. The release workflow then:

1. Publishes the Python package to PyPI via trusted publishing.
2. Builds the mod DLL, packages it as `WorldBoxBridge-vX.Y.Z.zip` with `install-mod.ps1`, computes SHA256, attaches it to the GitHub Release.
3. Updates `docs/compatibility.md` (manual for now; automated in a future iteration).
