namespace WorldBoxBridge.Commands.Action;

/// <summary>Which GodPower delegate <c>invoke_power</c> drives for a given call.</summary>
public enum PowerDelegatePath
{
    ClickAction,
    ClickPowerAction,
    ClickBrushAction,
    ClickPowerBrushAction,
    ToggleAction,

    /// <summary>The power exposes no delegate the bridge can drive.</summary>
    RejectNoDelegates,

    /// <summary>The caller asked for a radius but the power has no brush delegate.</summary>
    RejectRadiusUnsupported,
}

/// <summary>
/// Pure delegate-selection logic for <see cref="InvokePowerCommand"/>. Kept free of Unity types
/// so the test project can link and exercise the whole matrix.
/// </summary>
public static class PowerDelegateSelector
{
    /// <summary>Inclusive bounds for the caller-supplied brush radius (circ_1 ... circ_50 —
    /// the same ceiling the game's own <c>Brush.getRandom</c> uses).</summary>
    public const int MinRadius = 1;
    public const int MaxRadius = 50;

    /// <summary>Plain struct rather than a tuple, System.ValueTuple isn't always loadable under Unity Mono.</summary>
    public readonly struct Choice
    {
        public Choice(PowerDelegatePath path, int? brushRadius)
        {
            Path = path;
            BrushRadius = brushRadius;
        }

        public PowerDelegatePath Path { get; }

        /// <summary>The circle-brush radius to apply, when <see cref="Path"/> is a brush path.</summary>
        public int? BrushRadius { get; }
    }

    public static Choice Select(
        bool hasClickAction,
        bool hasClickPowerAction,
        bool hasClickBrushAction,
        bool hasClickPowerBrushAction,
        bool hasToggleAction,
        int? radius
    )
    {
        if (radius is int r)
        {
            // An explicit radius requires a brush delegate; the power-delegate family wins,
            // matching the order PlayerControl.clickPower checks in-game.
            if (hasClickPowerBrushAction)
            {
                return new Choice(PowerDelegatePath.ClickPowerBrushAction, r);
            }
            if (hasClickBrushAction)
            {
                return new Choice(PowerDelegatePath.ClickBrushAction, r);
            }
            return new Choice(PowerDelegatePath.RejectRadiusUnsupported, null);
        }

        // No radius: the pre-radius bridge behaviour first (single-tile delegates), so existing
        // callers see byte-for-byte identical results ...
        if (hasClickAction)
        {
            return new Choice(PowerDelegatePath.ClickAction, null);
        }
        if (hasClickPowerAction)
        {
            return new Choice(PowerDelegatePath.ClickPowerAction, null);
        }
        // ... then the delegates that used to be rejected outright: brush-only powers run at a
        // deterministic minimal brush, and global toggles fire their toggle_action.
        if (hasClickPowerBrushAction)
        {
            return new Choice(PowerDelegatePath.ClickPowerBrushAction, MinRadius);
        }
        if (hasClickBrushAction)
        {
            return new Choice(PowerDelegatePath.ClickBrushAction, MinRadius);
        }
        if (hasToggleAction)
        {
            return new Choice(PowerDelegatePath.ToggleAction, null);
        }
        return new Choice(PowerDelegatePath.RejectNoDelegates, null);
    }
}
