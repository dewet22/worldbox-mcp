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
using WorldBoxBridge.Session;

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
        "Loads a world from a save. Either `bytes_b64` (base64-encoded zipped save) or "
        + "`path`: a save file, a save folder, or a name under the game's saves directory "
        + "(e.g. `save1`, or whatever save_world returned). Like generate_world, the load "
        + "runs asynchronously over many frames.";
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
                                "Save file, save folder, or a name under the game's saves "
                                    + "directory (e.g. 'save1')."
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
                                    + "directly without writing to disk. Wins over `path` if "
                                    + "both are supplied; the response's `source` says which "
                                    + "was read."
                            )
                        )
                    )
                )
            ),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
    {
        ctx.Require(Permission.ControlWorld);
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

        // bytes_b64 wins when both are supplied. `readFrom` records which branch actually ran
        // so the response can say so: it used to be derived from `path` being non-empty, which
        // reported source "path" for a load that read nothing but the base64 payload.
        byte[] data;
        string? readFrom = null;
        if (!string.IsNullOrEmpty(bytesB64))
        {
            try
            {
                data = Convert.FromBase64String(bytesB64!);
            }
            catch (FormatException ex)
            {
                throw new BridgeRejectionException(
                    ErrorCode.BadArgs,
                    $"bytes_b64 invalid: {ex.Message}"
                );
            }
        }
        else
        {
            try
            {
                readFrom = SavePathResolver.ResolveMapFile(path, GameSavePaths.SavesRoot);
            }
            catch (ArgumentException ex)
            {
                throw new BridgeRejectionException(ErrorCode.BadArgs, ex.Message);
            }
            data = File.ReadAllBytes(readFrom);
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
                source = readFrom != null ? "path" : "bytes_b64",
                path = readFrom,
                note = "Load runs asynchronously. Poll get_world_state until tick advances.",
            }
        );
    }
}
