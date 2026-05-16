"""Complex end-to-end scenario.

Simulates what an agent does when asked: "Build a small ecology on the map and
report what happens after a few moments." Uses **only** the MCP-exposed tools —
no shortcuts, no hardcoded ids. The agent discovers what's available at runtime.

Run from the repo root:
    cd server && uv run python ../scratch/complex_scenario.py
"""

from __future__ import annotations

import asyncio
import base64
import time
from pathlib import Path

from worldbox_mcp.client import BridgeClient
from worldbox_mcp.config import load_settings

OUT_DIR = Path(__file__).resolve().parent / "scenario_out"
OUT_DIR.mkdir(parents=True, exist_ok=True)


def banner(label: str) -> None:
    print()
    print("=" * 72)
    print(f"  {label}")
    print("=" * 72)


async def save_screenshot(client: BridgeClient, name: str) -> Path:
    r = await client.call("screenshot")
    p = OUT_DIR / name
    p.write_bytes(base64.b64decode(r["base64"]))
    print(f"  [shot]{p.name}  {r['width']}x{r['height']}  {r['bytes']:,} bytes")
    return p


async def main() -> None:
    settings = load_settings()
    async with BridgeClient(settings.bridge) as c:
        # ── Phase 1 : observe ─────────────────────────────────────────────
        banner("1. Observe the current world")
        state = await c.call("get_world_state")
        w, h = state["width"], state["height"]
        print(f"  map = {w}x{h}  seed={state['seed']}  paused={state['paused']}")
        print(
            f"  population_alive={state['population_alive']}  "
            f"kingdoms={state['kingdoms_alive']}  cities={state['cities_alive']}"
        )
        await save_screenshot(c, "01_before.png")

        # ── Phase 2 : discover what assets the running build exposes ─────
        banner("2. Discover assets the game exposes")
        tiles = (await c.call("list_tiles"))["items"]
        actors = (await c.call("list_actors"))["items"]
        powers = (await c.call("list_powers"))["items"]
        print(f"  tiles: {len(tiles)}  actors: {len(actors)}  powers: {len(powers)}")

        def find(items: list[dict], *keywords: str) -> str | None:
            for it in items:
                idl = it["id"].lower()
                if all(k in idl for k in keywords):
                    return it["id"]
            return None

        sand_id = find(tiles, "sand") or "sand"
        forest_top = find(
            [{"id": i["id"]} for i in (await c.call("list_tiles"))["items"]],
            "forest",
        )
        wolf = find(actors, "wolf")
        bear = find(actors, "bear")
        sheep = find(actors, "sheep")
        human = find(actors, "human")
        dragon = find(actors, "dragon")
        lightning = find(powers, "lightning")
        meteorite = find(powers, "meteor")
        print(
            f"  picked: sand={sand_id}  wolf={wolf}  bear={bear}  sheep={sheep}  "
            f"human={human}  dragon={dragon}"
        )
        print(f"          lightning={lightning}  meteorite={meteorite}")

        # ── Phase 3 : prepare the arena ──────────────────────────────────
        banner("3. Carve an arena: three biomes at three corners of the map")
        cx, cy = w // 2, h // 2
        # A sand "beach" plateau north of center
        north = await c.call(
            "paint_tile", {"x": cx, "y": cy - 30, "tile_id": sand_id, "radius": 8}
        )
        print(f"  north sand plateau: painted={north['painted']} skipped={north['skipped']}")
        # A patch of soil at the south for the herbivores (use the same id the world has)
        soil_id = find(tiles, "soil", "high") or find(tiles, "soil") or sand_id
        south = await c.call(
            "paint_tile", {"x": cx, "y": cy + 30, "tile_id": soil_id, "radius": 8}
        )
        print(f"  south soil patch:   painted={south['painted']} skipped={south['skipped']}")

        # ── Phase 4 : populate ───────────────────────────────────────────
        banner("4. Populate the arena")
        if sheep:
            s = await c.call(
                "spawn",
                {"entity_id": sheep, "x": cx, "y": cy + 30, "count": 10, "adult": True},
            )
            print(f"  {s['spawned']}/{s['requested']} {sheep}s spawned south")
        if wolf:
            s = await c.call(
                "spawn",
                {"entity_id": wolf, "x": cx, "y": cy, "count": 4, "adult": True},
            )
            print(f"  {s['spawned']}/{s['requested']} {wolf}s spawned center")
        if human:
            s = await c.call(
                "spawn",
                {"entity_id": human, "x": cx, "y": cy - 30, "count": 6, "adult": True},
            )
            print(f"  {s['spawned']}/{s['requested']} {human}s spawned north")

        # ── Phase 5 : trigger an event ────────────────────────────────────
        banner("5. Strike with lightning near the center for drama")
        if lightning:
            ev = await c.call(
                "invoke_power", {"power_id": lightning, "x": cx, "y": cy}
            )
            print(f"  lightning at ({cx},{cy}) -> accepted={ev['accepted']}")

        # ── Phase 6 : let the simulation breathe ─────────────────────────
        banner("6. Let the simulation run for a few seconds")
        ticks_before = (await c.call("get_world_state"))["tick"]
        time.sleep(5)
        state_after = await c.call("get_world_state")
        ticks_after = state_after["tick"]
        print(
            f"  tick {ticks_before} -> {ticks_after}  (delta={ticks_after - ticks_before})  "
            f"population_alive={state_after['population_alive']}"
        )

        # ── Phase 7 : observe the outcome ────────────────────────────────
        banner("7. Observe the outcome")
        for race in filter(None, (sheep, wolf, human, dragon)):
            r = await c.call("query_actors", {"race": race, "alive": True, "limit": 50})
            sample = ", ".join(
                f"{it.get('name', '?')}@({it['x']},{it['y']})" for it in r["items"][:5]
            )
            print(f"  {race:8} alive={r['matched']}  sample={sample or '(none)'}")
        kingdoms = await c.call("list_kingdoms", {"include_wild": True})
        print(f"  kingdoms (including wild) = {kingdoms['count']}")

        await save_screenshot(c, "02_after.png")

        # ── Phase 8 : narration ──────────────────────────────────────────
        banner("8. Summary")
        delta_pop = state_after["population_alive"] - state["population_alive"]
        print(
            f"  Started with {state['population_alive']} actors; finished with "
            f"{state_after['population_alive']} (delta={delta_pop:+d})."
        )
        print(f"  Saved before/after screenshots to {OUT_DIR}")


if __name__ == "__main__":
    asyncio.run(main())
