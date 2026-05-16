using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Session;
using SessionState = WorldBoxBridge.Session.Session;

namespace WorldBoxBridge.Commands.Bus;

/// <summary>
/// Sends a message from the calling agent to another agent (by id) or to <c>"*"</c> for
/// broadcast. Broadcast requires <see cref="Permission.SendBroadcast"/> so faction
/// players can't flood everyone else's inbox.
/// </summary>
internal sealed class SendMessageCommand : ICommand
{
    private readonly SessionState _session;

    public SendMessageCommand(SessionState session)
    {
        _session = session;
    }

    public string Name => "send_message";
    public CommandCategory Category => CommandCategory.Bus;
    public string Description =>
        "Sends a message to another agent's inbox in the current session. `to` is an agent "
        + "id (see session_info) or '*' to broadcast to everyone except yourself. `content` "
        + "is freeform. Optional `kind` is a short categorization tag (e.g. 'diplomacy', "
        + "'alert'). Broadcasts require the send_broadcast permission (god + narrator only)."
        + " Returns {seq, recipients}.";
    public bool RequiresMainThread => false;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty("to", new JObject(
                        new JProperty("type", "string"),
                        new JProperty("description", "Target agent id, or '*' to broadcast.")
                    )),
                    new JProperty("content", new JObject(new JProperty("type", "string"))),
                    new JProperty("kind", new JObject(
                        new JProperty("type", "string"),
                        new JProperty("description", "Optional categorization tag.")
                    ))
                )
            ),
            new JProperty("required", new JArray("to", "content")),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken cancellationToken)
    {
        ctx.Require(Permission.SendMessage);

        var to = args.Value<string?>("to");
        var content = args.Value<string?>("content");
        var kind = args.Value<string?>("kind");

        if (string.IsNullOrWhiteSpace(to))
        {
            throw new BridgeRejectionException(ErrorCode.BadArgs, "`to` is required.");
        }
        if (content is null)
        {
            throw new BridgeRejectionException(ErrorCode.BadArgs, "`content` is required.");
        }

        if (to == "*")
        {
            ctx.Require(Permission.SendBroadcast);
        }

        long seq;
        try
        {
            seq = _session.MessageBus.Send(ctx.AgentId, to!, kind, content!);
        }
        catch (ArgumentException ax)
        {
            throw new BridgeRejectionException(ErrorCode.BadArgs, ax.Message);
        }

        // Compute recipient set for the response. Broadcast → all registered agents minus self.
        var recipients = to == "*"
            ? Recipients(except: ctx.AgentId)
            : new[] { to! };

        return Task.FromResult<object?>(new
        {
            seq,
            recipients,
            broadcast = to == "*",
        });
    }

    private string[] Recipients(string except)
    {
        var ids = _session.Agents.All;
        var result = new System.Collections.Generic.List<string>(ids.Count);
        foreach (var a in ids)
        {
            if (!string.Equals(a.Id, except, StringComparison.Ordinal)) result.Add(a.Id);
        }
        return result.ToArray();
    }
}
