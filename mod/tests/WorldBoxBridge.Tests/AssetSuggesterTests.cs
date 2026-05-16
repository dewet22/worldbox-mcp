using FluentAssertions;
using WorldBoxBridge;
using Xunit;

namespace WorldBoxBridge.Tests;

public class AssetSuggesterTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("a", "", 1)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("dragon_red", "dragon_red", 0)]
    [InlineData("dragon_red", "dragon_white", 5)]
    [InlineData("DRAGON", "dragon", 6)] // case-sensitive
    public void Levenshtein_returns_expected_distance(string a, string b, int expected)
    {
        AssetSuggester.Levenshtein(a, b).Should().Be(expected);
    }

    [Fact]
    public void Suggest_returns_closest_candidates_first()
    {
        var candidates = new[]
        {
            "dragon_red",
            "dragon_white",
            "dragon_black",
            "demon",
            "human",
            "elf",
            "orc",
            "wolf",
            "bear",
        };

        var suggestions = AssetSuggester.Suggest("dragon_re", candidates, limit: 3);

        suggestions.Should().HaveCount(3);
        suggestions[0].Should().Be("dragon_red");
        suggestions.Should().Contain(new[] { "dragon_white", "dragon_black" });
    }

    [Fact]
    public void Suggest_with_empty_input_returns_empty()
    {
        AssetSuggester.Suggest("", new[] { "a", "b" }).Should().BeEmpty();
    }

    [Fact]
    public void Suggest_breaks_ties_alphabetically_by_id_for_stable_ordering()
    {
        var candidates = new[] { "zebra", "alpha", "bravo" }; // all distance-5 from "xxxxx"
        var suggestions = AssetSuggester.Suggest("xxxxx", candidates, limit: 3);

        suggestions[0].Should().Be("alpha");
        suggestions[1].Should().Be("bravo");
        suggestions[2].Should().Be("zebra");
    }

    [Fact]
    public void Suggest_respects_limit()
    {
        var candidates = new[] { "a", "b", "c", "d", "e" };
        AssetSuggester.Suggest("x", candidates, limit: 2).Should().HaveCount(2);
    }
}
