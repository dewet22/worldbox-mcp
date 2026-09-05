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
    private static readonly string[] MapFileNames = { "map.wbox", "map.wbax", "map.json" };

    /// <summary>
    /// Rooted paths pass through untouched. Drive-relative Windows forms are rejected.
    /// Everything else is resolved under <paramref name="savesRoot"/>, with parent-directory
    /// segments rejected, so a relative name can never escape the saves directory.
    /// </summary>
    /// <remarks>
    /// The rules are applied to the string itself rather than through
    /// <c>Path.IsPathRooted</c>, which is platform-dependent in exactly the place that matters:
    /// on Windows it answers true for the drive-relative <c>C:foo</c> and so used to wave those
    /// straight past the <c>..</c> check, while on Linux it answers false and no test could see
    /// it. <c>C:foo</c> is not an absolute path, it resolves against the working directory
    /// <em>of drive C</em>, which for the game is its install folder. That is the escape this
    /// helper exists to prevent, so it is refused rather than silently accepted.
    /// </remarks>
    public static string ResolveFolder(string? folder, string savesRoot)
    {
        var trimmed = (folder ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "folder is required: an absolute path, or a name under the game's saves directory."
            );
        }
        if (IsDriveRelative(trimmed))
        {
            throw new ArgumentException(
                $"'{trimmed}' is drive-relative, which resolves against the game's working "
                    + "directory rather than the saves directory. Pass a full path such as "
                    + $"'{trimmed.Substring(0, 2)}\\{trimmed.Substring(2)}', or a plain save name."
            );
        }
        if (IsRooted(trimmed))
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
    /// Turns whatever the caller passed (map file, save folder, or slot name) into a map file
    /// path, or throws <see cref="ArgumentException"/> describing why it could not.
    /// </summary>
    /// <remarks>
    /// Takes <paramref name="savesRoot"/> for the same reason <see cref="ResolveFolder"/> does:
    /// reading <c>GameSavePaths.SavesRoot</c> here would touch
    /// <c>Application.persistentDataPath</c> and put the whole function out of reach of the
    /// tests.
    /// </remarks>
    public static string ResolveMapFile(string? path, string savesRoot)
    {
        var resolved = ResolveFolder(path, savesRoot);
        if (Directory.Exists(resolved))
        {
            return FindMapFile(resolved)
                ?? throw new ArgumentException(
                    $"'{resolved}' contains no {string.Join(" / ", MapFileNames)}."
                );
        }
        if (!File.Exists(resolved))
        {
            throw new ArgumentException(
                $"path '{path}' not found (resolved to '{resolved}'). Pass a save file, a save "
                    + "folder, or a name under the game's saves directory."
            );
        }
        return resolved;
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
        foreach (var name in MapFileNames)
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>Unix absolute, UNC, or a Windows drive followed by a separator.</summary>
    private static bool IsRooted(string path) =>
        path[0] == '/' || path[0] == '\\' || (HasDrivePrefix(path) && IsSeparator(path[2]));

    /// <summary>A Windows drive prefix that is <em>not</em> followed by a separator.</summary>
    private static bool IsDriveRelative(string path) =>
        HasDrivePrefix(path) && (path.Length == 2 || !IsSeparator(path[2]));

    private static bool HasDrivePrefix(string path) =>
        path.Length >= 2
        && path[1] == ':'
        && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'));

    private static bool IsSeparator(char c) => c == '/' || c == '\\';
}
