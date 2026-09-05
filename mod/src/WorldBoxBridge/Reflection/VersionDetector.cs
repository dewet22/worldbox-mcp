using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;

namespace WorldBoxBridge.Reflection;

/// <summary>
/// Captures WorldBox + Unity + DLL version metadata at startup so it can be served in
/// <c>/health</c> and <c>/capabilities</c> without ever touching Unity's main thread again.
/// </summary>
/// <remarks>
/// Including the SHA256 of <c>Assembly-CSharp.dll</c> in every health response lets users
/// correlate bug reports with the exact game build that produced them, much more precise
/// than just the displayed game version, which can lag behind Steam beta patches.
/// </remarks>
internal sealed class VersionInfo
{
    public string ModVersion { get; set; } = PluginInfo.Version;
    public string UnityVersion { get; set; } = "unknown";
    public string WorldBoxVersion { get; set; } = "unknown";
    public string AssemblyCSharpSha256 { get; set; } = "unknown";
}

internal static class VersionDetector
{
    /// <summary>
    /// Resolves all known version metadata. Safe to call from any thread; only touches files
    /// and statics that the Unity runtime has already initialised by the time BepInEx loads
    /// plugins.
    /// </summary>
    public static VersionInfo Detect(string unityVersion, string applicationVersion)
    {
        return new VersionInfo
        {
            UnityVersion = string.IsNullOrEmpty(unityVersion) ? "unknown" : unityVersion,
            WorldBoxVersion = string.IsNullOrEmpty(applicationVersion)
                ? "unknown"
                : applicationVersion,
            AssemblyCSharpSha256 = HashAssemblyCSharp(),
        };
    }

    private static string HashAssemblyCSharp()
    {
        try
        {
            // Paths.ManagedPath is resolved by BepInEx for the host platform: worldbox_Data/Managed
            // on Windows/Linux, worldbox.app/Contents/Resources/Data/Managed on macOS.
            var path = Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll");
            if (!File.Exists(path))
            {
                return "missing";
            }
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
        catch (Exception)
        {
            return "error";
        }
    }
}
