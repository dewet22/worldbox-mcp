using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Session;
using SessionState = WorldBoxBridge.Session.Session;

namespace WorldBoxBridge.Commands.Bus;

/// <summary>
/// Polls the calling agent's inbox. Non-destructive — messages stay in the inbox until
/// they age out under the bounded-queue policy. Callers use <c>since_seq</c> as a cursor:
/// pass the highest <c>seq</c> they saw last time to avoid re-fetching.
/// </summary>
internal sealed class RecvMessagesCommand : ICommand
{
    private readonly SessionState _session;

    private const int DefaultMax = 50;
    private const int HardMax = 500;

    public RecvMessagesCommand(SessionState session)
    {
        _session = session;
    }

    public string Name => "recv_messages";
    public CommandCategory Category => CommandCategory.Bus;
    public string Description =>
        "Polls this agent's inbox for new messages. `since_seq` is a cursor — pass the "
        + "highest `seq` from the previous response to avoid re-reading. `max` caps the "
        + "result (default 50, hard ceiling 500). Returns {items, count, next_cursor}. "
        + "Messages are NOT consumed — calling again with the same since_seq returns the "
        + "same items until they age out of the bounded inbox.";
    public bool RequiresMainThread => false;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty("since_seq", new JObject(
                        new JProperty("type", "integer"),
                        new JProperty("default", 0),
                        new JProperty("description", "Return messages with seq > this value.")
                    )),
                    new JProperty("max", new JObject(
                        new JProperty("type", "integer"),
                        new JProperty("default", DefaultMax),
                        new JProperty("maximum", HardMax)
                    ))
                )
            ),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken cancellationToken)
    {
        ctx.Require(Permission.RecvMessage);

        var sinceSeq = args.Value<long?>("since_seq") ?? 0L;
        var max = args.Value<int?>("max") ?? DefaultMax;
        if (max <= 0) max = DefaultMax;
        if (max > HardMax) max = HardMax;

        var messages = _session.MessageBus.Recv(ctx.AgentId, sinceSeq, max);
        var items = messages.Select(m => new
        {
            seq = m.Seq,
            from = m.From,
            to = m.To,
            kind = m.Kind,
            content = m.Content,
            sent_utc = m.SentUtc.ToString("o"),
        }).ToArray();

        return Task.FromResult<object?>(new
        {
            items,
            count = items.Length,
            // (avoid C# 8 ^1 index-from-end syntax: net462 doesn't have System.Index in the runtime)
            next_cursor = items.Length > 0 ? items[items.Length - 1].seq : sinceSeq,
        });
    }
}
