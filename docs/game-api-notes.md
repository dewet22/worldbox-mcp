# Game API notes

Working notes on WorldBox internals discovered while building the mod. These are **not authoritative documentation** — they reflect what we observed in `Assembly-CSharp.dll` at a specific SHA256.

| Field | Value |
|---|---|
| WorldBox version observed | 0.51.2 |
| Unity version | 2022.3.60f1, Mono scripting backend |
| Assembly-CSharp.dll SHA256 | `51d275f0168be2f6ca26341ab292406714e694e0270eafcb25b999d5df6dd69f` |
| Decompiler | [ilspycmd](https://github.com/icsharpcode/ILSpy) 8.2.0.7535 |

---

## World singleton

```csharp
public class MapBox : MonoBehaviour
{
    public static MapBox instance;       // ← THE singleton
    public static int width;             // map width in tiles
    public static int height;            // map height in tiles
    public static int current_world_seed_id;
    internal WorldTile[,] tiles_map;     // 2D grid
    internal WorldTile[] tiles_list;     // flat list
    internal MapStats map_stats;
    internal WorldLaws world_laws;
    // ...
}

// Convenience alias — `World.world` returns `MapBox.instance`.
public static class World
{
    public static MapBox world => MapBox.instance;
    public static WorldAgeAsset world_era => MapBox.instance.era_manager.getCurrentAge();
}
```

**Reflection path**: `Type.GetType("MapBox, Assembly-CSharp").GetField("instance").GetValue(null)`.

---

## AssetManager — central registry

`AssetManager` is a static class with ~150 public static fields, each pointing to a typed library. All libraries inherit from `AssetLibrary<T>` and follow the same iteration contract (see below).

The fields we care about for Phase 2/3 commands:

| AssetManager field | Type | Used by |
|---|---|---|
| `tiles` | `TileLibrary` | `list_tiles`, `paint_tile` |
| `top_tiles` | `TopTileLibrary` | `list_tiles` (overlays / decorations) |
| `actor_library` | `ActorAssetLibrary` | `list_actors`, `spawn` |
| `powers` | `PowerLibrary` | `list_powers`, `invoke_power` |
| `spells` | `SpellLibrary` | possible future: `cast_spell` |
| `disasters` | `DisasterLibrary` | included in `list_powers` (disasters are a power category) |
| `kingdoms` | `KingdomLibrary` | `list_kingdoms`, future kingdom ops |
| `biome_library` | `BiomeLibrary` | informational |
| `terraform` | `TerraformLibrary` | terrain reshaping commands |
| `buildings` | `BuildingLibrary` | future: spawn buildings |
| `projectiles` | `ProjectileLibrary` | future |
| `items` | `ItemLibrary` | future |
| `effects_library` | `EffectsLibrary` | future |

A full dump of `AssetManager`'s static fields is preserved in `scratch/AssetManager.cs`.

---

## Universal library contract

Every library inherits from a generic base that exposes a uniform read API:

```csharp
public abstract class AssetLibrary<T> : BaseAssetLibrary where T : Asset
{
    public List<T> list;                          // every registered asset
    [NonSerialized] public Dictionary<string, T> dict;   // id → asset
    public virtual T get(string pID);             // returns null on miss
    public virtual bool has(string pID);
    public override int total_items => list.Count;
}
```

This means **one piece of reflection code lists or resolves any asset id in the game**. No need to specialise per library beyond a string field name on `AssetManager`.

```csharp
// Pseudocode shape used in mod/src/WorldBoxBridge/Reflection/AssetCatalog.cs
var amType   = Type.GetType("AssetManager, Assembly-CSharp");
var libField = amType.GetField("tiles" /* or "actor_library", "powers", ... */);
var library  = libField.GetValue(null);                 // static field
var list     = library.GetType().GetField("list").GetValue(library) as IEnumerable;
foreach (var item in list)
{
    var id = (string)item.GetType().GetField("id").GetValue(item);
    // ...
}
```

### `Asset` base class

```csharp
public abstract class Asset : IEquatable<Asset>
{
    [JsonProperty(Order = -1)]
    public string id = "ASSET_ID";
    // ...
}
```

Every asset has `.id`. Template/internal assets (prefixed with `$` or `_`) are filtered out via `isTemplateAsset()`.

### `BaseLibraryWithUnlockables<T>`

`ActorAssetLibrary` is `BaseLibraryWithUnlockables<ActorAsset>` rather than the plain `AssetLibrary<T>`. The unlockables flavour adds `elements_list` (an `IEnumerable<BaseUnlockableAsset>` view) but inherits the same `list`/`dict`/`get` contract.

---

## Tile-specific

```csharp
public class TileLibrary : TileLibraryMain<TileType>
{
    public static TileType summit, mountains, hills;
    public static TileType deep_ocean, close_ocean, shallow_waters;
    public static TileType sand, soil_low, soil_high;
    public static TileType lava0, lava1, lava2, lava3;
    public static TileType pit_deep_ocean, pit_close_ocean, pit_shallow_waters;
    public static TileType grey_goo;
    public static List<TileType> lava_types;
    public static TileTypeBase[] array_tiles;  // fixed 256-slot table
    // ...
}

[Serializable] public class TileType : TileTypeBase { /* empty body */ }

public class TileTypeBase : Asset
{
    public WorldAction unit_death_action;
    public TileStepAction step_action;
    public float step_action_chance;
    public bool force_edge_variation;
    // ... biome tags, colors, height bands, etc.
}
```

---

## Action recipes (Phase 3 — to be confirmed)

These need to be verified once the discovery commands work and we can introspect the live game. Working hypotheses based on type signatures:

| Action | Likely entry point |
|---|---|
| Paint a tile | `MapBox.instance.setTileType(tile, x, y)` (need to confirm exact signature on `MapBox`/`WorldTile`) |
| Spawn an actor | `World.world.units_manager.spawnNewUnit(asset_id, x, y, …)` or similar |
| Invoke a power | `AssetManager.powers.get(id).action(...)` — `PowerLibrary` entries carry a delegate |

These will be filled in as Phase 3 lands.
