using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;
using WorldBoxBridge.Threading;
using SessionState = WorldBoxBridge.Session.Session;

namespace WorldBoxBridge.Http;

/// <summary>
/// Hosts the local HTTP API on a raw <see cref="TcpListener"/>.
/// </summary>
/// <remarks>
/// <para><b>Why not <see cref="System.Net.HttpListener"/>?</b> Under Unity's Mono runtime
/// (verified on Unity 2022.3.60f1), <c>HttpListener.Start()</c> returns successfully and
/// <c>IsListening</c> reports <c>true</c>, but no TCP socket is actually bound. This is a
/// long-standing bug in Mono's managed HTTP implementation — see
/// <see href="https://discussions.unity.com/t/httplistener-ignores-port-on-some-windows-platform-s/755558"/>
/// for the discussion. <see cref="TcpListener"/> bypasses the broken managed HTTP layer and
/// goes straight to the platform socket APIs, which work reliably.</para>
///
/// <para>The HTTP/1.1 subset we implement is intentionally minimal: connection-per-request,
/// no keep-alive, no chunked transfer encoding, no compression. Everything we need for a
/// loopback control plane and nothing more.</para>
/// </remarks>
internal sealed class HttpBridge : IDisposable
{
    /// <summary>
    /// Anti-GC anchor. Even if the BepInEx plugin MonoBehaviour gets destroyed (which happens
    /// on this game shortly after Awake), this static keeps the bridge instance alive for the
    /// full process lifetime so the accept thread + socket survive.
    /// </summary>
    private static HttpBridge? _alive;

    private readonly ManualLogSource _log;
    private readonly BridgeConfig _config;
    private readonly CommandRegistry _registry;
    private readonly VersionInfo _version;
    private readonly SessionState _session;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <summary>Per-connection read timeout. Local agents respond fast — any longer = wedged.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(35);
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
    };

    public HttpBridge(
        ManualLogSource log,
        BridgeConfig config,
        CommandRegistry registry,
        VersionInfo version,
        SessionState session
    )
    {
        _log = log;
        _config = config;
        _registry = registry;
        _version = version;
        _session = session;
        // Mono Unity quirk: IPAddress.Parse("127.0.0.1") does not always behave the same as
        // the IPAddress.Loopback constant. Several Unity dev threads document the listener
        // silently failing to bind with Parse'd addresses where the constant works fine.
        // We treat the common loopback strings as aliases for the constant; other addresses
        // go through Parse normally.
        var host = _config.Host.Value;
        IPAddress bindAddress = host switch
        {
            "127.0.0.1" or "localhost" => IPAddress.Loopback,
            "::1" => IPAddress.IPv6Loopback,
            _ => IPAddress.Parse(host),
        };
        _log.LogInfo(
            $"[diag] resolved host '{host}' to IPAddress={bindAddress} (family={bindAddress.AddressFamily})"
        );
        _listener = new TcpListener(bindAddress, _config.Port.Value);
    }

    public void Start()
    {
        _config.AssertLoopbackOnly();
        _log.LogInfo("[diag] about to call _listener.Start()...");
        try
        {
            _listener.Start();
        }
        catch (Exception ex)
        {
            _log.LogError($"[diag] _listener.Start() THREW: {ex.GetType().FullName}: {ex.Message}");
            throw;
        }
        var sock = _listener.Server;
        _log.LogInfo(
            $"[diag] after Start(): IsBound={sock.IsBound} "
                + $"LocalEndPoint={sock.LocalEndPoint} "
                + $"Handle={sock.Handle} "
                + $"AddressFamily={sock.AddressFamily} "
                + $"SocketType={sock.SocketType} "
                + $"ProtocolType={sock.ProtocolType}"
        );
        _log.LogInfo(
            $"listening on http://{_config.Host.Value}:{_config.Port.Value} "
                + $"(commands={_registry.Count}, agents={_session.Agents.Count}, "
                + $"scenario={_session.ScenarioPreset}, legacy_mode={_session.Agents.IsLegacyMode})"
        );
        // Use a dedicated NON-background thread.
        //   - Mono's thread pool inside Unity has shown odd behavior with long-lived tasks.
        //   - A plain Thread bypasses the pool entirely.
        //   - IsBackground=false would prevent process shutdown until the thread exits, so we
        //     keep IsBackground=true but anchor the listener via _alive (anti-GC) instead.
        _alive = this;
        var t = new Thread(AcceptLoopBlocking)
        {
            IsBackground = true,
            Name = "WorldBoxBridge.Accept",
        };
        t.Start();
        _loop = Task.CompletedTask;
        _log.LogInfo($"[diag] accept thread started (Id={t.ManagedThreadId}, IsBackground={t.IsBackground})");
    }

    private void AcceptLoopBlocking()
    {
        _log.LogInfo(
            $"[accept-thread] entered. listener.IsBound={_listener.Server.IsBound} "
                + $"LocalEndPoint={_listener.Server.LocalEndPoint}"
        );
        // (self-connect probe removed — it triggered OnDestroy in some Unity configurations)
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException sex) when (_cts.IsCancellationRequested)
            {
                _log.LogInfo($"[accept-thread] socket closed during shutdown: {sex.Message}");
                return;
            }
            catch (Exception ex)
            {
                _log.LogError($"[accept-thread] AcceptTcpClient threw: {ex.GetType().Name}: {ex.Message}");
                Thread.Sleep(200);
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, _cts.Token));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            client.ReceiveTimeout = (int)ReadTimeout.TotalMilliseconds;
            client.SendTimeout = (int)ReadTimeout.TotalMilliseconds;

            try
            {
                using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                if (request == null)
                {
                    return; // empty / malformed; drop silently
                }

                var response = await RouteAsync(request, cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError($"HandleClientAsync error: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Request parsing
    // ──────────────────────────────────────────────────────────────────────

    private sealed class HttpRequest
    {
        public string Method { get; set; } = "GET";
        public string Path { get; set; } = "/";
        public Dictionary<string, string> Headers { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public byte[] Body { get; set; } = Array.Empty<byte>();

        public string? GetHeader(string name)
        {
            return Headers.TryGetValue(name, out var v) ? v : null;
        }
    }

    private async Task<HttpRequest?> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        var headers = await ReadHeadersAsync(stream, cancellationToken).ConfigureAwait(false);
        if (headers.TotalRead == 0)
        {
            return null; // peer closed before sending anything
        }
        var buffer = headers.Buffer;
        var totalRead = headers.TotalRead;
        var headerEnd = headers.HeaderEnd;
        var headerText = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return null;
        }

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
        {
            return null;
        }
        var req = new HttpRequest
        {
            Method = requestLine[0].ToUpperInvariant(),
            Path = requestLine[1],
        };

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }
            req.Headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
        }

        // Body — only if Content-Length is present and positive.
        if (int.TryParse(req.GetHeader("Content-Length"), out var len) && len > 0)
        {
            if (len > MaxBodyBytes)
            {
                throw new InvalidOperationException(
                    $"Body of {len} bytes exceeds {MaxBodyBytes} byte limit."
                );
            }
            req.Body = new byte[len];

            // For small requests, the body often arrives in the same TCP read as the headers.
            // Copy whatever leftover bytes the header read already consumed into req.Body before
            // pulling more from the stream — otherwise we'd block forever (or, with timeouts,
            // throw EndOfStreamException on a connection that's perfectly fine).
            var leftover = totalRead - headerEnd;
            if (leftover > 0)
            {
                var take = System.Math.Min(leftover, len);
                Array.Copy(buffer, headerEnd, req.Body, 0, take);
            }
            var read = System.Math.Min(leftover, len);
            while (read < len)
            {
                var chunk = await stream
                    .ReadAsync(req.Body, read, len - read, cancellationToken)
                    .ConfigureAwait(false);
                if (chunk <= 0)
                {
                    throw new EndOfStreamException("Client closed connection mid-body.");
                }
                read += chunk;
            }
        }

        return req;
    }

    /// <summary>
    /// Triple returned by <see cref="ReadHeadersAsync"/>. Plain struct rather than a
    /// <c>ValueTuple</c> — <c>System.ValueTuple</c> isn't always loadable under Unity's
    /// Mono runtime (out-of-band package on net462).
    /// </summary>
    private readonly struct HeaderReadResult
    {
        public HeaderReadResult(byte[] buffer, int totalRead, int headerEnd)
        {
            Buffer = buffer;
            TotalRead = totalRead;
            HeaderEnd = headerEnd;
        }

        public byte[] Buffer { get; }
        public int TotalRead { get; }
        public int HeaderEnd { get; }
    }

    /// <summary>
    /// Reads from the stream until the empty-line CRLF CRLF terminator. Returns the buffer,
    /// total bytes read, and the offset where headers end — so the caller can recover any body
    /// bytes that arrived in the same TCP read as the headers.
    /// </summary>
    private static async Task<HeaderReadResult> ReadHeadersAsync(
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[MaxHeaderBytes];
        var pos = 0;
        while (pos < MaxHeaderBytes)
        {
            var n = await stream
                .ReadAsync(buffer, pos, MaxHeaderBytes - pos, cancellationToken)
                .ConfigureAwait(false);
            if (n <= 0)
            {
                if (pos == 0)
                {
                    return new HeaderReadResult(buffer, 0, 0);
                }
                break;
            }
            pos += n;

            for (var i = 3; i < pos; i++)
            {
                if (
                    buffer[i - 3] == (byte)'\r'
                    && buffer[i - 2] == (byte)'\n'
                    && buffer[i - 1] == (byte)'\r'
                    && buffer[i] == (byte)'\n'
                )
                {
                    return new HeaderReadResult(buffer, pos, i + 1);
                }
            }
        }
        throw new InvalidOperationException(
            $"Request header exceeds {MaxHeaderBytes} bytes — refusing."
        );
    }

    // ──────────────────────────────────────────────────────────────────────
    // Routing — same logic as before, just using HttpRequest instead of HttpListenerRequest
    // ──────────────────────────────────────────────────────────────────────

    private sealed class HttpResponse
    {
        public int Status { get; set; } = 200;
        public string StatusText { get; set; } = "OK";
        public string ContentType { get; set; } = "application/json; charset=utf-8";
        public byte[] Body { get; set; } = Array.Empty<byte>();
    }

    private async Task<HttpResponse> RouteAsync(
        HttpRequest req,
        CancellationToken cancellationToken
    )
    {
        if (!_config.Enabled.Value)
        {
            return ErrorResponse(
                503,
                "Service Unavailable",
                ErrorCode.Disabled,
                "WorldBoxBridge is disabled. Set enabled = true in WorldBoxBridge.cfg."
            );
        }
        var ctx = Authenticate(req);
        if (ctx == null)
        {
            return ErrorResponse(
                401,
                "Unauthorized",
                ErrorCode.Unauthorized,
                "Missing or invalid credential. Send either 'Authorization: Bearer <token>' "
                    + "or the legacy 'X-WB-Token: <token>' header."
            );
        }

        var path = req.Path.Split('?')[0];
        if (path == "/health" && req.Method == "GET")
        {
            return await ExecuteCommandAsync("health", new JObject(), ctx.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        if (path == "/cmd" && req.Method == "POST")
        {
            return await HandleCommandAsync(req, ctx.Value, cancellationToken).ConfigureAwait(false);
        }
        if (path == "/capabilities" && req.Method == "GET")
        {
            return CapabilitiesResponse();
        }

        return ErrorResponse(
            404,
            "Not Found",
            ErrorCode.UnknownCommand,
            $"No route for {req.Method} {path}."
        );
    }

    private async Task<HttpResponse> HandleCommandAsync(
        HttpRequest req,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
    {
        JObject body;
        try
        {
            var raw = req.Body.Length == 0 ? "{}" : Encoding.UTF8.GetString(req.Body);
            body = JObject.Parse(raw);
        }
        catch (JsonException ex)
        {
            return ErrorResponse(
                400,
                "Bad Request",
                ErrorCode.BadArgs,
                $"Request body is not valid JSON: {ex.Message}"
            );
        }

        var name = (string?)body["name"];
        if (string.IsNullOrWhiteSpace(name))
        {
            return ErrorResponse(
                400,
                "Bad Request",
                ErrorCode.BadArgs,
                "Body must contain a non-empty 'name' field."
            );
        }
        var args = body["args"] as JObject ?? new JObject();
        return await ExecuteCommandAsync(name!, args, ctx, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponse> ExecuteCommandAsync(
        string name,
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
    {
        if (!_registry.TryGet(name, out var command))
        {
            return ErrorResponse(
                404,
                "Not Found",
                ErrorCode.UnknownCommand,
                $"No command named '{name}'.",
                commandName: name,
                args: args
            );
        }

        try
        {
            // Capture ctx in locals so the closure passed to MainThreadDispatcher captures the
            // struct by value (instance is small; struct copies dodge a closure-allocation surprise).
            var capturedCtx = ctx;
            object? result;
            if (command.RequiresMainThread)
            {
                result = await MainThreadDispatcher
                    .RunOnMainThreadAsync(
                        () => command.ExecuteAsync(args, capturedCtx, cancellationToken).GetAwaiter().GetResult(),
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                result = await command.ExecuteAsync(args, ctx, cancellationToken).ConfigureAwait(false);
            }
            return SuccessResponse(result);
        }
        catch (TimeoutException tex)
        {
            return ErrorResponse(
                504,
                "Gateway Timeout",
                ErrorCode.MainThreadTimeout,
                tex.Message,
                commandName: name,
                args: args,
                exception: ExceptionInfo.From(tex)
            );
        }
        catch (WorldBoxBridge.Commands.Action.BridgeRejectionException brex)
        {
            // Structured rejection from a command — map directly to its error code.
            var status = brex.Code switch
            {
                ErrorCode.UnknownAsset => 400,
                ErrorCode.OutOfBounds => 400,
                ErrorCode.BadArgs => 400,
                ErrorCode.GameRejected => 422,
                ErrorCode.PermissionDenied => 403,
                ErrorCode.FactionScopeViolation => 403,
                ErrorCode.TurnNotYours => 409,
                _ => 500,
            };
            return ErrorResponse(
                status,
                "Rejected",
                brex.Code,
                brex.Message,
                commandName: name,
                args: args,
                didYouMean: brex.DidYouMean
            );
        }
        catch (Exception ex)
        {
            return ErrorResponse(
                500,
                "Internal Server Error",
                ErrorCode.GameCrash,
                ex.Message,
                commandName: name,
                args: args,
                exception: ExceptionInfo.From(ex)
            );
        }
    }

    private HttpResponse CapabilitiesResponse()
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
        return new HttpResponse
        {
            Status = 200,
            StatusText = "OK",
            Body = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)),
        };
    }

    private HttpResponse SuccessResponse(object? result)
    {
        var envelope = new SuccessEnvelope
        {
            Result = result,
            Tick = MainThreadDispatcher.LastTick,
        };
        return new HttpResponse
        {
            Status = 200,
            StatusText = "OK",
            Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope, JsonSettings)),
        };
    }

    private static HttpResponse ErrorResponse(
        int status,
        string statusText,
        string code,
        string message,
        string? commandName = null,
        JObject? args = null,
        IReadOnlyList<string>? didYouMean = null,
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
        return new HttpResponse
        {
            Status = status,
            StatusText = statusText,
            Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope, JsonSettings)),
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // Wire output
    // ──────────────────────────────────────────────────────────────────────

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        HttpResponse response,
        CancellationToken cancellationToken
    )
    {
        var header =
            $"HTTP/1.1 {response.Status} {response.StatusText}\r\n"
            + $"Content-Type: {response.ContentType}\r\n"
            + $"Content-Length: {response.Body.Length}\r\n"
            + "Connection: close\r\n"
            + "Cache-Control: no-store\r\n"
            + "\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken)
            .ConfigureAwait(false);
        if (response.Body.Length > 0)
        {
            await stream
                .WriteAsync(response.Body, 0, response.Body.Length, cancellationToken)
                .ConfigureAwait(false);
        }
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Auth — extracts a bearer credential from either the new
    // 'Authorization: Bearer <token>' header (multi-agent) or the legacy
    // 'X-WB-Token: <token>' header (v0.1–v0.2 single-tenant clients).
    // Looks the token up in the AgentRegistry; returns a RequestContext on
    // success, null otherwise. Constant-time lookup happens inside the
    // registry — see AgentRegistry.FixedTimeEquals.
    // ──────────────────────────────────────────────────────────────────────

    private RequestContext? Authenticate(HttpRequest req)
    {
        var presented = ExtractToken(req);
        if (string.IsNullOrEmpty(presented))
        {
            return null;
        }
        var agent = _session.Agents.TryAuthenticate(presented);
        if (agent == null)
        {
            return null;
        }
        return _session.ContextFor(agent);
    }

    private static string? ExtractToken(HttpRequest req)
    {
        var auth = req.GetHeader("Authorization");
        if (!string.IsNullOrEmpty(auth))
        {
            const string prefix = "Bearer ";
            if (auth!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return auth.Substring(prefix.Length).Trim();
            }
        }
        var legacy = req.GetHeader("X-WB-Token");
        return string.IsNullOrEmpty(legacy) ? null : legacy;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────────────

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
