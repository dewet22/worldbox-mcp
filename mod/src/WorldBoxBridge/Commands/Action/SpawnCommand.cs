using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Action;

/// <summary>
/// Spawns one or more actors (creatures, races, animals, monsters, mythicals) at a tile.
/// Covers the ~322 entries in <c>list_actors</c> — including the ones <c>invoke_power</c> can't
/// reach (e.g. <c>dragon_red</c>, <c>cthulhu</c>, <c>kraken</c>).
/// </summary>
/// <remarks>
/// Calls <c>MapBox.instance.units.spawnNewUnit(id, tile, true, false, 6f, null, false, adult)</c>
/// per spawn. The game auto-assigns the actor's wild kingdom from
/// <c>ActorAsset.kingdom_id_wild</c>; no kingdom argument is needed (or accepted).
/// </remarks>
internal sealed class SpawnCommand : ICommand
{
    private readonly AssetCatalog _catalog;
    private readonly WorldAccess _world;
    private readonly ManualLogSource _log;

    private MethodInfo? _spawnNewUnit;
    private MethodInfo? _actorGetName;

    public SpawnCommand(AssetCatalog catalog, WorldAccess world, ManualLogSource log)
    {
        _catalog = catalog;
        _world = world;
        _log = log;
    }

    public string Name => "spawn";
    public CommandCategory Category => CommandCategory.Action;
    public string Description =>
        "Spawns one or more actors of a given asset id at (x, y). The asset id comes from "
        + "list_actors and covers every creature in the game — races, animals, monsters, "
        + "dragons, demons, titans, kraken, cthulhu, …. The game auto-assigns the actor's "
        + "wild kingdom; no kingdom argument is needed. Returns the new actor ids.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty(
                        "entity_id",
                        new JObject(
                            new JProperty("type", "string"),
                            new JProperty(
                                "description",
                                "An id from list_actors (e.g. 'human', 'dragon_red', 'wolf')."
                            )
                        )
                    ),
                    new JProperty("x", new JObject(new JProperty("type", "integer"))),
                    new JProperty("y", new JObject(new JProperty("type", "integer"))),
                    new JProperty(
                        "count",
                        new JObject(
                            new JProperty("type", "integer"),
                            new JProperty("minimum", 1),
                            new JProperty("maximum", 100),
                            new JProperty("default", 1)
                        )
                    ),
                    new JProperty(
                        "adult",
                        new JObject(
                            new JProperty("type", "boolean"),
                            new JProperty("default", false),
                            new JProperty(
                                "description",
                                "If true, the actor spawns as an adult instead of a baby."
                            )
                        )
                    ),
                    new JProperty(
                        "spawn_height",
                        new JObject(
                            new JProperty("type", "number"),
                            new JProperty("default", 6),
                            new JProperty(
                                "description",
                                "Z-height the actor falls from. 0 = appears in place, 6 = dramatic drop."
                            )
                        )
                    )
                )
            ),
            new JProperty("required", new JArray("entity_id", "x", "y")),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
    {
        ctx.RequireAny(Permission.ActionFaction, Permission.ActionGlobal);
        var entityId = (string?)args["entity_id"];
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("entity_id is required and must be a non-empty string.");
        }
        if (!args.TryGetValue("x", out var xT) || !args.TryGetValue("y", out var yT))
        {
            throw new ArgumentException("x and y are required integers.");
        }
        var x = (int)xT!;
        var y = (int)yT!;
        var count = args.Value<int?>("count") ?? 1;
        var adult = args.Value<bool?>("adult") ?? false;
        var spawnHeight = (float)(args.Value<double?>("spawn_height") ?? 6.0);

        if (count < 1)
        {
            throw new ArgumentException("count must be >= 1.");
        }
        if (count > 100)
        {
            throw new ArgumentException(
                "count must be <= 100 (use a loop on the agent side for more)."
            );
        }

        // Pre-check id: gives us did_you_mean on typos before the game silently no-ops.
        if (_catalog.Resolve("actor_library", entityId!) == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.UnknownAsset,
                $"entity_id '{entityId}' is not a registered actor.",
                didYouMean: _catalog.Suggest("actor_library", entityId!)
            );
        }

        var tile = _world.GetTileAt(x, y);
        if (tile == null)
        {
            var dims = _world.GetMapDimensions();
            var range = dims.HasValue
                ? $" (map is {dims.Value.Width}x{dims.Value.Height})"
                : string.Empty;
            throw new BridgeRejectionException(
                ErrorCode.OutOfBounds,
                $"({x},{y}) is outside the map or the world isn't initialised yet{range}."
            );
        }

        var manager = _world.UnitsManager;
        if (manager == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "MapBox.units (ActorManager) not reachable. The game world may not be loaded."
            );
        }

        _spawnNewUnit ??= ResolveSpawnMethod(manager.GetType());
        if (_spawnNewUnit == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "ActorManager.spawnNewUnit(...) not found in this WorldBox build."
            );
        }

        // Signature: Actor spawnNewUnit(string id, WorldTile tile,
        //                                bool spawn_sound, bool miracle,
        //                                float spawn_height, Subspecies sub,
        //                                bool give_ownerless, bool adult)
        var spawnArgs = new object?[]
        {
            entityId,
            tile,
            true, // pSpawnSound — agent-initiated, give it a sound
            false, // pMiracleSpawn
            spawnHeight,
            null, // pSubspecies — leave to default
            false, // pGiveOwnerlessItems
            adult,
        };

        var spawnedIds = new List<object?>();
        var failed = 0;
        for (var i = 0; i < count; i++)
        {
            object? actor;
            try
            {
                actor = _spawnNewUnit.Invoke(manager, spawnArgs);
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException ?? tie;
            }
            if (actor == null)
            {
                failed++;
                continue;
            }
            spawnedIds.Add(GetActorName(actor) ?? "(unnamed)");
        }

        if (spawnedIds.Count == 0)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                $"spawn '{entityId}' at ({x},{y}) — the game refused all {count} attempts. "
                    + "Common cause: actor can't survive this terrain (e.g. land animal on water)."
            );
        }

        return Task.FromResult<object?>(
            new
            {
                entity_id = entityId,
                x,
                y,
                requested = count,
                spawned = spawnedIds.Count,
                failed,
                names = spawnedIds,
            }
        );
    }

    private static MethodInfo? ResolveSpawnMethod(Type managerType)
    {
        // Match the 8-arg overload by parameter count, then verify by names.
        // Walk inheritance just in case the method moves to a base in future builds.
        for (var cur = managerType; cur != null; cur = cur.BaseType)
        {
            foreach (
                var mi in cur.GetMethods(
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly
                )
            )
            {
                if (mi.Name != "spawnNewUnit")
                {
                    continue;
                }
                var ps = mi.GetParameters();
                if (ps.Length >= 7 && ps[0].ParameterType == typeof(string))
                {
                    return mi;
                }
            }
        }
        return null;
    }

    private string? GetActorName(object actor)
    {
        try
        {
            _actorGetName ??= actor
                .GetType()
                .GetMethod("getName", BindingFlags.Public | BindingFlags.Instance);
            return _actorGetName?.Invoke(actor, Array.Empty<object>()) as string;
        }
        catch
        {
            return null;
        }
    }
}
