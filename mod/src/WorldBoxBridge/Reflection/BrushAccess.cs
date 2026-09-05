using System;
using System.Reflection;
using BepInEx.Logging;

namespace WorldBoxBridge.Reflection;

/// <summary>
/// Reflection access to the game's brush machinery: <c>Brush.get</c> and the
/// <c>Config.current_brush</c> statics that every brush-driven GodPower delegate reads.
/// </summary>
/// <remarks>
/// The brush delegates (<c>click_brush_action</c> / <c>click_power_brush_action</c>) expand the
/// affected area inside the delegate: the game hands them one tile and they loop
/// <c>Config.current_brush_data</c> over it via <c>MapBox.loopWithBrush</c>. Setting
/// <c>Config.current_brush</c> (whose setter populates <c>current_brush_data</c>) is therefore
/// the entire steering surface. <c>Brush.get(int, string)</c> auto-creates missing circle sizes
/// by cloning <c>circ_1</c> and re-running its generate action, so arbitrary radii are safe.
/// All members are fail-soft: a missing symbol logs and returns false/null.
/// </remarks>
internal sealed class BrushAccess
{
    private const BindingFlags Static =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private readonly GameRefs _refs;
    private readonly ManualLogSource _log;
    private Type? _brushType;
    private Type? _configType;
    private MethodInfo? _brushGetBySize;
    private PropertyInfo? _currentBrush;
    private Type? _idFieldOwner;
    private FieldInfo? _idField;
    private bool _warnedIdDisagreement;

    public BrushAccess(GameRefs refs, ManualLogSource log)
    {
        _refs = refs;
        _log = log;
    }

    private PropertyInfo? CurrentBrushProperty
    {
        get
        {
            _configType ??= _refs.Type("Config");
            if (_configType == null)
            {
                return null;
            }
            return _currentBrush ??= _refs.Property(_configType, "current_brush", Static);
        }
    }

    /// <summary>The id of the brush the game currently has selected (e.g. "circ_5"), or null.</summary>
    public string? CurrentBrushId => CurrentBrushProperty?.GetValue(null) as string;

    /// <summary>
    /// Ensures the circle brush of the given radius exists in <c>AssetManager.brush_library</c>,
    /// creating it via <c>Brush.get(int, string)</c> if needed. Returns false when the brush
    /// machinery is missing in this build or the game call failed.
    /// </summary>
    /// <remarks>
    /// The id it hands back is the constructed <c>"circ_" + radius</c>, deliberately, and NOT
    /// the <c>id</c> of the asset the game returned. Reading that id back and trusting it was
    /// tried first and is the wrong trade: on a stock build the constructed name is already
    /// correct, since <c>Brush.get(int pSize, string pID)</c> clones <c>circ_1</c> as
    /// <c>circ_&lt;pSize&gt;</c>, while nothing in this repo records what that overload actually
    /// returns. <c>docs/game-api-notes.md</c> says the <c>Config.current_brush</c> setter fills
    /// <c>current_brush_data</c> "via <c>Brush.get(id)</c>", so at least one overload of this
    /// name answers with brush data rather than with the library asset. Preferring the read
    /// value would therefore replace a known-good key with an unverified one on every call, to
    /// guard a clamp-or-rename case nobody has seen.
    /// <para>So the read happens and only disagreement is reported. A warning naming both names
    /// is what a live session needs to settle the question, and it costs nothing when they
    /// match. Once <c>Brush.get(int, string)</c>'s return type is written down in
    /// game-api-notes, this can prefer the real id and drop the guess.</para>
    /// </remarks>
    public bool TryEnsureCircleBrush(int radius, out string brushId)
    {
        brushId = "circ_" + radius;
        _brushType ??= _refs.Type("Brush");
        if (_brushType == null)
        {
            return false;
        }
        _brushGetBySize ??= _refs.Method(_brushType, "get", Static, typeof(int), typeof(string));
        if (_brushGetBySize == null)
        {
            return false;
        }
        object? brush;
        try
        {
            brush = _brushGetBySize.Invoke(null, new object[] { radius, "circ_" });
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                $"[brush] Brush.get({radius}, \"circ_\") threw {ex.GetType().Name}: {ex.Message}"
            );
            return false;
        }
        if (brush == null)
        {
            return false;
        }
        WarnIfAssetIdDisagrees(brush, radius, brushId);
        return true;
    }

    /// <summary>
    /// Logs when the returned asset's <c>id</c> is not the name we are about to select. Purely
    /// diagnostic: it never changes what the caller gets, and it cannot fail the call.
    /// </summary>
    /// <remarks>
    /// Reflected here rather than through <see cref="GameRefs.Field"/> on purpose. That helper
    /// logs "Dependent commands disabled." when a field is missing, which would be false: a
    /// build whose asset has no readable <c>id</c> loses this warning and nothing else. Its
    /// cache also keys on type and member name without the binding flags, and this is the only
    /// lookup in the tree that would want the default flags, so it stays out of that cache too.
    /// <para>The three fields it caches into carry no barrier because every caller is on the
    /// Unity main thread: the only one is <c>invoke_power</c>, which reports
    /// <c>RequiresMainThread</c> true, and its multi-frame pulse path steps inside the
    /// dispatcher. A future off-thread caller has to revisit that, which is why it is stated
    /// here rather than left to be re-derived.</para>
    /// </remarks>
    private void WarnIfAssetIdDisagrees(object brush, int radius, string expectedId)
    {
        try
        {
            var brushType = brush.GetType();
            if (!ReferenceEquals(brushType, _idFieldOwner))
            {
                _idFieldOwner = brushType;
                _idField = brushType.GetField(
                    "id",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                _warnedIdDisagreement = false;
            }
            if (_idField?.GetValue(brush) is not string actualId || actualId == expectedId)
            {
                return;
            }
            if (_warnedIdDisagreement)
            {
                return;
            }
            // Once per type: a disagreement is a property of this build, not of this call, and
            // a pulse run would otherwise log it up to 200 times.
            _warnedIdDisagreement = true;
            _log.LogWarning(
                $"[brush] Brush.get({radius}, \"circ_\") returned an asset whose id is "
                    + $"'{actualId}', not '{expectedId}'. The bridge is selecting '{expectedId}', "
                    + "so the applied area may not match the requested radius. Please report this "
                    + "with your WorldBox version: it settles what Brush.get(int, string) returns, "
                    + "which docs/game-api-notes.md does not record."
            );
        }
        catch (Exception ex)
        {
            // Diagnostics must never be able to fail an invoke_power that the game accepted.
            _log.LogWarning(
                $"[brush] could not read the returned asset's id ({ex.GetType().Name}: "
                    + $"{ex.Message}). Selecting '{expectedId}' as before."
            );
        }
    }

    /// <summary>
    /// Selects a brush by id via the <c>Config.current_brush</c> setter (which also populates
    /// <c>Config.current_brush_data</c>). Returns false if the property is missing or the set failed.
    /// </summary>
    public bool TrySetCurrentBrush(string brushId)
    {
        var prop = CurrentBrushProperty;
        if (prop == null || !prop.CanWrite)
        {
            return false;
        }
        try
        {
            prop.SetValue(null, brushId);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                $"[brush] Could not set Config.current_brush = '{brushId}' "
                    + $"({ex.GetType().Name}: {ex.Message})."
            );
            return false;
        }
    }
}
