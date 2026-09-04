using System;
using System.IO;
using FluentAssertions;
using WorldBoxBridge.Commands.Control;
using Xunit;

namespace WorldBoxBridge.Tests;

public class SavePathResolverTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "wb-saves-root");

    [Fact]
    public void Absolute_path_passes_through_untouched()
    {
        var abs = Path.Combine(Path.GetTempPath(), "elsewhere", "my-world");
        SavePathResolver.ResolveFolder(abs, Root).Should().Be(abs);
    }

    [Theory]
    [InlineData("save1")]
    [InlineData("mcp-tyre-kick")]
    [InlineData("  save2  ")] // whitespace trimmed
    public void Bare_name_resolves_under_the_saves_root(string name)
    {
        var resolved = SavePathResolver.ResolveFolder(name, Root);
        resolved.Should().Be(Path.Combine(Root, name.Trim()));
    }

    [Fact]
    public void Relative_subpath_resolves_under_the_saves_root()
    {
        SavePathResolver
            .ResolveFolder("experiments/run-7", Root)
            .Should()
            .Be(Path.Combine(Root, "experiments/run-7"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("a/../../escape")]
    [InlineData("..\\escape")]
    public void Relative_path_with_parent_segments_is_rejected(string input)
    {
        Action act = () => SavePathResolver.ResolveFolder(input, Root);
        act.Should().Throw<ArgumentException>().WithMessage("*..*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_folder_is_rejected(string? input)
    {
        Action act = () => SavePathResolver.ResolveFolder(input, Root);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FindMapFile_prefers_wbox_then_wbax_then_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wb-find-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            SavePathResolver.FindMapFile(dir).Should().BeNull();
            File.WriteAllText(Path.Combine(dir, "map.json"), "{}");
            SavePathResolver.FindMapFile(dir).Should().Be(Path.Combine(dir, "map.json"));
            File.WriteAllText(Path.Combine(dir, "map.wbax"), "x");
            SavePathResolver.FindMapFile(dir).Should().Be(Path.Combine(dir, "map.wbax"));
            File.WriteAllText(Path.Combine(dir, "map.wbox"), "x");
            SavePathResolver.FindMapFile(dir).Should().Be(Path.Combine(dir, "map.wbox"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindMapFile_returns_null_for_missing_directory()
    {
        SavePathResolver
            .FindMapFile(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()))
            .Should()
            .BeNull();
    }
}
