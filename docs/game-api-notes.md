# Game API notes

Working notes on WorldBox internals discovered while building the mod. These are **not authoritative documentation** — they reflect what we observed in `Assembly-CSharp.dll` at a specific SHA256.

| Field | Value |
|---|---|
| WorldBox version observed | 0.51.2 |
| Unity version | 2022.3.60f1, Mono scripting backend |
| Assembly-CSharp.dll SHA256 | `51d275f0168be2f6ca26341ab292406714e694e0270eafcb25b999d5df6dd69f` |
| Decompiler | [ilspycmd](https://github.com/icsharpcode/ILSpy) 8.2.0.7535 |
| Last verified | 2026-05-16 against worldbox-mcp v0.1.1 |

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

The fields we actually use in commands:

| AssetManager field | Type | Used by |
|---|---|---|
| `tiles` | `TileLibrary` | `list_tiles`, `paint_tile` |
| `top_tiles` | `TopTileLibrary` | `list_tiles` (overlays / decorations) |
| `actor_library` | `ActorAssetLibrary` | `list_actors`, `spawn` |
| `powers` | `PowerLibrary` | `list_powers`, `invoke_power` |
| `spells` | `SpellLibrary` | possible future: `cast_spell` |
| `disasters` | `DisasterLibrary` | included in `list_powers` (disasters are a power category) |
| `kingdoms` | `KingdomLibrary` | kingdom *templates* (not live kingdoms — those live on `MapBox.instance.kingdoms`, see below) |
| `biome_library` | `BiomeLibrary` | informational |
| `terraform` | `TerraformLibrary` | terrain reshaping commands |
| `buildings` | `BuildingLibrary` | future: spawn buildings |
| `projectiles` | `ProjectileLibrary` | future |
| `items` | `ItemLibrary` | future |
| `effects_library` | `EffectsLibrary` | future |
| `time_scales` | `WorldTimeScaleLibrary` | `set_speed` (ids: `slow_mo`, `x1`, `x2`, `x3`, `x5`, `x10`, `x15`, `x20`) |

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

---

## Live entity iteration — `CoreSystemManager<T>`

This is **separate** from the asset library system above. Actor/Kingdom/City instances
that currently exist in the world live in manager objects on `MapBox.instance`:

| Field | Type | Iterated by |
|---|---|---|
| `MapBox.instance.units` | `ActorManager : SimSystemManager<Actor, ActorData>` | `query_actors`, `get_world_state` |
| `MapBox.instance.kingdoms` | `KingdomManager : MetaSystemManager<Kingdom, KingdomData>` | `list_kingdoms`, `get_world_state` |
| `MapBox.instance.cities` | `CityManager : MetaSystemManager<City, CityData>` | `list_cities`, `get_world_state` |
| `MapBox.instance.map_stats` | `MapStats` | `get_world_state` (lifetime counters: population, kingdomsCreated, citiesCreated, …) |

Both `SimSystemManager<T, TData>` and `MetaSystemManager<T, TData>` derive from a common base:

```csharp
public abstract class CoreSystemManager<TObject, TData>
    : SystemManager<TObject, TData>, IEnumerable<TObject>, IEnumerable
    where TObject : CoreSystemObject<TData>, new()
    where TData   : BaseSystemData, new()
{
    public IEnumerator<TObject> GetEnumerator() => _hashset.GetEnumerator();
    public override int Count => _hashset.Count;
}
```

**Both manager families implement `IEnumerable<T>`** with the storage being a private
`HashSet<TObject>`. The naive approach of looking for a `getSimpleList()` method only worked
for the `SimSystemManager` half — `MetaSystemManager` doesn't define it. The correct,
universal approach is to cast the manager to `IEnumerable` and use `foreach` (or read the
`Count` property for size).

`WorldAccess.GetSimpleList` and `WorldAccess.GetManagerCount` both use this pattern as of
v0.1.1.

---

## Action recipes — confirmed in production

| Action | Entry point |
|---|---|
| Paint a tile | `WorldTile.setTileType(string id)` — string overload that does the asset lookup internally. Optional `WorldTile.setTopTileType(TopTileType asset, bool updateStats=true)` for decoration overlay. The game handles dirty-flagging + stats updates. |
| Spawn an actor | `MapBox.instance.units.spawnNewUnit(string id, WorldTile tile, bool spawnSound=false, bool miracle=false, float spawnHeight=6f, Subspecies sub=null, bool giveOwnerlessItems=false, bool adult=false)`. Returns the new `Actor` (null on unknown id). Auto-assigns wild kingdom via `ActorAsset.kingdom_id_wild`. |
| Invoke a power | A `GodPower` carries one of several click delegates. Most set `PowerActionWithID click_action` (`bool (WorldTile, string powerId)`); the drops / bombs / drop-building templates (`rain`, `fire`, `bomb`, `volcano`, …) set `PowerAction click_power_action` (`bool (WorldTile, GodPower)`) instead. Resolve `AssetManager.powers.get(id)`, try `click_action` then `click_power_action`, invoke with the matching args. Returns `bool` = accepted (drops roll `falling_chance`). Still uncovered: `click_brush_action` (needs brush state), `toggle_action`, `click_special_action`. `finger` NREs because `drawFinger` reads `player_control.first_pressed_type`, set only by a real mouse press. |
| Pause | `Config.paused` static bool property. Setter toggles. |
| Set speed | `Config.setWorldSpeed(string speed_id, bool updateDebug=true)` — resolves via `AssetManager.time_scales.get(id)` internally. |
| Generate world | `MapBox.instance.setMapSize(int zone_x, int zone_y)` then `MapBox.instance.generateNewMap()`. Map size = zone × 64. Generation runs asynchronously over many frames via `SmoothLoader`. |
| Save world | `SaveManager.saveWorldToDirectory(string folder, bool compress=true, bool checkFolder=true)` — static, writes a folder of files. |
| Load world | `SaveManager.loadMapFromBytes(byte[] zippedBytes)` — static, async via `SmoothLoader`. |
| Screenshot | `ScreenCapture.CaptureScreenshotAsTexture()` then `texture.EncodeToPNG()`. Main-thread only — runs in our `PlayerLoop` Update phase. Destroy the texture immediately after to avoid VRAM/GC pressure. |

---

## Speed catalog

`AssetManager.time_scales` lists `WorldTimeScaleAsset` entries with `multiplier` /
`ticks` / `conway_ticks`. On stock WorldBox 0.51.2:

| id | multiplier | notes |
|---|---|---|
| `slow_mo` | 0.5× | half-speed for fine observation |
| `x1` | 1× | default |
| `x2` | 2× | UI button |
| `x3` | 3× | UI button |
| `x5` | 5× | UI button (5+ requires premium in vanilla, but no enforcement at the API layer) |
| `x10` | 10× | hidden via UI but accepted by API |
| `x15` | 15× | hidden via UI but accepted by API |
| `x20` | 20× | hidden via UI but accepted by API |

`set_speed("x99")` returns `UNKNOWN_ASSET` with `did_you_mean: ["x1", "x10", "x15", "x2", "x20"]` — that's how this list was discovered.
