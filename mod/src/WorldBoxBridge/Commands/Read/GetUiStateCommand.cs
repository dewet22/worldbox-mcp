using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Read;

/// <summary>
/// Reports the UI layer's state: which window (if any) is open, and both pause flags.
/// </summary>
/// <remarks>
/// <c>get_world_state.paused</c> is the game's <i>effective</i> pause, which is true whenever any
/// window is open, including the startup "welcome" screen. <c>pause</c>/<c>resume</c> only flip
/// <c>Config.paused</c>. Exposing both side by side lets an agent tell "I paused it" from
/// "a window is blocking the simulation" and react with <c>dismiss_window</c>.
/// </remarks>
internal sealed class GetUiStateCommand : ICommand
{
    private readonly IGameUiAccess _ui;
    private readonly WorldAccess _world;

    public GetUiStateCommand(IGameUiAccess ui, WorldAccess world)
    {
        _ui = ui;
        _world = world;
    }

    public string Name => "get_ui_state";
    public CommandCategory Category => CommandCategory.Read;
    public string Description =>
        "Returns the game's UI state: window_active (any in-game window open, this freezes the "
        + "simulation), current_window (its id, e.g. 'welcome' for the startup screen, "
        + "'settings', 'kingdom'), config_paused (the pause toggle set by pause/resume) and "
        + "effective_paused (what the simulation actually does: config_paused OR a window is "
        + "open), and world_loading (the game is still generating/loading a world; save_world "
        + "and similar refuse until it is false). If effective_paused is true but config_paused "
        + "is false, call dismiss_window.";
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
        // No permission gate: UI state reveals nothing about the world, and every role needs
        // it to understand why the simulation isn't advancing.
        var report = UiStateReport.From(_ui, _world.IsPaused, _world.IsWorldLoading);
        return Task.FromResult<object?>(
            new
            {
                window_active = report.WindowActive,
                current_window = report.CurrentWindow,
                config_paused = report.ConfigPaused,
                effective_paused = report.EffectivePaused,
                world_loading = report.WorldLoading,
            }
        );
    }
}
