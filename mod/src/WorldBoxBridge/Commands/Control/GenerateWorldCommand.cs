using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;

using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Control;

/// <summary>
/// Regenerates the world map. The new map starts empty (no kingdoms / cities / actors).
/// </summary>
/// <remarks>
/// Pipeline (per decompiled <c>MapBox.generateNewMap</c>):
///   1. <c>MapBox.instance.setMapSize(zone_x, zone_y)</c> if size args provided.
///   2. <c>MapBox.instance.generateNewMap()</c> queues work on SmoothLoader.
///   3. The actual generation happens over many frames after we return — our success
///      response signals "generation scheduled", not "world ready".
/// Map dimensions = zone_x * 64 × zone_y * 64. Default zones 4×4 → 256×256 tiles.
/// </remarks>
internal sealed class GenerateWorldCommand : ICommand
{
    private readonly WorldAccess _world;
    private readonly ManualLogSource _log;

    private MethodInfo? _setMapSize;
    private MethodInfo? _generateNewMap;

    public GenerateWorldCommand(WorldAccess world, ManualLogSource log)
    {
        _world = world;
        _log = log;
    }

    public string Name => "generate_world";
    public CommandCategory Category => CommandCategory.Control;
    public string Description =>
        "Regenerates the world map (kingdoms / cities / actors wiped). Optional zone_x and "
        + "zone_y set the map size in 64-tile zones (default 4x4 = 256x256). Generation runs "
        + "asynchronously over many frames; the response signals 'scheduled', not 'ready'.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty(
                        "zone_x",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty("minimum", 2),
                            new JProperty("maximum", 16),
                            new JProperty(
                                "description",
                                "Map width in 64-tile zones (e.g. 4 -> width 256)."
                            )
                        )
                    ),
                    new JProperty(
                        "zone_y",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty("minimum", 2),
                            new JProperty("maximum", 16)
                        )
                    )
                )
            ),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken cancellationToken)
    {
        var mb = _world.MapBoxInstance;
        if (mb == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "MapBox.instance is null — engine not ready to generate a world."
            );
        }
        var zoneX = args.Value<int?>("zone_x");
        var zoneY = args.Value<int?>("zone_y");

        var mbType = mb.GetType();
        if (zoneX.HasValue && zoneY.HasValue)
        {
            _setMapSize ??= mbType.GetMethod(
                "setMapSize",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(int), typeof(int) },
                modifiers: null
            );
            if (_setMapSize == null)
            {
                throw new BridgeRejectionException(
                    ErrorCode.GameRejected,
                    "MapBox.setMapSize(int, int) not found."
                );
            }
            _setMapSize.Invoke(mb, new object[] { zoneX.Value, zoneY.Value });
        }

        _generateNewMap ??= mbType.GetMethod(
            "generateNewMap",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null
        );
        if (_generateNewMap == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "MapBox.generateNewMap() not found."
            );
        }
        try
        {
            _generateNewMap.Invoke(mb, Array.Empty<object>());
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException ?? tie;
        }

        return Task.FromResult<object?>(
            new
            {
                scheduled = true,
                zone_x = zoneX,
                zone_y = zoneY,
                note = "Generation runs asynchronously. Poll get_world_state until tick advances.",
            }
        );
    }
}
