using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Session;
using SessionState = WorldBoxBridge.Session.Session;

namespace WorldBoxBridge.Commands.Meta;

/// <summary>Returns the active session: scenario, registered agents, turn mode, intel flags.</summary>
internal sealed class SessionInfoCommand : ICommand
{
    private readonly SessionState _session;

    public SessionInfoCommand(SessionState session)
    {
        _session = session;
    }

    public string Name => "session_info";
    public CommandCategory Category => CommandCategory.Meta;
    public string Description =>
        "Returns the live session metadata: scenario preset (pvp / coop / hierarchical / "
        + "sandbox), partial_intel flag, turn_based flag, and the list of all registered "
        + "agents (id, role, claimed kingdom). Tokens are never returned.";
    public bool RequiresMainThread => false;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty("properties", new JObject()),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
    {
        var agents = _session
            .Agents.All.Select(a => new
            {
                id = a.Id,
                role = a.Role.ToWireString(),
                claimed_kingdom_id = a.ClaimedKingdomId,
                last_seen_utc = a.LastSeenUtc == default ? null : a.LastSeenUtc.ToString("o"),
            })
            .ToArray();

        var payload = new
        {
            scenario = _session.ScenarioPreset,
            partial_intel = _session.PartialIntel,
            turn_based = _session.TurnBased,
            legacy_mode = _session.Agents.IsLegacyMode,
            agent_count = _session.Agents.Count,
            agents,
            turn_order = _session.TurnOrder?.AgentIds,
            current_turn = _session.TurnOrder?.Current,
        };
        return Task.FromResult<object?>(payload);
    }
}
