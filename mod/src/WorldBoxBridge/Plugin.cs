using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using WorldBoxBridge.Commands;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Threading;

namespace WorldBoxBridge;

/// <summary>
/// BepInEx entry point. Brings up the main-thread dispatcher, loads config, registers commands,
/// and starts the HTTP listener.
/// </summary>
/// <remarks>
/// All meaningful work is delegated to single-purpose classes (config, dispatcher, registry,
/// HTTP bridge). The plugin itself is intentionally thin and easy to read top-to-bottom.
/// </remarks>
[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    private HttpBridge? _bridge;

    private void Awake()
    {
        try
        {
            Logger.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} starting up...");

            MainThreadDispatcher.Bootstrap(Logger);

            var configPath = Path.Combine(Paths.ConfigPath, "WorldBoxBridge.cfg");
            var config = BridgeConfig.Load(configPath);
            config.AssertLoopbackOnly();
            Logger.LogInfo($"Config loaded from {configPath} (port={config.Port.Value}).");

            var version = VersionDetector.Detect(
                Application.unityVersion,
                Application.version
            );
            Logger.LogInfo(
                $"Detected WorldBox '{version.WorldBoxVersion}' on Unity {version.UnityVersion} "
                    + $"(Assembly-CSharp.dll sha256={Truncate(version.AssemblyCSharpSha256, 12)}…)."
            );

            var registry = new CommandRegistry();
            RegisterCommands(registry, version, config);
            Logger.LogInfo($"{registry.Count} commands registered.");

            _bridge = new HttpBridge(Logger, config, registry, version);
            _bridge.Start();

            Logger.LogInfo("Ready. Kill-switch: set enabled=false in WorldBoxBridge.cfg to hot-disable.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"FATAL: WorldBoxBridge failed to start: {ex}");
            // Don't rethrow — let other plugins keep loading. The mod is just inert.
        }
    }

    private void OnDestroy()
    {
        // IMPORTANT: do NOT dispose _bridge here. On WorldBox (Unity 2022.3.60f1), OnDestroy
        // fires shortly after Awake for the plugin MonoBehaviour — likely a scene-transition
        // side effect or BepInEx host lifecycle quirk that we can't reliably prevent.
        //
        // The bridge keeps itself alive through:
        //   1. A static reference inside HttpBridge (anti-GC anchor).
        //   2. A non-background accept thread that pins the listener.
        // Cleanup happens at process exit (OS) or on OnApplicationQuit (graceful).
        Logger.LogDebug("Plugin.OnDestroy() ignored — bridge keeps running.");
    }

    private void OnApplicationQuit()
    {
        Logger.LogInfo("OnApplicationQuit — disposing bridge.");
        try
        {
            _bridge?.Dispose();
            _bridge = null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error during shutdown: {ex.Message}");
        }
    }

    /// <summary>
    /// Wires every command into the registry. Adding a new command means adding one line here
    /// and one new class under <c>Commands/</c>.
    /// </summary>
    private static void RegisterCommands(
        CommandRegistry registry,
        VersionInfo version,
        BridgeConfig config
    )
    {
        registry.Register(new HealthCommand(version, config));

        // ── Phase 2 + 3 commands land here as they're implemented:
        // registry.Register(new ListTilesCommand(...));
        // registry.Register(new ListActorsCommand(...));
        // registry.Register(new ListPowersCommand(...));
        // registry.Register(new PaintTileCommand(...));
        // registry.Register(new SpawnCommand(...));
        // registry.Register(new InvokePowerCommand(...));
        // ...
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
