using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallSpellSaleView(string Revision, string Provider, DaggerfallSpellOffer[] Offers);
internal sealed record DaggerfallSpellInformation(string Key, string Name, int Element, string Target,
    string[] Details);

internal sealed partial class DaggerfallSession
{
    private DaggerfallSpellSales? _spellSales;
    private string? _spellInfo;
    internal DaggerfallSpellSales SpellSales => _spellSales ??= new(_definitions, State,
        key => Casting.AvailableSpellCost(State.Actors.Player.DurableId, key), () => _time.Calendar);

    private (DaggerfallServiceProvider Provider, int Quality)? CurrentSpellProvider(string? revision = null)
    {
        var npc = _dialogue?.CurrentNpc(revision);
        var building = CurrentInteriorBuilding();
        if (npc is null || building is null || !npc.Services.Contains("buy-spells", StringComparer.Ordinal)) return null;
        var source = _site.RequireBuildingSource(_activeProfileKey.Site, new(building.BlockX, building.BlockY, building.Building.Index));
        return (new(npc.DurableId, npc.Site, "buy-spells"), source.Quality);
    }

    private DaggerfallSpellSaleView? ReadSpellSale()
    {
        if (CurrentSpellProvider() is not { } context) return null;
        var offers = SpellSales.Offers(context.Provider, context.Quality);
        return offers.Length == 0 ? null : new(_activationPresentation.View.Dialogue!.Revision,
            _activationPresentation.View.Dialogue.TargetLabel, offers);
    }

    private DaggerfallSpellInformation? ReadSpellInformation()
    {
        if (_spellInfo is not { } key || !_definitions.Magic.Spells.TryGetValue(key, out var spell)) return null;
        return new(key, spell.Name, spell.Element, Policies.DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType).ToString(),
            spell.Effects.Select(effect => $"{_definitions.Magic.RequireEffectCost(effect).School}: duration {effect.DurationBase} + {effect.DurationMod} per {effect.DurationPerLevel} levels; chance {effect.ChanceBase}% + {effect.ChanceMod}% per {effect.ChancePerLevel} levels; magnitude {effect.MagnitudeBaseLow}–{effect.MagnitudeBaseHigh} + {effect.MagnitudeLevelBase}–{effect.MagnitudeLevelHigh} per {effect.MagnitudePerLevel} levels.").ToArray());
    }

    internal void ChangeSpellbook(DaggerfallPlayerUiAction action)
    {
        switch (action.Kind)
        {
            case DaggerfallUiActionKind.SpellInfo:
                if (State.Character.KnownSpells.Contains(action.Key!) || ReadSpellSale()?.Offers.Any(offer => offer.Key == action.Key) == true)
                    _spellInfo = action.Key;
                else _spellResult = "SpellUnavailable";
                break;
            case DaggerfallUiActionKind.SpellDelete:
                // Normalized source record for classic spell identity 92. Cure owns removal;
                // future vampire-granted ordinary spells require per-grant tags from the curse owner.
                _spellResult = action.Key == "spell.085" && State.Character.KnownSpells.Contains(action.Key)
                    ? "ProtectedSpell" : !action.Confirm ? "ConfirmationRequired"
                    : State.Character.ForgetSpell(action.Key!) ? "Forgotten" : "UnknownSpell";
                if (_spellResult == "Forgotten" && _spellInfo == action.Key) _spellInfo = null;
                break;
            case DaggerfallUiActionKind.SpellBuy:
                _spellResult = CurrentSpellProvider(action.Revision) is { } context
                    ? SpellSales.Buy(context.Provider, context.Quality, action.Key!, action.Amount!.Value, action.Confirm).Outcome
                    : "ProviderUnavailable";
                break;
        }
        Presentation.SetOutcome(_spellResult switch
        {
            "Purchased" => "Spell added to your spellbook.", "AlreadyKnown" => "You already know that spell.",
            "PriceChanged" => "The spell price changed. Check the current offer.", "Forgotten" => "Spell forgotten.",
            "ProtectedSpell" => "This transformation spell is removed when its curse is cured.",
            "SpellbookRequired" => "You need a spellbook to buy spells.", "InsufficientFunds" => "You do not have enough gold.",
            "ConfirmationRequired" => "Confirm this spellbook change.", "ProviderUnavailable" => "That spell seller is no longer available.",
            _ => _spellResult,
        });
    }
}
