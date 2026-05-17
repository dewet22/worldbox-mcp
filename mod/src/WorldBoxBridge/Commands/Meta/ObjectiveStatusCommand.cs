using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;
using SessionState = WorldBoxBridge.Session.Session;

namespace WorldBoxBridge.Commands.Meta;

/// <summary>
/// Returns each agent's declared objectives alongside live kingdom-population metrics. The
/// agent client computes its own score from this — see <see cref="Objective"/> for the
/// design rationale (objectives are intentionally not evaluated server-side).
/// </summary>
internal sealed class ObjectiveStatusCommand : ICommand
{
    private readonly SessionState _session;
    private readonly WorldAccess _world;

    public ObjectiveStatusCommand(SessionState session, WorldAccess world)
    {
        _session = session;
        _world = world;
    }

    public string Name => "objective_status";
    public CommandCategory Category => CommandCategory.Meta;
    public string Description =>
        "Returns each registered agent's declared objectives (from agents.json) alongside "
        + "live kingdom-population metrics. The client interprets the metrics against its "
        + "objective kinds (wipe_kingdom, maximize_pop, survive, etc.) to compute a score. "
        + "Use this as the scoreboard primitive in PvP scenarios. Read-only.";
    public bool RequiresMainThread => true;

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
        ctx.Require(Permission.ReadOwnFaction);

        var agentObjectives = _session
            .Agents.All.Select(a => new
            {
                agent_id = a.Id,
                role = a.Role.ToWireString(),
                claimed_kingdom_id = a.ClaimedKingdomId,
                objectives = a
                    .Objectives.Items.Select(o => new
                    {
                        id = o.Id,
                        label = o.Label,
                        kind = o.Kind,
                        target = o.Target,
                    })
                    .ToArray(),
            })
            .ToArray();

        // Live kingdom-population snapshot — same units count the game's UI shows in
        // the kingdoms panel. Fog-of-war is intentionally NOT applied here: the metrics
        // surface is opt-in (only declared objectives + their related kingdoms) so PvP
        // scoreboards work even with partial_intel on (otherwise FactionPlayers couldn't
        // compute the very score they're trying to optimize).
        var kingdomMetrics = new List<object>();
        var manager = _world.KingdomsManager;
        if (manager != null)
        {
            var raw = _world.GetSimpleList(manager);
            if (raw != null)
            {
                foreach (var kingdom in raw)
                {
                    if (kingdom is null)
                        continue;
                    var alive =
                        _world
                            .CachedMethod(kingdom.GetType(), "isAlive")
                            ?.Invoke(kingdom, System.Array.Empty<object>()) as bool?;
                    if (alive == false)
                        continue;
                    var units = _world.Read(kingdom, "units") as System.Collections.ICollection;
                    var cities = _world.Read(kingdom, "cities") as System.Collections.ICollection;
                    kingdomMetrics.Add(
                        new
                        {
                            id = _world.Read(kingdom, "id"),
                            name = _world.Read(kingdom, "name"),
                            units = units?.Count ?? 0,
                            cities = cities?.Count ?? 0,
                            wild = _world.Read(kingdom, "wild") as bool? ?? false,
                        }
                    );
                }
            }
        }

        return Task.FromResult<object?>(
            new
            {
                scenario = _session.ScenarioPreset,
                agents = agentObjectives,
                kingdoms = kingdomMetrics,
            }
        );
    }
}
