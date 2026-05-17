using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Control;

/// <summary>
/// Toggles the simulation pause flag. Implemented as two siblings (pause + resume) rather
/// than a single toggle so the agent's intent is explicit in logs.
/// </summary>
internal abstract class PausedCommandBase : ICommand
{
    private readonly GameRefs _refs;
    private readonly bool _value;
    private PropertyInfo? _pausedProp;

    protected PausedCommandBase(GameRefs refs, bool value)
    {
        _refs = refs;
        _value = value;
    }

    public abstract string Name { get; }
    public CommandCategory Category => CommandCategory.Control;
    public abstract string Description { get; }
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
        // pause / resume are simulation-flow controls -- shared experience, no griefing
        // potential (everyone sees the same pause). Gated on AdvanceTime so FactionPlayers
        // can use them too; destructive lifecycle ops keep ControlWorld.
        ctx.Require(Permission.AdvanceTime);
        var configType = _refs.Type("Config");
        if (configType == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "Config type not found — can't toggle pause."
            );
        }
        _pausedProp ??= configType.GetProperty(
            "paused",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        );
        if (_pausedProp == null || !_pausedProp.CanRead || !_pausedProp.CanWrite)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "Config.paused property not found or not writable."
            );
        }
        var previous = _pausedProp.GetValue(null) as bool? ?? false;
        _pausedProp.SetValue(null, _value);
        return Task.FromResult<object?>(new { previous_paused = previous, paused = _value });
    }
}

internal sealed class PauseCommand : PausedCommandBase
{
    public PauseCommand(GameRefs refs)
        : base(refs, value: true) { }

    public override string Name => "pause";
    public override string Description =>
        "Pauses the WorldBox simulation. Use this before building a complex scenario so "
        + "the world doesn't drift while the agent prepares the setup.";
}

internal sealed class ResumeCommand : PausedCommandBase
{
    public ResumeCommand(GameRefs refs)
        : base(refs, value: false) { }

    public override string Name => "resume";
    public override string Description =>
        "Resumes the simulation after a pause. Pair with set_speed if you want the simulation "
        + "to run faster than normal (x2/x3/x5).";
}
