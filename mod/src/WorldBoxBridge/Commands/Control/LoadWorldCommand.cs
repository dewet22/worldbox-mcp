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
/// The only command that reports <see cref="RequiresMainThread"/> false and still calls into
/// the game. The other six false commands never touch Assembly-CSharp at all, so this one is
/// not a pattern to copy without reading the property below.
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
        + "runs asynchronously over many frames. A `path` that is not a regular file, or is "
        + "larger than the save ceiling, comes back as BAD_ARGS rather than being read.";

    /// <summary>
    /// False, deliberately, even though this command ends in a game call.
    /// </summary>
    /// <remarks>
    /// Reading the file from the dispatcher put an unbounded blocking syscall in the middle of a
    /// frame, with no way to interrupt it, and <c>path</c> accepts absolute paths by contract, so
    /// one call naming a FIFO or a very large file froze the game until the process was killed.
    /// Gotcha 11 in <c>docs/game-api-notes.md</c> is the canonical statement of why the deadline
    /// does not save that; do not restate it here, point at it.
    /// <para>So the argument work, the path resolution and the read all happen on the HTTP
    /// thread, and only <c>loadMapFromBytes</c> is marshalled onto the main thread. Nothing
    /// before that call touches a Unity API, which is the invariant the whole change rests on:
    /// <see cref="GameSavePaths"/> is handed <c>persistentDataPath</c> at start-up precisely so
    /// this stays true, and <see cref="GameRefs"/> is a <c>ConcurrentDictionary</c> over plain
    /// reflection.</para>
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
            catch (SaveFileChangedException ex)
            {
                // Not the caller's fault and worth retrying, so not BAD_ARGS: something is
                // writing the file right now. Since the read moved off the main thread it can
                // interleave with the game's own save, which it never could before.
                throw new BridgeRejectionException(ErrorCode.GameRejected, ex.Message);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The path resolved to something that exists and still could not be read: a
                // directory permission, a locked file, a dead mount. That is the caller's path
                // being wrong, not the game breaking, so it must not surface as 500 GAME_CRASH.
                // `readFrom` is null when the resolver itself threw, which PathTooLongException
                // does on net462 from the fully-qualified branch, so fall back to what was asked
                // for rather than reporting an empty path.
                throw new BridgeRejectionException(
                    ErrorCode.BadArgs,
                    $"could not read '{readFrom ?? path}': {ex.Message}"
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
