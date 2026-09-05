using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Discovery;

/// <summary>
/// Lists the simulation speeds (<c>WorldTimeScaleAsset</c>s) this build knows, plus which one is
/// active. Their ids are the only valid inputs for <c>set_speed</c>.
/// </summary>
/// <remarks>
/// Stock 0.51.x ships slow_mo, x1, x2, x3, x4, x5, x10, x15, x20 and x40; the in-game speed button
/// cycles through all but the last (x40 is reserved for debug builds), but
/// <c>Config.setWorldSpeed</c> accepts any of them.
/// </remarks>
internal sealed class ListSpeedsCommand : ICommand
{
    private static readonly string[] ExtraFields =
    {
        "multiplier",
        "ticks",
        "sonic",
        "render_skip",
    };

    private readonly AssetCatalog _catalog;
    private readonly GameSpeedAccess _speed;

    public ListSpeedsCommand(AssetCatalog catalog, GameSpeedAccess speed)
    {
        _catalog = catalog;
        _speed = speed;
    }

    public string Name => "list_speeds";
    public CommandCategory Category => CommandCategory.Discovery;
    public string Description =>
        "Enumerates every simulation speed (WorldTimeScaleAsset) in this WorldBox build with its "
        + "multiplier, and reports the currently active one as `current`. Returned ids are the "
        + "valid inputs for `set_speed`.";
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
        var items = _catalog.ListAssets("time_scales", ExtraFields);
        return Task.FromResult<object?>(
            new
            {
                items,
                count = items.Count,
                current = _speed.CurrentSpeedId(),
            }
        );
    }
}
