using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;

namespace WorldBoxBridge.Reflection;

/// <summary>
/// Reflection-based view onto WorldBox's <c>AssetManager</c> registries.
/// </summary>
/// <remarks>
/// <para>Every library inside <c>AssetManager</c> derives from <c>AssetLibrary&lt;T&gt;</c>
/// and exposes a uniform iteration contract: a <c>List&lt;T&gt;</c> field named <c>list</c>
/// plus a <c>Dictionary&lt;string, T&gt;</c> field named <c>dict</c>. Every individual asset
/// derives from <c>Asset</c> and carries a <c>string id</c>. That uniformity means one piece
/// of reflection code can enumerate or resolve any asset id in the game, see
/// <c>docs/game-api-notes.md</c>.</para>
///
/// <para>All reflection lookups are cached on first use; misses are logged once.</para>
/// </remarks>
internal sealed class AssetCatalog
{
    private readonly GameRefs _refs;
    private readonly ManualLogSource _log;

    private Type? _assetManagerType;
    private readonly Dictionary<string, LibraryView> _views = new(StringComparer.Ordinal);

    public AssetCatalog(GameRefs refs, ManualLogSource log)
    {
        _refs = refs;
        _log = log;
    }

    /// <summary>
    /// Enumerates all assets in the named library, projecting each to an id (and optionally
    /// extra fields). Asset ids prefixed with <c>$</c> or <c>_</c> are filtered out, they're
    /// in-game templates / internal placeholders, never useful to an agent.
    /// </summary>
    /// <param name="libraryFieldName">Name of the static field on <c>AssetManager</c>
    /// (e.g. <c>"tiles"</c>, <c>"actor_library"</c>, <c>"powers"</c>).</param>
    /// <param name="extraFields">Optional list of extra fields to pull off each asset
    /// (best-effort, missing or null fields are silently skipped).</param>
    public List<Dictionary<string, object?>> ListAssets(
        string libraryFieldName,
        IReadOnlyList<string>? extraFields = null
    )
    {
        var view = GetView(libraryFieldName);
        if (view == null)
        {
            return new List<Dictionary<string, object?>>();
        }
        var list = view.GetList();
        if (list == null)
        {
            return new List<Dictionary<string, object?>>();
        }
        var result = new List<Dictionary<string, object?>>(view.LastKnownCount);
        foreach (var item in list)
        {
            if (item == null)
            {
                continue;
            }
            var id = view.GetId(item);
            if (id == null || id.Length == 0 || id[0] == '$' || id[0] == '_')
            {
                continue;
            }
            var entry = new Dictionary<string, object?> { ["id"] = id };
            if (extraFields != null && extraFields.Count > 0)
            {
                var itemType = item.GetType();
                foreach (var fieldName in extraFields)
                {
                    var fi = view.GetCachedField(itemType, fieldName);
                    if (fi == null)
                    {
                        continue;
                    }
                    var value = SafeGetValue(fi, item);
                    if (value != null && IsSerialisable(value))
                    {
                        entry[fieldName] = value;
                    }
                }
            }
            result.Add(entry);
        }
        view.LastKnownCount = result.Count;
        return result;
    }

    /// <summary>
    /// Lists just the asset ids (template/internal entries filtered out).
    /// </summary>
    public IReadOnlyList<string> ListIds(string libraryFieldName)
    {
        var view = GetView(libraryFieldName);
        if (view == null)
        {
            return Array.Empty<string>();
        }
        var list = view.GetList();
        if (list == null)
        {
            return Array.Empty<string>();
        }
        var ids = new List<string>(view.LastKnownCount);
        foreach (var item in list)
        {
            if (item == null)
            {
                continue;
            }
            var id = view.GetId(item);
            if (id == null || id.Length == 0 || id[0] == '$' || id[0] == '_')
            {
                continue;
            }
            ids.Add(id);
        }
        view.LastKnownCount = ids.Count;
        return ids;
    }

    /// <summary>Resolves an asset by id. Returns null on miss.</summary>
    public object? Resolve(string libraryFieldName, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        var view = GetView(libraryFieldName);
        if (view == null)
        {
            return null;
        }
        return view.Get(id);
    }

    /// <summary>
    /// Suggests asset ids close to <paramref name="badId"/> via Levenshtein distance.
    /// Used to populate <c>did_you_mean</c> on UNKNOWN_ASSET errors.
    /// </summary>
    public IReadOnlyList<string> Suggest(string libraryFieldName, string badId, int limit = 5)
    {
        var ids = ListIds(libraryFieldName);
        return AssetSuggester.Suggest(badId, ids, limit);
    }

    private LibraryView? GetView(string libraryFieldName)
    {
        if (_views.TryGetValue(libraryFieldName, out var existing))
        {
            return existing;
        }
        _assetManagerType ??= _refs.Type("AssetManager");
        if (_assetManagerType == null)
        {
            return null;
        }
        var libField = _assetManagerType.GetField(
            libraryFieldName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        );
        if (libField == null)
        {
            _log.LogWarning(
                $"[AssetCatalog] AssetManager.{libraryFieldName} not found in this WorldBox build."
            );
            return null;
        }
        var view = new LibraryView(libField, _log);
        _views[libraryFieldName] = view;
        return view;
    }

    private static object? SafeGetValue(FieldInfo fi, object instance)
    {
        try
        {
            return fi.GetValue(instance);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Restrict serialised values to types the JSON layer can actually emit.</summary>
    private static bool IsSerialisable(object value)
    {
        switch (value)
        {
            case string:
            case bool:
            case int:
            case long:
            case float:
            case double:
            case Enum:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Per-library reflection cache. One instance per AssetManager field name; reused across
    /// calls. <c>list</c>, <c>dict</c>, and per-asset-type <c>id</c> fields are resolved lazily
    /// and remembered.
    /// </summary>
    private sealed class LibraryView
    {
        private readonly FieldInfo _libField;
        private readonly ManualLogSource _log;
        private FieldInfo? _listField;
        private FieldInfo? _dictField;
        private MethodInfo? _getMethod;
        private readonly Dictionary<Type, FieldInfo?> _idFieldByItemType = new();
        private readonly Dictionary<TypeFieldKey, FieldInfo?> _arbitraryFields = new();

        /// <summary>
        /// Composite cache key. Avoids <c>System.ValueTuple</c>, that assembly isn't always
        /// loadable in the Mono runtime Unity ships with.
        /// </summary>
        private readonly struct TypeFieldKey : IEquatable<TypeFieldKey>
        {
            public TypeFieldKey(Type type, string field)
            {
                Type = type;
                Field = field;
            }

            public Type Type { get; }
            public string Field { get; }

            public bool Equals(TypeFieldKey other) => Type == other.Type && Field == other.Field;

            public override bool Equals(object obj) => obj is TypeFieldKey k && Equals(k);

            public override int GetHashCode() =>
                ((Type?.GetHashCode() ?? 0) * 397) ^ (Field?.GetHashCode() ?? 0);
        }

        public int LastKnownCount { get; set; }

        public LibraryView(FieldInfo libField, ManualLogSource log)
        {
            _libField = libField;
            _log = log;
        }

        public IEnumerable? GetList()
        {
            var library = _libField.GetValue(null);
            if (library == null)
            {
                return null;
            }
            _listField ??= library
                .GetType()
                .GetField(
                    "list",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
            return _listField?.GetValue(library) as IEnumerable;
        }

        public object? Get(string id)
        {
            var library = _libField.GetValue(null);
            if (library == null)
            {
                return null;
            }
            _getMethod ??= library.GetType().GetMethod("get", new[] { typeof(string) });
            if (_getMethod != null)
            {
                return _getMethod.Invoke(library, new object[] { id });
            }
            // Fallback: walk dict if get() isn't visible.
            _dictField ??= library
                .GetType()
                .GetField(
                    "dict",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
            if (_dictField?.GetValue(library) is IDictionary dict && dict.Contains(id))
            {
                return dict[id];
            }
            return null;
        }

        public string? GetId(object item)
        {
            var t = item.GetType();
            if (!_idFieldByItemType.TryGetValue(t, out var idField))
            {
                idField = t.GetField(
                    "id",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                _idFieldByItemType[t] = idField;
                if (idField == null)
                {
                    _log.LogWarning($"[AssetCatalog] Type {t.FullName} has no 'id' field.");
                }
            }
            return idField?.GetValue(item) as string;
        }

        public FieldInfo? GetCachedField(Type itemType, string fieldName)
        {
            var key = new TypeFieldKey(itemType, fieldName);
            if (_arbitraryFields.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var fi = itemType.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            _arbitraryFields[key] = fi;
            return fi;
        }
    }
}
