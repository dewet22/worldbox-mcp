using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;
using WorldBoxBridge.Threading;
using SessionState = WorldBoxBridge.Session.Session;

namespace WorldBoxBridge.Commands.Action;

/// <summary>
/// Invokes a god power on a given tile. Powers cover most in-game actions:
/// spawn-by-race, disasters (meteor, nuke, plague, ...), toggles (peace, civilization, ...),
/// and modifiers. The full list comes from <c>list_powers</c>.
/// </summary>
/// <remarks>
/// Mechanism: a <c>GodPower</c> carries several click delegates. Most use <c>click_action</c>
/// (<c>PowerActionWithID(WorldTile, string powerId) → bool</c>); the drops / bombs families
/// also or only use <c>click_power_action</c> (<c>PowerAction(WorldTile, GodPower) → bool</c>).
/// The brush variants (<c>click_brush_action</c> / <c>click_power_brush_action</c>, same two
/// signatures) expand the affected area internally by looping
/// <c>Config.current_brush_data</c> over the clicked tile, so steering the radius means
/// selecting a brush via <see cref="BrushAccess"/> before invoking and restoring it after.
/// <c>toggle_action</c> (<c>PowerToggleAction(string) → void</c>) flips global switches.
/// Which delegate wins for a given call is <see cref="PowerDelegateSelector"/>'s job (pure,
/// unit-tested); only <c>click_special_action</c> remains undrivable.
/// </remarks>
internal sealed class InvokePowerCommand : ICommand
{
    /// <summary>Soft wall-clock budget for a multi-pulse run: comfortably under both the
    /// dispatcher's 30 s job deadline and the MCP client's 35 s HTTP timeout, so a run cut
    /// short by low frame rates still delivers its partial aggregate instead of a timeout
    /// error after half the world mutation has already happened.</summary>
    private static readonly TimeSpan PulseRunBudget = TimeSpan.FromSeconds(25);

    private readonly AssetCatalog _catalog;
    private readonly GameRefs _refs;
    private readonly BrushAccess _brush;
    private readonly PowerDelegateFields _delegateFields;
    private readonly WorldAccess _world;
    private readonly SessionState _session;
    private readonly ManualLogSource _log;

    private FieldInfo? _mapBoxInstanceField;
    private FieldInfo? _tilesMapField;

    public InvokePowerCommand(
        AssetCatalog catalog,
        GameRefs refs,
        BrushAccess brush,
        PowerDelegateFields delegateFields,
        WorldAccess world,
        SessionState session,
        ManualLogSource log
    )
    {
        _catalog = catalog;
        _refs = refs;
        _brush = brush;
        _delegateFields = delegateFields;
        _world = world;
        _session = session;
        _log = log;
    }

    public string Name => "invoke_power";
    public CommandCategory Category => CommandCategory.Action;
    public string Description =>
        "Invokes any GodPower (god-mode action) on the world. Covers spawning by race, "
        + "disasters (meteor, nuke, plague, lightning, ...), area drops (rain, fire, lava, ...), "
        + "brush tools, global toggles (peace, civ, ...) and modifiers. Discover valid power_id "
        + "values via list_powers. Most powers target a position: pass x and y. Optional "
        + "radius (1-50) applies brush-driven powers (flagged supports_radius in list_powers) "
        + "over a circular area of that radius; radius on any other power is rejected. Toggle "
        + "powers (flagged is_toggle) flip global state and ignore x/y (still required, must "
        + "be in-bounds). Returns "
        + "{power_id, x, y, accepted, via} plus {radius, brush} when a brush was used. "
        + "Optional pulses (1-200) applies the power once per game frame that many times, "
        + "the equivalent of holding the mouse button (~60 pulses/s); with x2/y2 the pulses "
        + "sweep from (x, y) to (x2, y2) like a click-hold-drag. Multi-pulse calls return "
        + "{pulses, accepted_count, pulses_applied} instead of accepted and take pulses/60 "
        + "seconds to complete; a run stops early (stopped: turn_ended | world_changed | "
        + "deadline | error) when the caller's turn ends, the world is replaced mid-run, the "
        + "25s budget runs out, or a pulse throws (error_code/error_message then carry the "
        + "failure). accepted=false means the game declined this time (some powers roll a "
        + "chance). Powers that need live mouse/drag state (e.g. 'finger') are rejected with "
        + "GAME_REJECTED, use paint_tile / spawn instead. Needs the global action scope "
        + "(God role) in a multi-agent session; a FactionPlayer uses spawn.";
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
                    new JProperty("y", new JObject(new JProperty("type", "integer"))),
                    new JProperty(
                        "radius",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty("minimum", PowerDelegateSelector.MinRadius),
                            new JProperty("maximum", PowerDelegateSelector.MaxRadius),
                            new JProperty(
                                "description",
                                "Optional circle-brush radius, for powers flagged "
                                    + "supports_radius in list_powers."
                            )
                        )
                    ),
                    new JProperty(
                        "pulses",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty("minimum", PulsePath.MinPulses),
                            new JProperty("maximum", PulsePath.MaxPulses),
                            new JProperty(
                                "description",
                                "Apply the power once per game frame this many times, the "
                                    + "equivalent of holding the mouse button (~60/s). Default 1."
                            )
                        )
                    ),
                    new JProperty(
                        "x2",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty(
                                "description",
                                "Optional drag end point (with y2): pulses sweep from (x, y) "
                                    + "to (x2, y2), like dragging the cursor while holding."
                            )
                        )
                    ),
                    new JProperty("y2", new JObject(new JProperty("type", "integer")))
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
        // God powers are map-wide, same as paint_tile: see ActionPermissions for why a
        // FactionPlayer is kept out and what it keeps instead.
        ctx.Require(ActionPermissions.InvokePower);
        var powerId = (string?)args["power_id"];
        if (string.IsNullOrWhiteSpace(powerId))
        {
            throw new BridgeRejectionException(
                ErrorCode.BadArgs,
                "power_id is required and must be a non-empty string."
            );
        }
        if (!args.TryGetValue("x", out var xToken) || !args.TryGetValue("y", out var yToken))
        {
            throw new BridgeRejectionException(ErrorCode.BadArgs, "x and y are required integers.");
        }
        var x = (int)xToken!;
        var y = (int)yToken!;
        int? radius = null;
        if (args.TryGetValue("radius", out var radiusToken) && radiusToken!.Type != JTokenType.Null)
        {
            var requested = (int)radiusToken;
            if (
                requested < PowerDelegateSelector.MinRadius
                || requested > PowerDelegateSelector.MaxRadius
            )
            {
                throw new BridgeRejectionException(
                    ErrorCode.BadArgs,
                    $"radius must be between {PowerDelegateSelector.MinRadius} and "
                        + $"{PowerDelegateSelector.MaxRadius}."
                );
            }
            radius = requested;
        }
        var pulses = 1;
        if (args.TryGetValue("pulses", out var pulsesToken) && pulsesToken!.Type != JTokenType.Null)
        {
            pulses = (int)pulsesToken;
            if (pulses < PulsePath.MinPulses || pulses > PulsePath.MaxPulses)
            {
                throw new BridgeRejectionException(
                    ErrorCode.BadArgs,
                    $"pulses must be between {PulsePath.MinPulses} and {PulsePath.MaxPulses}."
                );
            }
        }
        int? x2 = null;
        int? y2 = null;
        var hasX2 = args.TryGetValue("x2", out var x2Token) && x2Token!.Type != JTokenType.Null;
        var hasY2 = args.TryGetValue("y2", out var y2Token) && y2Token!.Type != JTokenType.Null;
        if (hasX2 != hasY2)
        {
            throw new BridgeRejectionException(
                ErrorCode.BadArgs,
                "x2 and y2 must be provided together (they are the drag end point)."
            );
        }
        if (hasX2)
        {
            if (pulses == 1)
            {
                // Without this, a drag request with pulses omitted would validate the
                // endpoints and then silently tap only (x, y), documented behaviour ignored.
                throw new BridgeRejectionException(
                    ErrorCode.BadArgs,
                    "x2/y2 describe a drag, which needs pulses > 1, a single pulse would "
                        + "only ever land at (x, y)."
                );
            }
            x2 = (int)x2Token!;
            y2 = (int)y2Token!;
        }

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

        // 2. Bounds check, both drag endpoints. PulsePath's rounded lerp never leaves the
        // endpoints' bounding box, so checking the ends covers every interpolated pulse.
        if (!TryGetWorldTile(x, y, out var tile, out var why))
        {
            throw new BridgeRejectionException(ErrorCode.OutOfBounds, why);
        }
        if (x2 is int dragEndX && y2 is int dragEndY)
        {
            if (!TryGetWorldTile(dragEndX, dragEndY, out _, out var whyEnd))
            {
                throw new BridgeRejectionException(ErrorCode.OutOfBounds, whyEnd);
            }
        }

        // 3. Gather the delegates the game itself would consider (field lookups cached per
        // concrete asset type in PowerDelegateFields, shared with list_powers).
        if (!_delegateFields.AnyFieldPresent(power))
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "GodPower delegate fields (click_action ...) not found in this WorldBox build."
            );
        }

        var delegates = _delegateFields.Read(power);
        var clickDel = delegates.ClickAction;
        var clickPowerDel = delegates.ClickPowerAction;
        var clickBrushDel = delegates.ClickBrushAction;
        var clickPowerBrushDel = delegates.ClickPowerBrushAction;
        var toggleDel = delegates.ToggleAction;
        var choice = PowerDelegateSelector.Select(
            clickDel != null,
            clickPowerDel != null,
            clickBrushDel != null,
            clickPowerBrushDel != null,
            toggleDel != null,
            radius
        );

        string via;
        Delegate del;
        Func<object?, object?[]> buildArgs;
        switch (choice.Path)
        {
            case PowerDelegatePath.ClickAction:
                via = "click_action"; // bool (WorldTile tile, string powerId)
                del = clickDel!;
                buildArgs = pulseTile => new object?[] { pulseTile, powerId };
                break;
            case PowerDelegatePath.ClickPowerAction:
                via = "click_power_action"; // bool (WorldTile tile, GodPower power)
                del = clickPowerDel!;
                buildArgs = pulseTile => new object?[] { pulseTile, power };
                break;
            case PowerDelegatePath.ClickBrushAction:
                via = "click_brush_action"; // bool (WorldTile, string), loops the brush inside
                del = clickBrushDel!;
                buildArgs = pulseTile => new object?[] { pulseTile, powerId };
                break;
            case PowerDelegatePath.ClickPowerBrushAction:
                via = "click_power_brush_action"; // bool (WorldTile, GodPower), loops the brush inside
                del = clickPowerBrushDel!;
                buildArgs = pulseTile => new object?[] { pulseTile, power };
                break;
            case PowerDelegatePath.ToggleAction:
                if (pulses > 1)
                {
                    throw new BridgeRejectionException(
                        ErrorCode.BadArgs,
                        $"pulses does not apply to toggle power '{powerId}', repeating a "
                            + "toggle would just flip it back and forth."
                    );
                }
                // No extra permission check: the command-wide ActionPermissions.InvokePower
                // gate is already ActionGlobal, which is exactly what a world-global switch
                // (peace, civilisation, ...) demands.
                via = "toggle_action"; // void (string powerId)
                del = toggleDel!;
                buildArgs = _ => new object?[] { powerId };
                break;
            case PowerDelegatePath.RejectRadiusUnsupported:
                throw new BridgeRejectionException(
                    ErrorCode.GameRejected,
                    $"Power '{powerId}' does not support radius, it has no brush delegate. "
                        + "list_powers flags radius-capable powers with supports_radius."
                );
            default:
                throw new BridgeRejectionException(
                    ErrorCode.GameRejected,
                    $"Power '{powerId}' has no delegate invoke_power can drive; it is a "
                        + "special- or UI-only power."
                );
        }

        // 4. Brush paths: make sure circ_<radius> exists up front. Each pulse then selects it
        // just around its delegate call and restores the player's own selection immediately
        // after, so the in-game brush picker never visibly changes, even during multi-frame
        // pulse runs where the player could click between our frames.
        string? brushId = null;
        if (choice.BrushRadius is int brushRadius)
        {
            if (!_brush.TryEnsureCircleBrush(brushRadius, out var ensured))
            {
                throw new BridgeRejectionException(
                    ErrorCode.GameRejected,
                    "Brush machinery (Brush.get / Config.current_brush) not found in this "
                        + "WorldBox build, brush-driven powers can't be invoked."
                );
            }
            brushId = ensured;
        }

        bool DoPulse(object? pulseTile)
        {
            // Separate flag: a legitimately-null previous brush must still be restored,
            // otherwise our synthetic circ_N would stay selected in the player's picker.
            var overrodeBrush = false;
            string? previousBrush = null;
            if (brushId != null)
            {
                previousBrush = _brush.CurrentBrushId;
                overrodeBrush = true;
                if (!_brush.TrySetCurrentBrush(brushId))
                {
                    throw new BridgeRejectionException(
                        ErrorCode.GameRejected,
                        "Config.current_brush is not writable in this WorldBox build, "
                            + "brush-driven powers can't be invoked."
                    );
                }
            }
            try
            {
                var raw = del.DynamicInvoke(buildArgs(pulseTile));
                // PowerToggleAction returns void; treat "no verdict" as accepted.
                return raw switch
                {
                    bool b => b,
                    _ => true,
                };
            }
            catch (TargetInvocationException tie)
                when (tie.InnerException is NullReferenceException)
            {
                // The game's handler dereferenced state only a real pointer interaction sets
                // (e.g. 'finger' copies player_control.first_pressed_type from the mouse press).
                // That is a limitation of driving it from the API, not a crash worth a stack
                // trace. The rejection message below attributes every NRE to that cause, so log
                // the real one first: otherwise a genuine bug in an unrelated power's handler is
                // silently relabelled as a mouse-state limitation with nothing to diagnose from.
                _log.LogWarning(
                    $"[invoke_power] '{powerId}' via {via} threw {tie.InnerException}. Reported "
                        + "as GAME_REJECTED (assumed pointer-state dependency)."
                );
                throw new BridgeRejectionException(
                    ErrorCode.GameRejected,
                    $"Power '{powerId}' threw NullReferenceException inside the game, it "
                        + "depends on live mouse/drag state the API can't supply. Use paint_tile "
                        + "or spawn instead."
                );
            }
            catch (TargetInvocationException tie)
            {
                // Unwrap so the agent sees the game's real exception instead of a TIE wrapper.
                throw tie.InnerException ?? tie;
            }
            finally
            {
                if (overrodeBrush)
                {
                    // A null previous selection (unseen on stock builds, whose static default
                    // is "circ_5") still gets a restore: leaving our synthetic brush selected
                    // would be worse than resetting to the game default, and pushing null
                    // through the setter would null current_brush_data for the game itself.
                    _brush.TrySetCurrentBrush(previousBrush ?? "circ_5");
                }
            }
        }

        // 5. Single pulse: invoke now, inside this main-thread dispatch, identical timing and
        // response shape to the pre-pulse bridge.
        if (pulses == 1)
        {
            var accepted = DoPulse(tile);
            if (brushId != null)
            {
                return Task.FromResult<object?>(
                    new
                    {
                        power_id = powerId,
                        x,
                        y,
                        accepted,
                        via,
                        radius = choice.BrushRadius,
                        brush = brushId,
                    }
                );
            }
            return Task.FromResult<object?>(
                new
                {
                    power_id = powerId,
                    x,
                    y,
                    accepted,
                    via,
                }
            );
        }

        // 6. Multi-pulse: one application per Unity frame via the dispatcher, the synthetic
        // equivalent of holding the button, and with x2/y2 of dragging the cursor across the
        // map while holding it. The returned task completes frames later; HttpBridge awaits it
        // off the main thread. Because the run spans real time, each step re-checks the
        // conditions that were only admission-checked for single-frame commands, and a run cut
        // short still completes successfully with its partial aggregate (stopped + pulses_applied)
        // rather than erroring after the world was already part-mutated.
        var totalPulses = pulses;
        var endX = x2 ?? x;
        var endY = y2 ?? y;
        var acceptedCount = 0;
        var pulseIndex = 0;
        string? stoppedReason = null;
        string? stoppedErrorCode = null;
        string? stoppedErrorMessage = null;
        var runDeadline = DateTime.UtcNow + PulseRunBudget;
        var worldAtStart = GetTilesMap();
        return MainThreadDispatcher.RunPerFrameOnMainThreadAsync<object?>(
            step: () =>
            {
                // Turn guard: the admission gate in HttpBridge only checked the turn when the
                // request arrived. A synthetic "hold" must let go when the caller's turn ends,
                // exactly as a player loses the mouse when it stops being their turn. Same
                // shape as the admission gate: TurnGate says whether this command is gated at
                // all, ActionGlobal bypasses. (With ActionPermissions.InvokePower currently
                // being ActionGlobal every admitted caller bypasses, so this only bites if the
                // permission model widens again, which is exactly when it must not be missing.)
                if (
                    _session.TurnBased
                    && _session.TurnOrder is not null
                    && TurnGate.IsTurnGated(Name, Category)
                    && !ctx.Has(Permission.ActionGlobal)
                    && !_session.TurnOrder.IsCurrentlyActive(ctx.AgentId)
                )
                {
                    stoppedReason = "turn_ended";
                    return false;
                }
                // World guard: generate_world / load_world replaces MapBox.tiles_map, but the
                // replacement happens in asynchronous SmoothLoader steps that may run after a
                // short pulse run would otherwise finish. Config.worldLoading flips true the
                // moment the load is scheduled, so check it first; the reference comparison
                // then catches a swap already completed between frames. (The game itself locks
                // gameplay input during loading, this mirrors that.)
                if (_world.IsWorldLoading == true || !ReferenceEquals(GetTilesMap(), worldAtStart))
                {
                    stoppedReason = "world_changed";
                    return false;
                }
                // Deadline guard: at low frame rates a long run could outlive the dispatcher's
                // 30 s job deadline and the client's 35 s HTTP timeout, deliver the partial
                // aggregate instead of a timeout error.
                if (DateTime.UtcNow > runDeadline)
                {
                    stoppedReason = "deadline";
                    return false;
                }
                var point = PulsePath.At(pulseIndex, totalPulses, x, y, endX, endY);
                if (!TryGetWorldTile(point.X, point.Y, out var pulseTile, out var whyPulse))
                {
                    // Endpoints were checked up front and the world guard ran above; this is
                    // a same-frame race at worst, treat it like a world change.
                    _log.LogWarning(
                        $"[invoke_power] pulse {pulseIndex} tile lookup failed: {whyPulse}"
                    );
                    stoppedReason = "world_changed";
                    return false;
                }
                // A pulse that throws must not fault the whole job: the world is already
                // part-mutated, and the dispatcher would deliver only the exception, losing
                // pulses_applied/accepted_count, the exact harm the other guards avoid. Stop
                // instead and carry the failure inside the partial aggregate.
                try
                {
                    if (DoPulse(pulseTile))
                    {
                        acceptedCount++;
                    }
                }
                catch (BridgeRejectionException ex)
                {
                    stoppedReason = "error";
                    stoppedErrorCode = ex.Code;
                    stoppedErrorMessage = ex.Message;
                    return false;
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[invoke_power] pulse {pulseIndex} threw: {ex}");
                    stoppedReason = "error";
                    stoppedErrorCode = ErrorCode.GameCrash;
                    stoppedErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
                    return false;
                }
                pulseIndex++;
                return pulseIndex < totalPulses;
            },
            onCompleted: () =>
            {
                var aggregate = new JObject
                {
                    ["power_id"] = powerId,
                    ["x"] = x,
                    ["y"] = y,
                    ["pulses"] = totalPulses,
                    ["pulses_applied"] = pulseIndex,
                    ["accepted_count"] = acceptedCount,
                    ["via"] = via,
                };
                if (stoppedReason != null)
                {
                    aggregate["stopped"] = stoppedReason;
                }
                if (stoppedErrorCode != null)
                {
                    aggregate["error_code"] = stoppedErrorCode;
                    aggregate["error_message"] = stoppedErrorMessage;
                }
                if (x2 is int doneX2 && y2 is int doneY2)
                {
                    aggregate["x2"] = doneX2;
                    aggregate["y2"] = doneY2;
                }
                if (brushId != null)
                {
                    aggregate["radius"] = choice.BrushRadius;
                    aggregate["brush"] = brushId;
                }
                return (object?)aggregate;
            },
            cancellationToken: cancellationToken
        );
    }

    /// <summary>The live MapBox.tiles_map array, or null. Reference identity doubles as world
    /// identity: generate_world / load_world swap the array, never mutate it in place.</summary>
    private Array? GetTilesMap()
    {
        var mapBoxType = _refs.Type("MapBox");
        if (mapBoxType == null)
        {
            return null;
        }
        _mapBoxInstanceField ??= mapBoxType.GetField(
            "instance",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        );
        var mapBox = _mapBoxInstanceField?.GetValue(null);
        if (mapBox == null)
        {
            return null;
        }
        _tilesMapField ??= mapBoxType.GetField(
            "tiles_map",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
        return _tilesMapField?.GetValue(mapBox) as Array;
    }

    private bool TryGetWorldTile(int x, int y, out object? tile, out string why)
    {
        if (GetTilesMap() is not Array tilesMap)
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
