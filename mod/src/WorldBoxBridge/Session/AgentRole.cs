namespace WorldBoxBridge.Session;

public enum AgentRole
{
    God,
    FactionPlayer,
    Observer,
    Narrator,
}

public static class AgentRoleExtensions
{
    /// <summary>
    /// Canonical snake_case wire form, the same syntax accepted by <c>agents.json</c>.
    /// Stay consistent with what users type in so clients can round-trip role values.
    /// </summary>
    public static string ToWireString(this AgentRole role) =>
        role switch
        {
            AgentRole.God => "god",
            AgentRole.FactionPlayer => "faction_player",
            AgentRole.Observer => "observer",
            AgentRole.Narrator => "narrator",
            _ => role.ToString().ToLowerInvariant(),
        };
}
