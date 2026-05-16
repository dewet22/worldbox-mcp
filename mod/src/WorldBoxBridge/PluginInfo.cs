namespace WorldBoxBridge;

/// <summary>
/// Compile-time constants for the plugin identity.
/// Kept here (not in Plugin.cs) so tests can reference them without pulling in BepInEx.
/// </summary>
internal static class PluginInfo
{
    public const string Guid = "com.fullya99.worldbox-mcp.bridge";
    public const string Name = "WorldBoxBridge";

    /// <summary>
    /// SemVer string. Kept in sync with WorldBoxBridge.csproj's &lt;Version&gt; by release-please.
    /// Update both together.
    /// </summary>
    public const string Version = "0.1.1";
}
