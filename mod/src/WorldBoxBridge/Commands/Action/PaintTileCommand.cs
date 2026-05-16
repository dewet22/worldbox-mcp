using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;

namespace WorldBoxBridge.Commands.Action;

/// <summary>
/// Sets the tile type at a single coordinate or across a disc of <c>radius</c> cells.
/// Optional <c>top_id</c> sets the top decoration (forest, road, wasteland, …) on the same disc.
/// </summary>
/// <remarks>
/// Calls <c>WorldTile.setTileType(string)</c> and <c>WorldTile.setTopTileType(TopTileType, true)</c>.
/// The game handles dirty-flagging, neighbour wall recomputation, and stat updates internally.
/// </remarks>
internal sealed class PaintTileCommand : ICommand
{
    private readonly AssetCatalog _catalog;
    private readonly WorldAccess _world;
    private readonly ManualLogSource _log;

    private MethodInfo? _setTileTypeString;
    private MethodInfo? _setTopTileType;

    public PaintTileCommand(AssetCatalog catalog, WorldAccess world, ManualLogSource log)
    {
        _catalog = catalog;
        _world = world;
        _log = log;
    }

    public string Name => "paint_tile";
    public CommandCategory Category => CommandCategory.Action;
    public string Description =>
        "Paints a tile (or a disc of tiles for radius > 0). tile_id changes the main "
        + "ground type (water, lava, sand, soil, …). Optional top_id changes the top "
        + "decoration overlay (forests, roads, wasteland, …). Discover valid ids via "
        + "list_tiles. Out-of-map cells are silently skipped.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty("x", new JObject(new JProperty("type", "integer"))),
                    new JProperty("y", new JObject(new JProperty("type", "integer"))),
                    new JProperty(
                        "tile_id",
                        new JObject(
                            new JProperty("type", "string"),
                            new JProperty(
                                "description",
                                "Main tile id from list_tiles (e.g. 'sand', 'lava0', 'shallow_waters'). "
                                    + "Optional if top_id is set."
                            )
                        )
                    ),
                    new JProperty(
                        "top_id",
                        new JObject(
                            new JProperty("type", "string"),
                            new JProperty(
                                "description",
                                "Optional top decoration id (forests, roads, wasteland, …)."
                            )
                        )
                    ),
                    new JProperty(
                        "radius",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty("minimum", 0),
                            new JProperty("maximum", 200),
                            new JProperty("default", 0),
                            new JProperty(
                                "description",
                                "If > 0, paint every tile within this many cells (Euclidean disc) of (x, y)."
                            )
                        )
                    )
                )
            ),
            new JProperty("required", new JArray("x", "y")),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, CancellationToken cancellationToken)
    {
        if (!args.TryGetValue("x", out var xT) || !args.TryGetValue("y", out var yT))
        {
            throw new ArgumentException("x and y are required integers.");
        }
        var cx = (int)xT!;
        var cy = (int)yT!;
        var tileId = args.Value<string?>("tile_id");
        var topId = args.Value<string?>("top_id");
        var radius = Math.Max(0, args.Value<int?>("radius") ?? 0);

        if (string.IsNullOrEmpty(tileId) && string.IsNullOrEmpty(topId))
        {
            throw new ArgumentException("Provide at least one of tile_id, top_id.");
        }

        // Validate ids against the right libraries (pre-check → did_you_mean on typo).
        if (!string.IsNullOrEmpty(tileId) && _catalog.Resolve("tiles", tileId!) == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.UnknownAsset,
                $"tile_id '{tileId}' is not a registered tile.",
                didYouMean: _catalog.Suggest("tiles", tileId!)
            );
        }
        if (!string.IsNullOrEmpty(topId) && _catalog.Resolve("top_tiles", topId!) == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.UnknownAsset,
                $"top_id '{topId}' is not a registered top tile.",
                didYouMean: _catalog.Suggest("top_tiles", topId!)
            );
        }

        var dims = _world.GetMapDimensions();
        if (dims == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "MapBox dimensions not available — game world not initialised."
            );
        }
        var w = dims.Value.Width;
        var h = dims.Value.Height;

        // Resolve the top-tile asset once (we'll pass the same object every iteration).
        object? topAsset = string.IsNullOrEmpty(topId)
            ? null
            : _catalog.Resolve("top_tiles", topId!);

        var painted = 0;
        var skipped = 0;
        var rSquared = radius * radius;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > rSquared)
                {
                    continue;
                }
                var x = cx + dx;
                var y = cy + dy;
                if (x < 0 || y < 0 || x >= w || y >= h)
                {
                    skipped++;
                    continue;
                }
                var tile = _world.GetTileAt(x, y);
                if (tile == null)
                {
                    skipped++;
                    continue;
                }
                if (!string.IsNullOrEmpty(tileId))
                {
                    _setTileTypeString ??= ResolveSetTileTypeString(tile.GetType());
                    if (_setTileTypeString == null)
                    {
                        throw new BridgeRejectionException(
                            ErrorCode.GameRejected,
                            "WorldTile.setTileType(string) not found in this WorldBox build."
                        );
                    }
                    _setTileTypeString.Invoke(tile, new object?[] { tileId });
                }
                if (topAsset != null)
                {
                    _setTopTileType ??= ResolveSetTopTileType(tile.GetType());
                    if (_setTopTileType == null)
                    {
                        throw new BridgeRejectionException(
                            ErrorCode.GameRejected,
                            "WorldTile.setTopTileType(...) not found in this WorldBox build."
                        );
                    }
                    _setTopTileType.Invoke(tile, new object?[] { topAsset, true });
                }
                painted++;
            }
        }

        return Task.FromResult<object?>(
            new
            {
                center = new { x = cx, y = cy },
                radius,
                tile_id = tileId,
                top_id = topId,
                painted,
                skipped,
            }
        );
    }

    private static MethodInfo? ResolveSetTileTypeString(Type worldTileType)
    {
        foreach (
            var mi in worldTileType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )
        )
        {
            if (mi.Name != "setTileType")
            {
                continue;
            }
            var ps = mi.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
            {
                return mi;
            }
        }
        return null;
    }

    private static MethodInfo? ResolveSetTopTileType(Type worldTileType)
    {
        foreach (
            var mi in worldTileType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )
        )
        {
            if (mi.Name == "setTopTileType")
            {
                return mi;
            }
        }
        return null;
    }
}
