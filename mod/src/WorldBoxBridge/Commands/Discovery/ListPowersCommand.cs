using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using WorldBoxBridge.Commands.Action;
using WorldBoxBridge.Reflection;
using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Discovery;

/// <summary>
/// Lists every "power" the player can invoke, meteor, nuke, plague, toggle peace, kill race,
/// and so on. Powers cover what the in-game UI groups under the god-mode toolbar.
/// </summary>
internal sealed class ListPowersCommand : ICommand
{
    private readonly AssetCatalog _catalog;
    private readonly PowerDelegateFields _delegateFields;
    private readonly ManualLogSource _log;
    private bool _warnedFlagDerivation;

    private static readonly string[] ExtraFields =
    {
        "tab_id",
        "target_type",
        "tooltip_text_locale_id",
        "show_in_kingdoms_panel",
        "show_in_clans_panel",
    };

    public ListPowersCommand(
        AssetCatalog catalog,
        PowerDelegateFields delegateFields,
        ManualLogSource log
    )
    {
        _catalog = catalog;
        _delegateFields = delegateFields;
        _log = log;
    }

    public string Name => "list_powers";
    public CommandCategory Category => CommandCategory.Discovery;
    public string Description =>
        "Enumerates every PowerAsset registered in this WorldBox build (disasters, toggles, "
        + "modifiers). Returned ids are the valid inputs for `invoke_power`. Items flagged "
        + "supports_radius accept invoke_power's radius argument (applied via the game's brush "
        + "system); items flagged is_toggle are global on/off switches (x/y ignored). Both "
        + "flags are omitted when false.";
    public bool RequiresMainThread => true;

    public JObject ArgsSchema =>
        new(
            new JProperty("type", "object"),
            new JProperty("properties", new JObject()),
            new JProperty("additionalProperties", false)
        );

    public Task<object?> ExecuteAsync(
        JObject args,
        RequestContext ctx,
        CancellationToken cancellationToken
    )
    {
        // HttpBridge already marshalled us onto the main thread.
        var items = _catalog.ListAssets("powers", ExtraFields);
        foreach (var item in items)
        {
            // Fail-soft per item, matching AssetCatalog's discovery style: one asset whose
            // reflection misbehaves at worst lacks its flags, it must never take down the
            // whole listing, which is the recovery tool agents rely on.
            try
            {
                if (item["id"] is not string id || _catalog.Resolve("powers", id) is not { } power)
                {
                    continue;
                }
                // Both flags come from the same field reads and the same selector invoke_power
                // uses, so discovery cannot drift from behaviour.
                var delegates = _delegateFields.Read(power);
                var hasClick = delegates.ClickAction != null;
                var hasClickPower = delegates.ClickPowerAction != null;
                var hasClickBrush = delegates.ClickBrushAction != null;
                var hasClickPowerBrush = delegates.ClickPowerBrushAction != null;
                var hasToggle = delegates.ToggleAction != null;

                var radiusProbe = PowerDelegateSelector.Select(
                    hasClick,
                    hasClickPower,
                    hasClickBrush,
                    hasClickPowerBrush,
                    hasToggle,
                    radius: PowerDelegateSelector.MinRadius
                );
                if (
                    radiusProbe.Path
                    is PowerDelegatePath.ClickBrushAction
                        or PowerDelegatePath.ClickPowerBrushAction
                )
                {
                    item["supports_radius"] = true;
                }

                var defaultProbe = PowerDelegateSelector.Select(
                    hasClick,
                    hasClickPower,
                    hasClickBrush,
                    hasClickPowerBrush,
                    hasToggle,
                    radius: null
                );
                if (defaultProbe.Path == PowerDelegatePath.ToggleAction)
                {
                    item["is_toggle"] = true;
                }
            }
            catch (System.Exception ex)
            {
                if (!_warnedFlagDerivation)
                {
                    _warnedFlagDerivation = true;
                    _log.LogWarning(
                        $"[list_powers] flag derivation failed for '{item["id"]}' "
                            + $"({ex.GetType().Name}: {ex.Message}), flags omitted; further "
                            + "occurrences are not logged."
                    );
                }
            }
        }
        return Task.FromResult<object?>(new { items, count = items.Count });
    }
}
