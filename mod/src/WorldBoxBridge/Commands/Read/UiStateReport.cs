using WorldBoxBridge.Reflection;

namespace WorldBoxBridge.Commands.Read;

/// <summary>
/// The payload <c>get_ui_state</c> returns, built out of the command so it can be tested.
/// </summary>
/// <remarks>
/// The whole content of this type is the fail-soft coalescing: every source value is nullable
/// because a missing game symbol reads as null, and the wire contract is booleans. Null means
/// "this build does not expose it", which the bridge reports as "not blocked" rather than
/// refusing the read outright, since an agent that cannot see the UI state still needs an
/// answer it can act on.
/// </remarks>
public readonly struct UiStateReport
{
    private UiStateReport(
        bool windowActive,
        string? currentWindow,
        bool configPaused,
        bool effectivePaused,
        bool worldLoading
    )
    {
        WindowActive = windowActive;
        CurrentWindow = currentWindow;
        ConfigPaused = configPaused;
        EffectivePaused = effectivePaused;
        WorldLoading = worldLoading;
    }

    /// <summary>Any in-game window is open, which freezes the simulation.</summary>
    public bool WindowActive { get; }

    /// <summary>Its id ("welcome", "settings", ...), or null when nothing is open.</summary>
    public string? CurrentWindow { get; }

    /// <summary>The pause toggle set by <c>pause</c> / <c>resume</c>.</summary>
    public bool ConfigPaused { get; }

    /// <summary>What the simulation actually does: <c>config_paused</c> OR a window is open.</summary>
    public bool EffectivePaused { get; }

    /// <summary>The game is still generating or loading a world.</summary>
    public bool WorldLoading { get; }

    /// <summary>
    /// Reads the UI layer. <paramref name="effectivePaused"/> and <paramref name="worldLoading"/>
    /// come from <c>WorldAccess</c>, which touches Unity types and so is passed in rather than
    /// depended on.
    /// </summary>
    public static UiStateReport From(IGameUiAccess ui, bool? effectivePaused, bool? worldLoading)
    {
        var windowActive = ui.IsWindowActive() ?? false;
        var configPaused = ui.ConfigPaused ?? false;
        return new UiStateReport(
            windowActive: windowActive,
            currentWindow: ui.CurrentWindowId(),
            configPaused: configPaused,
            // The game's own effective pause when it can tell us, and the documented relation
            // when it cannot. Coalescing to false here would have reported a running
            // simulation while a window froze it, which is the one wrong answer that leaves an
            // agent stuck: it would keep polling for a tick that cannot arrive instead of
            // calling dismiss_window.
            effectivePaused: effectivePaused ?? (configPaused || windowActive),
            worldLoading: worldLoading ?? false
        );
    }
}
