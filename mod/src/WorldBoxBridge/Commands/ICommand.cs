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
