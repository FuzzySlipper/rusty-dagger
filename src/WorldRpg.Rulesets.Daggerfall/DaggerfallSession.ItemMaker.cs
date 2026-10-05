using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallItemMakerView(string Revision, string Provider, bool Eligible,
    DaggerfallItemMakerItem[] Items, DaggerfallItemMakerSetting[] Settings, DaggerfallItemMakerDraft Draft, DaggerfallItemMakerQuote? Quote);

internal sealed partial class DaggerfallSession
{
    private DaggerfallItemMaker? _itemMaker;
    internal DaggerfallItemMaker ItemMaker => _itemMaker ??= new(_definitions, State, ItemCondition, SoulGems, _uniqueItems, () => _time.Calendar);
    private DaggerfallItemMakerView? ReadItemMaker()
    {
        if (CurrentSpellProvider(service: "make-magic-items") is not { } context) return null;
        return new(_activationPresentation.View.Dialogue!.Revision, _activationPresentation.View.Dialogue.TargetLabel, ItemMaker.CanUse(context.Provider),
            ItemMaker.Items(), ItemMaker.Settings(), ItemMaker.Draft, ItemMaker.Quote(context.Provider));
    }

    internal void ChangeItemMaker(DaggerfallPlayerUiAction action)
    {
        string outcome;
        if (CurrentSpellProvider(action.Revision, "make-magic-items") is not { } context) outcome = "ProviderUnavailable";
        else if (action.Kind == DaggerfallUiActionKind.ItemMakerDraft)
        {
            try
            {
                var draft = JsonSerializer.Deserialize(action.Text!, DaggerfallItemMakerJsonContext.Default.DaggerfallItemMakerDraft);
                if (draft is null) outcome = "InvalidSettings";
                else { ItemMaker.SetDraft(draft); outcome = "Preview"; }
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException) { outcome = "InvalidSettings"; }
        }
        else outcome = ItemMaker.Buy(context.Provider, action.Key!, action.Amount!.Value, action.Confirm).Outcome;
        Presentation.SetOutcome(outcome switch
        {
            "Enchanted" => "Your item has been enchanted. Equip or use it to activate its powers.",
            "Preview" => "Enchantment preview updated.",
            "DraftChanged" or "PriceChanged" => "Check the current enchantment preview before buying.",
            "InsufficientFunds" => "You do not have enough gold.",
            "SoulUnavailable" => "The selected soul is no longer in your pack.",
            "ItemUnavailable" => "The selected item is no longer available.",
            "ProviderUnavailable" => "This enchanter is unavailable to you.",
            _ => outcome,
        });
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DaggerfallItemMakerDraft))]
internal partial class DaggerfallItemMakerJsonContext : JsonSerializerContext;
