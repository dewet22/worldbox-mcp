using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldBoxBridge.Session;

/// <summary>
/// Round-robin turn rotation for opt-in turn-based scenarios (Diplomacy-style PvP). Threadsafe:
/// every read of <see cref="Current"/> and every <see cref="Advance"/> takes the same internal
/// lock, so two HTTP requests racing on the same turn always see a consistent value.
/// </summary>
/// <remarks>
/// Constructed by <see cref="SessionLoader"/> only when <c>turn_based: true</c> is set in
/// agents.json. The rotation can be explicit (<c>"turn_order": ["athena", "ares"]</c>) or
/// implicit (all registered agents in declaration order — handy for quick tests).
/// God-role agents bypass the gate entirely (see <c>HttpBridge.CheckTurnGate</c>) so a
/// hierarchical "DM watches over the PvP" scenario remains workable.
/// </remarks>
public sealed class TurnOrder
{
    private readonly object _lock = new();
    private readonly List<string> _agentIds;
    private int _currentIndex;

    public TurnOrder(IEnumerable<string> agentIds)
    {
        if (agentIds is null) throw new ArgumentNullException(nameof(agentIds));
        _agentIds = agentIds.ToList();
        if (_agentIds.Count == 0)
        {
            throw new InvalidOperationException(
                "TurnOrder constructed with zero agents — no one would ever be allowed to act."
            );
        }
    }

    public IReadOnlyList<string> AgentIds
    {
        get { lock (_lock) { return _agentIds.AsReadOnly(); } }
    }

    public string Current
    {
        get { lock (_lock) { return _agentIds[_currentIndex]; } }
    }

    /// <summary>Advances to the next agent in the rotation, returning the new current agent.</summary>
    public string Advance()
    {
        lock (_lock)
        {
            _currentIndex = (_currentIndex + 1) % _agentIds.Count;
            return _agentIds[_currentIndex];
        }
    }

    /// <summary>True if <paramref name="agentId"/> is the agent whose turn is currently active.</summary>
    public bool IsCurrentlyActive(string agentId)
    {
        lock (_lock) { return _agentIds[_currentIndex] == agentId; }
    }
}
