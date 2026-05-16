# Claude Code

[Claude Code](https://www.anthropic.com/claude-code) is Anthropic's official CLI agent. It loads MCP servers natively via stdio.

## Install the bridge first

Make sure you've already installed the in-game mod and that WorldBox is running with `BepInEx/LogOutput.log` showing `listening on 127.0.0.1:8723`. If not, see [`install/`](.) for the mod side.

## Add the server

### Option A — from PyPI (once v0.1.0 is released)

```bash
claude mcp add worldbox -- uvx worldbox-mcp
```

### Option B — from a local clone (today, while PyPI release pending)

```bash
git clone https://github.com/fullya99/worldbox-mcp.git ~/code/worldbox-mcp
claude mcp add worldbox -- uv run --project ~/code/worldbox-mcp/server worldbox-mcp
```

Both forms produce the same stdio server. The local form is preferred while developing the mod or running unreleased commits.

## Verify

Start a new Claude Code session, then:

```
> /mcp
```

You should see `worldbox` listed with status `connected` (or whatever the equivalent status word is in your Claude Code version). Then sanity-check the chain:

```
> Call worldbox_health and tell me what version of WorldBox is running.
```

## Sample god-mode prompt

Copy this into a fresh Claude Code session. It exercises **discovery → action → wait → observation → narration** — the full agentic loop.

> Use the worldbox MCP server to run a small ecology experiment on the live game.
>
> 1. Call `worldbox_get_world_state` first. If width is 0 (no world loaded), tell me to load a world in-game first and stop.
> 2. Pause the simulation.
> 3. Discover available assets — call `worldbox_list_tiles`, `worldbox_list_actors` and `worldbox_list_powers`. From the results, pick ids that look like 'sand', 'soil_high', 'wolf', 'sheep', 'human', 'dragon' and 'lightning' (use Levenshtein-tolerant matching — exact ids may differ between WorldBox versions).
> 4. Paint a sand plateau of radius 8 at one corner of the map and a soil patch at another. Spawn 10 herbivores on the soil, 4 predators in the middle, 6 humans on the sand. Invoke a lightning strike at the center for drama.
> 5. Set the simulation to speed `x3` (or `x5` if it exists). Resume.
> 6. Wait 15 seconds of wall-clock time. During that pause, narrate what you'd expect to happen biologically and why.
> 7. Once the wait is over, query each race you spawned and tell me who survived, naming the individual actors using the names you get back from `worldbox_query_actors`. Take a `worldbox_screenshot` and tell me whether the resulting image is too big to render inline; if it is, summarise what you'd expect to see instead.
> 8. Pause the world again at the end so nothing else happens while we discuss the result.
>
> Be specific about coordinates — don't say "somewhere on the map", say "(120, 80)". When an id you tried doesn't resolve, read the `did_you_mean` suggestions and self-correct rather than asking me.

This prompt is intentionally **dense** — it forces the agent to use every tool category in the right order, handles the no-world-loaded edge case explicitly, and asks for narrative output that proves the agent understood the data it got back.

A working equivalent is committed at [`examples/scenarios/ecology_smoke.py`](../../examples/scenarios/ecology_smoke.py) — same plan in Python so you can diff agent reasoning against a deterministic reference.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `worldbox` status `failed` | `uv` not on PATH | `winget install astral-sh.uv` (Windows) or `curl -LsSf https://astral.sh/uv/install.sh \| sh` (Linux/macOS), then restart your shell |
| Tool calls return "connection refused" to 127.0.0.1:8723 | Mod not loaded or game not running | Check `BepInEx/LogOutput.log` for the "listening on" line. If absent, see [install/index.md](index.md) |
| `UNAUTHORIZED` errors | Token discovery failed | The server auto-discovers the token from `<worldbox>/BepInEx/config/WorldBoxBridge.cfg`. If your install path is non-standard, export `WORLDBOX_MCP_WORLDBOX_DIR=<your-path>` before launching Claude Code |
| `GAME_REJECTED: No world is currently loaded` on action tools | You're at the title screen | Click "New World" or load a save in-game first |
