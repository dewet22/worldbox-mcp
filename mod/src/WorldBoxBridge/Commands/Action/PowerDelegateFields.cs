using System;
using System.Collections.Generic;
using System.Reflection;

namespace WorldBoxBridge.Commands.Action;

/// <summary>
/// The one source of truth for reading a GodPower's click/toggle delegate fields, cached per
/// concrete asset type. invoke_power and list_powers both read through this, so the discovery
/// flags cannot drift from invocation behaviour, and a modded build that registers a GodPower
/// subclass (or hides a field) resolves against the right type instead of whichever asset
/// happened to be read first. Pure BCL reflection, linked into the test project.
/// </summary>
public sealed class PowerDelegateFields
{
    private const BindingFlags Inst =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly Dictionary<Type, Entry> _byType = new();

    /// <summary>Plain struct rather than a tuple, System.ValueTuple isn't always loadable under Unity Mono.</summary>
    public readonly struct Snapshot
    {
        public Snapshot(
            Delegate? clickAction,
            Delegate? clickPowerAction,
            Delegate? clickBrushAction,
            Delegate? clickPowerBrushAction,
            Delegate? toggleAction
        )
        {
            ClickAction = clickAction;
            ClickPowerAction = clickPowerAction;
            ClickBrushAction = clickBrushAction;
            ClickPowerBrushAction = clickPowerBrushAction;
            ToggleAction = toggleAction;
        }

        public Delegate? ClickAction { get; }
        public Delegate? ClickPowerAction { get; }
        public Delegate? ClickBrushAction { get; }
        public Delegate? ClickPowerBrushAction { get; }
        public Delegate? ToggleAction { get; }
    }

    /// <summary>False when the power's type declares none of the five delegate fields —
    /// i.e. this build's GodPower shape is unrecognisable.</summary>
    public bool AnyFieldPresent(object power)
    {
        var e = For(power.GetType());
        return e.ClickAction != null
            || e.ClickPowerAction != null
            || e.ClickBrushAction != null
            || e.ClickPowerBrushAction != null
            || e.ToggleAction != null;
    }

    /// <summary>Reads the five delegate values off <paramref name="power"/>. A field that is
    /// missing, or holds something that is not a delegate, reads as null.</summary>
    public Snapshot Read(object power)
    {
        var e = For(power.GetType());
        return new Snapshot(
            e.ClickAction?.GetValue(power) as Delegate,
            e.ClickPowerAction?.GetValue(power) as Delegate,
            e.ClickBrushAction?.GetValue(power) as Delegate,
            e.ClickPowerBrushAction?.GetValue(power) as Delegate,
            e.ToggleAction?.GetValue(power) as Delegate
        );
    }

    private Entry For(Type powerType)
    {
        if (_byType.TryGetValue(powerType, out var cached))
        {
            return cached;
        }
        var entry = new Entry(
            powerType.GetField("click_action", Inst),
            powerType.GetField("click_power_action", Inst),
            powerType.GetField("click_brush_action", Inst),
            powerType.GetField("click_power_brush_action", Inst),
            powerType.GetField("toggle_action", Inst)
        );
        _byType[powerType] = entry;
        return entry;
    }

    private sealed class Entry
    {
        public Entry(
            FieldInfo? clickAction,
            FieldInfo? clickPowerAction,
            FieldInfo? clickBrushAction,
            FieldInfo? clickPowerBrushAction,
            FieldInfo? toggleAction
        )
        {
            ClickAction = clickAction;
            ClickPowerAction = clickPowerAction;
            ClickBrushAction = clickBrushAction;
            ClickPowerBrushAction = clickPowerBrushAction;
            ToggleAction = toggleAction;
        }

        public FieldInfo? ClickAction { get; }
        public FieldInfo? ClickPowerAction { get; }
        public FieldInfo? ClickBrushAction { get; }
        public FieldInfo? ClickPowerBrushAction { get; }
        public FieldInfo? ToggleAction { get; }
    }
}
