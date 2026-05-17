using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;
using WorldBoxBridge.Threading;

namespace WorldBoxBridge.Commands.Discovery;

/// <summary>
/// Lists every "power" the player can invoke — meteor, nuke, plague, toggle peace, kill race,
/// and so on. Powers cover what the in-game UI groups under the god-mode toolbar.
/// </summary>
internal sealed class ListPowersCommand : ICommand
{
    private readonly AssetCatalog _catalog;

    private static readonly string[] ExtraFields =
    {
        "tab_id",
        "target_type",
        "tooltip_text_locale_id",
        "show_in_kingdoms_panel",
        "show_in_clans_panel",
    };

    public ListPowersCommand(AssetCatalog catalog) => _catalog = catalog;

    public string Name => "list_powers";
    public CommandCategory Category => CommandCategory.Discovery;
    public string Description =>
        "Enumerates every PowerAsset registered in this WorldBox build (disasters, toggles, "
        + "modifiers). Returned ids are the valid inputs for `invoke_power`.";
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
        // HttpBridge already marshalled us onto the main thread.
        var items = _catalog.ListAssets("powers", ExtraFields);
        return Task.FromResult<object?>(new { items, count = items.Count });
    }
}
