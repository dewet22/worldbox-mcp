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

    /// <summary>
    /// Destructive world-lifecycle ops: generate_world, save_world, load_world. God-only.
    /// </summary>
    ControlWorld = 1 << 4,

    SendMessage = 1 << 5,
    RecvMessage = 1 << 6,
    SendBroadcast = 1 << 7,

    /// <summary>
    /// Non-destructive simulation-flow controls: pause, resume, set_speed, dismiss_window.
    /// Granted to
    /// active-player roles (God + FactionPlayer) so PvP agents can fast-forward through
    /// quiet phases without needing a god agent in the session. Spectator roles
    /// (Observer / Narrator) intentionally do NOT have this, they shouldn't be able to
    /// skip ahead while the actual players are still deliberating.
    /// </summary>
    AdvanceTime = 1 << 8,

    God =
        ReadAll
        | ReadOwnFaction
        | ActionGlobal
        | ActionFaction
        | ControlWorld
        | SendMessage
        | RecvMessage
        | SendBroadcast
        | AdvanceTime,
    FactionPlayer = ReadOwnFaction | ActionFaction | SendMessage | RecvMessage | AdvanceTime,
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
