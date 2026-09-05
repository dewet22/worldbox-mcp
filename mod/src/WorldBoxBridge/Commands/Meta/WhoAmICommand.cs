using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Meta;

/// <summary>Returns this agent's identity, role, faction claim, and permission bits.</summary>
/// <remarks>
/// The first thing every multi-agent client should call after <c>health</c>, it surfaces
/// "who am I in this session" so the agent can adapt its behavior (e.g., a FactionPlayer
/// learning it controls kingdom #3).
/// </remarks>
internal sealed class WhoAmICommand : ICommand
{
    public string Name => "whoami";
    public CommandCategory Category => CommandCategory.Meta;
    public string Description =>
        "Returns the current agent's id, role, claimed kingdom (if any), permission flags, "
        + "and the active session scenario preset. Call this after `health` to discover what "
        + "this client is allowed to do in the current session.";
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
        var payload = new
        {
            agent_id = ctx.AgentId,
            role = ctx.Role.ToWireString(),
            claimed_kingdom_id = ctx.ClaimedKingdomId,
            permissions = PermissionList(ctx.Permissions),
            scenario = ctx.ScenarioPreset,
            partial_intel = ctx.PartialIntel,
        };
        return Task.FromResult<object?>(payload);
    }

    private static string[] PermissionList(Permission p)
    {
        var list = new System.Collections.Generic.List<string>();
        if (p.HasFlag(Permission.ReadAll))
            list.Add("read_all");
        if (p.HasFlag(Permission.ReadOwnFaction))
            list.Add("read_own_faction");
        if (p.HasFlag(Permission.ActionGlobal))
            list.Add("action_global");
        if (p.HasFlag(Permission.ActionFaction))
            list.Add("action_faction");
        if (p.HasFlag(Permission.ControlWorld))
            list.Add("control_world");
        if (p.HasFlag(Permission.SendMessage))
            list.Add("send_message");
        if (p.HasFlag(Permission.RecvMessage))
            list.Add("recv_message");
        if (p.HasFlag(Permission.SendBroadcast))
            list.Add("send_broadcast");
        if (p.HasFlag(Permission.AdvanceTime))
            list.Add("advance_time");
        return list.ToArray();
    }
}
