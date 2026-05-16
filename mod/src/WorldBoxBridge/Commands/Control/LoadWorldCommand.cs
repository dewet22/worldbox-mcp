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
/// Loads a previously-saved world from disk via <c>SaveManager.loadMapFromBytes</c>.
/// </summary>
internal sealed class LoadWorldCommand : ICommand
{
    private readonly GameRefs _refs;
    private MethodInfo? _loadMapFromBytes;

    public LoadWorldCommand(GameRefs refs) => _refs = refs;

    public string Name => "load_world";
    public CommandCategory Category => CommandCategory.Control;
    public string Description =>
        "Loads a world from a save file. Either `bytes_b64` (base64-encoded zipped save) or "
        + "`path` (absolute path to a save file on disk). Like generate_world, the load runs "
        + "asynchronously over many frames.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty(
                "properties",
                new JObject(
                    new JProperty(
                        "path",
                        new JObject(
                            new JProperty("type", "string"),
                            new JProperty(
                                "description",
                                "Absolute path to a .save / .map file produced by save_world."
                            )
                        )
                    ),
                    new JProperty(
                        "bytes_b64",
                        new JObject(
                            new JProperty("type", "string"),
                            new JProperty(
                                "description",
                                "Base64-encoded zipped save bytes. Use when sending the save "
                                    + "directly without writing to disk."
                            )
                        )
                    )
                )
            ),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(JObject args, CancellationToken cancellationToken)
    {
        var path = args.Value<string?>("path");
        var bytesB64 = args.Value<string?>("bytes_b64");

        if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(bytesB64))
        {
            throw new ArgumentException("Provide either `path` or `bytes_b64`.");
        }

        var saveMgrType = _refs.Type("SaveManager");
        if (saveMgrType == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "SaveManager type not found."
            );
        }

        byte[] data;
        if (!string.IsNullOrEmpty(bytesB64))
        {
            try
            {
                data = Convert.FromBase64String(bytesB64!);
            }
            catch (FormatException ex)
            {
                throw new BridgeRejectionException(ErrorCode.BadArgs, $"bytes_b64 invalid: {ex.Message}");
            }
        }
        else
        {
            if (!File.Exists(path))
            {
                throw new BridgeRejectionException(
                    ErrorCode.BadArgs,
                    $"path '{path}' does not exist."
                );
            }
            data = File.ReadAllBytes(path!);
        }

        _loadMapFromBytes ??= saveMgrType.GetMethod(
            "loadMapFromBytes",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(byte[]) },
            modifiers: null
        );
        if (_loadMapFromBytes == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "SaveManager.loadMapFromBytes(byte[]) not found."
            );
        }
        try
        {
            _loadMapFromBytes.Invoke(null, new object[] { data });
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException ?? tie;
        }

        return Task.FromResult<object?>(
            new
            {
                scheduled = true,
                bytes = data.Length,
                source = !string.IsNullOrEmpty(path) ? "path" : "bytes_b64",
                note = "Load runs asynchronously. Poll get_world_state until tick advances.",
            }
        );
    }
}
