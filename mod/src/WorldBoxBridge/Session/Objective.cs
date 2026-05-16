using System.Collections.Generic;

namespace WorldBoxBridge.Session;

/// <summary>
/// A single objective declared on an agent. <see cref="Kind"/> + <see cref="Target"/> are
/// informational metadata — the bridge stores and reports them but does <em>not</em>
/// evaluate scores on the C# side. Score interpretation lives in the AI client (which
/// understands the scenario context) using the metrics surfaced by
/// <c>worldbox_objective_status</c>.
/// </summary>
/// <remarks>
/// Keeping objective evaluation client-side intentional:
/// (a) the same metrics serve cooperative and adversarial framings ("maximize pop" can be
///     team-cooperative or per-faction-competitive depending on the scenario),
/// (b) the bridge stays a stateless transport — no game-state subscription / tick callback
///     plumbing — which dodges the Unity main-thread coupling that's already a sharp
///     edge (see CLAUDE.md gotchas #1-#3).
/// </remarks>
public sealed class Objective
{
    public Objective(string id, string label, string kind, string? target = null)
    {
        Id = id;
        Label = label;
        Kind = kind;
        Target = target;
    }

    public string Id { get; }
    public string Label { get; }

    /// <summary>
    /// Conventional values (recognized by the included scenario examples):
    /// <c>"survive"</c>, <c>"wipe_kingdom"</c>, <c>"maximize_pop"</c>,
    /// <c>"minimize_pop_of"</c>, <c>"reach_pop"</c>. Free-form for custom scenarios.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Optional target identifier — typically a kingdom id ("3"), a kingdom name
    /// ("Aetheria"), or a numeric threshold ("1000"). Free-form on purpose.
    /// </summary>
    public string? Target { get; }
}

public sealed class ObjectiveSet
{
    public ObjectiveSet(IReadOnlyList<Objective> objectives)
    {
        Items = objectives;
    }

    public IReadOnlyList<Objective> Items { get; }
}
