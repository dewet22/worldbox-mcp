---
title: worldbox-mcp
---

# worldbox-mcp

> _<!-- TODO(pitch): one-line tagline. Same as README. -->_

`worldbox-mcp` is an open-source bridge that lets any MCP-compatible AI client — **Claude Code, OpenCode, Codex, Cursor, Continue, and more** — directly control the game [WorldBox](https://www.superworldbox.com/).

It ships as two open-source components:

1. **`WorldBoxBridge`** — a [BepInEx](https://bepinex.org/) plugin (C#) injected into the running game that exposes an authenticated HTTP API to the game's internals.
2. **`worldbox-mcp`** — a Python MCP server distributed via PyPI (`uvx worldbox-mcp`) that translates MCP tool calls into HTTP requests.

## Quick links

- 🚀 **[Install](install/)** — pick your AI client
- 🏗 **[Architecture](architecture.md)** — how the two halves talk to each other
- 📚 **[Command reference](command-reference.md)** — every tool, auto-generated
- 🧩 **[Compatibility](compatibility.md)** — WorldBox × mod version matrix
- 🤝 **[Contributing](contributing.md)** — code, docs, issues

## Design principles

- **100% game coverage** through three generic primitives (`paint_tile`, `spawn`, `invoke_power`) backed by the game's own asset registry — see [protocol.md](protocol.md).
- **Survives game updates**: zero static binding to WorldBox internals; everything resolves through cached reflection with explicit logging when a symbol disappears.
- **Local-only by design**: HTTP bound to `127.0.0.1`, per-install random auth token, no telemetry.
- **Production-grade**: typed, tested, signed releases, automated CI/CD.

---

> _worldbox-mcp is an unofficial community project and is not affiliated with or endorsed by the WorldBox developers._
