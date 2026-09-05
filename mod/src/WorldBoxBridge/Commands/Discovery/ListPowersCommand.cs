using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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

    private static readonly string[] ExtraFields =
    {
        "tab_id",
        "target_type",
        "tooltip_text_locale_id",
        "show_in_kingdoms_panel",
        "show_in_clans_panel",
    };

    private FieldInfo? _clickActionField;
    private FieldInfo? _clickPowerActionField;
    private FieldInfo? _clickBrushActionField;
    private FieldInfo? _clickPowerBrushActionField;
    private FieldInfo? _toggleActionField;

    public ListPowersCommand(AssetCatalog catalog) => _catalog = catalog;

    public string Name => "list_powers";
    public CommandCategory Category => CommandCategory.Discovery;
    public string Description =>
        "Enumerates every PowerAsset registered in this WorldBox build (disasters, toggles, "
        + "modifiers). Returned ids are the valid inputs for `invoke_power`. Items flagged "
        + "supports_radius accept invoke_power's radius argument (applied via the game's brush "
        + "system); items flagged is_toggle are global on/off switches (x/y ignored, ActionGlobal "
        + "permission required). Both flags are omitted when false.";
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
            if (item["id"] is not string id || _catalog.Resolve("powers", id) is not object power)
            {
                continue;
            }
            // Derive the two capability flags from the same delegate fields (and the same
            // selection logic) invoke_power uses, so discovery can never drift from behaviour.
            const BindingFlags Inst =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var powerType = power.GetType();
            _clickActionField ??= powerType.GetField("click_action", Inst);
            _clickPowerActionField ??= powerType.GetField("click_power_action", Inst);
            _clickBrushActionField ??= powerType.GetField("click_brush_action", Inst);
            _clickPowerBrushActionField ??= powerType.GetField("click_power_brush_action", Inst);
            _toggleActionField ??= powerType.GetField("toggle_action", Inst);

            var hasBrush =
                _clickBrushActionField?.GetValue(power) is Delegate
                || _clickPowerBrushActionField?.GetValue(power) is Delegate;
            if (hasBrush)
            {
                item["supports_radius"] = true;
            }
            var choice = PowerDelegateSelector.Select(
                _clickActionField?.GetValue(power) is Delegate,
                _clickPowerActionField?.GetValue(power) is Delegate,
                _clickBrushActionField?.GetValue(power) is Delegate,
                _clickPowerBrushActionField?.GetValue(power) is Delegate,
                _toggleActionField?.GetValue(power) is Delegate,
                radius: null
            );
            if (choice.Path == PowerDelegatePath.ToggleAction)
            {
                item["is_toggle"] = true;
            }
        }
        return Task.FromResult<object?>(new { items, count = items.Count });
    }
}
