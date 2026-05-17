using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Read;

/// <summary>
/// Filters every live <c>Actor</c> in the simulation by race / kingdom / rect / alive
/// status, with pagination.
/// </summary>
internal sealed class QueryActorsCommand : ICommand
{
    private readonly WorldAccess _world;

    private const int DefaultLimit = 500;
    private const int MaxLimit = 5000;

    public QueryActorsCommand(WorldAccess world) => _world = world;

    public string Name => "query_actors";
    public CommandCategory Category => CommandCategory.Read;
    public string Description =>
        "Walks every Actor in the simulation, filters by race / kingdom_id / bounding rect / "
        + "alive, returns up to `limit` matches with pagination via `offset`. Use this to count "
        + "or sample populations without dumping the whole world.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty("race", new JObject(new JProperty("type", "string"))),
                    new JProperty("kingdom_id", new JObject(new JProperty("type", "integer"))),
                    new JProperty(
                        "in_rect",
                        new JObject(
                            new JProperty("type", "object"),
                            new JProperty(
                                "properties",
                                new JObject(
                                    new JProperty(
                                        "x",
                                        new JObject(new JProperty("type", "integer"))
                                    ),
                                    new JProperty(
                                        "y",
                                        new JObject(new JProperty("type", "integer"))
                                    ),
                                    new JProperty(
                                        "w",
                                        new JObject(new JProperty("type", "integer"))
                                    ),
                                    new JProperty(
                                        "h",
                                        new JObject(new JProperty("type", "integer"))
                                    )
                                )
                            ),
                            new JProperty("required", new JArray("x", "y", "w", "h"))
                        )
                    ),
                    new JProperty(
                        "alive",
                        new JObject(
                            new JProperty("type", "boolean"),
                            new JProperty("default", true)
                        )
                    ),
                    new JProperty(
                        "limit",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty("default", DefaultLimit),
                            new JProperty("maximum", MaxLimit)
                        )
                    ),
                    new JProperty(
                        "offset",
                        new JObject(new JProperty("type", "integer"), new JProperty("default", 0))
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
        var raceFilter = args.Value<string?>("race");
        var kingdomFilter = args.Value<long?>("kingdom_id");
        var aliveOnly = args.Value<bool?>("alive") ?? true;
        var limit = Math.Min(args.Value<int?>("limit") ?? DefaultLimit, MaxLimit);
        var offset = Math.Max(args.Value<int?>("offset") ?? 0, 0);

        int? rectX = null,
            rectY = null,
            rectW = null,
            rectH = null;
        if (args["in_rect"] is JObject rect)
        {
            rectX = rect.Value<int?>("x");
            rectY = rect.Value<int?>("y");
            rectW = rect.Value<int?>("w");
            rectH = rect.Value<int?>("h");
        }

        var manager = _world.UnitsManager;
        if (manager == null)
        {
            return Task.FromResult<object?>(
                new
                {
                    items = Array.Empty<object>(),
                    matched = 0,
                    returned = 0,
                    total_scanned = 0,
                }
            );
        }
        var raw = _world.GetSimpleList(manager);
        if (raw == null)
        {
            return Task.FromResult<object?>(
                new
                {
                    items = Array.Empty<object>(),
                    matched = 0,
                    returned = 0,
                    total_scanned = 0,
                }
            );
        }

        var matched = 0;
        var scanned = 0;
        var skipped = 0;
        var items = new List<object>();
        foreach (var actor in raw)
        {
            scanned++;
            if (actor == null)
            {
                continue;
            }

            if (aliveOnly)
            {
                var alive =
                    _world
                        .CachedMethod(actor.GetType(), "isAlive")
                        ?.Invoke(actor, Array.Empty<object>()) as bool?;
                if (alive == false)
                {
                    continue;
                }
            }

            var assetObj = _world.Read(actor, "asset");
            var assetId = assetObj != null ? _world.Read(assetObj, "id") as string : null;
            if (raceFilter != null && !string.Equals(assetId, raceFilter, StringComparison.Ordinal))
            {
                continue;
            }

            var kingdom = _world.Read(actor, "kingdom");
            var kid = kingdom != null ? _world.Read(kingdom, "id") as long? : null;
            // Fog-of-war: hide actors of kingdoms this agent can't see (PvP scoping).
            if (kid.HasValue && !ctx.CanSeeKingdom(kid.Value))
            {
                continue;
            }
            if (kingdomFilter.HasValue && kid != kingdomFilter)
            {
                continue;
            }

            var tile = _world.Read(actor, "current_tile");
            int? tx = null,
                ty = null;
            if (tile != null)
            {
                tx = _world.Read(tile, "x") as int?;
                ty = _world.Read(tile, "y") as int?;
            }
            if (rectX.HasValue && rectY.HasValue && rectW.HasValue && rectH.HasValue)
            {
                if (
                    tx is null
                    || ty is null
                    || tx < rectX
                    || ty < rectY
                    || tx >= rectX + rectW
                    || ty >= rectY + rectH
                )
                {
                    continue;
                }
            }

            matched++;
            if (matched <= offset)
            {
                skipped++;
                continue;
            }
            if (items.Count >= limit)
            {
                continue;
            }

            items.Add(
                new
                {
                    name = _world
                        .CachedMethod(actor.GetType(), "getName")
                        ?.Invoke(actor, Array.Empty<object>()) as string,
                    asset_id = assetId,
                    kingdom_id = kid,
                    x = tx,
                    y = ty,
                }
            );
        }

        return Task.FromResult<object?>(
            new
            {
                items,
                matched,
                returned = items.Count,
                offset,
                limit,
                total_scanned = scanned,
                has_more = matched > offset + items.Count,
            }
        );
    }
}
