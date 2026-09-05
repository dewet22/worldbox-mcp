using WorldBoxBridge.Reflection;

namespace WorldBoxBridge.Commands.Control;

/// <summary>What a <c>dismiss_window</c> attempt did.</summary>
/// <remarks>
/// A <c>readonly struct</c> rather than a tuple: <c>System.ValueTuple</c> is not reliably
/// loadable under Unity Mono on net462.
/// </remarks>
public readonly struct DismissResult
{
    private DismissResult(bool dismissed, string? window, bool unsupported)
    {
        Dismissed = dismissed;
        Window = window;
        Unsupported = unsupported;
    }

    /// <summary>True when a window was open and has been closed.</summary>
    public bool Dismissed { get; }

    /// <summary>The id of the window that was closed, null when nothing was open.</summary>
    public string? Window { get; }

    /// <summary>True when the game build has no <c>hideAllEvent</c> to call.</summary>
    public bool Unsupported { get; }

    public static DismissResult NothingOpen => new(false, null, false);

    public static DismissResult Closed(string? window) => new(true, window, false);

    public static DismissResult NotSupported => new(false, null, true);
}

/// <summary>
/// The decision behind <c>dismiss_window</c>, kept out of the command so it can be tested.
/// </summary>
/// <remarks>
/// Same seam as <see cref="WorldBoxBridge.Commands.TurnGate"/>: the command itself needs
/// <c>JObject</c> and cannot be linked into the test project, so the branching moves here.
/// </remarks>
public static class WindowDismissal
{
    /// <summary>
    /// Reads the current window, then closes everything if a window is actually open.
    /// </summary>
    /// <remarks>
    /// The window id is read <em>before</em> the dismissal on purpose. Once
    /// <see cref="IGameUiAccess.HideAllWindows"/> has run there is no current window left to
    /// name, so reading it afterwards always reports null and the response loses the one piece
    /// of information the caller wanted.
    /// </remarks>
    public static DismissResult Run(IGameUiAccess ui)
    {
        var window = ui.CurrentWindowId();
        if (!(ui.IsWindowActive() ?? false))
        {
            return DismissResult.NothingOpen;
        }
        return ui.HideAllWindows() ? DismissResult.Closed(window) : DismissResult.NotSupported;
    }
}
