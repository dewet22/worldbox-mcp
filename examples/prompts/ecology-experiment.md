# Demo prompt — Predator-prey ecology experiment

Highlights the read/query side of the API — agent as scientist, not god.

---

> Run a controlled ecology experiment.
>
> 1. `generate_world(width=128, height=128)`. `pause()`.
> 2. Cover the entire map with the grass tile and scatter a `"forest_oak"` cluster every 20 tiles. Use `list_tiles()` to confirm the exact ids before painting.
> 3. Spawn **300 deer** uniformly across the map, then **30 wolves** in three packs of 10 along the southern edge.
> 4. Disable all civilization-related toggles via `invoke_power` so this stays purely animal.
> 5. `resume()` and `set_speed(5)`.
>
> Every 60 seconds of wall-clock time:
> - `query_actors({"race": "wolf", "alive": true})` → record count
> - `query_actors({"race": "deer", "alive": true})` → record count
> - Output a single line: `t=<minutes> wolves=<n> deer=<n>`
>
> Stop after 10 minutes or when either species is extinct.
>
> Conclude with:
> - A plain-text ASCII chart of the two populations over time.
> - A short Lotka–Volterra-style interpretation.
> - One follow-up experiment to propose (e.g., introducing a third species).

---

## Why this prompt is good for demos

- Shows that the agent can **measure** the simulation, not just disrupt it.
- Demonstrates the `query_actors` filter API at scale.
- Produces structured output (table + chart) that's screenshot-friendly.
