using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using WorldBoxBridge.Commands;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Commands.Bus;
using WorldBoxBridge.Commands.Control;
using WorldBoxBridge.Commands.Discovery;
using WorldBoxBridge.Commands.Meta;
using WorldBoxBridge.Commands.Read;
using WorldBoxBridge.Http;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;
using WorldBoxBridge.Threading;
using SessionState = WorldBoxBridge.Session.Session;

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

            var gameRefs = new GameRefs(Logger);
            var assetCatalog = new AssetCatalog(gameRefs, Logger);
            var worldAccess = new WorldAccess(gameRefs, Logger);

            // Multi-agent v0.3 Phase 2: load agents.json if it exists, else fall back to
            // legacy single-token mode (one God agent with the credential from BridgeConfig.Token).
            var agentsJsonPath = Path.Combine(Paths.ConfigPath, "WorldBoxBridge.agents.json");
            var session = SessionLoader.Load(agentsJsonPath, config.Token.Value, Logger);

            var registry = new CommandRegistry();
            RegisterCommands(registry, version, config, assetCatalog, gameRefs, worldAccess, session);
            Logger.LogInfo($"{registry.Count} commands registered.");

            _bridge = new HttpBridge(Logger, config, registry, version, session);
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
    private void RegisterCommands(
        CommandRegistry registry,
        VersionInfo version,
        BridgeConfig config,
        AssetCatalog assetCatalog,
        GameRefs gameRefs,
        WorldAccess worldAccess,
        SessionState session
    )
    {
        registry.Register(new HealthCommand(version, config, session));

        // Meta — multi-agent identity introspection.
        registry.Register(new WhoAmICommand());
        registry.Register(new SessionInfoCommand(session));
        registry.Register(new TurnAdvanceCommand(session));
        registry.Register(new ObjectiveStatusCommand(session, worldAccess));

        // Bus — inter-agent messaging.
        registry.Register(new SendMessageCommand(session));
        registry.Register(new RecvMessagesCommand(session));

        // Discovery — introspect the in-game asset registries.
        registry.Register(new ListTilesCommand(assetCatalog));
        registry.Register(new ListActorsCommand(assetCatalog));
        registry.Register(new ListPowersCommand(assetCatalog));

        // Actions — modify the world.
        registry.Register(new InvokePowerCommand(assetCatalog, gameRefs, Logger));
        registry.Register(new SpawnCommand(assetCatalog, worldAccess, Logger));
        registry.Register(new PaintTileCommand(assetCatalog, worldAccess, Logger));

        // Read — observe the world before/after acting.
        registry.Register(new GetWorldStateCommand(worldAccess));
        registry.Register(new GetTileCommand(worldAccess));
        registry.Register(new ListKingdomsCommand(worldAccess));
        registry.Register(new ListCitiesCommand(worldAccess));
        registry.Register(new QueryActorsCommand(worldAccess));
        registry.Register(new ScreenshotCommand());

        // Control — simulation flow + world lifecycle.
        registry.Register(new PauseCommand(gameRefs));
        registry.Register(new ResumeCommand(gameRefs));
        registry.Register(new SetSpeedCommand(assetCatalog, gameRefs));
        registry.Register(new GenerateWorldCommand(worldAccess, Logger));
        registry.Register(new SaveWorldCommand(gameRefs, worldAccess));
        registry.Register(new LoadWorldCommand(gameRefs));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
