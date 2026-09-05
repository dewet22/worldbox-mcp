using System;

namespace WorldBoxBridge.Commands.Action;

/// <summary>
/// Pure tile-path interpolation for <see cref="InvokePowerCommand"/>'s pulse/drag support.
/// Kept free of Unity types so the test project can link and exercise it.
/// </summary>
public static class PulsePath
{
    /// <summary>Inclusive bounds for the pulse count (one power application per Unity frame —
    /// the synthetic equivalent of holding the mouse button; 200 ≈ 3.3 s at 60 fps).</summary>
    public const int MinPulses = 1;
    public const int MaxPulses = 200;

    /// <summary>Plain struct rather than a tuple, System.ValueTuple isn't always loadable under Unity Mono.</summary>
    public readonly struct Point
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
    }

    /// <summary>
    /// The tile coordinate for pulse <paramref name="index"/> (0-based) of
    /// <paramref name="pulses"/> total, linearly interpolated from (x, y) to (x2, y2) —
    /// the synthetic equivalent of dragging the cursor across the map while holding the
    /// button. A single pulse, or a degenerate drag with equal endpoints, stays at (x, y).
    /// </summary>
    public static Point At(int index, int pulses, int x, int y, int x2, int y2)
    {
        if (pulses <= 1)
        {
            return new Point(x, y);
        }
        var t = (double)index / (pulses - 1);
        var px = (int)Math.Round(x + (x2 - x) * t);
        var py = (int)Math.Round(y + (y2 - y) * t);
        return new Point(px, py);
    }
}
