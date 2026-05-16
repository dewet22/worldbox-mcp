using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldBoxBridge.Session;

/// <summary>
/// In-memory pub-sub for inter-agent messages. Each registered agent owns a bounded inbox
/// (default 200 messages) protected by a single bus-wide lock. When an inbox fills up,
/// the oldest message is dropped — drop-newest would lose the fresh signal that's usually
/// the more interesting one in a simulation game.
/// </summary>
/// <remarks>
/// The sequence number is *bus-wide* and monotonic, not per-agent. Clients can poll with a
/// single <c>since_seq</c> cursor that's stable across recipients. Messages addressed to
/// <c>"*"</c> (broadcast) are fan-out copied into every registered agent's inbox except
/// the sender's. Per-agent state lives only inside this object; nothing is persisted to
/// disk in v0.3 (deferred to v0.3.2).
/// </remarks>
public sealed class MessageBus
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<Message>> _inboxes;
    private readonly int _maxInboxSize;
    private long _seq;

    public MessageBus(IEnumerable<string> agentIds, int maxInboxSize = 200)
    {
        if (agentIds is null) throw new ArgumentNullException(nameof(agentIds));
        if (maxInboxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxInboxSize));
        _maxInboxSize = maxInboxSize;
        _inboxes = new Dictionary<string, Queue<Message>>(StringComparer.Ordinal);
        foreach (var id in agentIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            _inboxes[id] = new Queue<Message>(capacity: 16);
        }
        if (_inboxes.Count == 0)
        {
            throw new InvalidOperationException(
                "MessageBus constructed with zero registered agents — nobody could send or receive."
            );
        }
    }

    /// <summary>Total messages delivered (post-fan-out for broadcasts). Useful for tests.</summary>
    public long DeliveredCount
    {
        get { lock (_lock) { return _seq; } }
    }

    /// <summary>
    /// Sends a message. <paramref name="to"/> is either a registered agent id or
    /// <c>"*"</c> to broadcast to all other agents. Returns the sequence number of the
    /// last delivery (the sender always receives a single seq even on broadcast — that's
    /// the cursor consumers should advance past after polling).
    /// </summary>
    public long Send(string from, string to, string? kind, string content)
    {
        if (string.IsNullOrEmpty(from)) throw new ArgumentException("from required", nameof(from));
        if (string.IsNullOrEmpty(to)) throw new ArgumentException("to required", nameof(to));
        content ??= string.Empty;

        lock (_lock)
        {
            if (!_inboxes.ContainsKey(from))
            {
                throw new InvalidOperationException(
                    $"Sender '{from}' is not registered on this bus."
                );
            }

            long lastSeq = _seq;
            if (to == "*")
            {
                foreach (var recipient in _inboxes.Keys.Where(k => k != from))
                {
                    lastSeq = Deliver(recipient, from, to, kind, content);
                }
            }
            else
            {
                if (!_inboxes.ContainsKey(to))
                {
                    throw new ArgumentException(
                        $"Recipient '{to}' is not registered on this bus. Known: [{string.Join(",", _inboxes.Keys)}]",
                        nameof(to)
                    );
                }
                lastSeq = Deliver(to, from, to, kind, content);
            }
            return lastSeq;
        }
    }

    private long Deliver(string recipient, string from, string to, string? kind, string content)
    {
        // Caller must already hold _lock.
        _seq++;
        var msg = new Message
        {
            Seq = _seq,
            From = from,
            To = to,
            Kind = kind ?? string.Empty,
            Content = content,
            SentUtc = DateTime.UtcNow,
        };
        var inbox = _inboxes[recipient];
        inbox.Enqueue(msg);
        while (inbox.Count > _maxInboxSize)
        {
            inbox.Dequeue();   // drop-oldest
        }
        return _seq;
    }

    /// <summary>
    /// Returns messages addressed to <paramref name="agentId"/> with seq &gt; <paramref name="sinceSeq"/>,
    /// up to <paramref name="max"/>. Non-destructive: the inbox is not drained. Use the
    /// returned sequence numbers to advance the client cursor.
    /// </summary>
    public IReadOnlyList<Message> Recv(string agentId, long sinceSeq = 0, int max = 100)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentException("agentId required");
        if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
        lock (_lock)
        {
            if (!_inboxes.TryGetValue(agentId, out var inbox))
            {
                throw new InvalidOperationException(
                    $"Agent '{agentId}' is not registered on this bus."
                );
            }
            // Inbox is a queue ordered by seq (insertion order). Filter + take in one pass.
            var result = new List<Message>(Math.Min(max, inbox.Count));
            foreach (var msg in inbox)
            {
                if (msg.Seq <= sinceSeq) continue;
                result.Add(msg);
                if (result.Count >= max) break;
            }
            return result;
        }
    }
}

public sealed class Message
{
    public long Seq { get; set; }
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentUtc { get; set; }
}
