using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;

namespace WorldBoxBridge.Reflection;

/// <summary>
/// Cached, fail-soft access to the WorldBox world: <c>MapBox.instance</c>, its sub-managers
/// (<c>units</c> / <c>kingdoms</c> / <c>cities</c>), and the methods we use repeatedly
/// (<c>getSimpleList()</c>, the indexer into <c>tiles_map</c>, etc.).
/// </summary>
/// <remarks>
/// Same fail-soft pattern as <see cref="GameRefs"/>: if a symbol disappears in a future game
/// update, the affected getter returns null and the dependent command surfaces a clear error
/// — the rest of the bridge keeps running.
/// </remarks>
internal sealed class WorldAccess
{
    private readonly GameRefs _refs;
    private readonly ManualLogSource _log;

    private Type? _mapBoxType;
    private FieldInfo? _instanceField;
    private FieldInfo? _widthField;
    private FieldInfo? _heightField;
    private FieldInfo? _seedField;
    private FieldInfo? _unitsField;
    private FieldInfo? _kingdomsField;
    private FieldInfo? _citiesField;
    private FieldInfo? _tilesMapField;
    private FieldInfo? _mapStatsField;
    private MethodInfo? _isPausedMethod;

    private readonly Dictionary<TypeMemberKey, FieldInfo?> _fieldCache = new();
    private readonly Dictionary<TypeMemberKey, PropertyInfo?> _propertyCache = new();
    private readonly Dictionary<TypeMemberKey, MethodInfo?> _methodCache = new();

    public WorldAccess(GameRefs refs, ManualLogSource log)
    {
        _refs = refs;
        _log = log;
    }

    public Type? MapBoxType => _mapBoxType ??= _refs.Type("MapBox");

    public object? MapBoxInstance
    {
        get
        {
            var t = MapBoxType;
            if (t == null)
            {
                return null;
            }
            _instanceField ??= t.GetField(
                "instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            return _instanceField?.GetValue(null);
        }
    }

    public int? Width
    {
        get
        {
            var t = MapBoxType;
            if (t == null)
            {
                return null;
            }
            _widthField ??= t.GetField(
                "width",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            return _widthField?.GetValue(null) as int?;
        }
    }

    public int? Height
    {
        get
        {
            var t = MapBoxType;
            if (t == null)
            {
                return null;
            }
            _heightField ??= t.GetField(
                "height",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            return _heightField?.GetValue(null) as int?;
        }
    }

    public int? WorldSeed
    {
        get
        {
            var t = MapBoxType;
            if (t == null)
            {
                return null;
            }
            _seedField ??= t.GetField(
                "current_world_seed_id",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            return _seedField?.GetValue(null) as int?;
        }
    }

    public object? UnitsManager =>
        GetInstanceField(ref _unitsField, "units", _mapBoxType);
    public object? KingdomsManager =>
        GetInstanceField(ref _kingdomsField, "kingdoms", _mapBoxType);
    public object? CitiesManager =>
        GetInstanceField(ref _citiesField, "cities", _mapBoxType);
    public object? MapStats =>
        GetInstanceField(ref _mapStatsField, "map_stats", _mapBoxType);

    public bool? IsPaused
    {
        get
        {
            var mb = MapBoxInstance;
            if (mb == null)
            {
                return null;
            }
            _isPausedMethod ??= mb.GetType().GetMethod(
                "isPaused",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            return _isPausedMethod?.Invoke(mb, Array.Empty<object>()) as bool?;
        }
    }

    /// <summary>
    /// Returns the <c>WorldTile</c> at <paramref name="x"/>, <paramref name="y"/> or null if
    /// out of bounds / map not ready. Bounds checked against <see cref="Width"/> / <see cref="Height"/>.
    /// </summary>
    public object? GetTileAt(int x, int y)
    {
        var mb = MapBoxInstance;
        if (mb == null)
        {
            return null;
        }
        _tilesMapField ??= mb.GetType().GetField(
            "tiles_map",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
        if (_tilesMapField?.GetValue(mb) is not Array map)
        {
            return null;
        }
        if (x < 0 || y < 0 || x >= map.GetLength(0) || y >= map.GetLength(1))
        {
            return null;
        }
        return map.GetValue(x, y);
    }

    /// <summary>Plain struct rather than <c>(int, int)?</c> — avoids System.ValueTuple
    /// which isn't always loadable in Unity Mono.</summary>
    public readonly struct MapDimensions
    {
        public MapDimensions(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }
    }

    public MapDimensions? GetMapDimensions()
    {
        var w = Width;
        var h = Height;
        if (w is null || h is null)
        {
            return null;
        }
        return new MapDimensions(w.Value, h.Value);
    }

    /// <summary>
    /// Enumerates the live objects managed by <paramref name="manager"/>.
    /// </summary>
    /// <remarks>
    /// All WorldBox managers (<c>ActorManager</c>, <c>KingdomManager</c>, <c>CityManager</c>, …)
    /// derive from <c>CoreSystemManager&lt;T, TData&gt;</c> which implements <c>IEnumerable&lt;T&gt;</c>
    /// with the underlying storage being a <c>HashSet&lt;T&gt;</c>. Iterating via the
    /// non-generic <see cref="IEnumerable"/> interface works uniformly across the two manager
    /// hierarchies (<c>SimSystemManager</c> for actors, <c>MetaSystemManager</c> for
    /// kingdoms / cities). The earlier reflection-on-<c>getSimpleList()</c> only worked for
    /// the SimSystem half — the MetaSystem managers don't define that method.
    /// </remarks>
    public IList? GetSimpleList(object manager)
    {
        // Snapshot via IEnumerable — works for every CoreSystemManager subclass.
        if (manager is IEnumerable enumerable)
        {
            var list = new List<object>();
            foreach (var item in enumerable)
            {
                if (item != null)
                {
                    list.Add(item);
                }
            }
            return list;
        }
        _log.LogWarning(
            $"[WorldAccess] {manager.GetType().FullName} does not implement IEnumerable — can't enumerate."
        );
        return null;
    }

    /// <summary>
    /// Reads the <c>Count</c> property exposed by <c>CoreSystemManager</c>. Returns null if
    /// the property isn't present in this game build.
    /// </summary>
    public int? GetManagerCount(object manager)
    {
        var prop = CachedProperty(manager.GetType(), "Count");
        return prop?.GetValue(manager) as int?;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Reflection helpers reused by Read commands
    // ──────────────────────────────────────────────────────────────────────

    public FieldInfo? Field(object instance, string name) =>
        CachedField(instance.GetType(), name);

    public FieldInfo? CachedField(Type type, string name)
    {
        var key = new TypeMemberKey(type, name);
        if (_fieldCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        FieldInfo? fi = null;
        for (var cur = type; cur != null && fi == null; cur = cur.BaseType)
        {
            fi = cur.GetField(
                name,
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly
            );
        }
        _fieldCache[key] = fi;
        return fi;
    }

    public PropertyInfo? CachedProperty(Type type, string name)
    {
        var key = new TypeMemberKey(type, name);
        if (_propertyCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        PropertyInfo? pi = null;
        for (var cur = type; cur != null && pi == null; cur = cur.BaseType)
        {
            pi = cur.GetProperty(
                name,
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly
            );
        }
        _propertyCache[key] = pi;
        return pi;
    }

    public MethodInfo? CachedMethod(Type type, string name, params Type[] argTypes)
    {
        var key = new TypeMemberKey(type, name + "(" + argTypes.Length + ")");
        if (_methodCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        MethodInfo? mi = null;
        for (var cur = type; cur != null && mi == null; cur = cur.BaseType)
        {
            // Don't use GetMethod(name, flags) without arg types — it throws
            // AmbiguousMatchException when overloads exist. Enumerate instead and pick
            // the first method whose signature matches the requested arg types.
            foreach (
                var candidate in cur.GetMethods(
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly
                )
            )
            {
                if (candidate.Name != name)
                {
                    continue;
                }
                var ps = candidate.GetParameters();
                if (argTypes is { Length: > 0 })
                {
                    if (ps.Length != argTypes.Length)
                    {
                        continue;
                    }
                    var match = true;
                    for (var i = 0; i < ps.Length; i++)
                    {
                        if (ps[i].ParameterType != argTypes[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (!match)
                    {
                        continue;
                    }
                }
                else
                {
                    // No-arg lookup: prefer a parameterless overload.
                    if (ps.Length != 0)
                    {
                        continue;
                    }
                }
                mi = candidate;
                break;
            }
        }
        _methodCache[key] = mi;
        return mi;
    }

    /// <summary>Read a field/property by name, walking inheritance. Returns null on miss.</summary>
    public object? Read(object instance, string memberName)
    {
        var fi = CachedField(instance.GetType(), memberName);
        if (fi != null)
        {
            return fi.GetValue(instance);
        }
        var pi = CachedProperty(instance.GetType(), memberName);
        if (pi != null && pi.CanRead)
        {
            return pi.GetValue(instance);
        }
        return null;
    }

    private object? GetInstanceField(ref FieldInfo? cache, string name, Type? mapBoxType)
    {
        var mb = MapBoxInstance;
        if (mb == null)
        {
            return null;
        }
        cache ??= mb.GetType().GetField(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
        return cache?.GetValue(mb);
    }

    private readonly struct TypeMemberKey : IEquatable<TypeMemberKey>
    {
        public TypeMemberKey(Type type, string name)
        {
            Type = type;
            Name = name;
        }

        public Type Type { get; }
        public string Name { get; }

        public bool Equals(TypeMemberKey other) => Type == other.Type && Name == other.Name;
        public override bool Equals(object obj) => obj is TypeMemberKey k && Equals(k);
        public override int GetHashCode() =>
            ((Type?.GetHashCode() ?? 0) * 397) ^ (Name?.GetHashCode() ?? 0);
    }
}
