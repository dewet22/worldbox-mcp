# Multi-agent scenario presets

Four ready-to-customize `agents.json` shapes for the multi-AI session layer introduced in
v0.3. Pick one, replace the placeholder tokens with real secrets, drop the file into
`<worldbox>/BepInEx/config/WorldBoxBridge.agents.json`, and restart the game.

The bridge auto-detects the file. With it present, every request must carry one of the
declared agent tokens via `Authorization: Bearer <token>`. Without the file the bridge
falls back to legacy single-token mode (one God agent with the shared `WorldBoxBridge.cfg`
token), which is still supported.

| File | Mode | Agents | Distinguishing |
|---|---|---|---|
| `pvp.json` | Competitive | 2 FactionPlayers (athena, ares) | partial_intel=true, each player only sees their own kingdom; mutual "wipe the other" objective |
| `coop.json` | Cooperative | 3 Gods (ecology / civilization / disasters) | partial_intel=false; no claims; use the message bus for handoffs |
| `hierarchical.json` | DM-led story | 1 God + 2 FactionPlayers + 1 Narrator | god orchestrates, players act in-faction, narrator broadcasts; partial_intel=true |
| `sandbox.json` | Free-form | N Gods | no constraints; closest to legacy mode but with multiple identities for per-agent inboxes |

## Generating secure tokens

The bridge's `BridgeConfig.GenerateToken` produces 48-char `[A-Za-z0-9]` strings. You can
match its format from PowerShell:

```powershell
$alphabet = [char[]]([char]'A'..[char]'Z' + [char]'a'..[char]'z' + [char]'0'..[char]'9')
1..N | ForEach-Object {
  $bytes = New-Object byte[] 48
  [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
  -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Count] })
}
```

...where `N` is the number of agents you need. Or grab them from `scripts/install-mod.ps1`
once the install script grows multi-agent support (tracked for the v0.3 release).

## Client wiring

Each AI client connects with its own bearer:

```bash
# Stdio path: spawn one worldbox-mcp process per agent
WORLDBOX_MCP_TOKEN=<athena_token> uvx worldbox-mcp
# (separate terminal)
WORLDBOX_MCP_TOKEN=<ares_token>   uvx worldbox-mcp
```

```bash
# HTTP path (one shared MCP server, many clients) — Claude Code example:
claude mcp add worldbox-athena --transport http http://localhost:8724/mcp \
  --header "Authorization: Bearer <athena_token>"
claude mcp add worldbox-ares   --transport http http://localhost:8724/mcp \
  --header "Authorization: Bearer <ares_token>"
```

## Try it without configuring anything

Run the e2e smoke at `examples/scenarios/multi-agent/pvp_smoke.py`. It spawns two
`BridgeClient`s against a single bridge and walks through whoami → list_kingdoms →
send_message → recv_messages → objective_status for both agents in sequence, so you
can see the multi-agent flow without setting up MCP clients.

Prerequisite: `pvp.json` deployed (or any `agents.json` defining at least `athena` and
`ares` tokens that match the constants at the top of `pvp_smoke.py`).
