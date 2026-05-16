using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Threading;

using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Discovery;

/// <summary>Lists every actor (race / animal / monster / mythical) registered by the game.</summary>
internal sealed class ListActorsCommand : ICommand
{
    private readonly AssetCatalog _catalog;

    private static readonly string[] ExtraFields =
    {
        "race",
        "subspecies",
        "asset_type",
        "base_health",
        "base_damage",
        "base_speed",
        "water_walking",
    };

    public ListActorsCommand(AssetCatalog catalog) => _catalog = catalog;

    public string Name => "list_actors";
    public CommandCategory Category => CommandCategory.Discovery;
    public string Description =>
        "Enumerates every ActorAsset registered in this WorldBox build (humans, elves, orcs, "
        + "dwarves, animals, monsters, mythical creatures). Returned ids are the valid inputs "
        + "for `spawn`.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty("properties", new JObject()),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken cancellationToken)
    {
        // HttpBridge already marshalled us onto the main thread.
        var items = _catalog.ListAssets("actor_library", ExtraFields);
        return Task.FromResult<object?>(new { items, count = items.Count });
    }
}
