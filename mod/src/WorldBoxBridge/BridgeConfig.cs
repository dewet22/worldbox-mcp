using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;

namespace WorldBoxBridge;

/// <summary>
/// User-tunable settings exposed to <c>BepInEx/config/WorldBoxBridge.cfg</c>.
/// </summary>
/// <remarks>
/// BepInEx auto-creates the config file with defaults on first run. A random per-install
/// token is generated the first time the plugin loads, written to the config, and reused
/// on subsequent launches. The token is also what the Python MCP server reads to authenticate.
/// </remarks>
internal sealed class BridgeConfig
{
    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<string> Host { get; }
    public ConfigEntry<int> Port { get; }
    public ConfigEntry<string> Token { get; }
    public ConfigEntry<bool> SuppressStartupWindow { get; }

    private BridgeConfig(ConfigFile file)
    {
        Enabled = file.Bind(
            "Bridge",
            "enabled",
            true,
            "Whether the HTTP bridge accepts requests. Toggle to false to hot-disable without restarting the game."
        );

        Host = file.Bind(
            "Bridge",
            "host",
            "127.0.0.1",
            "Listener host. 127.0.0.1 only, binding to 0.0.0.0 or external IPs is refused at startup."
        );

        Port = file.Bind("Bridge", "port", 8723, "Listener TCP port.");

        Token = file.Bind(
            "Bridge",
            "token",
            string.Empty,
            "Shared secret. Sent by clients in the X-WB-Token header. Generated automatically if empty."
        );

        SuppressStartupWindow = file.Bind(
            "Game",
            "suppress_startup_window",
            true,
            "Skip the 'welcome' window WorldBox shows after loading. That window pauses the "
                + "simulation until someone closes it, which defeats unattended agent control. "
                + "Set to false to get the vanilla startup screen back (dismiss_window still works)."
        );

        if (string.IsNullOrWhiteSpace(Token.Value))
        {
            Token.Value = GenerateToken();
            file.Save();
        }
    }

    public static BridgeConfig Load(string configPath)
    {
        var file = new ConfigFile(configPath, saveOnInit: true);
        return new BridgeConfig(file);
    }

    public void AssertLoopbackOnly()
    {
        var host = Host.Value?.Trim() ?? string.Empty;
        if (host != "127.0.0.1" && host != "localhost" && host != "::1")
        {
            throw new InvalidOperationException(
                $"WorldBoxBridge refuses to bind to '{host}'. Loopback addresses only "
                    + "(127.0.0.1, localhost, ::1). Edit BepInEx/config/WorldBoxBridge.cfg."
            );
        }
    }

    private static string GenerateToken()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = new byte[48];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }
}
