using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Control;

/// <summary>
/// Changes the simulation tick rate. Accepts a <c>WorldTimeScaleAsset</c> id from the
/// game's <c>WorldTimeScaleLibrary</c> (the same ids the in-game speed-control UI uses).
/// </summary>
/// <remarks>
/// Mechanism: <c>Config.setWorldSpeed(string)</c> resolves the asset via
/// <c>AssetManager.time_scales.get(id)</c> and applies it. We just call that static
/// method by reflection. Stock WorldBox 0.51.x ids: <c>slow_mo</c>, <c>x1</c>, <c>x2</c>,
/// <c>x3</c>, <c>x4</c>, <c>x5</c>, <c>x10</c>, <c>x15</c>, <c>x20</c>, <c>x40</c>, see
/// <c>list_speeds</c> for the live list.
/// </remarks>
internal sealed class SetSpeedCommand : ICommand
{
    private readonly AssetCatalog _catalog;
    private readonly GameRefs _refs;
    private readonly GameSpeedAccess _speed;
    private MethodInfo? _setWorldSpeedString;

    public SetSpeedCommand(AssetCatalog catalog, GameRefs refs, GameSpeedAccess speed)
    {
        _catalog = catalog;
        _refs = refs;
        _speed = speed;
    }

    public string Name => "set_speed";
    public CommandCategory Category => CommandCategory.Control;
    public string Description =>
        "Sets the simulation tick rate by WorldTimeScaleAsset id. Stock WorldBox 0.51.x: "
        + "'slow_mo', 'x1', 'x2', 'x3', 'x4', 'x5', 'x10', 'x15', 'x20', 'x40', call "
        + "`list_speeds` for the live list and the current speed. Higher values run the "
        + "simulation faster so longer experiments take less wall-clock time. Returns "
        + "{speed_id, multiplier, previous}.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty(
                        "speed_id",
                        new JObject(
                            new JProperty("type", "string"),
                            new JProperty(
                                "description",
                                "WorldTimeScaleAsset id (e.g. 'x1', 'x5', 'x20', 'slow_mo'); see list_speeds."
                            )
                        )
                    )
                )
            ),
            new JProperty("required", new JArray("speed_id")),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
    {
        // Speed change is shared (no per-agent speed) and non-destructive -- granted to
        // AdvanceTime instead of ControlWorld so FactionPlayers can fast-forward through
        // quiet phases without needing a god agent in the PvP session.
        ctx.Require(Permission.AdvanceTime);
        var speedId = (string?)args["speed_id"];
        if (string.IsNullOrWhiteSpace(speedId))
        {
            throw new ArgumentException("speed_id is required and must be a non-empty string.");
        }
        var asset = _catalog.Resolve("time_scales", speedId!);
        if (asset == null)
        {
            // The speed list is tiny, so hand the agent every valid id up front instead of
            // making it guess from Levenshtein neighbours.
            var validIds = string.Join(", ", _catalog.ListIds("time_scales"));
            throw new BridgeRejectionException(
                ErrorCode.UnknownAsset,
                $"speed_id '{speedId}' is not a registered WorldTimeScaleAsset. Valid ids: {validIds}.",
                didYouMean: _catalog.Suggest("time_scales", speedId!)
            );
        }

        var configType = _refs.Type("Config");
        if (configType == null)
        {
            throw new BridgeRejectionException(ErrorCode.GameRejected, "Config type not found.");
        }
        _setWorldSpeedString ??= configType.GetMethod(
            "setWorldSpeed",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string), typeof(bool) },
            modifiers: null
        );
        if (_setWorldSpeedString == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "Config.setWorldSpeed(string, bool) not found in this WorldBox build."
            );
        }
        var previous = _speed.CurrentSpeedId();
        _setWorldSpeedString.Invoke(null, new object?[] { speedId, true });
        return Task.FromResult<object?>(
            new
            {
                speed_id = speedId,
                multiplier = ReadFloat(asset, "multiplier"),
                previous,
            }
        );
    }

    private static object? ReadField(object target, string name) =>
        target
            .GetType()
            .GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(target);

    private static float? ReadFloat(object target, string name) =>
        ReadField(target, name) is float f ? f : (float?)null;
}
