# Compatibility matrix

Status legend: ✅ tested & green · 🔵 shipped, not yet verified against a live install · ⚠️ partial (specific commands disabled) · ❌ broken

| WorldBox version | Unity | Scripting backend | Mod version | Status | Notes |
|---|---|---|---|---|---|
| **0.51.2** | 2022.3.60f1 | Mono | **0.6.0** | 🔵 | Builds and passes 213 xUnit and 80 pytest tests on a bare machine, and CI is green on Windows, Linux and macOS, but nobody has run it against a game yet. No breaking change. `invoke_power` gains an optional `radius`, `pulses` for a held button, and `x2`/`y2` for a click-hold-drag sweep, and it now drives the brush and toggle delegates as well as the click ones. `load_world` reads the save off the main thread, so a slow or hostile path can no longer hold a frame, and it refuses a pipe or a character device before opening it. `BrushAccess` logs once per build when the id the game hands back disagrees with the `circ_<radius>` it constructs, instead of trusting either silently. Four things still owe a live run: the `load_world` load path, the missing `IsWorldLoading` pre-flight, what `Brush.get(int, string)` returns, and the two untested guards in `invoke_power`. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.5.0 | 🔵 | Builds and passes 175 xUnit and 54 pytest tests on a bare machine, and CI is green on Windows, Linux and macOS, but nobody has run it against a game yet. Breaking: a FactionPlayer can no longer call `invoke_power`. Save path resolution now verifies containment by resolving rather than by inspecting the string, and command argument errors return 400 `BAD_ARGS` instead of 500 `GAME_CRASH`. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.4.0 | ✅ | HarmonyX reference dropped and Newtonsoft.Json pinned to the game's 13.0.2; 29 tools; verified on macOS (BepInEx 5.4.23.5) incl. `/capabilities`, screenshots, powers, save/load. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.3.0 to 0.3.3 | ❌ | Release DLLs (all built with HarmonyX 2.16.1; reproduced with the 0.3.3 ZIP on macOS) reference `MonoMod.Backports`, which BepInEx 5.4.23 does not ship: the plugin fails to load (`FileNotFoundException` in Unity's Player.log, `LogOutput.log` looks normal). Even with a compatible MonoMod present, `/capabilities` throws `MissingMethodException` because Newtonsoft.Json 13.0.4 binds `JToken.ToString(Formatting)`, absent from the game's bundled 13.0.2. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.3.0 (upstream's own validation, Windows) | ⚠️ | Reported upstream at release time: 26 tools, multi-agent session layer (`examples/scenarios/multi-agent/pvp_smoke.py`). 3-agent PvP roster (faction_player × 2 + narrator) with fog-of-war, turn-based mode, message bus, and AdvanceTime perm split, all green live. Assembly-CSharp.dll SHA256 `51d275f0…df6dd69f`. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.2.x | ✅ | 20-tool baseline + `generate_world` / `save_world` / `load_world`. Single-tenant only. |
| 0.51.2 | 2022.3.60f1 | Mono | 0.1.1 | ✅ | 20 tools, end-to-end agentic loop (`examples/scenarios/ecology_smoke.py`). |
| 0.51.2 | 2022.3.60f1 | Mono | 0.1.0 | ⚠️ | `list_kingdoms` / `list_cities` / `get_world_state.{kingdoms,cities}_alive` return 0 even when alive, fixed in 0.1.1. |

## Reading the matrix

- We track WorldBox releases via the [daily compat-check workflow](https://github.com/fullya99/worldbox-mcp/actions/workflows/compat-check.yml), which reads the build id of the game's `public` branch on Steam and compares it against `.github/worldbox-build-baseline.txt`, the build this matrix was last verified against. A build that does not match opens an issue labelled `wb-update`, once per build. After re-testing, write the new build id into that file to close the loop.
- WorldBox 0.51.2 is Steam build `19962337`, published on 2025-09-13.
- A version is considered ✅ only after the [e2e smoke suite](development.md#end-to-end-smoke-tests) passes against a real install. A release that has not had that pass yet is 🔵, not ✅. The row exists because 0.3.0 to 0.3.3 shipped DLLs that never loaded, and an empty matrix row would have hidden that just as well as a green one.
- If you run into a combination not yet listed, please open an issue with your `BepInEx/LogOutput.log`.

## Survival strategy

The mod uses reflection lookups cached at startup (see [architecture.md](architecture.md)). When WorldBox renames or removes a symbol, the affected command is **disabled** with a clear log line but the rest of the bridge keeps working, the mod operates in graceful degradation rather than all-or-nothing.

Practically, this means the mod's compatibility window is usually wider than the matrix below suggests, it just isn't formally tested.
