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
    // ResolveFolder takes windowsPaths so the Windows branch runs on a Linux CI box.
    // Production never passes it.

    [Theory]
    [InlineData("C:\\saves\\world")]
    [InlineData("c:/saves/world")]
    [InlineData("\\\\server\\share\\world")]
    public void Windows_fully_qualified_paths_pass_through_untouched(string input)
    {
        SavePathResolver.ResolveFolder(input, Root, windowsPaths: true).Should().Be(input);
    }

    [Theory]
    [InlineData("C:foo")] // drive-relative: resolves against drive C's working directory
    [InlineData("\\foo")] // root-relative: resolves against the current drive
    [InlineData("/foo")] // the same thing with the other separator
    public void Windows_forms_that_only_look_absolute_are_not_passed_through(string input)
    {
        // None of these is handed to the game verbatim. Depending on the platform they either
        // land contained under the saves root or are refused for landing outside it. Both are
        // safe answers; being waved through is not, which is what used to happen.
        try
        {
            SavePathResolver
                .ResolveFolder(input, Root, windowsPaths: true)
                .Should()
                .StartWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar);
        }
        catch (ArgumentException ex)
        {
            ex.Message.Should().Contain("outside the saves directory");
        }
    }

    [Theory]
    [InlineData("/tmp/elsewhere/my-world")]
    [InlineData("/../../etc/passwd")]
    public void Unix_absolute_paths_pass_through_untouched(string input)
    {
        // The second case is deliberate. It looks hostile and it is genuinely absolute on
        // Unix, so it is in contract: save_world and load_world require ControlWorld, which
        // only a God agent holds, and such an agent can already name any absolute path it
        // likes. Containment exists to stop a relative name reaching somewhere it should not,
        // not to sandbox an agent that is trusted with the world's lifecycle.
        SavePathResolver.ResolveFolder(input, Root, windowsPaths: false).Should().Be(input);
    }

    [Fact]
    public void A_sibling_whose_name_merely_starts_with_the_root_is_not_inside_it()
    {
        // "/tmp/wb-saves-root-backup" starts with "/tmp/wb-saves-root" as a string but is not
        // under it. The containment check compares with a trailing separator for this reason.
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
