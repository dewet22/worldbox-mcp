using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;

namespace WorldBoxBridge.Commands.Control;

/// <summary>
/// Writes the current world to disk via <c>SaveManager.saveWorldToDirectory</c>.
/// </summary>
internal sealed class SaveWorldCommand : ICommand
{
    private readonly GameRefs _refs;
    private readonly WorldAccess _world;
    private MethodInfo? _saveWorldToDirectory;

    public SaveWorldCommand(GameRefs refs, WorldAccess world)
    {
        _refs = refs;
        _world = world;
    }

    public string Name => "save_world";
    public CommandCategory Category => CommandCategory.Control;
    public string Description =>
        "Saves the current world to a folder on disk. `folder` is required (absolute path); "
        + "the directory is created if missing. The save format is compatible with the "
        + "in-game load UI.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty(
                        "folder",
                        new JObject(
                            new JProperty("type", "string"),
                            new JProperty(
                                "description",
                                "Absolute path to the target save folder (created if missing)."
                            )
                        )
                    ),
                    new JProperty(
                        "compress",
                        new JObject(
                            new JProperty("type", "boolean"),
                            new JProperty("default", true)
                        )
                    )
                )
            ),
            new JProperty("required", new JArray("folder")),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, CancellationToken cancellationToken)
    {
        var folder = args.Value<string?>("folder");
        var compress = args.Value<bool?>("compress") ?? true;

        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("folder is required (absolute path).");
        }

        // Pre-flight: a save only makes sense when a world is actually loaded.
        if ((_world.Width ?? 0) == 0)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "No world is currently loaded. Start or load a world in-game first."
            );
        }

        var saveMgrType = _refs.Type("SaveManager");
        if (saveMgrType == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "SaveManager type not found."
            );
        }

        _saveWorldToDirectory ??= saveMgrType.GetMethod(
            "saveWorldToDirectory",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string), typeof(bool), typeof(bool) },
            modifiers: null
        );
        if (_saveWorldToDirectory == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "SaveManager.saveWorldToDirectory(string, bool, bool) not found."
            );
        }
        try
        {
            var savedMap = _saveWorldToDirectory.Invoke(
                null,
                new object[] { folder!, compress, true }
            );
            return Task.FromResult<object?>(
                new
                {
                    saved = savedMap != null,
                    folder = folder,
                    compressed = compress,
                }
            );
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException ?? tie;
        }
    }

}
