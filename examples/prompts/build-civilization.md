# Demo prompt — Build a civilization

Hand this prompt to an AI agent connected to `worldbox-mcp`. Best run on an empty or freshly generated world.

---

> Generate an empty 256×256 grass world (`generate_world`). Pause the simulation.
>
> Inspect the available actor and tile catalogs via `list_actors()` and `list_tiles()` so you know what asset ids exist in this version.
>
> Then build:
>
> 1. A **central river** running roughly north–south through the map, with banks of sand and pockets of forest on either side. Use `paint_tile` with a small radius for natural-looking edges.
> 2. Three **starter human kingdoms** spaced 60 tiles apart along the eastern bank — each seeded with ~15 humans clustered around an open patch. Use `spawn` with `count=15`.
> 3. A single **rival orc kingdom** on the western bank, with terrain around it pushed slightly more hostile (sparse trees, scattered hills).
>
> Now `resume()` and `set_speed(3)`. Every 30 seconds (~30 ticks), call `get_world_state()` and `list_kingdoms()` and **briefly narrate** what's happening: who's growing, who's at war, where the borders are forming.
>
> After 5 minutes of simulated time, take a `screenshot()` and summarize the outcome. Identify which kingdom is winning and propose what divine intervention would make the simulation more interesting from here.

---

## Why this prompt is good for demos

- Exercises **discovery, action, read, and control** tools in one flow.
- Forces the agent to reason about asset ids it can't have memorized.
- Produces a narrated outcome rather than a static screenshot — good for video.
