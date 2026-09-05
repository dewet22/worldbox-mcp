# Game API notes

Working notes on WorldBox internals discovered while building the mod. These are **not authoritative documentation**, they reflect what we observed in `Assembly-CSharp.dll` at a specific SHA256.

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

## AssetManager, central registry

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
| `kingdoms` | `KingdomLibrary` | kingdom *templates* (not live kingdoms, those live on `MapBox.instance.kingdoms`, see below) |
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

## Live entity iteration, `CoreSystemManager<T>`

This is **separate** from the asset library system above. Actor/Kingdom/City instances
that currently exist in the world live in manager objects on `MapBox.instance`:

| Field | Type | Iterated by |
|---|---|---|
| `MapBox.instance.units` | `ActorManager : SimSystemManager<Actor, ActorData>` | `query_actors`, `get_world_state` |
| `MapBox.instance.kingdoms` | `KingdomManager : MetaSystemManager<Kingdom, KingdomData>` | `list_kingdoms`, `get_world_state` |
| `MapBox.instance.cities` | `CityManager : MetaSystemManager<City, CityData>` | `list_cities`, `get_world_state` |
| `MapBox.instance.map_stats` | `MapStats` | `get_world_state` (lifetime counters: population, kingdomsCreated, citiesCreated, ...) |

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
for the `SimSystemManager` half, `MetaSystemManager` doesn't define it. The correct,
universal approach is to cast the manager to `IEnumerable` and use `foreach` (or read the
`Count` property for size).

`WorldAccess.GetSimpleList` and `WorldAccess.GetManagerCount` both use this pattern as of
v0.1.1.

---

## Action recipes, confirmed in production

| Action | Entry point |
|---|---|
| Paint a tile | `WorldTile.setTileType(string id)`, string overload that does the asset lookup internally. Optional `WorldTile.setTopTileType(TopTileType asset, bool updateStats=true)` for decoration overlay. The game handles dirty-flagging + stats updates. |
| Spawn an actor | `MapBox.instance.units.spawnNewUnit(string id, WorldTile tile, bool spawnSound=false, bool miracle=false, float spawnHeight=6f, Subspecies sub=null, bool giveOwnerlessItems=false, bool adult=false)`. Returns the new `Actor` (null on unknown id). Auto-assigns wild kingdom via `ActorAsset.kingdom_id_wild`. |
| Invoke a power | A `GodPower` carries five click delegates plus a toggle. `PowerActionWithID click_action` and `click_brush_action` share `bool (WorldTile, string powerId)`; `PowerAction click_power_action` and `click_power_brush_action` share `bool (WorldTile, GodPower)`; `PowerToggleAction toggle_action` is `void (string powerId)` (invoked by `PowerButton` with just the id, no tile). Resolve `AssetManager.powers.get(id)`, pick a delegate (see brush machinery below), invoke with the matching args. Returns `bool` = accepted (drops roll `falling_chance`). Still uncovered: `click_special_action`. `finger` NREs because `drawFinger` reads `player_control.first_pressed_type`, set only by a real mouse press. |
| Pause | `Config.paused` static bool property. Setter toggles. |
| Set speed | `Config.setWorldSpeed(string speed_id, bool updateDebug=true)`, resolves via `AssetManager.time_scales.get(id)` internally. |
| Generate world | `MapBox.instance.setMapSize(int zone_x, int zone_y)` then `MapBox.instance.generateNewMap()`. Map size = zone × 64. Generation runs asynchronously over many frames via `SmoothLoader`. |
| Save world | `SaveManager.saveWorldToDirectory(string folder, bool compress=true, bool checkFolder=true)`, static, writes a folder of files. |
| Load world | `SaveManager.loadMapFromBytes(byte[] zippedBytes)`, static, async via `SmoothLoader`. |
| Screenshot | `ScreenCapture.CaptureScreenshotAsTexture()` then `texture.EncodeToPNG()`. Main-thread only, runs in our `PlayerLoop` Update phase. Destroy the texture immediately after to avoid VRAM/GC pressure. |

---

## Brush machinery, how radius reaches a power

All verified against the 0.51.2 decompile.

- The brush delegates expand the affected area **inside the delegate**, not in the caller:
  `PlayerControl.clickPower` hands them only the clicked tile. The standard bodies
  (`PowerLibrary.loopWithCurrentBrush`, `loopWithCurrentBrushPowerForDropsFull` /
  `...Random`) read `Config.current_brush_data` and call
  `MapBox.loopWithBrush(centerTile, brushData, perTileAction, power)`, which iterates
  `BrushData.pos` (a precomputed offset array) with bounds checks.
- In-game delegate precedence (`PlayerControl.clickPower`): the power-delegate family is
  checked first, brush variant preferred, `click_power_brush_action` >
  `click_power_action`, then `click_brush_action` > `click_action`.
- `Config.current_brush` is a public static string property (default `"circ_5"`); its
  setter populates `Config.current_brush_data` via `Brush.get(id)`. Brushes are assets in
  `AssetManager.brush_library`.
- `Brush.get(int pSize, string pID = "circ_")` auto-creates missing sizes: it clones
  `circ_1` as `circ_N`, sets `size`, and calls `brush_library.post_init()`, whose
  `generate_action` (inherited from `circ_1`, a filled-circle generator parameterised by
  `size`) rebuilds the pixel grid. Arbitrary radii therefore work natively.
- The drops template (`$template_drops$`: rain, fire, lava, acid, ...) sets **both**
  `click_power_action` (single tile) and `click_power_brush_action` (area), which is why
  single-tile invocation worked before the bridge drove brushes at all.
- The bridge (`invoke_power` with `radius`) ensures `circ_<radius>` exists, then sets
  `Config.current_brush` around each brush-delegate invocation and restores the previous
  brush in a `finally`, so the player's own brush selection never visibly changes, even
  during multi-frame pulse runs.
- There is no intensity parameter anywhere in the game's power model. The interactive
  "storm" is repetition: holding the mouse (or the shift modifier,
  `HotkeyLibrary.many_mod`) re-fires the click delegate once per frame in
  `PlayerControl.update`, and dragging moves the sampled tile between frames. The bridge's
  `pulses` / `x2`+`y2` arguments reproduce exactly that, one delegate call per PlayerLoop
  frame via the dispatcher's per-frame job, with the target tile interpolated along the
  drag line. Note the in-game `click_interval` throttle lives in `PlayerControl`, not in
  the delegates, so bridge pulses are not subject to it.

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
| `x4` | 4× | UI button |
| `x5` | 5× | UI button (5+ requires premium in vanilla, but no enforcement at the API layer) |
| `x10` | 10× | hidden via UI but accepted by API |
| `x15` | 15× | hidden via UI but accepted by API |
| `x20` | 20× | hidden via UI but accepted by API |
| `x40` | 20× | hidden via UI. The multiplier really is 20, same as `x20`, so these ids are labels rather than factors |

Ten entries in total. Call `list_speeds` for the live list from the running build, including which
one is currently active. `set_speed("x99")` returns `UNKNOWN_ASSET` and lists every valid id.

---

## Gotchas, the ones that cost us a day each

These are real bugs and mismatches we hit and fixed. **If something in the reflection layer
breaks, read this list before debugging anything else.**

1. **`System.Net.HttpListener` silently refuses to bind** under Unity 2022.3 Mono. `IsListening`
   returns true while `netstat` shows no port at all. That is why `HttpBridge.cs` is a
   `TcpListener` plus a hand-rolled HTTP/1.1 parser instead of the obvious thing. See
   [Unity Discussions #755558](https://discussions.unity.com/t/httplistener-ignores-port-on-some-windows-platform-s/755558).

2. **`new TcpListener(IPAddress.Parse("127.0.0.1"), port)` also silently fails to bind.** The
   `Parse` path produces an instance Mono treats differently from the static constant. Always use
   `IPAddress.Loopback`, or `IPAddress.IPv6Loopback` / `IPAddress.Any` if you really mean those.
   `BridgeConfig.AssertLoopbackOnly` and the host switch in `HttpBridge` enforce it.

3. **BepInEx `MonoBehaviour` GameObjects get destroyed shortly after `Awake`** in this game, so
   `MainThreadDispatcher` does not live on one. It injects a delegate straight into Unity's
   `PlayerLoop` Update phase through `PlayerLoop.SetPlayerLoop`. That entry is part of the
   engine's tick table and survives the lifecycle quirk.

4. **`SimSystemManager<,>` has `getSimpleList()`, `MetaSystemManager<,>` does not.** Both inherit
   from `CoreSystemManager<,>`, which implements `IEnumerable<T>`. Iterate any manager through
   `IEnumerable`, never through `getSimpleList` reflection. Same for `Count`, which is a property
   on `CoreSystemManager`. Getting this wrong makes `list_kingdoms` return zero while kingdoms
   are plainly alive.

5. **`System.ValueTuple` is not always loadable under Unity Mono** on net462, since it ships
   out of band. Tuple syntax in a signature, a field type or a dictionary key can throw
   `TypeLoadException` at first JIT. Use a plain `readonly struct`. `WorldAccess.MapDimensions`,
   `AssetCatalog.TypeFieldKey` and `HttpBridge.HeaderReadResult` all exist for this reason.

6. **`Type.GetMethod(name, flags)` without explicit argument types throws
   `AmbiguousMatchException`** as soon as the name has overloads, and `Actor.getName` and
   `WorldTile.setTileType` both do. `WorldAccess.CachedMethod` and `GameRefs.Method` enumerate
   `GetMethods()` and filter by hand rather than using the convenience overload. Pass explicit
   argument types anyway when you know the method is overloaded.

7. **Powers use different click delegates.** Most `GodPower`s set `click_action`, typed
   `(WorldTile, string)`. The drops, bombs and drop-building families (`rain`, `fire`, `bomb`,
   `volcano`, `plague`, `acid`) set `click_power_action`, typed `(WorldTile, GodPower)` instead,
   and often a brush variant too (`click_brush_action` / `click_power_brush_action`, same two
   signatures, expanding `Config.current_brush_data` internally; see the brush machinery
   section). `invoke_power` drives all four plus `toggle_action` and reports which one fired in
   `via`. Still not drivable: `click_special_action`, and anything reading live pointer state.
   `finger` reads `player_control.first_pressed_type`, which only a real mouse press sets, so it
   throws inside the game and is reported as `GAME_REJECTED`.

8. **`SaveManager.saveWorldToDirectory` throws a `NullReferenceException` when no world is
   loaded**, deep inside `World.world.items.diagnostic()`. `SaveWorldCommand` pre-flights on map
   dimensions and refuses with a clear message. It also refuses while `Config.worldLoading` is
   true, because `MapBox` reports its dimensions before loading has actually finished.

9. **`Application.unityVersion` reports `"2022.3.60f1"` while the real build is
   `2022.3.60.6251517`** according to the BepInEx log. `/health` reports the public-facing string.

10. **A dependency bump can break the plugin at load time without touching a line of game code.**
    The plugin binds at runtime to whatever BepInEx and the game bundle, not to what NuGet
    restored. Two real cases, both from one automated dependency sweep. HarmonyX 2.16 pulled in
    `MonoMod.Backports`, which BepInEx 5.4.23 does not ship, so `Chainloader.Start` threw
    `FileNotFoundException` and `Awake` never ran. Newtonsoft.Json 13.0.4 added
    `JToken.ToString(Formatting)`, the compiler preferred that overload, and the game's bundled
    Newtonsoft.Json-for-Unity 13.0.2 threw `MissingMethodException` on `/capabilities`. Both were
    invisible in `LogOutput.log` and only showed up in Unity's `Player.log`. The rules that came
    out of it: keep Newtonsoft.Json pinned to the game's version, never reference a package you
    do not use, and after any mod dependency change check the built DLL's references
    (`strings WorldBoxBridge.dll | grep -i monomod`) and make one live `/capabilities` call.

---

## Two registry families, do not confuse them

| Asset library, the templates | Live entity manager, the instances |
|---|---|
| `AssetManager.tiles`, TileType templates | `MapBox.instance.tiles_map[x,y]`, actual WorldTile instances |
| `AssetManager.actor_library`, ActorAsset templates | `MapBox.instance.units`, ActorManager of live Actors |
| `AssetManager.kingdoms`, KingdomAsset templates, meaning race definitions | `MapBox.instance.kingdoms`, KingdomManager of live Kingdoms |
| Iterated through the `AssetLibrary<T>.list` field | Iterated through `IEnumerable<T>` on `CoreSystemManager` |

`list_tiles`, `list_actors`, `list_powers` and `list_speeds` read the asset side.
`list_kingdoms`, `list_cities`, `query_actors` and `get_world_state` read the live side.
