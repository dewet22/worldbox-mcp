using System;
using System.IO;
using UnityEngine;

namespace WorldBoxBridge.Commands.Control;

/// <summary>Where the game keeps its save slots: <c>SaveManager.generateMainPath("saves")</c>
/// is <c>Application.persistentDataPath/saves/</c>, so we compute it the same way.</summary>
/// <remarks>
/// Sampled once on the main thread rather than read per call. <c>Application.persistentDataPath</c>
/// is a Unity API like any other and its getter throws
/// <c>get_persistentDataPath can only be called from the main thread</c> when read off-thread.
/// Since <c>load_world</c> now resolves its path on the HTTP thread, so that reading the file
/// cannot stall a frame, reading the property lazily would have traded the freeze for a crash.
/// </remarks>
internal static class GameSavePaths
{
    private static string? _savesRoot;

    /// <summary>
    /// Samples the saves directory. Call from <c>Plugin.Awake</c>, which Unity runs on the main
    /// thread, before the HTTP listener can accept a request.
    /// </summary>
    public static void Capture()
    {
        _savesRoot = Path.Combine(Application.persistentDataPath, "saves");
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
}
