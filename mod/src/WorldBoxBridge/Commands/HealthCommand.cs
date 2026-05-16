using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;
using WorldBoxBridge.Threading;

namespace WorldBoxBridge.Commands;

/// <summary>
/// Liveness probe + version snapshot. The very first thing every client calls.
/// </summary>
/// <remarks>
/// Runs off-thread because it only reads cached metadata — there is no value in waiting a
/// frame to answer this one.
/// </remarks>
internal sealed class HealthCommand : ICommand
{
    private readonly VersionInfo _version;
    private readonly BridgeConfig _config;

    public HealthCommand(VersionInfo version, BridgeConfig config)
    {
        _version = version;
        _config = config;
    }

    public string Name => "health";
    public CommandCategory Category => CommandCategory.Meta;
    public string Description =>
        "Returns plugin liveness, mod version, WorldBox version, Unity version and the most recent main-thread tick.";
    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty("properties", new JObject()),
            new JProperty("additionalProperties", false)
        );
    public bool RequiresMainThread => false;

    public Task<object?> ExecuteAsync(JObject args, RequestContext ctx, CancellationToken cancellationToken)
    {
        object payload = new
        {
            ok = true,
            mod_version = _version.ModVersion,
            worldbox_version = _version.WorldBoxVersion,
            unity_version = _version.UnityVersion,
            assembly_csharp_sha256 = _version.AssemblyCSharpSha256,
            tick = MainThreadDispatcher.LastTick,
            enabled = _config.Enabled.Value,
        };
        return Task.FromResult<object?>(payload);
    }
}
