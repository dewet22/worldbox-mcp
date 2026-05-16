# Game API notes

Working notes on the WorldBox internals discovered while building the mod. These are **not authoritative documentation** — they reflect what we observed in `Assembly-CSharp.dll` of a specific version. Keep this page updated as you decompile new versions.

> _Stub. Populated incrementally during Phase 2._

## Decompilation setup

1. Install [ILSpy](https://github.com/icsharpcode/ILSpy/releases) (portable).
2. Open `<worldbox>/worldbox_Data/Managed/Assembly-CSharp.dll`.
3. Useful starting points:
   - `MapBox` — world singleton / accessor
   - `World` — current simulation
   - `Actor` — unit base class
   - `Kingdom` — civilization
   - `AssetManager` — registries (tiles, actors, powers)

## Recording template

When you identify a useful API, document it here in this format:

```markdown
### MapBox.SetTileType(int x, int y, TileType type)

- **WorldBox version observed**: 0.x.x
- **DLL SHA256**: …
- **Signature**: `public void SetTileType(int x, int y, TileType type)`
- **Thread**: must be called on Unity main thread.
- **Side effects**: triggers `TileChanged` event; updates ECS.
- **Used by**: `PaintTileCommand`.
- **Notes**: …
```

Keep entries sorted by class name. Mark broken/changed bindings with `⚠️ broken since 0.x.x`.
