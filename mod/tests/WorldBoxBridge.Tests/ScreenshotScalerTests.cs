using System;
using FluentAssertions;
using WorldBoxBridge.Commands.Read;
using Xunit;

namespace WorldBoxBridge.Tests;

public class ScreenshotScalerTests
{
    [Theory]
    [InlineData(3354, 2654, 1280, 1280, 1013)] // Retina landscape: longest edge clamped, aspect kept
    [InlineData(600, 800, 400, 300, 400)] // portrait
    [InlineData(1920, 1080, 1920, 1920, 1080)] // exactly at the limit: untouched
    [InlineData(800, 600, 1280, 800, 600)] // smaller than the limit: never upscaled
    [InlineData(800, 600, 0, 800, 600)] // 0 disables scaling
    [InlineData(10000, 10, 100, 100, 1)] // extreme aspect never rounds to zero
    [InlineData(0, 100, 500, 0, 100)] // non-positive width passes through unscaled
    [InlineData(100, 0, 500, 100, 0)] // non-positive height passes through unscaled
    [InlineData(-5, 100, 500, -5, 100)] // negative dimensions are not "scaled" either
    public void Fit_clamps_longest_edge_and_preserves_aspect(
        int width,
        int height,
        int maxDimension,
        int expectedWidth,
        int expectedHeight
    )
    {
        var size = ScreenshotScaler.Fit(width, height, maxDimension);
        size.Width.Should().Be(expectedWidth);
        size.Height.Should().Be(expectedHeight);
        size.IsScaled.Should().Be(expectedWidth != width || expectedHeight != height);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(80, 80)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void ClampQuality_keeps_jpeg_quality_in_range(int input, int expected)
    {
        ScreenshotScaler.ClampQuality(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "jpg")]
    [InlineData("", "jpg")]
    [InlineData("jpg", "jpg")]
    [InlineData("JPEG", "jpg")]
    [InlineData("png", "png")]
    [InlineData(" PNG ", "png")]
    public void NormalizeFormat_accepts_jpg_and_png_spellings(string? input, string expected)
    {
        ScreenshotScaler.NormalizeFormat(input).Should().Be(expected);
    }

    [Fact]
    public void NormalizeFormat_rejects_unknown_formats()
    {
        Action act = () => ScreenshotScaler.NormalizeFormat("gif");
        act.Should().Throw<ArgumentException>().WithMessage("*gif*");
    }
}
