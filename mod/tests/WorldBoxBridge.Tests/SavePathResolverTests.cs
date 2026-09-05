using System;
using System.IO;
using FluentAssertions;
using WorldBoxBridge.Commands.Control;
using Xunit;

namespace WorldBoxBridge.Tests;

public class SavePathResolverTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "wb-saves-root");

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

    // ─── Containment ──────────────────────────────────────────────────────
    //
    // Two rounds of prefix rules were written here and both leaked, in opposite directions.
    // Path.IsPathRooted answers true on Windows for the drive-relative "C:foo", which waved it
    // past the ".." check. Hand-written prefix rules then classified "C:/../../etc/passwd" as
    // rooted on Linux, where "C:" is just filename characters, and passed it through for the
    // same reason. The contract is no longer about shapes: anything that is not fully
    // qualified either lands inside the saves root or is refused, and these tests assert
    // exactly that, so they hold on whichever platform runs them.

    public static TheoryData<string> HostileNames =>
        new()
        {
            "..",
            "../escape",
            "a/../../escape",
            "../../../../../../etc/passwd",
            "C:foo",
            "C:/../../../../etc/passwd",
            "c:..\\..\\Windows",
            "Z:",
            "\\foo",
            "\\..\\..\\etc\\passwd",
            "\\",
            "sub/../..",
            "./../..",
            "a//../../b",
        };

    [Theory]
    [MemberData(nameof(HostileNames))]
    public void A_name_that_is_not_fully_qualified_never_lands_outside_the_saves_root(string input)
    {
        var root = Path.GetFullPath(Root);
        string? resolved = null;
        try
        {
            resolved = SavePathResolver.ResolveFolder(input, root);
        }
        catch (ArgumentException)
        {
            return; // refused, which is the other acceptable answer
        }
        resolved
            .Should()
            .StartWith(
                root + Path.DirectorySeparatorChar,
                because: $"'{input}' was accepted, so it must be contained"
            );
    }

    [Theory]
    [InlineData("save1")]
    [InlineData("experiments/run-7")]
    [InlineData("run:7")]
    [InlineData(".hidden")]
    [InlineData("-dash")]
    [InlineData("sp ace")]
    public void An_ordinary_save_name_lands_inside_the_saves_root(string input)
    {
        var root = Path.GetFullPath(Root);
        SavePathResolver
            .ResolveFolder(input, root)
            .Should()
            .Be(Path.Combine(root, input.Replace('/', Path.DirectorySeparatorChar)));
    }

    // ─── Fully qualified, both platform branches ──────────────────────────
    //
    // The classification is tested directly rather than through ResolveFolder. Only the
    // classification is platform-switchable: Path.Combine and Path.GetFullPath always follow
    // the running platform, so driving ResolveFolder with the other platform's rules would
    // pin behaviour that platform does not actually have.

    [Theory]
    [InlineData("C:\\saves\\world")]
    [InlineData("c:/saves/world")]
    [InlineData("\\\\server\\share\\world")]
    [InlineData("//server/share/world")]
    public void Windows_recognises_drive_and_UNC_paths_as_fully_qualified(string input)
    {
        SavePathResolver.IsFullyQualified(input, windows: true).Should().BeTrue();
    }

    [Theory]
    [InlineData("C:foo")] // drive-relative: resolves against drive C's working directory
    [InlineData("Z:")]
    [InlineData("\\foo")] // root-relative: resolves against the current drive
    [InlineData("/foo")] // the same thing with the other separator
    [InlineData("save1")]
    public void Windows_does_not_mistake_a_rooted_looking_path_for_a_qualified_one(string input)
    {
        // Path.IsPathRooted answers true for the first four of these. That is what waved
        // "C:foo" past the '..' check, and it is why this test does not use it.
        SavePathResolver.IsFullyQualified(input, windows: true).Should().BeFalse();
    }

    [Theory]
    [InlineData("/tmp/elsewhere/my-world", true)]
    [InlineData("/../../etc/passwd", true)]
    [InlineData("C:\\saves\\world", false)] // just filename characters on Unix
    [InlineData("\\\\server\\share", false)]
    [InlineData("save1", false)]
    public void Unix_counts_only_a_leading_slash(string input, bool expected)
    {
        SavePathResolver.IsFullyQualified(input, windows: false).Should().Be(expected);
    }

    [Fact]
    public void A_fully_qualified_path_comes_back_normalised()
    {
        // save_world's schema promises "the resolved absolute path". Returning it verbatim
        // while the relative branch returned a normalised one made that field mean two
        // different things depending on which branch produced it.
        var root = Path.GetFullPath(Root);
        var messy = Path.Combine(Path.GetTempPath(), "a", "..", "b");

        SavePathResolver.ResolveFolder(messy, root).Should().Be(Path.GetFullPath(messy));
    }

    [Fact]
    public void A_sibling_whose_name_merely_starts_with_the_root_is_not_inside_it()
    {
        // "/tmp/wb-saves-root-backup" starts with "/tmp/wb-saves-root" as a string but is not
        // under it. The containment check compares with a trailing separator for that reason.
        var root = Path.GetFullPath(Root);
        Action act = () =>
            SavePathResolver.ResolveFolder("../" + Path.GetFileName(root) + "-backup", root);

        act.Should().Throw<ArgumentException>().WithMessage("*outside the saves directory*");
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
