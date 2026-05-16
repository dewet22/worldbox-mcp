using System.Collections.Generic;
using System.Linq;

namespace WorldBoxBridge;

/// <summary>
/// Helps clients recover from typos when an asset id (tile/actor/power) doesn't resolve.
/// Used to populate the <c>did_you_mean</c> field on <c>UNKNOWN_ASSET</c> errors.
/// </summary>
public static class AssetSuggester
{
    public static IReadOnlyList<string> Suggest(
        string input,
        IEnumerable<string> candidates,
        int limit = 5
    )
    {
        if (string.IsNullOrEmpty(input))
        {
            return System.Array.Empty<string>();
        }

        return candidates
            .Select(c => new { Id = c, Distance = Levenshtein(input, c) })
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Id, System.StringComparer.Ordinal)
            .Take(limit)
            .Select(x => x.Id)
            .ToArray();
    }

    /// <summary>
    /// Levenshtein edit distance. Iterative two-row implementation, O(|a| * |b|) time and
    /// O(min(|a|, |b|)) space.
    /// </summary>
    internal static int Levenshtein(string a, string b)
    {
        if (a == b)
        {
            return 0;
        }
        if (a.Length == 0)
        {
            return b.Length;
        }
        if (b.Length == 0)
        {
            return a.Length;
        }

        // Make `b` the shorter so working memory is O(min).
        if (a.Length < b.Length)
        {
            (a, b) = (b, a);
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = System.Math.Min(
                    System.Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost
                );
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
