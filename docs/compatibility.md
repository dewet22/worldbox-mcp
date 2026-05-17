# Compatibility matrix

Status legend: ✅ tested & green · ⚠️ partial (specific commands disabled) · ❌ broken

| WorldBox version | Unity | Scripting backend | Mod version | Status | Notes |
|---|---|---|---|---|---|
| **0.51.2** | 2022.3.60f1 | Mono | **0.3.1** | ✅ | Latest release. CI/docs cleanup follow-up to v0.3.0. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.3.0 | ✅ | Full validation: 26 tools, multi-agent session layer (`examples/scenarios/multi-agent/pvp_smoke.py`). 3-agent PvP roster (faction_player × 2 + narrator) with fog-of-war, turn-based mode, message bus, and AdvanceTime perm split — all green live. Assembly-CSharp.dll SHA256 `51d275f0…df6dd69f`. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.2.x | ✅ | 20-tool baseline + `generate_world` / `save_world` / `load_world`. Single-tenant only. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.1.1 | ✅ | 20 tools, end-to-end agentic loop (`examples/scenarios/ecology_smoke.py`). |
| 0.51.2 | 2022.3.60f1 | Mono | 0.1.0 | ⚠️ | `list_kingdoms` / `list_cities` / `get_world_state.{kingdoms,cities}_alive` return 0 even when alive — fixed in 0.1.1. |

## Reading the matrix

- We track WorldBox releases via the [daily compat-check workflow](https://github.com/fullya99/worldbox-mcp/actions/workflows/compat-check.yml). A new WorldBox version automatically opens an issue with the `wb-update` label.
- A version is considered ✅ only after the [e2e smoke suite](development.md#end-to-end-smoke-tests) passes against a real install.
- If you run into a combination not yet listed, please open an issue with your `BepInEx/LogOutput.log`.

## Survival strategy

The mod uses reflection lookups cached at startup (see [architecture.md](architecture.md)). When WorldBox renames or removes a symbol, the affected command is **disabled** with a clear log line but the rest of the bridge keeps working — the mod operates in graceful degradation rather than all-or-nothing.

Practically, this means the mod's compatibility window is usually wider than the matrix below suggests — it just isn't formally tested.
