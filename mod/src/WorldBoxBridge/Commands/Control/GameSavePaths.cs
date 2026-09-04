using System.IO;
using UnityEngine;

namespace WorldBoxBridge.Commands.Control;

/// <summary>Where the game keeps its save slots: <c>SaveManager.generateMainPath("saves")</c>
/// is <c>Application.persistentDataPath/saves/</c>, so we compute it the same way.</summary>
internal static class GameSavePaths
{
    public static string SavesRoot => Path.Combine(Application.persistentDataPath, "saves");
}
