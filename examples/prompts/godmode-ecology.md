# Sample god-mode prompt, ecology experiment

Paste this into a fresh Claude Code (or any other MCP client) session **after** you've added the `worldbox` MCP server. Requires a world loaded in-game (`worldbox_get_world_state.width` > 0).

---

> Use the worldbox MCP server to run a small ecology experiment on the live game.
>
> 1. Call `worldbox_get_world_state` first. If width is 0 (no world loaded), tell me to load a world in-game first and stop.
> 2. Pause the simulation.
> 3. Discover available assets: call `worldbox_list_tiles`, `worldbox_list_actors` and `worldbox_list_powers`. From the results, pick ids that look like 'sand', 'soil_high', 'wolf', 'sheep', 'human', 'dragon' and 'lightning' (use Levenshtein-tolerant matching, exact ids may differ between WorldBox versions).
> 4. Paint a sand plateau of radius 8 at one corner of the map and a soil patch at another. Spawn 10 herbivores on the soil, 4 predators in the middle, 6 humans on the sand. Invoke a lightning strike at the center for drama.
> 5. Set the simulation to speed `x3` (or `x5` if it exists). Resume.
> 6. Wait 15 seconds of wall-clock time. During that pause, narrate what you'd expect to happen biologically and why.
> 7. Once the wait is over, query each race you spawned and tell me who survived, naming the individual actors using the names you get back from `worldbox_query_actors`. Take a `worldbox_screenshot` and tell me whether the resulting image is too big to render inline; if it is, summarise what you'd expect to see instead.
> 8. Pause the world again at the end so nothing else happens while we discuss the result.
>
> Be specific about coordinates, don't say "somewhere on the map", say "(120, 80)". When an id you tried doesn't resolve, read the `did_you_mean` suggestions and self-correct rather than asking me.

---

## Why this prompt is dense by design

Every clause exercises a different part of the bridge:

| Clause | Tool exercised | What's being tested |
|---|---|---|
| step 1 (early exit) | `get_world_state` | Agent reads state before acting |
| step 2 / 8 | `pause` | Determinism, setup runs without drift |
| step 3 | `list_tiles`/`list_actors`/`list_powers` | Discovery, no hardcoded ids |
| "Levenshtein-tolerant matching" | (agent reasoning) | Resilience to id drift across versions |
| step 4 (paint + spawn + invoke) | `paint_tile`, `spawn`, `invoke_power` | All three action primitives |
| step 5 | `set_speed`, `resume` | Time control |
| step 7 (query + screenshot) | `query_actors`, `screenshot` | Observation + visual evidence |
| "did_you_mean self-correct" | (error envelope) | Agent uses structured errors instead of asking the human |

Run the same plan deterministically via `examples/scenarios/ecology_smoke.py`.
