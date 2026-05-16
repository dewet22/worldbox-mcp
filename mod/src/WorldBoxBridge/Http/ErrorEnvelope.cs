using System.Collections.Generic;
using Newtonsoft.Json;

namespace WorldBoxBridge.Http;

/// <summary>Stable, normalized error codes returned to clients.</summary>
/// <remarks>
/// These strings are part of the public protocol contract (see docs/protocol.md).
/// Renaming an existing code is a breaking change. Adding a new code is backwards-compatible.
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
}

public sealed class SuccessEnvelope
{
    [JsonProperty("ok")]
    public bool Ok => true;

    [JsonProperty("result")]
    public object? Result { get; set; }

    [JsonProperty("tick")]
    public int Tick { get; set; }
}

public sealed class ErrorEnvelope
{
    [JsonProperty("ok")]
    public bool Ok => false;

    [JsonProperty("error")]
    public ErrorDetail Error { get; set; } = new();
}

public sealed class ErrorDetail
{
    [JsonProperty("code")]
    public string Code { get; set; } = ErrorCode.Internal;

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("command", NullValueHandling = NullValueHandling.Ignore)]
    public string? Command { get; set; }

    [JsonProperty("args", NullValueHandling = NullValueHandling.Ignore)]
    public object? Args { get; set; }

    [JsonProperty("did_you_mean", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<string>? DidYouMean { get; set; }

    [JsonProperty("exception", NullValueHandling = NullValueHandling.Ignore)]
    public ExceptionInfo? Exception { get; set; }
}

public sealed class ExceptionInfo
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("stack_top", NullValueHandling = NullValueHandling.Ignore)]
    public string? StackTop { get; set; }

    public static ExceptionInfo From(System.Exception ex, int stackTopLines = 3)
    {
        var stack = ex.StackTrace;
        if (!string.IsNullOrEmpty(stack))
        {
            var lines = stack!.Split(
                new[] { '\n' },
                stackTopLines + 1,
                System.StringSplitOptions.RemoveEmptyEntries
            );
            var len = System.Math.Min(stackTopLines, lines.Length);
            stack = string.Join("\n", new System.ArraySegment<string>(lines, 0, len));
        }
        return new ExceptionInfo
        {
            Type = ex.GetType().FullName ?? ex.GetType().Name,
            Message = ex.Message,
            StackTop = stack,
        };
    }
}
