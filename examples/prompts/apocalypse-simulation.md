# Demo prompt — Apocalypse simulation

For demoing the `invoke_power` primitive across the full disaster catalog.

---

> The world is currently inhabited — let's stress-test civilization.
>
> First, `pause()` and `screenshot()` so we have a "before" image. Call `get_world_state()` and remember the population count.
>
> Then `list_powers()` and identify every power whose `target_type` is `"point"` or `"area"` and that maps to a natural disaster (meteor, volcano, tsunami, earthquake, lightning storm, …).
>
> Now `resume()` and run the apocalypse in escalating waves, one minute apart:
>
> - **Wave 1 (minute 0)**: pick three random populated tiles and `invoke_power("lightning_strike", x, y)` on each.
> - **Wave 2 (minute 1)**: a `"meteor"` on the largest city you can identify via `list_cities()`.
> - **Wave 3 (minute 2)**: a sustained `"acid_storm"` covering the eastern half of the map.
> - **Wave 4 (minute 3)**: `"plague"` released globally.
> - **Final (minute 4)**: a single `"nuke"` at the geographic center of the map.
>
> After each wave: `screenshot()` and report the surviving population delta.
>
> Conclude with a short post-mortem: which disaster killed the most? Which civilization survived longest? What's the new dominant species, if any?

---

## Why this prompt is good for demos

- Visually spectacular for video clips.
- Forces the agent to **discover** which power ids exist rather than hardcoding.
- Tests the `invoke_power` primitive across global / area / point target types.
