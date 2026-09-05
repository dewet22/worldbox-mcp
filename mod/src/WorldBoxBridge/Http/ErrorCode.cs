namespace WorldBoxBridge.Http;

/// <summary>Stable, normalized error codes returned to clients.</summary>
/// <remarks>
/// These strings are part of the public protocol contract (see <c>docs/protocol.md</c>).
/// Renaming an existing code is a breaking change; adding a new code is backwards-compatible.
///
/// Kept in a dedicated file (split from <c>ErrorEnvelope.cs</c>) so the test project can
/// link it without pulling in Newtonsoft.Json, only the envelope/detail/exception types
/// carry serialization attributes; the codes themselves are plain strings.
/// </remarks>
public static class ErrorCode
{
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Disabled = "DISABLED";
    public const string UnknownCommand = "UNKNOWN_COMMAND";
    public const string BadArgs = "BAD_ARGS";
    public const string UnknownAsset = "UNKNOWN_ASSET";
    public const string OutOfBounds = "OUT_OF_BOUNDS";
    public const string GameRejected = "GAME_REJECTED";
    public const string GameCrash = "GAME_CRASH";
    public const string MainThreadTimeout = "MAIN_THREAD_TIMEOUT";
    public const string Internal = "INTERNAL";

    /// <summary>
    /// The bridge is already running as much work as it admits at once. Distinct from
    /// <see cref="MainThreadTimeout"/>, which means the frame never came, and from
    /// <see cref="Disabled"/>, which means somebody turned the bridge off: this one clears on
    /// its own and the caller should retry.
    /// </summary>
    public const string Busy = "BUSY";

    // v0.3 multi-agent additions:
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string FactionScopeViolation = "FACTION_SCOPE_VIOLATION";
    public const string TurnNotYours = "TURN_NOT_YOURS";
}
