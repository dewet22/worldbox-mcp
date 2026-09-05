namespace WorldBoxBridge.Commands;

/// <summary>Logical grouping of a command, surfaced in <c>capabilities()</c> metadata.</summary>
/// <remarks>
/// Kept in its own file, free of Unity, BepInEx and Newtonsoft references, so the test project
/// can link it alongside <see cref="TurnGate"/>.
/// </remarks>
public enum CommandCategory
{
    Meta,
    Discovery,
    Action,
    Read,
    Control,
    Bus,
}
