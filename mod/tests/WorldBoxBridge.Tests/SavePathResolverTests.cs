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

    // ─── Rooted forms ─────────────────────────────────────────────────────
    //
    // The invariant this class documents ("a relative name can never escape the saves
    // directory") used to be false. Path.IsPathRooted answers true on Windows for the
    // drive-relative "C:foo", which resolves against the working directory of drive C, so
    // those went straight through and skipped the ".." check. On Linux it answers false and
    // no test could see it. The rules are read off the string now, so these run everywhere.

    [Theory]
    [InlineData("/tmp/elsewhere/world")]
    [InlineData("\\\\server\\share\\world")]
    [InlineData("C:\\saves\\world")]
    [InlineData("c:/saves/world")]
    public void Rooted_path_passes_through_untouched(string input)
    {
        SavePathResolver.ResolveFolder(input, Root).Should().Be(input);
    }

    [Theory]
    [InlineData("C:foo")]
    [InlineData("c:..\\..\\Windows")]
    [InlineData("Z:")]
    public void Drive_relative_path_is_rejected(string input)
    {
        Action act = () => SavePathResolver.ResolveFolder(input, Root);
        act.Should().Throw<ArgumentException>().WithMessage("*drive-relative*");
    }

    [Fact]
    public void Colon_that_is_not_a_drive_prefix_stays_a_relative_name()
    {
        SavePathResolver.ResolveFolder("run:7", Root).Should().Be(Path.Combine(Root, "run:7"));
    }

    // ─── ResolveMapFile ───────────────────────────────────────────────────

    [Fact]
    public void ResolveMapFile_finds_the_map_inside_a_save_folder()
    {
        WithTempRoot(root =>
        {
            var slot = Path.Combine(root, "save1");
            Directory.CreateDirectory(slot);
            File.WriteAllText(Path.Combine(slot, "map.wbox"), "x");
            SavePathResolver
                .ResolveMapFile("save1", root)
                .Should()
                .Be(Path.Combine(slot, "map.wbox"));
        });
    }

    [Fact]
    public void ResolveMapFile_accepts_a_map_file_directly()
    {
        WithTempRoot(root =>
        {
            var file = Path.Combine(root, "map.json");
            File.WriteAllText(file, "{}");
            SavePathResolver.ResolveMapFile("map.json", root).Should().Be(file);
        });
    }

    [Fact]
    public void ResolveMapFile_rejects_a_save_folder_with_no_map()
    {
        WithTempRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "empty"));
            Action act = () => SavePathResolver.ResolveMapFile("empty", root);
            act.Should().Throw<ArgumentException>().WithMessage("*contains no map.wbox*");
        });
    }

    [Fact]
    public void ResolveMapFile_rejects_a_path_that_does_not_exist()
    {
        WithTempRoot(root =>
        {
            Action act = () => SavePathResolver.ResolveMapFile("nope", root);
            act.Should().Throw<ArgumentException>().WithMessage("*not found*resolved to*");
        });
    }

    [Fact]
    public void ResolveMapFile_rejects_an_escaping_name_before_touching_the_disk()
    {
        Action act = () => SavePathResolver.ResolveMapFile("../etc", Root);
        act.Should().Throw<ArgumentException>().WithMessage("*..*");
    }

    private static void WithTempRoot(Action<string> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "wb-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            body(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
