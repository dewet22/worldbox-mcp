---
title: Command reference
---

# Command reference

!!! info "Auto-generated"
    This page is generated from the mod's `GET /capabilities` response at release time. To regenerate locally:

    ```bash
    uv run python scripts/gen-docs.py > docs/command-reference.md
    ```

> _Stub. This page becomes meaningful once Phase 3 (action primitives) lands and `scripts/gen-docs.py` exists._

## Categories

The tool surface is grouped into four categories:

| Category | Purpose | Tools (planned) |
|---|---|---|
| **Discovery** | Introspect the game's asset registry. | `list_tiles`, `list_actors`, `list_powers` |
| **Action** | Modify the world. Three generic primitives cover 100% of game actions. | `paint_tile`, `spawn`, `invoke_power` |
| **Read** | Inspect world state. | `get_world_state`, `get_tile`, `get_actor`, `list_kingdoms`, `list_cities`, `query_actors`, `screenshot` |
| **Control** | Affect simulation flow. | `pause`, `resume`, `set_speed`, `time_skip`, `generate_world`, `save_world`, `load_world`, `camera_goto` |

Plus a meta tool `capabilities()` which is the source of truth for everything else.

## Discovery first

The fundamental design choice: rather than hardcoding ~200 command variants, three primitives accept a string asset id resolved at runtime against the game's own registry. Use the discovery tools to enumerate what's valid in your current WorldBox build:

```
list_tiles()  → [{id, display_name, category}, ...]
list_actors() → [{id, display_name, race, kingdom_default}, ...]
list_powers() → [{id, display_name, target_type, required_args}, ...]
```

Asset ids returned here are valid inputs for the action primitives in the same session.
