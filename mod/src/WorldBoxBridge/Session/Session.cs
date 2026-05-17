using System;
using System.Collections.Generic;

namespace WorldBoxBridge.Session;

/// <summary>
/// One game world = one Session. Holds the agent roster, scenario preset, and global
/// switches that affect how every command behaves (fog-of-war, turn ordering, …).
/// </summary>
/// <remarks>
/// v0.3 ships with a single Session singleton constructed at <see cref="WorldBoxBridge.Plugin.Awake"/>.
/// Multi-session routing is deferred to a later release — see plan §6.
/// </remarks>
public sealed class Session
{
    public Session(
        AgentRegistry agents,
        string scenarioPreset,
        bool partialIntel,
        bool turnBased,
        TurnOrder? turnOrder = null,
        MessageBus? messageBus = null
    )
    {
        Agents = agents ?? throw new ArgumentNullException(nameof(agents));
        ScenarioPreset = scenarioPreset ?? "sandbox";
        PartialIntel = partialIntel;
        TurnBased = turnBased;
        TurnOrder = turnOrder;
        // Auto-build a default MessageBus from the registry if the caller didn't pass one —
        // tests and the legacy factory just want a working bus without ceremony.
        var allIds = new List<string>(agents.Count);
        foreach (var a in agents.All)
            allIds.Add(a.Id);
        MessageBus = messageBus ?? new MessageBus(allIds);
        CreatedUtc = DateTime.UtcNow;

        if (TurnBased && TurnOrder is null)
        {
            throw new ArgumentException(
                "turn_based session requires a TurnOrder. Pass one or set turnBased=false.",
                nameof(turnOrder)
            );
        }
    }

    public AgentRegistry Agents { get; }
    public string ScenarioPreset { get; }
    public bool PartialIntel { get; }
    public bool TurnBased { get; }
    public TurnOrder? TurnOrder { get; }
    public MessageBus MessageBus { get; }
    public DateTime CreatedUtc { get; }

    /// <summary>
    /// Wraps an authenticated <see cref="Agent"/> in a <see cref="RequestContext"/> with this
    /// session's per-scenario flags. Called by <c>HttpBridge.Authenticate</c>.
    /// </summary>
    public RequestContext ContextFor(Agent agent) => new(agent, ScenarioPreset, PartialIntel);

    /// <summary>Legacy single-token bootstrap. Used when no agents.toml is present.</summary>
    public static Session Legacy(string token) =>
        new(
            agents: AgentRegistry.Legacy(token),
            scenarioPreset: "sandbox",
            partialIntel: false,
            turnBased: false
        );
}
