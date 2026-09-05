using FluentAssertions;
using WorldBoxBridge.Commands;
using Xunit;

namespace WorldBoxBridge.Tests;

public class TurnGateTests
{
    [Theory]
    [InlineData("paint_tile", CommandCategory.Action)]
    [InlineData("spawn", CommandCategory.Action)]
    [InlineData("invoke_power", CommandCategory.Action)]
    [InlineData("pause", CommandCategory.Control)]
    [InlineData("set_speed", CommandCategory.Control)]
    [InlineData("generate_world", CommandCategory.Control)]
    [InlineData("save_world", CommandCategory.Control)]
    [InlineData("load_world", CommandCategory.Control)]
    public void Action_and_control_commands_are_reserved_for_the_current_agent(
        string name,
        CommandCategory category
    )
    {
        TurnGate.IsTurnGated(name, category).Should().BeTrue();
    }

    [Theory]
    [InlineData("health", CommandCategory.Meta)]
    [InlineData("turn_advance", CommandCategory.Meta)]
    [InlineData("list_powers", CommandCategory.Discovery)]
    [InlineData("get_ui_state", CommandCategory.Read)]
    [InlineData("screenshot", CommandCategory.Read)]
    [InlineData("send_message", CommandCategory.Bus)]
    [InlineData("recv_messages", CommandCategory.Bus)]
    public void Meta_discovery_read_and_bus_commands_stay_open(
        string name,
        CommandCategory category
    )
    {
        TurnGate.IsTurnGated(name, category).Should().BeFalse();
    }

    [Fact]
    public void Dismiss_window_stays_open_although_it_is_a_control_command()
    {
        // An open window freezes the simulation for everyone, so clearing it is a shared
        // unblock rather than a move. Permission.AdvanceTime still keeps Observer and
        // Narrator out.
        TurnGate.IsTurnGated("dismiss_window", CommandCategory.Control).Should().BeFalse();
    }

    [Fact]
    public void Exemption_is_by_registered_name_not_by_category()
    {
        // pause shares dismiss_window's category and permission but resumes the clock,
        // which is a move. It stays gated.
        TurnGate.IsTurnGated("pause", CommandCategory.Control).Should().BeTrue();
    }

    [Theory]
    [InlineData("Dismiss_Window")]
    [InlineData("dismissWindow")]
    [InlineData(" dismiss_window")]
    public void Exemption_matches_the_registered_spelling_exactly(string name)
    {
        // CommandRegistry resolves names with StringComparer.Ordinal, so anything that would
        // not resolve to the real command must not slip through the gate either.
        TurnGate.IsTurnGated(name, CommandCategory.Control).Should().BeTrue();
    }
}
