using System;

namespace WorldBoxBridge.Threading;

/// <summary>
/// Thrown when <c>MainThreadDispatcher.RunPerFrameOnMainThreadAsync</c> is asked for a job while
/// the per-frame registry is already full.
/// </summary>
/// <remarks>
/// It has its own type because the HTTP layer has to tell it apart from a command that actually
/// broke: a full registry is a 503 the caller can retry, not a 500 that says the game faulted.
/// Every other route out of the dispatcher is a timeout, a cancellation, or the command's own
/// exception, and all three already have a label.
/// </remarks>
internal sealed class DispatcherSaturatedException : Exception
{
    public DispatcherSaturatedException(string message)
        : base(message) { }
}
