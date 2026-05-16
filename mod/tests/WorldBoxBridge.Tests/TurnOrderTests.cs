using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using WorldBoxBridge.Session;
using SessionState = WorldBoxBridge.Session.Session;
using Xunit;

namespace WorldBoxBridge.Tests;

public class TurnOrderTests
{
    [Fact]
    public void Constructor_rejects_empty_rotation()
    {
        FluentActions.Invoking(() => new TurnOrder(Array.Empty<string>()))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*no one would ever be allowed to act*");
    }

    [Fact]
    public void Initial_current_is_first_agent()
    {
        var order = new TurnOrder(new[] { "athena", "ares", "narrator" });
        order.Current.Should().Be("athena");
    }

    [Fact]
    public void Advance_cycles_round_robin()
    {
        var order = new TurnOrder(new[] { "a", "b", "c" });

        order.Advance().Should().Be("b");
        order.Advance().Should().Be("c");
        order.Advance().Should().Be("a");
        order.Advance().Should().Be("b");
    }

    [Fact]
    public void IsCurrentlyActive_matches_current_agent_only()
    {
        var order = new TurnOrder(new[] { "athena", "ares" });

        order.IsCurrentlyActive("athena").Should().BeTrue();
        order.IsCurrentlyActive("ares").Should().BeFalse();
        order.Advance();
        order.IsCurrentlyActive("athena").Should().BeFalse();
        order.IsCurrentlyActive("ares").Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_advance_calls_remain_consistent()
    {
        // 4 agents, 1000 advances on each of 4 threads → final position must be 4000 % 4 = 0
        // (back to the original "agent_0" current) and no exception thrown.
        var ids = Enumerable.Range(0, 4).Select(i => $"agent_{i}").ToArray();
        var order = new TurnOrder(ids);

        var threads = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => { for (var i = 0; i < 1000; i++) order.Advance(); }))
            .ToArray();
        await Task.WhenAll(threads);

        // 4000 total advances + initial 0 = 4000 → 4000 % 4 = 0 → back to agent_0
        order.Current.Should().Be("agent_0");
    }

    [Fact]
    public void AgentIds_is_a_stable_snapshot()
    {
        var order = new TurnOrder(new[] { "a", "b" });
        var ids = order.AgentIds;
        ids.Should().Equal(new[] { "a", "b" });
        order.Advance();
        // Advance doesn't reorder — the rotation list is immutable.
        order.AgentIds.Should().Equal(new[] { "a", "b" });
    }
}

public class SessionTurnConfigTests
{
    private static AgentRegistry TwoAgents() =>
        new(new[]
        {
            new Agent("a", "tok_a", AgentRole.FactionPlayer, claimedKingdomId: 1, Permission.FactionPlayer),
            new Agent("b", "tok_b", AgentRole.FactionPlayer, claimedKingdomId: 2, Permission.FactionPlayer),
        });

    [Fact]
    public void TurnBased_session_requires_a_turn_order()
    {
        FluentActions.Invoking(() => new SessionState(
            agents: TwoAgents(),
            scenarioPreset: "pvp",
            partialIntel: true,
            turnBased: true,
            turnOrder: null
        )).Should().Throw<ArgumentException>().WithMessage("*requires a TurnOrder*");
    }

    [Fact]
    public void NonTurnBased_session_does_not_need_a_turn_order()
    {
        FluentActions.Invoking(() => new SessionState(
            agents: TwoAgents(),
            scenarioPreset: "pvp",
            partialIntel: true,
            turnBased: false,
            turnOrder: null
        )).Should().NotThrow();
    }

    [Fact]
    public void TurnBased_session_with_order_exposes_it()
    {
        var rotation = new TurnOrder(new[] { "a", "b" });
        var session = new SessionState(
            agents: TwoAgents(),
            scenarioPreset: "pvp",
            partialIntel: true,
            turnBased: true,
            turnOrder: rotation
        );

        session.TurnOrder.Should().BeSameAs(rotation);
        session.TurnOrder!.Current.Should().Be("a");
    }
}
