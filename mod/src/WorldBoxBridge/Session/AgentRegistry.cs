using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldBoxBridge.Session;

/// <summary>
/// In-memory token → agent lookup. Loaded once at plugin startup from
/// <c>BridgeConfig</c> (legacy single-token mode) or from <c>agents.toml</c>
/// (multi-agent mode, populated in Phase 2).
/// </summary>
/// <remarks>
/// Lookups are constant-time-equal (timing-oracle safe) to match the legacy
/// auth behavior. The registry itself is small (typically 1–8 entries) so the
/// linear scan dominated by FixedTimeEquals is fine.
/// </remarks>
public sealed class AgentRegistry
{
    private readonly List<Agent> _agents;

    public AgentRegistry(IEnumerable<Agent> agents)
    {
        _agents = agents?.ToList() ?? throw new ArgumentNullException(nameof(agents));
        if (_agents.Count == 0)
        {
            throw new InvalidOperationException(
                "AgentRegistry constructed with zero agents — the bridge would reject every request."
            );
        }
    }

    public int Count => _agents.Count;

    public IReadOnlyList<Agent> All => _agents;

    public bool IsLegacyMode => _agents.Count == 1 && _agents[0].Id == "legacy";

    public Agent? TryAuthenticate(string? presentedToken)
    {
        if (string.IsNullOrEmpty(presentedToken))
        {
            return null;
        }
        foreach (var agent in _agents)
        {
            if (FixedTimeEquals(presentedToken!, agent.Token))
            {
                agent.LastSeenUtc = DateTime.UtcNow;
                return agent;
            }
        }
        return null;
    }

    public Agent? GetById(string id) => _agents.FirstOrDefault(a => a.Id == id);

    /// <summary>Constant-time string comparison to avoid token timing oracles.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    /// <summary>
    /// Builds the registry that's used when no <c>agents.toml</c> exists. Single God agent
    /// using the legacy <c>BridgeConfig.Token</c> as its credential.
    /// </summary>
    public static AgentRegistry Legacy(string token) =>
        new(new[]
        {
            new Agent(
                id: "legacy",
                token: token,
                role: AgentRole.God,
                claimedKingdomId: null,
                permissions: Permission.God
            ),
        });
}
