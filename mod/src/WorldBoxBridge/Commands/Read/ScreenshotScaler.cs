using System;

namespace WorldBoxBridge.Commands.Read;

/// <summary>
/// Pure sizing/argument helpers for <see cref="ScreenshotCommand"/>. Kept free of Unity types so
/// the test project can link and exercise them.
/// </summary>
public static class ScreenshotScaler
{
    /// <summary>Longest edge in pixels a screenshot is scaled down to unless the caller asks otherwise.
    /// 1280 keeps a 16:9 frame under ~200 KB as JPEG while remaining legible to vision models.</summary>
    public const int DefaultMaxDimension = 1280;

    public const int DefaultQuality = 80;
    public const string Jpg = "jpg";
    public const string Png = "png";

    /// <summary>Plain struct rather than a tuple — System.ValueTuple isn't always loadable under Unity Mono.</summary>
    public readonly struct ScaledSize
    {
        public ScaledSize(int width, int height, bool isScaled)
        {
            Width = width;
            Height = height;
            IsScaled = isScaled;
        }

        public int Width { get; }
        public int Height { get; }
        public bool IsScaled { get; }
    }

    /// <summary>
    /// Shrinks <paramref name="width"/> × <paramref name="height"/> so the longest edge is at most
    /// <paramref name="maxDimension"/>, preserving aspect ratio. Never upscales; 0 disables scaling.
    /// </summary>
    public static ScaledSize Fit(int width, int height, int maxDimension)
    {
        if (maxDimension <= 0 || width <= 0 || height <= 0)
        {
            return new ScaledSize(width, height, false);
        }
        var longest = Math.Max(width, height);
        if (longest <= maxDimension)
        {
            return new ScaledSize(width, height, false);
        }
        var scale = (double)maxDimension / longest;
        var w = Math.Max(1, (int)Math.Round(width * scale));
        var h = Math.Max(1, (int)Math.Round(height * scale));
        return new ScaledSize(w, h, true);
    }

    public static int ClampQuality(int quality) => Math.Min(100, Math.Max(1, quality));

    /// <summary>Accepts jpg/jpeg/png in any case; empty means the default (jpg).</summary>
    public static string NormalizeFormat(string? format)
    {
        var f = (format ?? string.Empty).Trim().ToLowerInvariant();
        switch (f)
        {
            case "":
            case "jpg":
            case "jpeg":
                return Jpg;
            case "png":
                return Png;
            default:
                throw new ArgumentException(
                    $"format '{format}' is not supported; use '{Jpg}' or '{Png}'."
                );
        }
    }
}
