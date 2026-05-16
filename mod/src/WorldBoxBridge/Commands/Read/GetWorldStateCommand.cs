using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Threading;

namespace WorldBoxBridge.Commands.Read;

/// <summary>
/// Snapshot of the world's overall state — dimensions, current seed, tick, paused flag,
/// population, kingdom + city counts. The first read tool an agent should call after
/// <c>worldbox_health</c>.
/// </summary>
internal sealed class GetWorldStateCommand : ICommand
{
    private readonly WorldAccess _world;

    public GetWorldStateCommand(WorldAccess world) => _world = world;

    public string Name => "get_world_state";
    public CommandCategory Category => CommandCategory.Read;
    public string Description =>
        "Returns the world snapshot: dimensions, seed, tick, paused flag, total population, "
        + "number of kingdoms, number of cities. Use this to size other queries (e.g. so the "
        + "agent knows the map bounds before calling paint_tile) and to detect whether the "
        + "simulation is currently running.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty("properties", new JObject()),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, CancellationToken cancellationToken)
    {
        var width = _world.Width ?? 0;
        var height = _world.Height ?? 0;
        var seed = _world.WorldSeed ?? 0;
        var paused = _world.IsPaused ?? false;

        var units = _world.UnitsManager;
        var kingdoms = _world.KingdomsManager;
        var cities = _world.CitiesManager;
        var population = units != null ? CountList(units) : 0;
        var nbKingdoms = kingdoms != null ? CountList(kingdoms) : 0;
        var nbCities = cities != null ? CountList(cities) : 0;

        long mapStatsPopulation = 0;
        long kingdomsCreated = 0;
        long citiesCreated = 0;
        var mapStats = _world.MapStats;
        if (mapStats != null)
        {
            if (_world.Read(mapStats, "population") is long p)
            {
                mapStatsPopulation = p;
            }
            if (_world.Read(mapStats, "kingdomsCreated") is long kc)
            {
                kingdomsCreated = kc;
            }
            if (_world.Read(mapStats, "citiesCreated") is long cc)
            {
                citiesCreated = cc;
            }
        }

        return Task.FromResult<object?>(
            new
            {
                width,
                height,
                seed,
                tick = MainThreadDispatcher.LastTick,
                paused,
                population_alive = population,
                population_lifetime = mapStatsPopulation,
                kingdoms_alive = nbKingdoms,
                kingdoms_ever_created = kingdomsCreated,
                cities_alive = nbCities,
                cities_ever_created = citiesCreated,
            }
        );
    }

    private int CountList(object manager)
    {
        var list = _world.GetSimpleList(manager);
        return list?.Count ?? 0;
    }
}
