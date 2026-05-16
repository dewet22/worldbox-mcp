using System;

namespace WorldBoxBridge.Session;

public sealed class Agent
{
    public Agent(string id, string token, AgentRole role, int? claimedKingdomId, Permission permissions)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Token = token ?? throw new ArgumentNullException(nameof(token));
        Role = role;
        ClaimedKingdomId = claimedKingdomId;
        Permissions = permissions;
    }

    public string Id { get; }
    public string Token { get; }
    public AgentRole Role { get; }
    public int? ClaimedKingdomId { get; set; }
    public Permission Permissions { get; }
    public DateTime LastSeenUtc { get; set; }
}
