using System;
using FluentAssertions;
using WorldBoxBridge.Session;
using Xunit;

namespace WorldBoxBridge.Tests;

public class AgentRegistryTests
{
    [Fact]
    public void Legacy_creates_single_god_agent_with_legacy_id()
    {
        var registry = AgentRegistry.Legacy("secret-token");

        registry.Count.Should().Be(1);
        registry.IsLegacyMode.Should().BeTrue();
        registry.All[0].Id.Should().Be("legacy");
        registry.All[0].Role.Should().Be(AgentRole.God);
        registry.All[0].Permissions.Should().Be(Permission.God);
        registry.All[0].ClaimedKingdomId.Should().BeNull();
    }

    [Fact]
    public void TryAuthenticate_returns_agent_on_correct_token()
    {
        var registry = AgentRegistry.Legacy("abc123");
        var agent = registry.TryAuthenticate("abc123");

        agent.Should().NotBeNull();
        agent!.Id.Should().Be("legacy");
    }

    [Fact]
    public void TryAuthenticate_returns_null_on_wrong_token()
    {
        var registry = AgentRegistry.Legacy("abc123");
        registry.TryAuthenticate("wrong").Should().BeNull();
    }

    [Fact]
    public void TryAuthenticate_returns_null_on_empty_token()
    {
        var registry = AgentRegistry.Legacy("abc123");
        registry.TryAuthenticate(string.Empty).Should().BeNull();
        registry.TryAuthenticate(null).Should().BeNull();
    }

    [Fact]
    public void TryAuthenticate_returns_null_on_token_of_different_length()
    {
        // Same prefix but different length — FixedTimeEquals must short-circuit on length.
        var registry = AgentRegistry.Legacy("abc123");
        registry.TryAuthenticate("abc123extra").Should().BeNull();
    }

    [Fact]
    public void TryAuthenticate_updates_last_seen()
    {
        var registry = AgentRegistry.Legacy("abc123");
        var before = registry.All[0].LastSeenUtc;

        var agent = registry.TryAuthenticate("abc123");

        agent!.LastSeenUtc.Should().BeAfter(before);
    }

    [Fact]
    public void Multi_agent_constructor_routes_per_token()
    {
        var athena = new Agent(
            "athena",
            "tok_a",
            AgentRole.FactionPlayer,
            claimedKingdomId: 1,
            Permission.FactionPlayer
        );
        var ares = new Agent(
            "ares",
            "tok_b",
            AgentRole.FactionPlayer,
            claimedKingdomId: 2,
            Permission.FactionPlayer
        );
        var registry = new AgentRegistry(new[] { athena, ares });

        registry.Count.Should().Be(2);
        registry.IsLegacyMode.Should().BeFalse();
        registry.TryAuthenticate("tok_a")!.Id.Should().Be("athena");
        registry.TryAuthenticate("tok_b")!.Id.Should().Be("ares");
        registry.TryAuthenticate("tok_c").Should().BeNull();
    }

    [Fact]
    public void Zero_agents_is_an_invalid_configuration()
    {
        Action act = () => _ = new AgentRegistry(Array.Empty<Agent>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*reject every request*");
    }

    [Fact]
    public void GetById_returns_matching_agent_or_null()
    {
        var registry = AgentRegistry.Legacy("abc");

        registry.GetById("legacy").Should().NotBeNull();
        registry.GetById("nope").Should().BeNull();
    }
}

public class PermissionDefaultsTests
{
    [Fact]
    public void God_has_every_permission()
    {
        var perms = PermissionDefaults.For(AgentRole.God);
        perms.HasFlag(Permission.ReadAll).Should().BeTrue();
        perms.HasFlag(Permission.ActionGlobal).Should().BeTrue();
        perms.HasFlag(Permission.ControlWorld).Should().BeTrue();
        perms.HasFlag(Permission.SendBroadcast).Should().BeTrue();
        perms.HasFlag(Permission.AdvanceTime).Should().BeTrue();
    }

    [Fact]
    public void FactionPlayer_cannot_control_world_or_action_globally()
    {
        var perms = PermissionDefaults.For(AgentRole.FactionPlayer);
        perms.HasFlag(Permission.ControlWorld).Should().BeFalse();
        perms.HasFlag(Permission.ActionGlobal).Should().BeFalse();
        perms.HasFlag(Permission.ActionFaction).Should().BeTrue();
        perms.HasFlag(Permission.ReadOwnFaction).Should().BeTrue();
        perms
            .HasFlag(Permission.AdvanceTime)
            .Should()
            .BeTrue("FactionPlayers need to fast-forward through quiet phases");
    }

    [Fact]
    public void Spectator_roles_cannot_advance_time()
    {
        // Observer + Narrator are watchers -- they shouldn't be able to skip ahead while
        // the actual players are still deliberating.
        PermissionDefaults
            .For(AgentRole.Observer)
            .HasFlag(Permission.AdvanceTime)
            .Should()
            .BeFalse();
        PermissionDefaults
            .For(AgentRole.Narrator)
            .HasFlag(Permission.AdvanceTime)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Observer_can_read_and_message_but_not_act()
    {
        var perms = PermissionDefaults.For(AgentRole.Observer);
        perms.HasFlag(Permission.ReadAll).Should().BeTrue();
        perms.HasFlag(Permission.ActionFaction).Should().BeFalse();
        perms.HasFlag(Permission.ActionGlobal).Should().BeFalse();
        perms.HasFlag(Permission.SendMessage).Should().BeTrue();
    }

    [Fact]
    public void Narrator_can_broadcast_but_not_act()
    {
        var perms = PermissionDefaults.For(AgentRole.Narrator);
        perms.HasFlag(Permission.SendBroadcast).Should().BeTrue();
        perms.HasFlag(Permission.ActionFaction).Should().BeFalse();
    }
}
