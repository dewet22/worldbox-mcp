using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands;

/// <summary>
/// Contract every HTTP-exposed command implements. One <see cref="ICommand"/> instance lives
/// for the lifetime of the plugin and is invoked concurrently from many request threads, keep
/// implementations stateless or thread-safe.
/// </summary>
public interface ICommand
{
    string Name { get; }

    CommandCategory Category { get; }

    string Description { get; }

    /// <summary>JSON-Schema describing the <c>args</c> object. Use an empty object if no args.</summary>
    JObject ArgsSchema { get; }

    /// <summary>
    /// Whether this command needs to run on Unity's main thread. Pure metadata or filesystem
    /// commands can return false and run directly on the HTTP thread for lower latency.
    /// </summary>
    /// <remarks>
    /// No default value provided: the compiler error reminds command authors to make a conscious
    /// decision per command. (.NET Framework 4.6.2 doesn't support default interface methods
    /// anyway.)
    /// <para><b>True buys you the first thread, not the whole method.</b> <c>HttpBridge</c>
    /// starts <see cref="ExecuteAsync"/> on the main thread and then awaits the returned task
    /// off it, which is what lets a multi-frame command such as <c>invoke_power</c>'s pulse run
    /// complete on a later frame instead of deadlocking the frame it started on. So an
    /// <c>await</c> inside such a command is the wrong tool, though not for the reason it is
    /// tempting to write down. Unity does install a synchronization context on the main thread:
    /// <c>UnityEngine.UnitySynchronizationContext</c> is present in the UnityEngine.Modules
    /// reference assembly this project compiles against, and the engine initializes it and
    /// pumps it from the player loop. The continuation therefore comes back to the main thread,
    /// not to a pool thread. It comes back pumped by the engine and outside
    /// <c>MainThreadDispatcher</c>, which means no queueing deadline, no <c>maxPerFrame</c>
    /// bound, and no defined order against the actions the dispatcher still has queued. The
    /// trap worth naming is that reaching for <c>ConfigureAwait(false)</c> or <c>Task.Run</c>
    /// to "get back on the main thread" is what actually leaves it. Write such a command as one
    /// synchronous body that returns a task, and marshal anything that must wait through
    /// <c>MainThreadDispatcher.RunPerFrameOnMainThreadAsync</c>. No command that reports true
    /// awaits today; this exists so the first one to try knows what it is doing.</para>
    /// <para>False is the other shape, and it is not the lesser one: the command runs entirely
    /// on the pool thread and marshals only the calls that need a frame. <c>LoadWorldCommand</c>
    /// is the worked example, and the one command that does await, which is safe there because
    /// it awaits from the pool thread and its continuation touches no game API. Gotcha 11 in
    /// <c>docs/game-api-notes.md</c> says why blocking I/O has no other option.</para>
    /// </remarks>
    bool RequiresMainThread { get; }

    /// <summary>
    /// Executes the command. <paramref name="ctx"/> identifies the calling agent and carries
    /// session-level scope (role, faction claim, fog-of-war flag). Commands should call
    /// <see cref="RequestContext.Require"/> early to enforce per-role gating.
    /// </summary>
    Task<object?> ExecuteAsync(
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    );
}
