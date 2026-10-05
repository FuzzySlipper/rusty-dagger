using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallPotionMakerView(string Revision, string Provider, bool Eligible,
    DaggerfallPotionIngredientView[] Ingredients, DaggerfallPotionRecipeView[] Recipes);

internal sealed partial class DaggerfallSession
{
    private DaggerfallPotionMaking? _potionMaker;
    internal DaggerfallPotionMaking PotionMaker => _potionMaker ??= new(_definitions, State, _engine.Random, () => _time.Calendar);

    private DaggerfallPotionMakerView? ReadPotionMaker()
    {
        if (CurrentSpellProvider(service: "make-potions") is not { } context) return null;
        return new(_activationPresentation.View.Dialogue!.Revision, _activationPresentation.View.Dialogue.TargetLabel,
            PotionMaker.CanUse(context.Provider), PotionMaker.ReadIngredients(), PotionMaker.ReadRecipes());
    }

    internal void MakePotion(DaggerfallPlayerUiAction action)
    {
        var result = new DaggerfallPotionMakingResult(false, "ProviderUnavailable");
        if (CurrentSpellProvider(action.Revision, "make-potions") is { } context)
        {
            try
            {
                int[]? selected = JsonSerializer.Deserialize(action.Text!, DaggerfallPotionMakerJsonContext.Default.Int32Array);
                result = PotionMaker.Mix(context.Provider, selected!);
            }
            catch (JsonException) { result = new(false, "InvalidIngredients"); }
        }
        Presentation.SetOutcome(result.Outcome switch
        {
            "PotionMade" => $"Made a potion of {_definitions.Magic.PotionRecipes[result.Recipe!.Value].Name}.",
            "MixtureFailed" => "The mixture failed. The ingredients were used up.",
            "InsufficientIngredients" => "You do not have all the selected ingredients.",
            "Capacity" => "You cannot carry the finished potion.",
            "InvalidIngredients" => "Choose one to eight ingredients.",
            _ => "This potion maker is unavailable to you.",
        });
    }
}

[JsonSerializable(typeof(int[]))]
internal partial class DaggerfallPotionMakerJsonContext : JsonSerializerContext;
