using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallSpellMakerView(string Revision, string Provider, DaggerfallSpellMakerEffect[] Effects,
    DaggerfallSpellDraft Draft, DaggerfallSpellConstructionQuote? Quote);

internal sealed partial class DaggerfallSession
{
    private DaggerfallSpellMaker? _spellMaker;
    internal DaggerfallSpellMaker SpellMaker => _spellMaker ??= new(_definitions, State, () => _time.Calendar);

    private DaggerfallSpellMakerView? ReadSpellMaker()
    {
        if (CurrentSpellProvider(service: "make-spells") is not { } context) return null;
        return new(_activationPresentation.View.Dialogue!.Revision, _activationPresentation.View.Dialogue.TargetLabel,
            SpellMaker.Effects, SpellMaker.Draft, SpellMaker.Quote(context.Provider));
    }

    internal void ChangeSpellMaker(DaggerfallPlayerUiAction action)
    {
        if (CurrentSpellProvider(action.Revision, "make-spells") is not { } context)
            _spellResult = "ProviderUnavailable";
        else if (action.Kind == DaggerfallUiActionKind.SpellMakerDraft)
        {
            try
            {
                var draft = JsonSerializer.Deserialize(action.Text!, DaggerfallSpellDraftJsonContext.Default.DaggerfallSpellDraft);
                if (draft is null || draft.Name is null || draft.Effects is null) _spellResult = "InvalidSettings";
                else
                {
                    SpellMaker.SetDraft(draft);
                    _spellResult = SpellMaker.Quote(context.Provider) is { Eligible: true } ? "Preview" : "InvalidSettings";
                }
            }
            catch (JsonException) { _spellResult = "InvalidSettings"; }
        }
        else _spellResult = SpellMaker.Buy(context.Provider, action.Key!, action.Amount!.Value, action.Confirm).Outcome;
        Presentation.SetOutcome(_spellResult switch
        {
            "Purchased" => "Custom spell added to your spellbook.",
            "Preview" => "Spell construction preview updated.",
            "DraftChanged" or "PriceChanged" => "Check the current spell construction preview before buying.",
            "SpellbookRequired" => "You need a spellbook to make spells.",
            "InsufficientFunds" => "You do not have enough gold.",
            "ProviderUnavailable" => "This spellmaker is unavailable to you.",
            "InvalidSettings" or "InvalidCombination" => "Choose valid supported effects and settings.",
            _ => _spellResult,
        });
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DaggerfallSpellDraft))]
internal partial class DaggerfallSpellDraftJsonContext : JsonSerializerContext;
