using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The item maker's and the potion maker's panes of the spellbook frame and their actions, worded for the player.</summary>
internal sealed class DaggerfallMakerPanels(DaggerfallItemMaker itemMaker, DaggerfallPotionMaking potionMaker, DaggerfallDefinitions definitions,
    DaggerfallServiceCounter counter, PresentationState presentation)
{
    internal DaggerfallItemMakerView? ReadItemMaker()
    {
        if (counter.Provider(service: "make-magic-items") is not { } context) return null;
        return new(counter.Page.Revision, counter.Page.TargetLabel, itemMaker.CanUse(context.Provider),
            itemMaker.Items(), itemMaker.Settings(), itemMaker.Draft, itemMaker.Quote(context.Provider));
    }

    internal void ChangeItemMaker(DaggerfallPlayerUiAction action)
    {
        string outcome;
        if (counter.Provider(action.Revision, "make-magic-items") is not { } context) outcome = "ProviderUnavailable";
        else if (action.Kind == DaggerfallUiActionKind.ItemMakerDraft)
        {
            try
            {
                var draft = JsonSerializer.Deserialize(action.Text!, DaggerfallItemMakerJsonContext.Default.DaggerfallItemMakerDraft);
                if (draft is null) outcome = "InvalidSettings";
                else { itemMaker.SetDraft(draft); outcome = "Preview"; }
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException) { outcome = "InvalidSettings"; }
        }
        else outcome = itemMaker.Buy(context.Provider, action.Key!, action.Amount!.Value, action.Confirm).Outcome;
        presentation.SetOutcome(ItemMakerOutcomeText(outcome));
    }

    /// <summary>
    /// Player wording for item-maker previews, refusals and purchases. Enchantment construction refuses
    /// a draft with its own player sentence (capacity, conflicts, weapon-only powers), which passes through.
    /// </summary>
    internal static string ItemMakerOutcomeText(string outcome) => outcome switch
    {
        "Enchanted" => "Your item has been enchanted. Equip or use it to activate its powers.",
        "Preview" => "Enchantment preview updated.",
        "DraftChanged" or "PriceChanged" => "Check the current enchantment preview before buying.",
        "SoulUnavailable" => "The selected soul is no longer in your pack.",
        "ItemUnavailable" => "The selected item is no longer available.",
        "ProviderUnavailable" => "This enchanter is unavailable to you.",
        "InvalidSettings" => "Choose an item and valid enchantments.",
        _ when outcome.Contains(' ', StringComparison.Ordinal) => outcome,
        _ => DaggerfallServiceOutcomeText.Common(outcome),
    };

    internal DaggerfallPotionMakerView? ReadPotionMaker()
    {
        if (counter.Provider(service: "make-potions") is not { } context) return null;
        return new(counter.Page.Revision, counter.Page.TargetLabel,
            potionMaker.CanUse(context.Provider), potionMaker.ReadIngredients(), potionMaker.ReadRecipes());
    }

    internal void MakePotion(DaggerfallPlayerUiAction action)
    {
        var result = new DaggerfallPotionMakingResult(false, "ProviderUnavailable");
        if (counter.Provider(action.Revision, "make-potions") is { } context)
        {
            try
            {
                int[]? selected = JsonSerializer.Deserialize(action.Text!, DaggerfallPotionMakerJsonContext.Default.Int32Array);
                result = potionMaker.Mix(context.Provider, selected!);
            }
            catch (JsonException) { result = new(false, "InvalidIngredients"); }
        }
        presentation.SetOutcome(result.Outcome switch
        {
            "PotionMade" => $"Made a potion of {definitions.Magic.PotionRecipes[result.Recipe!.Value].Name}.",
            "MixtureFailed" => "The mixture failed. The ingredients were used up.",
            "InsufficientIngredients" => "You do not have all the selected ingredients.",
            "Capacity" => "You cannot carry the finished potion.",
            "InvalidIngredients" => "Choose one to eight ingredients.",
            _ => "This potion maker is unavailable to you.",
        });
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DaggerfallItemMakerDraft))]
internal partial class DaggerfallItemMakerJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(int[]))]
internal partial class DaggerfallPotionMakerJsonContext : JsonSerializerContext;
