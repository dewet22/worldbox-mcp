namespace WorldBoxBridge.Reflection;

/// <summary>
/// The game's UI state as the commands consume it, separated from the reflection that reads it.
/// </summary>
/// <remarks>
/// <see cref="GameUiAccess"/> is the only production implementation and it cannot be linked into
/// the test project: it holds <c>GameRefs</c> and a BepInEx <c>ManualLogSource</c>. This interface
/// mentions neither, so the branch logic in <see cref="WorldBoxBridge.Commands.Control.WindowDismissal"/>
/// and <see cref="WorldBoxBridge.Commands.Read.UiStateReport"/> can be driven by a fake and tested.
/// <para>
/// Every member is fail-soft, and null is not false: it means the symbol is missing in this
/// WorldBox build, which the callers deliberately coalesce to "not blocked".
/// </para>
/// </remarks>
public interface IGameUiAccess
{
    /// <summary>True if any ScrollWindow is open (which pauses the simulation); null if unknown.</summary>
    bool? IsWindowActive();

    /// <summary>The <c>screen_id</c> of the window currently shown ("welcome", "settings", ...), or null.</summary>
    string? CurrentWindowId();

    /// <summary>Closes every open window. False if the game symbol is missing in this build.</summary>
    bool HideAllWindows();

    /// <summary>The user-facing pause toggle (<c>Config.paused</c>), independent of open windows.</summary>
    bool? ConfigPaused { get; }
}
