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
    /// A fully qualified path passes through, normalised. Anything else is resolved under
    /// <paramref name="savesRoot"/> and then verified to still be inside it, so a relative
    /// name can never escape the saves directory lexically.
    /// </summary>
    /// <remarks>
    /// Containment is established by resolving, not by inspecting the string. Two rounds of
    /// prefix rules were written here and both leaked, in opposite directions:
    /// <c>Path.IsPathRooted</c> answers true on Windows for the drive-relative <c>C:foo</c>,
    /// which waved it past the <c>..</c> check; hand-written prefix rules then classified
    /// <c>C:/../../etc/passwd</c> as rooted on Linux, where <c>C:</c> is just filename
    /// characters, and passed it through for the same reason. The shape of a path does not
    /// tell you where it lands. <c>Path.GetFullPath</c> does, on whichever platform is
    /// actually running, and it collapses <c>..</c>, mixed separators and redundant
    /// separators on the way.
    /// <para>
    /// This also covers the <c>Path.Combine</c> trap: <c>Combine</c> discards its first
    /// argument when the second is rooted for the running platform, so
    /// <c>Combine(savesRoot, @"\foo")</c> is <c>\foo</c> on Windows. That result simply
    /// fails the containment check rather than needing a rule of its own.
    /// </para>
    /// <para>
    /// What it does <em>not</em> do is follow links. <c>GetFullPath</c> is lexical, so a
    /// symlink or NTFS junction planted inside the saves directory still leads out of it and
    /// net462 has no portable <c>realpath</c> to ask. The guarantee is therefore that no
    /// relative name escapes <em>lexically</em>, which is the whole of the attack this helper
    /// was written for, and not that the final bytes land inside the directory come what may.
    /// </para>
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
        if (IsFullyQualified(trimmed, WindowsPaths))
        {
            // Normalised like the relative branch: save_world's schema promises "the resolved
            // absolute path", and returning '/a/../b' verbatim made that one field mean two
            // different things depending on which branch produced it.
            return Path.GetFullPath(trimmed);
        }
        var root = Path.GetFullPath(savesRoot);
        string resolved;
        try
        {
            resolved = Path.GetFullPath(Path.Combine(root, trimmed));
        }
        catch (Exception ex)
            when (ex is ArgumentException
                || ex is NotSupportedException
                || ex is PathTooLongException
            )
        {
            // PathTooLongException derives from IOException, not ArgumentException, so a long
            // name used to sail past this filter and come back as 500 GAME_CRASH: a caller
            // mistake reported as the game breaking, which is what this branch exists to stop.
            // Not covered by a test: the mod targets net462, where the limit is real, while the
            // test project is net8.0, where .NET dropped MAX_PATH and GetFullPath accepts an
            // 8000-character name without complaint.
            throw new ArgumentException($"'{trimmed}' is not a usable save name: {ex.Message}");
        }
        if (!IsInside(resolved, root))
        {
            throw new ArgumentException(
                $"'{trimmed}' resolves to '{resolved}', outside the saves directory '{root}'. "
                    + "Pass a fully qualified path if you meant somewhere else, or a plain save "
                    + "name to stay inside."
            );
        }
        return resolved;
    }

    internal static readonly bool WindowsPaths = Path.DirectorySeparatorChar == '\\';

    /// <summary>
    /// Absolute on the given platform: a Unix path from the root, or on Windows a drive letter
    /// plus a separator, or a UNC path. Deliberately <em>not</em> <c>Path.IsPathRooted</c>,
    /// which also answers true for the drive-relative <c>C:foo</c> and for a bare leading
    /// separator, neither of which says where the path lands.
    /// </summary>
    /// <remarks>
    /// <paramref name="windows"/> is a parameter rather than a read of
    /// <see cref="Path.DirectorySeparatorChar"/> so both branches can be exercised from a Linux
    /// CI runner. It is <c>internal</c>, not part of the public surface, because only the
    /// classification is platform-switchable: <c>Path.Combine</c> and <c>Path.GetFullPath</c>
    /// always follow the running platform, so a caller who passed the wrong value would get
    /// classification and containment disagreeing with each other.
    /// </remarks>
    internal static bool IsFullyQualified(string path, bool windows)
    {
        if (!windows)
        {
            return path[0] == '/';
        }
        if (path.Length > 1 && IsSeparator(path[0]) && IsSeparator(path[1]))
        {
            return true; // UNC
        }
        return HasDrivePrefix(path) && path.Length > 2 && IsSeparator(path[2]);
    }

    /// <summary>Whether <paramref name="candidate"/> is the root itself or sits under it.</summary>
    /// <remarks>
    /// Compares with a trailing separator so a sibling directory whose name merely starts with
    /// the root's, <c>/saves-backup</c> against <c>/saves</c>, is not mistaken for a child.
    /// Ordinal rather than culture-aware: this is a path, not prose.
    /// </remarks>
    private static bool IsInside(string candidate, string root)
    {
        var normalized = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(candidate, normalized, StringComparison.Ordinal))
        {
            return true;
        }
        return candidate.StartsWith(
            normalized + Path.DirectorySeparatorChar,
            StringComparison.Ordinal
        );
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
        // Ordered so each success path pays the fewest syscalls. Not for frame time any more:
        // load_world resolves on the HTTP thread now, so no stat here sits inside a frame, and
        // the order stays because it is free to. FindMapFile does its own
        // Directory.Exists, so asking it first covers the folder case; a direct map file then
        // costs one File.Exists on top. Only the two error paths look twice.
        var map = FindMapFile(resolved);
        if (map != null)
        {
            return map;
        }
        if (File.Exists(resolved))
        {
            return resolved;
        }
        if (Directory.Exists(resolved))
        {
            throw new ArgumentException(
                $"'{resolved}' contains no {string.Join(" / ", MapFileNames)}."
            );
        }
        throw new ArgumentException(
            $"path '{path}' not found (resolved to '{resolved}'). Pass a save file, a save "
                + "folder, or a name under the game's saves directory."
        );
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

    private static bool HasDrivePrefix(string path) =>
        path.Length >= 2
        && path[1] == ':'
        && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'));

    private static bool IsSeparator(char c) => c == '/' || c == '\\';
}
