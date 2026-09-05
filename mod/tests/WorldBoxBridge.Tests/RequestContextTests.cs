using FluentAssertions;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Session;
using Xunit;
using SessionState = WorldBoxBridge.Session.Session;

namespace WorldBoxBridge.Tests;

public class RequestContextTests
{
    private static RequestContext Ctx(
        AgentRole role,
        long? kingdomClaim = null,
        bool partialIntel = false,
        string scenario = "sandbox"
    )
    {
        var agent = new Agent(
            id: role.ToString().ToLowerInvariant(),
            token: "tok-" + role,
            role: role,
            claimedKingdomId: kingdomClaim,
            permissions: PermissionDefaults.For(role)
        );
        return new RequestContext(agent, scenario, partialIntel);
    }

    // ─── Has / HasAny ─────────────────────────────────────────────────────

    [Fact]
    public void God_has_every_permission()
    {
        var ctx = Ctx(AgentRole.God);
        ctx.Has(Permission.ReadAll).Should().BeTrue();
        ctx.Has(Permission.ActionGlobal).Should().BeTrue();
        ctx.Has(Permission.ControlWorld).Should().BeTrue();
        ctx.Has(Permission.SendBroadcast).Should().BeTrue();
    }

    [Fact]
    public void FactionPlayer_lacks_god_only_permissions()
    {
        var ctx = Ctx(AgentRole.FactionPlayer);
        ctx.Has(Permission.ActionGlobal).Should().BeFalse();
        ctx.Has(Permission.ControlWorld).Should().BeFalse();
        ctx.Has(Permission.ReadAll).Should().BeFalse();
        ctx.Has(Permission.ActionFaction).Should().BeTrue();
        ctx.Has(Permission.SendMessage).Should().BeTrue();
    }

    [Fact]
    public void HasAny_returns_true_when_at_least_one_matches()
    {
        var ctx = Ctx(AgentRole.FactionPlayer);
        ctx.HasAny(Permission.ControlWorld, Permission.ActionFaction).Should().BeTrue();
        ctx.HasAny(Permission.ControlWorld, Permission.ReadAll).Should().BeFalse();
    }

    // ─── Require / RequireAny ─────────────────────────────────────────────

    [Fact]
    public void Require_passes_when_permission_present()
    {
        var ctx = Ctx(AgentRole.God);
        FluentActions.Invoking(() => ctx.Require(Permission.ControlWorld)).Should().NotThrow();
    }

    [Fact]
    public void Require_throws_PERMISSION_DENIED_when_missing()
    {
        var ctx = Ctx(AgentRole.Observer);
        FluentActions
            .Invoking(() => ctx.Require(Permission.ActionGlobal))
            .Should()
            .Throw<BridgeRejectionException>()
            .Where(ex => ex.Code == ErrorCode.PermissionDenied)
            .WithMessage("*role=Observer*ActionGlobal*");
    }

    [Fact]
    public void RequireAny_passes_when_at_least_one_held()
    {
        var ctx = Ctx(AgentRole.FactionPlayer);
        FluentActions
            .Invoking(() => ctx.RequireAny(Permission.ActionGlobal, Permission.ActionFaction))
            .Should()
            .NotThrow();
    }

    [Fact]
    public void RequireAny_throws_when_none_held()
    {
        var ctx = Ctx(AgentRole.Observer);
        FluentActions
            .Invoking(() => ctx.RequireAny(Permission.ActionGlobal, Permission.ActionFaction))
            .Should()
            .Throw<BridgeRejectionException>()
            .Where(ex => ex.Code == ErrorCode.PermissionDenied);
    }

    // ─── CanSeeKingdom (fog-of-war filter for Read commands) ──────────────

    // Matrix encodes intent per row in the test name; no extra "why" param needed (xUnit1026).
    // xUnit InlineData treats null literals as object?, so we use a sentinel constant for nulls.
    private const long NullClaim = long.MinValue; // not a real kingdom id, marker for "no claim"

    [Theory]
    [InlineData(AgentRole.God, NullClaim, true, 3L, true)] // god always sees all (ReadAll)
    [InlineData(AgentRole.Observer, NullClaim, true, 3L, true)] // observer always sees all (ReadAll)
    [InlineData(AgentRole.FactionPlayer, 5L, false, 3L, true)] // no partial_intel → see all
    [InlineData(AgentRole.FactionPlayer, NullClaim, true, 3L, true)] // unclaimed factionplayer → see all
    [InlineData(AgentRole.FactionPlayer, 5L, true, 5L, true)] // claimed factionplayer sees own kingdom
    [InlineData(AgentRole.FactionPlayer, 5L, true, 3L, false)] // claimed factionplayer hides others
    [InlineData(AgentRole.Narrator, NullClaim, true, 3L, true)] // narrator sees all (ReadAll)
    public void CanSeeKingdom_matrix(
        AgentRole role,
        long claim,
        bool partialIntel,
        long target,
        bool expected
    )
    {
        long? actualClaim = claim == NullClaim ? null : claim;
        var ctx = Ctx(role, actualClaim, partialIntel);
        ctx.CanSeeKingdom(target).Should().Be(expected);
    }

    // ─── RequireKingdomAccess (faction binding for Action commands) ───────

    [Fact]
    public void RequireKingdomAccess_god_can_touch_anything()
    {
        var ctx = Ctx(AgentRole.God);
        FluentActions.Invoking(() => ctx.RequireKingdomAccess(42)).Should().NotThrow();
    }

    [Fact]
    public void RequireKingdomAccess_unbound_factionplayer_can_touch_anything()
    {
        // "auto:N" not yet resolved → unbound → permissive (will be tightened in Phase 6 once resolution is wired)
        var ctx = Ctx(AgentRole.FactionPlayer, kingdomClaim: null);
        FluentActions.Invoking(() => ctx.RequireKingdomAccess(42)).Should().NotThrow();
    }

    [Fact]
    public void RequireKingdomAccess_factionplayer_can_touch_own_kingdom()
    {
        var ctx = Ctx(AgentRole.FactionPlayer, kingdomClaim: 7);
        FluentActions.Invoking(() => ctx.RequireKingdomAccess(7)).Should().NotThrow();
    }

    [Fact]
    public void RequireKingdomAccess_factionplayer_cannot_touch_foreign_kingdom()
    {
        var ctx = Ctx(AgentRole.FactionPlayer, kingdomClaim: 7);
        FluentActions
            .Invoking(() => ctx.RequireKingdomAccess(3))
            .Should()
            .Throw<BridgeRejectionException>()
            .Where(ex => ex.Code == ErrorCode.FactionScopeViolation)
            .WithMessage("*kingdom=7*kingdom 3*");
    }

    [Fact]
    public void RequireKingdomAccess_observer_without_ActionGlobal_cannot_act_on_foreign_kingdom()
    {
        // Observer has no claim and no ActionGlobal, the throwsite hits.
        // (Observer can't act at all due to a separate Require(Permission.ActionFaction) gate
        // in the command itself, but this test isolates RequireKingdomAccess's own logic.)
        var ctx = Ctx(AgentRole.Observer, kingdomClaim: 7);
        FluentActions
            .Invoking(() => ctx.RequireKingdomAccess(3))
            .Should()
            .Throw<BridgeRejectionException>()
            .Where(ex => ex.Code == ErrorCode.FactionScopeViolation);
    }

    // ─── Legacy() factory ────────────────────────────────────────────────

    [Fact]
    public void Legacy_returns_god_context_with_sandbox_scenario()
    {
        var ctx = RequestContext.Legacy("test-token");
        ctx.AgentId.Should().Be("legacy");
        ctx.Role.Should().Be(AgentRole.God);
        ctx.Permissions.Should().Be(Permission.God);
        ctx.ScenarioPreset.Should().Be("sandbox");
        ctx.PartialIntel.Should().BeFalse();
        ctx.ClaimedKingdomId.Should().BeNull();
    }
}

public class SessionTests
{
    [Fact]
    public void ContextFor_propagates_session_flags_into_context()
    {
        var registry = AgentRegistry.Legacy("tok");
        var session = new SessionState(
            agents: registry,
            scenarioPreset: "pvp",
            partialIntel: true,
            turnBased: true,
            turnOrder: new TurnOrder(new[] { "legacy" })
        );
        var ctx = session.ContextFor(registry.All[0]);

        ctx.ScenarioPreset.Should().Be("pvp");
        ctx.PartialIntel.Should().BeTrue();
        ctx.AgentId.Should().Be("legacy");
    }

    [Fact]
    public void Legacy_session_uses_sandbox_with_fog_off()
    {
        var session = SessionState.Legacy("abc");
        session.ScenarioPreset.Should().Be("sandbox");
        session.PartialIntel.Should().BeFalse();
        session.TurnBased.Should().BeFalse();
        session.Agents.IsLegacyMode.Should().BeTrue();
    }
}
