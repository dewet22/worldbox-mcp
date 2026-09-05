using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;
using WorldBoxBridge.Threading;

namespace WorldBoxBridge.Commands.Discovery;

/// <summary>Lists every tile type registered by the running game.</summary>
internal sealed class ListTilesCommand : ICommand
{
    private readonly AssetCatalog _catalog;

    private static readonly string[] ExtraFields =
    {
        "color_hex",
        "edge_color_hex",
        "has_biome_tags",
        "force_edge_variation",
    };

    public ListTilesCommand(AssetCatalog catalog) => _catalog = catalog;

    public string Name => "list_tiles";
    public CommandCategory Category => CommandCategory.Discovery;
    public string Description =>
        "Enumerates every TileType currently registered in this WorldBox build. "
        + "Returned ids are the valid inputs for `paint_tile`.";
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
        // HttpBridge already marshalled us onto the main thread (we declare
        // RequiresMainThread = true), re-dispatching would deadlock.
        var items = _catalog.ListAssets("tiles", ExtraFields);
        return Task.FromResult<object?>(new { items, count = items.Count });
    }
}
