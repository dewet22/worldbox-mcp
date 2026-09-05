using System;
using FluentAssertions;
using WorldBoxBridge.Commands.Action;
using Xunit;

namespace WorldBoxBridge.Tests;

public class PowerDelegateFieldsTests
{
    private delegate bool FakeClick(object tile, string id);

#pragma warning disable CS0649 // fields assigned via object initialisers below
    private sealed class FullPower
    {
        public FakeClick? click_action;
        public FakeClick? click_power_action;
        public FakeClick? click_brush_action;
        public FakeClick? click_power_brush_action;
        public Action<string>? toggle_action;
    }

    private sealed class ToggleOnlyPower
    {
        public Action<string>? toggle_action;
    }

    private sealed class NoFieldsPower
    {
        public int unrelated;
    }

    private sealed class WrongTypedPower
    {
        public string? click_action; // right name, not a delegate
    }
#pragma warning restore CS0649

    private static bool Click(object tile, string id) => true;

    [Fact]
    public void Reads_set_delegates_and_null_for_unset_ones()
    {
        var fields = new PowerDelegateFields();
        var power = new FullPower { click_action = Click, toggle_action = _ => { } };

        var snap = fields.Read(power);

        snap.ClickAction.Should().NotBeNull();
        snap.ToggleAction.Should().NotBeNull();
        snap.ClickPowerAction.Should().BeNull();
        snap.ClickBrushAction.Should().BeNull();
        snap.ClickPowerBrushAction.Should().BeNull();
    }

    [Fact]
    public void Resolves_fields_per_concrete_type_not_per_first_asset_seen()
    {
        // The regression this class exists to prevent: caching FieldInfos from the first
        // asset's type and reusing them against instances of a different type.
        var fields = new PowerDelegateFields();
        var full = new FullPower { click_action = Click };
        var toggleOnly = new ToggleOnlyPower { toggle_action = _ => { } };

        fields.Read(full).ClickAction.Should().NotBeNull();
        var snap = fields.Read(toggleOnly);
        snap.ToggleAction.Should().NotBeNull();
        snap.ClickAction.Should().BeNull();
    }

    [Fact]
    public void Any_field_present_reflects_the_type_shape()
    {
        var fields = new PowerDelegateFields();
        fields.AnyFieldPresent(new ToggleOnlyPower()).Should().BeTrue();
        fields.AnyFieldPresent(new NoFieldsPower()).Should().BeFalse();
    }

    [Fact]
    public void A_field_with_the_right_name_but_wrong_type_reads_as_null_not_a_throw()
    {
        var fields = new PowerDelegateFields();
        var power = new WrongTypedPower { click_action = "not a delegate" };

        var act = () => fields.Read(power);

        act.Should().NotThrow();
        fields.Read(power).ClickAction.Should().BeNull();
    }
}
