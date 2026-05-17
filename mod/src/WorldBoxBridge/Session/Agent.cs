using System;

namespace WorldBoxBridge.Session;

public sealed class Agent
{
    public Agent(
        string id,
        string token,
        AgentRole role,
        long? claimedKingdomId,
        Permission permissions,
        ObjectiveSet? objectives = null
    )
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Token = token ?? throw new ArgumentNullException(nameof(token));
        Role = role;
        ClaimedKingdomId = claimedKingdomId;
        Permissions = permissions;
        Objectives = objectives ?? new ObjectiveSet(System.Array.Empty<Objective>());
    }

    public string Id { get; }
    public string Token { get; }
    public AgentRole Role { get; }
    public long? ClaimedKingdomId { get; set; }
    public Permission Permissions { get; }
    public ObjectiveSet Objectives { get; }
    public DateTime LastSeenUtc { get; set; }
}
