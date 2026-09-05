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
    public UiStateReport(
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
    public static UiStateReport From(IGameUiAccess ui, bool? effectivePaused, bool? worldLoading) =>
        new(
            windowActive: ui.IsWindowActive() ?? false,
            currentWindow: ui.CurrentWindowId(),
            configPaused: ui.ConfigPaused ?? false,
            effectivePaused: effectivePaused ?? false,
            worldLoading: worldLoading ?? false
        );
}
