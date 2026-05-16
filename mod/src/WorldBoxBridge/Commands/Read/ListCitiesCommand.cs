using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;

using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Read;

/// <summary>Lists every city alive — optionally filtered by kingdom id.</summary>
internal sealed class ListCitiesCommand : ICommand
{
    private readonly WorldAccess _world;

    public ListCitiesCommand(WorldAccess world) => _world = world;

    public string Name => "list_cities";
    public CommandCategory Category => CommandCategory.Read;
    public string Description =>
        "Returns every alive city: id, name, kingdom_id, leader name, building count, "
        + "population. Optionally filter by kingdom_id.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty(
                        "kingdom_id",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty(
                                "description",
                                "If set, return only cities belonging to this kingdom id."
                            )
                        )
                    )
                )
            ),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken cancellationToken)
    {
        ctx.Require(Permission.ReadOwnFaction);
        var filterKingdomId = args.Value<long?>("kingdom_id");

        var manager = _world.CitiesManager;
        if (manager == null)
        {
            return Task.FromResult<object?>(new { items = Array.Empty<object>(), count = 0 });
        }
        var raw = _world.GetSimpleList(manager);
        var items = new List<object>();
        if (raw != null)
        {
            foreach (var city in raw)
            {
                if (city == null)
                {
                    continue;
                }
                var rektMi = _world.CachedMethod(city.GetType(), "isRekt");
                var rekt = rektMi?.Invoke(city, Array.Empty<object>()) as bool? ?? false;
                if (rekt)
                {
                    continue;
                }
                var kingdom = _world.Read(city, "kingdom");
                var kid = kingdom != null ? _world.Read(kingdom, "id") as long? : null;
                // Fog-of-war: hide cities of kingdoms this agent can't see (PvP scoping).
                if (kid.HasValue && !ctx.CanSeeKingdom(kid.Value))
                {
                    continue;
                }
                if (filterKingdomId.HasValue && kid != filterKingdomId)
                {
                    continue;
                }
                items.Add(Project(city, kingdom, kid));
            }
        }
        return Task.FromResult<object?>(new { items, count = items.Count });
    }

    private object Project(object city, object? kingdom, long? kingdomId)
    {
        var leader = _world.Read(city, "leader");
        var buildings = _world.Read(city, "buildings") as System.Collections.ICollection;
        var units = _world.Read(city, "units") as System.Collections.ICollection;
        return new
        {
            id = _world.Read(city, "id"),
            name = _world.Read(city, "name"),
            kingdom_id = kingdomId,
            kingdom_name = kingdom != null ? _world.Read(kingdom, "name") as string : null,
            leader_name = leader != null
                ? _world.CachedMethod(leader.GetType(), "getName")?.Invoke(leader, Array.Empty<object>())
                    as string
                : null,
            building_count = buildings?.Count ?? 0,
            unit_count = units?.Count ?? 0,
        };
    }
}
