# Contributing to worldbox-mcp

Thanks for your interest in contributing. This guide covers setup, conventions, and how to add new commands.

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). Be kind.

## Project layout

See [docs/architecture.md](docs/architecture.md). Top level:

- `mod/` — BepInEx plugin (C#, .NET Framework 4.6.2)
- `server/` — MCP server (Python 3.11+)
- `docs/` — MkDocs Material site
- `scripts/` — installers and dev helpers
- `examples/` — client configs and demo prompts

## Local setup

### Prerequisites

- Windows 10/11 (or Linux/macOS for server-only work)
- [.NET SDK 8](https://dotnet.microsoft.com/download)
- [Python 3.11+](https://www.python.org/)
- [uv](https://docs.astral.sh/uv/) (`winget install astral-sh.uv`)
- WorldBox installed (Steam) for e2e testing
- BepInEx 5.4.23.x x64 Unity Mono installed in your WorldBox directory
- [ILSpy](https://github.com/icsharpcode/ILSpy/releases) for decompiling the game

Run `scripts/dev-setup.ps1` to install all dev tooling on Windows.

### Bootstrapping

```powershell
git clone https://github.com/fullya99/worldbox-mcp.git
cd worldbox-mcp

# Mod
cd mod
dotnet restore
dotnet build

# Server
cd ../server
uv sync --all-extras
uv run pytest
```

### Pre-commit hooks

```bash
pip install pre-commit
pre-commit install
```

Runs `ruff`, `mypy`, `csharpier`, and Conventional Commits check on every commit.

## Coding conventions

### C# (mod)

- C# 10, `<Nullable>enable</Nullable>`, warnings as errors
- Formatted by `csharpier` (no debate)
- All Unity API calls go through `MainThreadDispatcher.Enqueue` — see [docs/architecture.md](docs/architecture.md)
- Never reference game types by `using` — always resolve via `GameRefs` reflection cache

### Python (server)

- Python 3.11+ syntax, `from __future__ import annotations`
- Formatted + linted by `ruff`
- Typed with `mypy --strict`
- Pydantic models for all I/O at the MCP boundary

### Commits

[Conventional Commits](https://www.conventionalcommits.org/). Examples:

- `feat(server): add screenshot tool`
- `fix(mod): handle null kingdom on actor query`
- `docs: update install instructions for Codex`
- `chore(deps): bump pydantic to 2.11`

`release-please` reads these to bump SemVer + generate CHANGELOG.

## Adding a new command

A command is implemented in **two places**: the mod (C#) executes it; the server (Python) exposes it as an MCP tool.

1. **Mod side** — pick a folder under `mod/src/WorldBoxBridge/Commands/{Discovery,Action,Read,Control}/`. Implement `ICommand`. Register in `CommandRegistry`. Add unit test in `mod/tests/`.
2. **Server side** — add a `@server.tool` function in `server/src/worldbox_mcp/tools/{discovery,action,read,control}.py`. Add unit test under `server/tests/unit/`.
3. **Test e2e manually** — launch WorldBox with the new mod build, call the tool via MCP Inspector.
4. **Document** — `capabilities()` will auto-include it; `command-reference.md` regenerates on CI.

## Testing

```bash
# Server (unit + integration; no game needed)
cd server
uv run pytest

# Mod (xUnit; no game needed)
cd mod
dotnet test

# E2E (requires WorldBox running with mod installed)
cd server
uv run pytest tests/e2e --run-e2e
```

## Pull requests

- One PR per logical change.
- Title in Conventional Commits format.
- CI must be green.
- Maintain or improve test coverage.
- Update `docs/` when behavior changes.
- Link the issue in the description.

## Releasing (maintainers)

Tags are cut by `release-please` automatically once a PR with conventional commits lands on `main`. The release workflow publishes to PyPI and attaches the mod ZIP to the GitHub Release.
