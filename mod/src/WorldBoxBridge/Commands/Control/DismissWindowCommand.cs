using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Control;

/// <summary>
/// Closes whatever in-game window is open, most usefully the startup "welcome" screen, via
/// the game's own <c>ScrollWindow.hideAllEvent</c>.
/// </summary>
internal sealed class DismissWindowCommand : ICommand
{
    private readonly GameUiAccess _ui;

    public DismissWindowCommand(GameUiAccess ui) => _ui = ui;

    public string Name => "dismiss_window";
    public CommandCategory Category => CommandCategory.Control;
    public string Description =>
        "Closes any open in-game window (the startup 'welcome' screen, settings, info panels, "
        + "confirmations). Open windows freeze the simulation, so call this when get_ui_state "
        + "reports window_active=true. Returns {dismissed, window} where window is the id that "
        + "was open, or null if nothing was.";
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
        // Same gate as pause/resume: a simulation-flow control everyone in the session
        // experiences identically, with no griefing potential.
        ctx.Require(Permission.AdvanceTime);
        var window = _ui.CurrentWindowId();
        var active = _ui.IsWindowActive() ?? false;
        if (!active)
        {
            return Task.FromResult<object?>(new { dismissed = false, window = (string?)null });
        }
        if (!_ui.HideAllWindows())
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "ScrollWindow.hideAllEvent not found in this WorldBox build."
            );
        }
        return Task.FromResult<object?>(new { dismissed = true, window });
    }
}
