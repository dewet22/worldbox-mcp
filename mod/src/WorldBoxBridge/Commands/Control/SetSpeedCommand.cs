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
/// method by reflection. Common ids on stock WorldBox: <c>slow_mo</c>, <c>x1</c>,
/// <c>x2</c>, <c>x3</c>, <c>x5</c>.
/// </remarks>
internal sealed class SetSpeedCommand : ICommand
{
    private readonly AssetCatalog _catalog;
    private readonly GameRefs _refs;
    private MethodInfo? _setWorldSpeedString;

    public SetSpeedCommand(AssetCatalog catalog, GameRefs refs)
    {
        _catalog = catalog;
        _refs = refs;
    }

    public string Name => "set_speed";
    public CommandCategory Category => CommandCategory.Control;
    public string Description =>
        "Sets the simulation tick rate by WorldTimeScaleAsset id. Typical values on stock "
        + "WorldBox: 'slow_mo', 'x1', 'x2', 'x3', 'x5'. Higher values run the simulation "
        + "faster so longer experiments take less wall-clock time.";
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
                                "WorldTimeScaleAsset id (e.g. 'x1', 'x3', 'slow_mo')."
                            )
                        )
                    )
                )
            ),
            new JProperty("required", new JArray("speed_id")),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken cancellationToken)
    {
        var speedId = (string?)args["speed_id"];
        if (string.IsNullOrWhiteSpace(speedId))
        {
            throw new ArgumentException("speed_id is required and must be a non-empty string.");
        }
        if (_catalog.Resolve("time_scales", speedId!) == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.UnknownAsset,
                $"speed_id '{speedId}' is not a registered WorldTimeScaleAsset.",
                didYouMean: _catalog.Suggest("time_scales", speedId!)
            );
        }

        var configType = _refs.Type("Config");
        if (configType == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "Config type not found."
            );
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
        _setWorldSpeedString.Invoke(null, new object?[] { speedId, true });
        return Task.FromResult<object?>(new { speed_id = speedId });
    }
}
