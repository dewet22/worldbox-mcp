# Compatibility matrix

Status legend: ✅ tested & green · ⚠️ partial (specific commands disabled) · ❌ broken

| WorldBox version | Unity | Scripting backend | Mod version | Status | Notes |
|---|---|---|---|---|---|
| 0.x.x | 2022.3.60f1 | Mono | 0.1.x | _pending v0.1.0 release_ | Initial target |

## Reading the matrix

- We track WorldBox releases via the [daily compat-check workflow](https://github.com/fullya99/worldbox-mcp/actions/workflows/compat-check.yml). A new WorldBox version automatically opens an issue with the `wb-update` label.
- A version is considered ✅ only after the [e2e smoke suite](development.md#end-to-end-smoke-tests) passes against a real install.
- If you run into a combination not yet listed, please open an issue with your `BepInEx/LogOutput.log`.

## Survival strategy

The mod uses reflection lookups cached at startup (see [architecture.md](architecture.md)). When WorldBox renames or removes a symbol, the affected command is **disabled** with a clear log line but the rest of the bridge keeps working — the mod operates in graceful degradation rather than all-or-nothing.

Practically, this means the mod's compatibility window is usually wider than the matrix below suggests — it just isn't formally tested.
