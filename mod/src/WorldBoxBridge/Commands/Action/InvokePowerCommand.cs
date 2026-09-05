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
/// Mechanism: a <c>GodPower</c> carries one of several click delegates. Most use
/// <c>click_action</c> (<c>PowerActionWithID(WorldTile, string powerId) → bool</c>); the
/// drops / bombs / drop-building families (rain, fire, bomb, volcano, …) use
/// <c>click_power_action</c> (<c>PowerAction(WorldTile, GodPower) → bool</c>) instead. The
/// game's UI calls whichever is set when the user clicks a tile; we try them in that order.
/// Brush-only and toggle powers (<c>click_brush_action</c>, <c>toggle_action</c>) are not
/// covered yet and are rejected with a clear message.
/// </remarks>
internal sealed class InvokePowerCommand : ICommand
{
    private readonly AssetCatalog _catalog;
    private readonly GameRefs _refs;
    private readonly ManualLogSource _log;

    private FieldInfo? _clickActionField;
    private FieldInfo? _clickPowerActionField;
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
        + "ignored but must still be inside the map. Returns {power_id, x, y, accepted, via}; "
        + "accepted=false means the game declined this time (some powers roll a chance). "
        + "Powers that need live mouse/drag state (e.g. 'finger') or a brush are rejected "
        + "with GAME_REJECTED — use paint_tile / spawn instead.";
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

    public Task<object?> ExecuteAsync(
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
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

        // 3. Pick the delegate the game itself would call. FieldInfos are cached per session.
        const BindingFlags Inst =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var powerType = power.GetType();
        _clickActionField ??= powerType.GetField("click_action", Inst);
        _clickPowerActionField ??= powerType.GetField("click_power_action", Inst);
        if (_clickActionField == null && _clickPowerActionField == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "GodPower.click_action / click_power_action fields not found in this WorldBox build."
            );
        }

        string via;
        object?[] callArgs;
        var del = _clickActionField?.GetValue(power) as Delegate;
        if (del != null)
        {
            via = "click_action"; // bool (WorldTile tile, string powerId)
            callArgs = new object?[] { tile, powerId };
        }
        else if ((del = _clickPowerActionField?.GetValue(power) as Delegate) != null)
        {
            via = "click_power_action"; // bool (WorldTile tile, GodPower power)
            callArgs = new object?[] { tile, power };
        }
        else
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                $"Power '{powerId}' has neither click_action nor click_power_action; it is a "
                    + "brush-, toggle- or UI-only power that invoke_power can't drive yet."
            );
        }

        // 4. Invoke.
        bool result;
        try
        {
            result = del.DynamicInvoke(callArgs) is bool b && b;
        }
        catch (TargetInvocationException tie) when (tie.InnerException is NullReferenceException)
        {
            // The game's handler dereferenced state only a real pointer interaction sets
            // (e.g. 'finger' copies player_control.first_pressed_type from the mouse press).
            // That is a limitation of driving it from the API, not a crash worth a stack trace.
            // The rejection message below attributes every NRE to that cause, so log the real
            // one first: otherwise a genuine bug in an unrelated power's handler is silently
            // relabelled as a mouse-state limitation with nothing left to diagnose it from.
            _log.LogWarning(
                $"[invoke_power] '{powerId}' via {via} threw {tie.InnerException}. Reported as "
                    + "GAME_REJECTED (assumed pointer-state dependency)."
            );
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                $"Power '{powerId}' threw NullReferenceException inside the game — it depends on "
                    + "live mouse/drag state the API can't supply. Use paint_tile or spawn instead."
            );
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
                via,
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
