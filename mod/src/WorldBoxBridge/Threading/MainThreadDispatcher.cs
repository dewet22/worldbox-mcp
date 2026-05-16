using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using UnityEngine;

namespace WorldBoxBridge.Threading;

/// <summary>
/// Marshals work from arbitrary threads (e.g. the HTTP thread pool) onto Unity's main thread.
/// </summary>
/// <remarks>
/// Unity's API surface is overwhelmingly not thread-safe. Anything that reads or writes game
/// state must run on the main thread — the thread that drives <see cref="MonoBehaviour"/>'s
/// <c>Update</c> loop. This component is a hidden <c>MonoBehaviour</c> attached to a persistent
/// GameObject; it drains a concurrent queue of actions on every frame.
///
/// <para>The per-action timeout is a safety valve: if a reflection call deadlocks or enters an
/// infinite loop, the HTTP handler still returns a <c>MAIN_THREAD_TIMEOUT</c> response instead
/// of leaving the caller hanging forever.</para>
/// </remarks>
public sealed class MainThreadDispatcher : MonoBehaviour
{
    private static MainThreadDispatcher? _instance;
    private static readonly ConcurrentQueue<PendingAction> Queue = new();
    private static ManualLogSource? _log;

    /// <summary>Latest <c>Time.frameCount</c> captured on the main thread, safe to read from any thread.</summary>
    public static int LastTick { get; private set; }

    /// <summary>Unity version string captured on the main thread.</summary>
    public static string? UnityVersion { get; private set; }

    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal static void Bootstrap(ManualLogSource log)
    {
        if (_instance != null)
        {
            return;
        }
        _log = log;
        var go = new GameObject("WorldBoxBridge.MainThreadDispatcher");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        _instance = go.AddComponent<MainThreadDispatcher>();
    }

    /// <summary>
    /// Schedules <paramref name="work"/> on the next Unity frame and asynchronously
    /// returns its result, or faults the task on exception/timeout/cancellation.
    /// </summary>
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
        if (_instance == null)
        {
            throw new InvalidOperationException(
                "MainThreadDispatcher not bootstrapped. Call Bootstrap() during plugin Awake()."
            );
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var effectiveTimeout = timeout ?? DefaultTimeout;

        var pending = new PendingAction(
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
        );
        Queue.Enqueue(pending);

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

    private void Update()
    {
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
                // Caller already cancelled or timed out elsewhere.
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
