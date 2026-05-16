using System;

namespace WorldBoxBridge.Session;

[Flags]
public enum Permission
{
    None = 0,

    ReadAll = 1 << 0,
    ReadOwnFaction = 1 << 1,
    ActionGlobal = 1 << 2,
    ActionFaction = 1 << 3,
    ControlWorld = 1 << 4,
    SendMessage = 1 << 5,
    RecvMessage = 1 << 6,
    SendBroadcast = 1 << 7,

    God = ReadAll | ReadOwnFaction | ActionGlobal | ActionFaction | ControlWorld | SendMessage | RecvMessage | SendBroadcast,
    FactionPlayer = ReadOwnFaction | ActionFaction | SendMessage | RecvMessage,
    Observer = ReadAll | ReadOwnFaction | SendMessage | RecvMessage,
    Narrator = ReadAll | ReadOwnFaction | SendMessage | RecvMessage | SendBroadcast,
}

public static class PermissionDefaults
{
    public static Permission For(AgentRole role) =>
        role switch
        {
            AgentRole.God => Permission.God,
            AgentRole.FactionPlayer => Permission.FactionPlayer,
            AgentRole.Observer => Permission.Observer,
            AgentRole.Narrator => Permission.Narrator,
            _ => Permission.None,
        };
}
