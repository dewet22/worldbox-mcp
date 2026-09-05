using System;
using System.IO;

namespace WorldBoxBridge.Commands.Control;

/// <summary>
/// Pure path rules shared by <c>save_world</c> and <c>load_world</c>. No Unity dependency so the
/// test project can link it.
/// </summary>
/// <remarks>
/// The game keeps its save slots under <c>&lt;persistentDataPath&gt;/saves/saveN</c>. Agents
/// naturally pass a bare name ("save3", "before-the-flood") rather than an absolute path, and
/// before this helper existed such a name was handed to the game verbatim, which resolved it
/// against the process working directory, i.e. the game install folder.
/// </remarks>
public static class SavePathResolver
{
    /// <summary>
    /// Absolute paths pass through untouched. Anything else is resolved under
    /// <paramref name="savesRoot"/>; parent-directory segments are rejected so a relative name
    /// can never escape the saves directory.
    /// </summary>
    public static string ResolveFolder(string? folder, string savesRoot)
    {
        var trimmed = (folder ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "folder is required: an absolute path, or a name under the game's saves directory."
            );
        }
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }
        foreach (var segment in trimmed.Split('/', '\\'))
        {
            if (segment == "..")
            {
                throw new ArgumentException(
                    $"'{trimmed}' contains '..'; relative save names must stay inside the saves directory."
                );
            }
        }
        return Path.Combine(savesRoot, trimmed);
    }

    /// <summary>
    /// Returns the map file inside a save folder in the game's own preference order
    /// (<c>map.wbox</c>, <c>map.wbax</c>, <c>map.json</c>), or null if none exists.
    /// </summary>
    public static string? FindMapFile(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }
        foreach (var name in new[] { "map.wbox", "map.wbax", "map.json" })
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
