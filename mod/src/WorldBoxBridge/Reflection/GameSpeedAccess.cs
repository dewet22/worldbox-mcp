using System;
using System.Reflection;

namespace WorldBoxBridge.Reflection;

/// <summary>
/// Reflection access to the game's simulation speed: <c>Config.time_scale_asset</c>, the
/// <c>WorldTimeScaleAsset</c> currently applied.
/// </summary>
/// <remarks>
/// <c>list_speeds</c> and <c>set_speed</c> both report the active speed, and each carried its own
/// copy of this reader. The copies had already diverged: <c>SetSpeedCommand</c>'s went straight to
/// <c>Type.GetField</c> on every read, bypassing the <see cref="GameRefs"/> cache that the other
/// one used. Fail-soft like the rest of the reflection layer: a missing symbol reads as null.
/// </remarks>
internal sealed class GameSpeedAccess
{
    private const BindingFlags Static =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags Instance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly GameRefs _refs;
    private FieldInfo? _timeScaleAsset;
    private FieldInfo? _assetId;
    private Type? _assetIdOwner;

    public GameSpeedAccess(GameRefs refs) => _refs = refs;

    /// <summary>The active <c>WorldTimeScaleAsset</c>, or null if unavailable.</summary>
    private object? CurrentSpeedAsset()
    {
        var configType = _refs.Type("Config");
        if (configType == null)
        {
            return null;
        }
        _timeScaleAsset ??= _refs.Field(configType, "time_scale_asset", Static);
        return _timeScaleAsset?.GetValue(null);
    }

    /// <summary><c>Config.time_scale_asset.id</c>, or null if unavailable.</summary>
    public string? CurrentSpeedId()
    {
        var asset = CurrentSpeedAsset();
        if (asset == null)
        {
            return null;
        }
        // Keyed to the type it was resolved against. set_speed used to call GetField on every
        // read, which was slower but immune to a stale cache; sharing one reader between
        // list_speeds and set_speed would otherwise have pinned whichever asset type happened
        // to be current on the first call in the process, and a mismatch surfaces as a
        // reflection failure rather than a null.
        var assetType = asset.GetType();
        if (_assetIdOwner != assetType)
        {
            _assetId = assetType.GetField("id", Instance);
            _assetIdOwner = assetType;
        }
        return _assetId?.GetValue(asset) as string;
    }
}
