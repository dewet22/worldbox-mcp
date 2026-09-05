using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace WorldBoxBridge.Threading;

/// <summary>
/// Marshals work from arbitrary threads onto Unity's main thread by injecting a callback
/// directly into the engine's <see cref="PlayerLoop"/>.
/// </summary>
/// <remarks>
/// <para><b>Why not a <see cref="MonoBehaviour"/>?</b> On WorldBox 0.51.2 (and likely other
/// Unity games), <see cref="MonoBehaviour"/> instances created from a BepInEx plugin's Awake
/// can be destroyed by Unity shortly after, even when placed on a <c>DontDestroyOnLoad</c>
/// GameObject. The plugin code never sees the destruction (no exception, just a silent stop
/// of <c>Update()</c>), which is the worst kind of failure.</para>
///
/// <para><b>PlayerLoop instead:</b> we hook a delegate into Unity's internal frame loop, /// the same mechanism the engine uses for built-in subsystems like the physics tick or the
/// animation update. This delegate lives in the engine's player-loop table, not as a managed
/// Component, so GameObject lifecycle quirks can't reach it. The same pattern is used by
/// <a href="https://github.com/BepInEx/RuntimeUnityEditor">RuntimeUnityEditor</a> and most
/// long-running BepInEx plugins for the same reason.</para>
///
/// <para><b>Per-action timeout:</b> if a reflection call wedges, the HTTP handler still gets
/// <c>MAIN_THREAD_TIMEOUT</c> back instead of hanging indefinitely.</para>
/// </remarks>
public static class MainThreadDispatcher
{
    private static readonly ConcurrentQueue<PendingAction> Queue = new();
    private static ManualLogSource? _log;
    private static bool _registered;

    /// <summary>Latest <c>Time.frameCount</c> sampled on the main thread.</summary>
    public static int LastTick { get; private set; }

    /// <summary>Number of times our PlayerLoop callback has fired since startup.</summary>
    public static long UpdateCount { get; private set; }

    /// <summary>Unity version string sampled on the main thread.</summary>
    public static string? UnityVersion { get; private set; }

    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Unique marker type identifying our subsystem inside the PlayerLoop tree. Using a
    /// distinct private type means we never collide with built-in or third-party subsystems,
    /// and we can spot our entry on subsequent reads of the loop.
    /// </summary>
    private struct WorldBoxBridgeTick { }

    /// <summary>Injects the dispatcher into Unity's PlayerLoop. Idempotent.</summary>
    public static void Bootstrap(ManualLogSource log)
    {
        if (_registered)
        {
            return;
        }
        _log = log;

        var loop = PlayerLoop.GetCurrentPlayerLoop();
        if (!TryInjectInto(ref loop, typeof(Update)))
        {
            log.LogError(
                "Could not find the Update phase in Unity's PlayerLoop, dispatcher disabled. "
                    + "Commands that require main-thread access will fail."
            );
            return;
        }
        PlayerLoop.SetPlayerLoop(loop);
        _registered = true;
        log.LogInfo(
            "[dispatcher] injected into Unity PlayerLoop Update phase, survives MonoBehaviour lifecycle."
        );
    }

    /// <summary>Schedules <paramref name="work"/> on the next Unity frame.</summary>
    public static Task<T> RunOnMainThreadAsync<T>(
        Func<T> work,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        if (work == null)
        {
            throw new ArgumentNullException(nameof(work));
        }
        if (!_registered)
        {
            throw new InvalidOperationException(
                "MainThreadDispatcher not registered. Call Bootstrap() during plugin Awake()."
            );
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var effectiveTimeout = timeout ?? DefaultTimeout;

        Queue.Enqueue(
            new PendingAction(
                run: () =>
                {
                    try
                    {
                        tcs.TrySetResult(work());
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                },
                isAlreadyDone: () => tcs.Task.IsCompleted,
                onTimeout: () =>
                    tcs.TrySetException(
                        new TimeoutException(
                            $"Action exceeded its deadline of {effectiveTimeout.TotalSeconds:F1}s before reaching the main thread."
                        )
                    ),
                deadline: DateTime.UtcNow + effectiveTimeout
            )
        );

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }

    /// <inheritdoc cref="RunOnMainThreadAsync{T}(Func{T}, TimeSpan?, CancellationToken)"/>
    public static Task RunOnMainThreadAsync(
        Action work,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return RunOnMainThreadAsync<object?>(
            () =>
            {
                work();
                return null;
            },
            timeout,
            cancellationToken
        );
    }

    private static void Tick()
    {
        UpdateCount++;
        LastTick = Time.frameCount;
        UnityVersion ??= Application.unityVersion;

        // Bound the per-frame work to avoid frame stutter from request bursts.
        const int maxPerFrame = 32;
        for (var i = 0; i < maxPerFrame; i++)
        {
            if (!Queue.TryDequeue(out var pending))
            {
                return;
            }
            if (pending.IsAlreadyDone())
            {
                continue;
            }
            if (DateTime.UtcNow > pending.Deadline)
            {
                pending.OnTimeout();
                continue;
            }
            try
            {
                pending.Run();
            }
            catch (Exception ex)
            {
                _log?.LogError($"Dispatcher caught unhandled exception: {ex}");
            }
        }
    }

    /// <summary>
    /// Recursively walks the PlayerLoop tree looking for the given phase type and appends our
    /// tick subsystem as its last child. Returns true on success.
    /// </summary>
    private static bool TryInjectInto(ref PlayerLoopSystem root, Type phase)
    {
        if (root.subSystemList == null)
        {
            return false;
        }
        var subs = new List<PlayerLoopSystem>(root.subSystemList);
        for (var i = 0; i < subs.Count; i++)
        {
            if (subs[i].type == phase)
            {
                var target = subs[i];
                var children = new List<PlayerLoopSystem>(
                    target.subSystemList ?? Array.Empty<PlayerLoopSystem>()
                );
                children.Add(
                    new PlayerLoopSystem
                    {
                        type = typeof(WorldBoxBridgeTick),
                        updateDelegate = Tick,
                    }
                );
                target.subSystemList = children.ToArray();
                subs[i] = target;
                root.subSystemList = subs.ToArray();
                return true;
            }
            // Recurse so we still work if Unity reorganises the loop someday.
            var child = subs[i];
            if (TryInjectInto(ref child, phase))
            {
                subs[i] = child;
                root.subSystemList = subs.ToArray();
                return true;
            }
        }
        return false;
    }

    private readonly struct PendingAction
    {
        public PendingAction(
            Action run,
            Func<bool> isAlreadyDone,
            Action onTimeout,
            DateTime deadline
        )
        {
            Run = run;
            IsAlreadyDone = isAlreadyDone;
            OnTimeout = onTimeout;
            Deadline = deadline;
        }

        public Action Run { get; }
        public Func<bool> IsAlreadyDone { get; }
        public Action OnTimeout { get; }
        public DateTime Deadline { get; }
    }
}
