using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;

using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Read;

/// <summary>
/// Returns a snapshot of one tile: ground type, top decoration, height, and the list of
/// actor names currently standing on it.
/// </summary>
internal sealed class GetTileCommand : ICommand
{
    private readonly WorldAccess _world;

    public GetTileCommand(WorldAccess world) => _world = world;

    public string Name => "get_tile";
    public CommandCategory Category => CommandCategory.Read;
    public string Description =>
        "Returns the tile at (x, y): tile_id, top_id (decoration), height, kingdom_id, city_id, "
        + "and the names of the actors standing on it. OUT_OF_BOUNDS when (x, y) is off the map.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty("x", new JObject(new JProperty("type", "integer"))),
                    new JProperty("y", new JObject(new JProperty("type", "integer")))
                )
            ),
            new JProperty("required", new JArray("x", "y")),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken cancellationToken)
    {
        if (!args.TryGetValue("x", out var xT) || !args.TryGetValue("y", out var yT))
        {
            throw new ArgumentException("x and y are required integers.");
        }
        var x = (int)xT!;
        var y = (int)yT!;

        var tile = _world.GetTileAt(x, y);
        if (tile == null)
        {
            var dims = _world.GetMapDimensions();
            var range = dims.HasValue ? $" (map is {dims.Value.Width}x{dims.Value.Height})" : string.Empty;
            throw new BridgeRejectionException(
                ErrorCode.OutOfBounds,
                $"({x},{y}) is outside the map or the world isn't initialised yet{range}."
            );
        }

        var mainType = _world.Read(tile, "main_type");
        var topType = _world.Read(tile, "top_type");
        var height = _world.Read(tile, "Height") as int?;

        var actors = new List<string>();
        // Walk WorldTile's private `_units` list (faster + simpler than building a delegate
        // for the public doUnits(Action<Actor>) helper).
        var unitsField = _world.CachedField(tile.GetType(), "_units");
        if (unitsField?.GetValue(tile) is System.Collections.IEnumerable unitsEnum)
        {
            foreach (var actor in unitsEnum)
            {
                if (actor == null)
                {
                    continue;
                }
                if (_world.CachedMethod(actor.GetType(), "getName")?.Invoke(actor, Array.Empty<object>()) is string name)
                {
                    actors.Add(name);
                }
            }
        }

        return Task.FromResult<object?>(
            new
            {
                x,
                y,
                tile_id = ReadId(mainType),
                top_id = ReadId(topType),
                height = height ?? 0,
                actors,
                actor_count = actors.Count,
            }
        );
    }

    private string? ReadId(object? asset) =>
        asset == null ? null : _world.Read(asset, "id") as string;
}
