using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Session;
using SessionState = WorldBoxBridge.Session.Session;

namespace WorldBoxBridge.Commands.Meta;

/// <summary>
/// Ends the calling agent's turn and hands the active slot to the next agent in the
/// session's <see cref="TurnOrder"/>. Only valid in turn_based sessions.
/// </summary>
/// <remarks>
/// Category is <see cref="CommandCategory.Meta"/> on purpose: if it were Action / Control,
/// the turn gate in <c>HttpBridge</c> would block <em>this very command</em>, leaving the
/// session permanently stuck. Meta commands are always callable; this command does its own
/// "is it my turn" check at the top.
/// </remarks>
internal sealed class TurnAdvanceCommand : ICommand
{
    private readonly SessionState _session;

    public TurnAdvanceCommand(SessionState session)
    {
        _session = session;
    }

    public string Name => "turn_advance";
    public CommandCategory Category => CommandCategory.Meta;
    public string Description =>
        "Ends the calling agent's turn in a turn_based session and hands the active slot to "
        + "the next agent in the rotation. Returns {previous, next}. Errors with TURN_NOT_YOURS "
        + "if it's not your turn, BAD_ARGS if the session is not turn_based.";
    public bool RequiresMainThread => false;

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
        if (!_session.TurnBased || _session.TurnOrder is null)
        {
            throw new BridgeRejectionException(
                ErrorCode.BadArgs,
                "Session is not turn_based; turn_advance has no effect. Set turn_based=true in agents.json."
            );
        }

        // God agents can advance the turn even if the rotation doesn't currently point at
        // them — useful for unsticking a dropped player in hierarchical scenarios.
        if (!ctx.Has(Permission.ActionGlobal))
        {
            if (!_session.TurnOrder.IsCurrentlyActive(ctx.AgentId))
            {
                throw new BridgeRejectionException(
                    ErrorCode.TurnNotYours,
                    $"Not your turn — current is '{_session.TurnOrder.Current}', you are '{ctx.AgentId}'."
                );
            }
        }

        var previous = _session.TurnOrder.Current;
        var next = _session.TurnOrder.Advance();
        return Task.FromResult<object?>(
            new
            {
                previous,
                next,
                forced_by_god = ctx.Has(Permission.ActionGlobal) && previous != ctx.AgentId,
            }
        );
    }
}
