using System;
using System.Collections.Generic;

namespace WorldBoxBridge.Commands.Action;

/// <summary>
/// Throws a structured rejection that <c>HttpBridge</c> maps to a precise error envelope —
/// rather than a generic 500. The HttpBridge catches BridgeRejectionException at the top of
/// the executor and serialises its fields directly.
/// </summary>
/// <remarks>
/// Lives in <c>Commands.Action</c> for historical reasons (the first commands to throw it
/// were <c>InvokePowerCommand</c> / <c>SpawnCommand</c>). Now reused by the multi-agent
/// session layer for <c>PERMISSION_DENIED</c> / <c>FACTION_SCOPE_VIOLATION</c> / etc. —
/// kept in a dedicated file (rather than inline in InvokePowerCommand) so the test project
/// can link it without dragging in Unity references.
/// </remarks>
public sealed class BridgeRejectionException : Exception
{
    public BridgeRejectionException(
        string code,
        string message,
        IReadOnlyList<string>? didYouMean = null
    )
        : base(message)
    {
        Code = code;
        DidYouMean = didYouMean;
    }

    public string Code { get; }
    public IReadOnlyList<string>? DidYouMean { get; }
}
