using System;
using System.Reflection;
using BepInEx.Logging;

namespace WorldBoxBridge.Reflection;

/// <summary>
/// Reflection access to the game's UI layer: the <c>ScrollWindow</c> stack (every in-game
/// window, including the startup "welcome" screen) and the <c>Config</c> statics that control it.
/// </summary>
/// <remarks>
/// Why this matters for an agent: the game's effective pause is
/// <c>Config.paused || ScrollWindow.isWindowActive() || RewardedAds.isShowing()</c>. Any open
/// window freezes the simulation, and the welcome window is open after every launch until
/// someone closes it. All members are fail-soft: a missing symbol logs once and returns null/false.
/// </remarks>
internal sealed class GameUiAccess : IGameUiAccess
{
    private const BindingFlags Static =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private readonly GameRefs _refs;
    private readonly ManualLogSource _log;
    private Type? _scrollWindowType;
    private Type? _configType;
    private MethodInfo? _isWindowActive;
    private MethodInfo? _getCurrentWindow;
    private MethodInfo? _hideAllEvent;
    private FieldInfo? _screenId;
    private PropertyInfo? _configPaused;
    private FieldInfo? _disableStartupWindow;

    public GameUiAccess(GameRefs refs, ManualLogSource log)
    {
        _refs = refs;
        _log = log;
    }

    private Type? ScrollWindowType => _scrollWindowType ??= _refs.Type("ScrollWindow");
    private Type? ConfigType => _configType ??= _refs.Type("Config");

    /// <summary>True if any ScrollWindow is open (which pauses the simulation); null if unknown.</summary>
    public bool? IsWindowActive()
    {
        var t = ScrollWindowType;
        if (t == null)
        {
            return null;
        }
        _isWindowActive ??= _refs.Method(t, "isWindowActive", Static);
        return _isWindowActive?.Invoke(null, Array.Empty<object>()) as bool?;
    }

    /// <summary>The <c>screen_id</c> of the window currently shown ("welcome", "settings", ...), or null.</summary>
    public string? CurrentWindowId()
    {
        var t = ScrollWindowType;
        if (t == null)
        {
            return null;
        }
        _getCurrentWindow ??= _refs.Method(t, "getCurrentWindow", Static);
        var window = _getCurrentWindow?.Invoke(null, Array.Empty<object>());
        if (window == null)
        {
            return null;
        }
        _screenId ??= _refs.Field(
            t,
            "screen_id",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
        return _screenId?.GetValue(window) as string;
    }

    /// <summary>Closes every open window via the game's own <c>ScrollWindow.hideAllEvent</c>.
    /// Returns false if the symbol is missing in this build.</summary>
    public bool HideAllWindows()
    {
        var t = ScrollWindowType;
        if (t == null)
        {
            return false;
        }
        _hideAllEvent ??= _refs.Method(t, "hideAllEvent", Static, typeof(bool));
        if (_hideAllEvent == null)
        {
            return false;
        }
        _hideAllEvent.Invoke(
            null,
            new object[]
            {
                false, /* pWithAnimation */
            }
        );
        return true;
    }

    /// <summary>The user-facing pause toggle (<c>Config.paused</c>), independent of open windows.</summary>
    public bool? ConfigPaused
    {
        get
        {
            var t = ConfigType;
            if (t == null)
            {
                return null;
            }
            _configPaused ??= _refs.Property(t, "paused", Static);
            return _configPaused?.GetValue(null) as bool?;
        }
    }

    /// <summary>
    /// Sets <c>Config.disable_startup_window</c>, which the game checks at the end of world
    /// loading before showing the "welcome" window. Must run before the first world loads,
    /// i.e. from plugin Awake, to take effect. Returns false if the field is missing.
    /// </summary>
    public bool SetDisableStartupWindow(bool value)
    {
        var t = ConfigType;
        if (t == null)
        {
            return false;
        }
        _disableStartupWindow ??= _refs.Field(t, "disable_startup_window", Static);
        // IsLiteral catches `const` (no storage to write), IsInitOnly catches `readonly`.
        if (
            _disableStartupWindow == null
            || _disableStartupWindow.IsInitOnly
            || _disableStartupWindow.IsLiteral
        )
        {
            _log.LogWarning(
                "[ui] Config.disable_startup_window not writable, startup window can't be suppressed."
            );
            return false;
        }
        // This runs from Plugin.Awake, before the HTTP bridge is built, inside the one try/catch
        // that aborts the whole plugin. A cosmetic default-on convenience must never be able to
        // take the bridge down, so it stays fail-soft like every other member of this class:
        // SetValue still throws if the field's type is not bool in some future build.
        try
        {
            _disableStartupWindow.SetValue(null, value);
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                $"[ui] Could not set Config.disable_startup_window ({ex.GetType().Name}: {ex.Message}). "
                    + "Startup window will appear; use dismiss_window to clear it."
            );
            return false;
        }
        return true;
    }
}
