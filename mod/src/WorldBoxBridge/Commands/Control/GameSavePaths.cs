using System;
using System.IO;

namespace WorldBoxBridge.Commands.Control;

/// <summary>Where the game keeps its save slots: <c>SaveManager.generateMainPath("saves")</c>
/// is <c>Application.persistentDataPath/saves/</c>, so we compute it the same way.</summary>
/// <remarks>
/// Sampled once at start-up rather than read per call, and handed in rather than read here.
/// <c>Application.persistentDataPath</c> is a Unity API like any other and its getter throws
/// <c>get_persistentDataPath can only be called from the main thread</c> off-thread, so
/// <c>load_world</c> resolving a save name on the HTTP thread would have traded the freeze it
/// fixes for a crash. Taking the value as a parameter keeps this file free of
/// <c>UnityEngine</c>, which is what lets the test project link it and pin both branches: the
/// invariant that nothing before the marshalled call touches Unity rests on
/// <see cref="Capture"/> having run, and an invariant nothing tests is a comment.
/// </remarks>
internal static class GameSavePaths
{
    // Volatile so the guarantee is stated rather than reconstructed. The write happens in
    // Plugin.Awake, before the listener thread exists, so the happens-before chain already holds
    // through Thread.Start; this costs nothing on that path and saves the next reader the proof.
    private static volatile string? _savesRoot;

    /// <summary>
    /// Samples the saves directory from the value the caller read on the main thread. Call from
    /// <c>Plugin.Awake</c>, before the HTTP listener can accept a request.
    /// </summary>
    /// <param name="persistentDataPath">
    /// <c>UnityEngine.Application.persistentDataPath</c>, read by the caller because only the
    /// caller is on the main thread.
    /// </param>
    public static void Capture(string persistentDataPath)
    {
        if (string.IsNullOrEmpty(persistentDataPath))
        {
            throw new ArgumentException(
                "persistentDataPath is empty. Unity returns a real path once the player is up, so "
                    + "an empty one means Capture ran too early to trust.",
                nameof(persistentDataPath)
            );
        }
        _savesRoot = Path.Combine(persistentDataPath, "saves");
    }

    /// <summary>The absolute saves directory, as sampled by <see cref="Capture"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// If <see cref="Capture"/> never ran. That can only mean start-up failed before the bridge
    /// existed, in which case no command is reachable and nobody sees this.
    /// </exception>
    public static string SavesRoot =>
        _savesRoot
        ?? throw new InvalidOperationException(
            "GameSavePaths.Capture() has not run. It must be called on the main thread during "
                + "plugin start-up, before any command resolves a save path."
        );

    /// <summary>Drops the sampled value. Exists for tests, which share a process.</summary>
    internal static void ResetForTests() => _savesRoot = null;
}
