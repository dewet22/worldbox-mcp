using System;
using System.Collections.Concurrent;
using System.Reflection;
using BepInEx.Logging;

namespace WorldBoxBridge.Reflection;

/// <summary>
/// Cached, fail-soft reflection lookup into the running game's <c>Assembly-CSharp</c>.
/// </summary>
/// <remarks>
/// <para>The mod never references game types via <c>using</c> — it resolves them by name through
/// this class. Why: WorldBox updates can rename, remove or obfuscate types. If a hard <c>using</c>
/// were used, the whole plugin would fail to load. With this indirection, an individual command
/// can degrade gracefully while the rest of the bridge keeps serving.</para>
///
/// <para>All lookups are cached after the first hit. Misses are logged once and remembered to
/// avoid repeated noise.</para>
///
/// <para>Phase 2 will add accessors for the WorldBox singletons (<c>MapBox</c>, <c>World</c>,
/// <c>AssetManager</c>, …). The infrastructure here is intentionally generic so adding a new
/// accessor is a one-liner.</para>
/// </remarks>
internal sealed class GameRefs
{
    private readonly ManualLogSource _log;
    private readonly ConcurrentDictionary<string, Type?> _types = new();
    private readonly ConcurrentDictionary<string, MethodInfo?> _methods = new();
    private readonly ConcurrentDictionary<string, FieldInfo?> _fields = new();
    private readonly ConcurrentDictionary<string, PropertyInfo?> _properties = new();

    public GameRefs(ManualLogSource log) => _log = log;

    public Type? Type(string fullName)
    {
        return _types.GetOrAdd(
            fullName,
            n =>
            {
                var t = System.Type.GetType($"{n}, Assembly-CSharp");
                if (t == null)
                {
                    _log.LogWarning(
                        $"[GameRefs] Type '{n}' not found in this WorldBox build. "
                            + "Commands depending on it will be disabled."
                    );
                }
                return t;
            }
        );
    }

    public MethodInfo? Method(
        Type owner,
        string name,
        BindingFlags flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static,
        params Type[] argTypes
    )
    {
        var key = $"{owner.FullName}.{name}({string.Join(",", System.Linq.Enumerable.Select(argTypes, t => t.FullName))})";
        return _methods.GetOrAdd(
            key,
            _ =>
            {
                var mi =
                    argTypes is { Length: > 0 }
                        ? owner.GetMethod(name, flags, binder: null, types: argTypes, modifiers: null)
                        : owner.GetMethod(name, flags);
                if (mi == null)
                {
                    _log.LogWarning(
                        $"[GameRefs] Method '{owner.FullName}.{name}' not found. Dependent commands disabled."
                    );
                }
                return mi;
            }
        );
    }

    public FieldInfo? Field(
        Type owner,
        string name,
        BindingFlags flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
    )
    {
        var key = $"{owner.FullName}.{name}";
        return _fields.GetOrAdd(
            key,
            _ =>
            {
                var fi = owner.GetField(name, flags);
                if (fi == null)
                {
                    _log.LogWarning(
                        $"[GameRefs] Field '{owner.FullName}.{name}' not found. Dependent commands disabled."
                    );
                }
                return fi;
            }
        );
    }

    public PropertyInfo? Property(
        Type owner,
        string name,
        BindingFlags flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
    )
    {
        var key = $"{owner.FullName}.{name}";
        return _properties.GetOrAdd(
            key,
            _ =>
            {
                var pi = owner.GetProperty(name, flags);
                if (pi == null)
                {
                    _log.LogWarning(
                        $"[GameRefs] Property '{owner.FullName}.{name}' not found. Dependent commands disabled."
                    );
                }
                return pi;
            }
        );
    }
}
