using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Read;

/// <summary>Lists every kingdom currently alive, with race / king / capital / pop summary.</summary>
internal sealed class ListKingdomsCommand : ICommand
{
    private readonly WorldAccess _world;

    public ListKingdomsCommand(WorldAccess world) => _world = world;

    public string Name => "list_kingdoms";
    public CommandCategory Category => CommandCategory.Read;
    public string Description =>
        "Returns every alive kingdom: id, name, race (asset_id), king name, capital city id, "
        + "population (alive units), color, and wild/civilization flag.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty(
                        "include_wild",
                        new JObject(
                            new JProperty("type", "boolean"),
                            new JProperty("default", false),
                            new JProperty(
                                "description",
                                "If true, include wild kingdoms (animal packs, sea monsters). "
                                    + "Default is false — most agents only care about civilizations."
                            )
                        )
                    )
                )
            ),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
    {
        ctx.Require(Permission.ReadOwnFaction);
        // Intentional: kingdoms list is NEVER fog-of-war filtered. Agents need to know
        // what factions exist for diplomacy / threat awareness, even in PvP. Per-faction
        // city + unit details *are* scoped (see list_cities, query_actors).

        var includeWild = args.Value<bool?>("include_wild") ?? false;

        var manager = _world.KingdomsManager;
        if (manager == null)
        {
            return Task.FromResult<object?>(new { items = Array.Empty<object>(), count = 0 });
        }
        var raw = _world.GetSimpleList(manager);
        var items = new List<object>();
        if (raw != null)
        {
            foreach (var kingdom in raw)
            {
                if (kingdom == null)
                {
                    continue;
                }
                var alive =
                    _world
                        .CachedMethod(kingdom.GetType(), "isAlive")
                        ?.Invoke(kingdom, Array.Empty<object>()) as bool?;
                if (alive == false)
                {
                    continue;
                }
                var wild = _world.Read(kingdom, "wild") as bool? ?? false;
                if (wild && !includeWild)
                {
                    continue;
                }
                items.Add(Project(kingdom));
            }
        }
        return Task.FromResult<object?>(new { items, count = items.Count });
    }

    private object Project(object kingdom)
    {
        var asset = _world.Read(kingdom, "asset");
        var king = _world.Read(kingdom, "king");
        var capital = _world.Read(kingdom, "capital");
        var cities = _world.Read(kingdom, "cities") as System.Collections.ICollection;
        var units = _world.Read(kingdom, "units") as System.Collections.ICollection;
        return new
        {
            id = _world.Read(kingdom, "id"),
            name = _world.Read(kingdom, "name"),
            race = asset != null ? _world.Read(asset, "id") : null,
            king_name = king != null
                ? _world
                    .CachedMethod(king.GetType(), "getName")
                    ?.Invoke(king, Array.Empty<object>()) as string
                : null,
            capital_id = capital != null ? _world.Read(capital, "id") : null,
            cities_count = cities?.Count ?? 0,
            units_count = units?.Count ?? 0,
            wild = _world.Read(kingdom, "wild") as bool? ?? false,
        };
    }
}
