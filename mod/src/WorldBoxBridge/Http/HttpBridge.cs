using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Threading;

namespace WorldBoxBridge.Http;

/// <summary>
/// Hosts the local HTTP API. Owns the <see cref="HttpListener"/>, the auth check, the routing
/// table and the JSON envelope shaping. Knows nothing about the game — delegates everything
/// concrete to <see cref="ICommand"/> implementations.
/// </summary>
/// <remarks>
/// Threading: <c>HttpListener.GetContextAsync</c> returns on a thread pool thread. Every
/// request is processed entirely off the Unity main thread; commands that need game state
/// hop onto the main thread via <see cref="MainThreadDispatcher"/>.
/// </remarks>
internal sealed class HttpBridge : IDisposable
{
    private readonly ManualLogSource _log;
    private readonly BridgeConfig _config;
    private readonly CommandRegistry _registry;
    private readonly VersionInfo _version;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
        ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver(),
    };

    public HttpBridge(
        ManualLogSource log,
        BridgeConfig config,
        CommandRegistry registry,
        VersionInfo version
    )
    {
        _log = log;
        _config = config;
        _registry = registry;
        _version = version;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{_config.Host.Value}:{_config.Port.Value}/");
    }

    public void Start()
    {
        _config.AssertLoopbackOnly();
        _listener.Start();
        _log.LogInfo(
            $"listening on http://{_config.Host.Value}:{_config.Port.Value} "
                + $"(commands={_registry.Count}, token=<configured>)"
        );
        _loop = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError($"HttpListener.GetContextAsync failed: {ex}");
                continue;
            }

            // Fire-and-forget per request — never block the accept loop.
            _ = Task.Run(() => HandleAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken
    )
    {
        var req = context.Request;
        var res = context.Response;
        try
        {
            if (!_config.Enabled.Value)
            {
                await WriteErrorAsync(
                    res,
                    HttpStatusCode.ServiceUnavailable,
                    ErrorCode.Disabled,
                    "WorldBoxBridge is disabled. Set enabled = true in WorldBoxBridge.cfg."
                );
                return;
            }

            if (!IsAuthorized(req))
            {
                await WriteErrorAsync(
                    res,
                    HttpStatusCode.Unauthorized,
                    ErrorCode.Unauthorized,
                    "Missing or invalid X-WB-Token header."
                );
                return;
            }

            var path = req.Url?.AbsolutePath ?? "/";
            switch (path)
            {
                case "/health" when req.HttpMethod == "GET":
                    await ExecuteAndWriteAsync(res, "health", new JObject(), cancellationToken);
                    return;

                case "/cmd" when req.HttpMethod == "POST":
                    await HandleCommandAsync(req, res, cancellationToken);
                    return;

                case "/capabilities" when req.HttpMethod == "GET":
                    await WriteCapabilitiesAsync(res);
                    return;

                default:
                    await WriteErrorAsync(
                        res,
                        HttpStatusCode.NotFound,
                        ErrorCode.UnknownCommand,
                        $"No route for {req.HttpMethod} {path}."
                    );
                    return;
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Unhandled error in HandleAsync: {ex}");
            try
            {
                await WriteErrorAsync(
                    res,
                    HttpStatusCode.InternalServerError,
                    ErrorCode.Internal,
                    ex.Message,
                    exception: ExceptionInfo.From(ex)
                );
            }
            catch
            {
                // Best-effort.
            }
        }
        finally
        {
            try
            {
                res.OutputStream.Close();
            }
            catch
            {
                // Already closed.
            }
        }
    }

    private async Task HandleCommandAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken cancellationToken
    )
    {
        JObject? body;
        try
        {
            using var reader = new StreamReader(
                req.InputStream,
                req.ContentEncoding ?? Encoding.UTF8
            );
            var raw = await reader.ReadToEndAsync().ConfigureAwait(false);
            body = string.IsNullOrWhiteSpace(raw) ? new JObject() : JObject.Parse(raw);
        }
        catch (JsonException ex)
        {
            await WriteErrorAsync(
                res,
                HttpStatusCode.BadRequest,
                ErrorCode.BadArgs,
                $"Request body is not valid JSON: {ex.Message}"
            );
            return;
        }

        var name = (string?)body["name"];
        if (string.IsNullOrWhiteSpace(name))
        {
            await WriteErrorAsync(
                res,
                HttpStatusCode.BadRequest,
                ErrorCode.BadArgs,
                "Body must contain a non-empty 'name' field."
            );
            return;
        }

        var args = body["args"] as JObject ?? new JObject();
        await ExecuteAndWriteAsync(res, name!, args, cancellationToken);
    }

    private async Task ExecuteAndWriteAsync(
        HttpListenerResponse res,
        string name,
        JObject args,
        CancellationToken cancellationToken
    )
    {
        if (!_registry.TryGet(name, out var command))
        {
            await WriteErrorAsync(
                res,
                HttpStatusCode.NotFound,
                ErrorCode.UnknownCommand,
                $"No command named '{name}'.",
                commandName: name,
                args: args
            );
            return;
        }

        try
        {
            object? result;
            if (command.RequiresMainThread)
            {
                result = await MainThreadDispatcher
                    .RunOnMainThreadAsync(
                        () => command.ExecuteAsync(args, cancellationToken).GetAwaiter().GetResult(),
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                result = await command.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            }

            await WriteSuccessAsync(res, result);
        }
        catch (TimeoutException tex)
        {
            await WriteErrorAsync(
                res,
                HttpStatusCode.GatewayTimeout,
                ErrorCode.MainThreadTimeout,
                tex.Message,
                commandName: name,
                args: args,
                exception: ExceptionInfo.From(tex)
            );
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(
                res,
                HttpStatusCode.InternalServerError,
                ErrorCode.GameCrash,
                ex.Message,
                commandName: name,
                args: args,
                exception: ExceptionInfo.From(ex)
            );
        }
    }

    private async Task WriteCapabilitiesAsync(HttpListenerResponse res)
    {
        var commands = new JArray();
        foreach (var cmd in _registry.All)
        {
            commands.Add(
                new JObject
                {
                    ["name"] = cmd.Name,
                    ["category"] = cmd.Category.ToString().ToLowerInvariant(),
                    ["description"] = cmd.Description,
                    ["requires_main_thread"] = cmd.RequiresMainThread,
                    ["schema"] = cmd.ArgsSchema,
                }
            );
        }

        var payload = new JObject
        {
            ["mod_version"] = _version.ModVersion,
            ["worldbox_version"] = _version.WorldBoxVersion,
            ["unity_version"] = _version.UnityVersion,
            ["assembly_csharp_sha256"] = _version.AssemblyCSharpSha256,
            ["commands"] = commands,
        };

        await WriteJsonAsync(res, HttpStatusCode.OK, payload.ToString(Formatting.None));
    }

    private async Task WriteSuccessAsync(HttpListenerResponse res, object? result)
    {
        var envelope = new SuccessEnvelope
        {
            Result = result,
            Tick = MainThreadDispatcher.LastTick,
        };
        var json = JsonConvert.SerializeObject(envelope, JsonSettings);
        await WriteJsonAsync(res, HttpStatusCode.OK, json);
    }

    private async Task WriteErrorAsync(
        HttpListenerResponse res,
        HttpStatusCode status,
        string code,
        string message,
        string? commandName = null,
        JObject? args = null,
        System.Collections.Generic.IReadOnlyList<string>? didYouMean = null,
        ExceptionInfo? exception = null
    )
    {
        var envelope = new ErrorEnvelope
        {
            Error = new ErrorDetail
            {
                Code = code,
                Message = message,
                Command = commandName,
                Args = args,
                DidYouMean = didYouMean,
                Exception = exception,
            },
        };
        var json = JsonConvert.SerializeObject(envelope, JsonSettings);
        await WriteJsonAsync(res, status, json);
    }

    private async Task WriteJsonAsync(HttpListenerResponse res, HttpStatusCode status, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        res.StatusCode = (int)status;
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private bool IsAuthorized(HttpListenerRequest req)
    {
        var presented = req.Headers["X-WB-Token"];
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }
        var expected = _config.Token.Value;
        return FixedTimeEquals(presented!, expected);
    }

    /// <summary>Constant-time string comparison to avoid timing oracles on the token.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            // Length difference itself is not secret, so an early-return is fine.
            return false;
        }
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // ignore
        }
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // ignore
        }
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore
        }
        _cts.Dispose();
    }
}
