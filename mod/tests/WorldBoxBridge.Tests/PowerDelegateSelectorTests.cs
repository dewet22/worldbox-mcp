using FluentAssertions;
using WorldBoxBridge.Commands.Action;
using Xunit;

namespace WorldBoxBridge.Tests;

public class PowerDelegateSelectorTests
{
    private static PowerDelegateSelector.Choice Select(
        bool clickAction = false,
        bool clickPowerAction = false,
        bool clickBrushAction = false,
        bool clickPowerBrushAction = false,
        bool toggleAction = false,
        int? radius = null
    ) =>
        PowerDelegateSelector.Select(
            clickAction,
            clickPowerAction,
            clickBrushAction,
            clickPowerBrushAction,
            toggleAction,
            radius
        );

    // --- radius omitted: existing behaviour is preserved exactly ---

    [Fact]
    public void No_radius_prefers_click_action()
    {
        var c = Select(clickAction: true, clickBrushAction: true, toggleAction: true);
        c.Path.Should().Be(PowerDelegatePath.ClickAction);
        c.BrushRadius.Should().BeNull();
    }

    [Fact]
    public void No_radius_falls_back_to_click_power_action()
    {
        // The drops family (rain, fire, ...) carries click_power_action AND
        // click_power_brush_action; without a radius the single-tile variant wins,
        // matching the bridge's pre-radius behaviour.
        var c = Select(clickPowerAction: true, clickPowerBrushAction: true);
        c.Path.Should().Be(PowerDelegatePath.ClickPowerAction);
        c.BrushRadius.Should().BeNull();
    }

    // --- radius omitted: brush-only powers now run at a minimal brush ---

    [Fact]
    public void No_radius_drives_brush_only_power_at_radius_one()
    {
        var c = Select(clickBrushAction: true);
        c.Path.Should().Be(PowerDelegatePath.ClickBrushAction);
        c.BrushRadius.Should().Be(1);
    }

    [Fact]
    public void No_radius_prefers_power_brush_over_id_brush_like_the_game()
    {
        // PlayerControl.clickPower checks the power-delegate family first.
        var c = Select(clickBrushAction: true, clickPowerBrushAction: true);
        c.Path.Should().Be(PowerDelegatePath.ClickPowerBrushAction);
        c.BrushRadius.Should().Be(1);
    }

    // --- radius omitted: toggles ---

    [Fact]
    public void No_radius_drives_toggle_only_power()
    {
        var c = Select(toggleAction: true);
        c.Path.Should().Be(PowerDelegatePath.ToggleAction);
        c.BrushRadius.Should().BeNull();
    }

    [Fact]
    public void Click_delegates_win_over_toggle()
    {
        Select(clickAction: true, toggleAction: true)
            .Path.Should()
            .Be(PowerDelegatePath.ClickAction);
    }

    [Fact]
    public void Nothing_drivable_is_rejected()
    {
        Select().Path.Should().Be(PowerDelegatePath.RejectNoDelegates);
    }

    // --- radius given: requires a brush delegate ---

    [Fact]
    public void Radius_selects_power_brush_delegate_first()
    {
        var c = Select(clickPowerAction: true, clickPowerBrushAction: true, radius: 7);
        c.Path.Should().Be(PowerDelegatePath.ClickPowerBrushAction);
        c.BrushRadius.Should().Be(7);
    }

    [Fact]
    public void Radius_selects_id_brush_delegate_when_power_brush_absent()
    {
        var c = Select(clickAction: true, clickBrushAction: true, radius: 12);
        c.Path.Should().Be(PowerDelegatePath.ClickBrushAction);
        c.BrushRadius.Should().Be(12);
    }

    [Fact]
    public void Radius_on_power_without_brush_delegate_is_rejected()
    {
        var c = Select(clickAction: true, radius: 5);
        c.Path.Should().Be(PowerDelegatePath.RejectRadiusUnsupported);
        c.BrushRadius.Should().BeNull();
    }

    [Fact]
    public void Radius_on_toggle_only_power_is_rejected()
    {
        Select(toggleAction: true, radius: 5)
            .Path.Should()
            .Be(PowerDelegatePath.RejectRadiusUnsupported);
    }
}
