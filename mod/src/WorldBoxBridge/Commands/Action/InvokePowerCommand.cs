using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;

using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Action;

/// <summary>
/// Invokes a god power on a given tile. Powers cover most in-game actions:
/// spawn-by-race, disasters (meteor, nuke, plague, …), toggles (peace, civilization, …),
/// and modifiers. The full list comes from <c>list_powers</c>.
/// </summary>
/// <remarks>
/// Mechanism: every <c>GodPower</c> carries a <c>click_action</c> delegate of type
/// <c>PowerActionWithID(WorldTile tile, string powerId) → bool</c>. The game's UI just
/// calls that delegate when the user clicks the power button on a tile; we do the same
/// from the bridge.
/// </remarks>
internal sealed class InvokePowerCommand : ICommand
{
    private readonly AssetCatalog _catalog;
    private readonly GameRefs _refs;
    private readonly ManualLogSource _log;

    private MethodInfo? _delegateInvoke;
    private FieldInfo? _clickActionField;
    private FieldInfo? _mapBoxInstanceField;
    private FieldInfo? _tilesMapField;

    public InvokePowerCommand(AssetCatalog catalog, GameRefs refs, ManualLogSource log)
    {
        _catalog = catalog;
        _refs = refs;
        _log = log;
    }

    public string Name => "invoke_power";
    public CommandCategory Category => CommandCategory.Action;
    public string Description =>
        "Invokes any GodPower (god-mode action) on the world. Covers spawning by race, "
        + "disasters (meteor, nuke, plague, lightning, …), global toggles (peace, civ, …) "
        + "and modifiers. Discover valid power_id values via list_powers. For powers that "
        + "target a position (most), pass x and y; for global toggles, x/y are typically "
        + "ignored but must still be inside the map.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty(
                        "power_id",
                        new JObject(
                            new JProperty("type", "string"),
                            new JProperty(
                                "description",
                                "An id from list_powers (e.g. 'meteor', 'nuke', 'human')."
                            )
                        )
                    ),
                    new JProperty("x", new JObject(new JProperty("type", "integer"))),
                    new JProperty("y", new JObject(new JProperty("type", "integer")))
                )
            ),
            new JProperty("required", new JArray("power_id", "x", "y")),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken cancellationToken)
    {
        ctx.RequireAny(Permission.ActionFaction, Permission.ActionGlobal);
        var powerId = (string?)args["power_id"];
        if (string.IsNullOrWhiteSpace(powerId))
        {
            throw new ArgumentException("power_id is required and must be a non-empty string.");
        }
        if (!args.TryGetValue("x", out var xToken) || !args.TryGetValue("y", out var yToken))
        {
            throw new ArgumentException("x and y are required integers.");
        }
        var x = (int)xToken!;
        var y = (int)yToken!;

        // 1. Resolve the power. did_you_mean on miss.
        var power = _catalog.Resolve("powers", powerId!);
        if (power == null)
        {
            var suggestions = _catalog.Suggest("powers", powerId!);
            throw new BridgeRejectionException(
                ErrorCode.UnknownAsset,
                $"power_id '{powerId}' is not registered. Try one of the suggestions.",
                didYouMean: suggestions
            );
        }

        // 2. Bounds check.
        if (!TryGetWorldTile(x, y, out var tile, out var why))
        {
            throw new BridgeRejectionException(ErrorCode.OutOfBounds, why);
        }

        // 3. Cache the click_action FieldInfo across calls (one type lookup per session).
        _clickActionField ??= power
            .GetType()
            .GetField(
                "click_action",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
        if (_clickActionField == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "GodPower.click_action field not found in this WorldBox build."
            );
        }
        var del = _clickActionField.GetValue(power) as Delegate;
        if (del == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                $"Power '{powerId}' has no click_action delegate (probably UI-only)."
            );
        }
        _delegateInvoke ??= del.GetType().GetMethod("Invoke");

        // 4. Invoke. Delegate signature: bool (WorldTile tile, string powerId).
        bool result;
        try
        {
            var raw = _delegateInvoke!.Invoke(del, new object?[] { tile, powerId });
            result = raw is bool b && b;
        }
        catch (TargetInvocationException tie)
        {
            // Unwrap so the agent sees the game's real exception instead of a TIE wrapper.
            throw tie.InnerException ?? tie;
        }

        return Task.FromResult<object?>(
            new
            {
                power_id = powerId,
                x,
                y,
                accepted = result,
            }
        );
    }

    private bool TryGetWorldTile(int x, int y, out object? tile, out string why)
    {
        var mapBoxType = _refs.Type("MapBox");
        if (mapBoxType == null)
        {
            tile = null;
            why = "MapBox type not found in this WorldBox build.";
            return false;
        }
        _mapBoxInstanceField ??= mapBoxType.GetField(
            "instance",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        );
        var mapBox = _mapBoxInstanceField?.GetValue(null);
        if (mapBox == null)
        {
            tile = null;
            why = "MapBox.instance is null — game world not yet initialised.";
            return false;
        }
        _tilesMapField ??= mapBoxType.GetField(
            "tiles_map",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
        if (_tilesMapField?.GetValue(mapBox) is not Array tilesMap)
        {
            tile = null;
            why = "MapBox.tiles_map not found or not an array.";
            return false;
        }
        var width = tilesMap.GetLength(0);
        var height = tilesMap.GetLength(1);
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            tile = null;
            why = $"({x},{y}) is outside the map ({width}x{height}).";
            return false;
        }
        tile = tilesMap.GetValue(x, y);
        if (tile == null)
        {
            why = $"Tile at ({x},{y}) is null.";
            return false;
        }
        why = string.Empty;
        return true;
    }
}

// BridgeRejectionException moved to its own file (BridgeRejectionException.cs) so the test
// project can link it without dragging in Unity references.
