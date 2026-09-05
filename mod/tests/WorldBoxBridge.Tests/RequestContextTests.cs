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

    // ─── Has / HasAnyOf ───────────────────────────────────────────────────

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
    public void HasAnyOf_returns_true_when_at_least_one_flag_matches()
    {
        var ctx = Ctx(AgentRole.FactionPlayer);
        ctx.HasAnyOf(Permission.ControlWorld | Permission.ActionFaction).Should().BeTrue();
        ctx.HasAnyOf(Permission.ControlWorld | Permission.ReadAll).Should().BeFalse();
    }

    [Fact]
    public void HasAnyOf_differs_from_Has_which_demands_every_flag()
    {
        var ctx = Ctx(AgentRole.FactionPlayer);
        var mask = Permission.ActionFaction | Permission.ActionGlobal;
        ctx.HasAnyOf(mask).Should().BeTrue();
        ctx.Has(mask).Should().BeFalse();
    }

    // ─── Require / RequireAnyOf ───────────────────────────────────────────

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
    public void RequireAnyOf_passes_when_at_least_one_held()
    {
        var ctx = Ctx(AgentRole.FactionPlayer);
        FluentActions
            .Invoking(() => ctx.RequireAnyOf(Permission.ActionGlobal | Permission.ActionFaction))
            .Should()
            .NotThrow();
    }

    [Fact]
    public void RequireAnyOf_throws_when_none_held()
    {
        var ctx = Ctx(AgentRole.Observer);
        FluentActions
            .Invoking(() => ctx.RequireAnyOf(Permission.ActionGlobal | Permission.ActionFaction))
            .Should()
            .Throw<BridgeRejectionException>()
            .Where(ex => ex.Code == ErrorCode.PermissionDenied);
    }

    // ─── Action gates (ActionPermissions) ─────────────────────────────────
    //
    // The three Action commands cannot be linked into this project (Unity types), so these
    // lock the policy they read instead. A FactionPlayer must not reach invoke_power or
    // paint_tile, and must keep spawn.

    [Theory]
    [InlineData(AgentRole.God, true)]
    [InlineData(AgentRole.FactionPlayer, false)]
    [InlineData(AgentRole.Observer, false)]
    [InlineData(AgentRole.Narrator, false)]
    public void InvokePower_and_PaintTile_are_god_only(AgentRole role, bool allowed)
    {
        var ctx = Ctx(role);
        ctx.Has(ActionPermissions.InvokePower).Should().Be(allowed);
        ctx.Has(ActionPermissions.PaintTile).Should().Be(allowed);
    }

    [Theory]
    [InlineData(AgentRole.God, true)]
    [InlineData(AgentRole.FactionPlayer, true)]
    [InlineData(AgentRole.Observer, false)]
    [InlineData(AgentRole.Narrator, false)]
    public void Spawn_stays_open_to_faction_players(AgentRole role, bool allowed)
    {
        Ctx(role).HasAnyOf(ActionPermissions.Spawn).Should().Be(allowed);
    }

    [Fact]
    public void InvokePower_matches_PaintTile_so_the_two_cannot_drift_apart()
    {
        ActionPermissions.InvokePower.Should().Be(ActionPermissions.PaintTile);
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
