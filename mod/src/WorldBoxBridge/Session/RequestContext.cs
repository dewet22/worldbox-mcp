using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;

namespace WorldBoxBridge.Session;

/// <summary>
/// Per-request identity + scope. Threaded into <see cref="WorldBoxBridge.Commands.ICommand.ExecuteAsync"/>
/// so every command knows which agent is calling and what they're allowed to touch.
/// </summary>
/// <remarks>
/// A readonly struct rather than a class: avoids a heap allocation per call and dodges any
/// chance of accidentally mutating it mid-command. Mono on net462 doesn't always load
/// <c>System.ValueTuple</c>, but plain user-defined readonly structs are fine.
/// </remarks>
public readonly struct RequestContext
{
    public RequestContext(Agent agent, string scenarioPreset, bool partialIntel)
    {
        Agent = agent;
        ScenarioPreset = scenarioPreset;
        PartialIntel = partialIntel;
    }

    public Agent Agent { get; }
    public string ScenarioPreset { get; }
    public bool PartialIntel { get; }

    public string AgentId => Agent.Id;
    public AgentRole Role => Agent.Role;
    public long? ClaimedKingdomId => Agent.ClaimedKingdomId;
    public Permission Permissions => Agent.Permissions;

    public bool Has(Permission p) => (Permissions & p) == p;

    public void Require(Permission p)
    {
        if (!Has(p))
        {
            throw new BridgeRejectionException(
                ErrorCode.PermissionDenied,
                $"Agent '{AgentId}' (role={Role}) lacks permission {p}. Required for this command."
            );
        }
    }

    /// <summary>
    /// Returns true if <em>any</em> flag in <paramref name="mask"/> is held, where
    /// <see cref="Has"/> demands all of them. Useful for OR-gates such as
    /// <see cref="WorldBoxBridge.Commands.Action.ActionPermissions.Spawn"/>.
    /// </summary>
    /// <remarks>
    /// A single flags mask rather than a <c>params Permission[]</c>: the array allocated on
    /// every action call, and callers had no way to name the alternatives as one value.
    /// </remarks>
    public bool HasAnyOf(Permission mask) => (Permissions & mask) != Permission.None;

    /// <summary>Throws <c>PERMISSION_DENIED</c> unless <see cref="HasAnyOf"/> holds.</summary>
    public void RequireAnyOf(Permission mask)
    {
        if (!HasAnyOf(mask))
        {
            throw new BridgeRejectionException(
                ErrorCode.PermissionDenied,
                $"Agent '{AgentId}' (role={Role}) lacks any of: {mask}."
            );
        }
    }

    /// <summary>
    /// Whether this agent should see information about <paramref name="kingdomId"/> in a
    /// read result. Gods/observers (ReadAll) always see all; with fog-of-war off everyone
    /// sees all; bound factionplayers only see their own kingdom.
    /// </summary>
    public bool CanSeeKingdom(long kingdomId)
    {
        if (Has(Permission.ReadAll))
            return true;
        if (!PartialIntel)
            return true;
        if (!ClaimedKingdomId.HasValue)
            return true;
        return ClaimedKingdomId.Value == kingdomId;
    }

    /// <summary>
    /// Validates that the agent is allowed to *act* on the given kingdom. Stricter than
    /// <see cref="CanSeeKingdom"/>: ignores partial_intel (claim is enforced even with
    /// fog-of-war off, since it scopes *who can act* not just *who can see*).
    /// </summary>
    public void RequireKingdomAccess(long kingdomId)
    {
        if (Has(Permission.ActionGlobal))
            return;
        if (!ClaimedKingdomId.HasValue)
            return; // unbound (auto:N not yet resolved), allow
        if (ClaimedKingdomId.Value == kingdomId)
            return;
        throw new BridgeRejectionException(
            ErrorCode.FactionScopeViolation,
            $"Agent '{AgentId}' (kingdom={ClaimedKingdomId}) cannot affect kingdom {kingdomId}."
        );
    }

    /// <summary>
    /// Bridges into the existing legacy single-token deployment as the "legacy" God agent.
    /// Returned when no agents.toml is present and the request authenticated against the
    /// legacy <c>X-WB-Token</c> / <c>Authorization: Bearer</c> single secret.
    /// </summary>
    public static RequestContext Legacy(string token) =>
        new(
            new Agent(
                id: "legacy",
                token: token,
                role: AgentRole.God,
                claimedKingdomId: null,
                permissions: Permission.God
            ),
            scenarioPreset: "sandbox",
            partialIntel: false
        );
}
