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
using WorldBoxBridge.Threading;

namespace WorldBoxBridge.Commands.Control;

/// <summary>
/// Loads a previously-saved world from disk via <c>SaveManager.loadMapFromBytes</c>.
/// </summary>
/// <remarks>
/// One of the few commands that reports <see cref="RequiresMainThread"/> false while still
/// touching the game. See the property for why.
/// </remarks>
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

    /// <summary>
    /// False, deliberately, even though this command ends in a game call.
    /// </summary>
    /// <remarks>
    /// The dispatcher runs a queued action to completion inside <c>MainThreadDispatcher.Tick</c>,
    /// and its deadline is only checked <em>before</em> an action starts: nothing interrupts one
    /// that has begun. Reading the file from there therefore put an unbounded blocking syscall in
    /// the middle of a frame, and since <c>path</c> accepts absolute paths by contract, one call
    /// naming a FIFO, a character device or a very large file froze the game until the process was
    /// killed. So the argument work, the path resolution and the read all happen on the HTTP
    /// thread, where a stuck read costs one thread-pool thread, and only
    /// <c>loadMapFromBytes</c> is marshalled onto the main thread. Nothing before that call
    /// touches a Unity API: <see cref="GameSavePaths"/> samples <c>persistentDataPath</c> at
    /// start-up precisely so this stays true, and <see cref="GameRefs"/> is a
    /// <c>ConcurrentDictionary</c> over plain reflection.
    /// </remarks>
    public bool RequiresMainThread => false;

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

    public async Task<object?> ExecuteAsync(
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
            throw new BridgeRejectionException(
                ErrorCode.BadArgs,
                "Provide either `path` or `bytes_b64`."
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
                data = SaveFileReader.ReadBounded(readFrom);
            }
            catch (ArgumentException ex)
            {
                // SavePathResolver and SaveFileReader stay pure helpers and signal with
                // ArgumentException; translating at the boundary is the same shape
                // ScreenshotCommand uses for ScreenshotScaler, and it keeps this command's error
                // contract its own.
                throw new BridgeRejectionException(ErrorCode.BadArgs, ex.Message);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The path resolved to something that exists and still could not be read: a
                // directory permission, a locked file, a dead mount. That is the caller's path
                // being wrong, not the game breaking, so it must not surface as 500 GAME_CRASH.
                throw new BridgeRejectionException(
                    ErrorCode.BadArgs,
                    $"could not read '{readFrom}': {ex.Message}"
                );
            }
        }

        _loadMapFromBytes ??= saveMgrType.GetMethod(
            "loadMapFromBytes",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(byte[]) },
            modifiers: null
        );
        // Copied to a local so the closure below does not have to reason about the nullable
        // field, which another request thread may be assigning the same value to concurrently.
        var loadMapFromBytes = _loadMapFromBytes;
        if (loadMapFromBytes == null)
        {
            throw new BridgeRejectionException(
                ErrorCode.GameRejected,
                "SaveManager.loadMapFromBytes(byte[]) not found."
            );
        }

        // The only line that needs a frame. It hands the bytes to the game and returns; the load
        // itself then runs over many frames, which is what `scheduled` below reports.
        await MainThreadDispatcher
            .RunOnMainThreadAsync(
                () =>
                {
                    try
                    {
                        loadMapFromBytes.Invoke(null, new object[] { data });
                    }
                    catch (TargetInvocationException tie)
                    {
                        throw tie.InnerException ?? tie;
                    }
                },
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return new
        {
            scheduled = true,
            bytes = data.Length,
            source = readFrom != null ? "path" : "bytes_b64",
            path = readFrom,
            note = "Load runs asynchronously. Poll get_world_state until tick advances.",
        };
    }
}
