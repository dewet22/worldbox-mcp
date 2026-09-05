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
        try
        {
            return _brushGetBySize.Invoke(null, new object[] { radius, "circ_" }) != null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                $"[brush] Brush.get({radius}, \"circ_\") threw {ex.GetType().Name}: {ex.Message}"
            );
            return false;
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
