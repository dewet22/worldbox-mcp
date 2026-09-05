using System;
using FluentAssertions;
using WorldBoxBridge.Commands.Control;
using Xunit;

namespace WorldBoxBridge.Tests;

/// <summary>
/// Pins the invariant the whole load_world threading change rests on: that the saves root is
/// available off the main thread because it was sampled on it. The static is process-wide, so
/// these run in their own collection rather than racing another test's Capture.
/// </summary>
[Collection("GameSavePaths")]
public class GameSavePathsTests : IDisposable
{
    public GameSavePathsTests() => GameSavePaths.ResetForTests();

    public void Dispose() => GameSavePaths.ResetForTests();

    [Fact]
    public void SavesRoot_before_Capture_says_so_rather_than_returning_a_wrong_path()
    {
        // If this ever returns something instead of throwing, load_world resolves `save1`
        // against the process working directory, which is the game install folder.
        Func<string> act = () => GameSavePaths.SavesRoot;

        act.Should().Throw<InvalidOperationException>().WithMessage("*has not run*");
    }

    [Fact]
    public void Capture_puts_saves_under_the_path_it_was_given()
    {
        GameSavePaths.Capture(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wb-persistent")
        );

        GameSavePaths
            .SavesRoot.Should()
            .Be(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wb-persistent", "saves"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Capture_refuses_an_empty_path(string? persistentDataPath)
    {
        // Unity hands back a real path once the player is up, so an empty one means Capture ran
        // somewhere it cannot be trusted, and silently building "/saves" would be worse.
        Action act = () => GameSavePaths.Capture(persistentDataPath!);

        act.Should().Throw<ArgumentException>();
    }
}
