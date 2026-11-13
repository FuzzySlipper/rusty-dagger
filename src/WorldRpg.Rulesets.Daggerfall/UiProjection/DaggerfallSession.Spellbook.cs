using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The spellbook frame: the spells the player knows and the one ready, with the guild's spell sale, spellmaker
/// and summoning panes beside them, and the actions that change them, worded for the player.
/// </summary>
internal sealed partial class DaggerfallSession
{
    internal DaggerfallSpellSales SpellSales { get; private set; } = null!;
    internal DaggerfallSpellMaker SpellMaker { get; private set; } = null!;

    private string _spellResult="";
    internal DaggerfallSpellbookView ReadSpells() => new(
        State.Character.KnownSpells.Select(key =>
        {
            int? cost = Casting.AvailableSpellCost(State.Actors.Player.DurableId, key);
            DaggerfallSpellDefinition spell = _definitions.Magic.Spells[key];
            string school = spell.Effects.Count == 0 ? string.Empty : _definitions.Magic.RequireEffectCost(spell.Effects[0]).School;
            return new DaggerfallKnownSpellView(key, State.Character.IsGrantedSpell(key) ? spell.Name.TrimStart('!') : spell.Name, cost ?? 0, cost is not null,
                Math.Clamp(spell.Icon, 0, DaggerfallSpellConstruction.IconCount - 1), school);
        }).ToArray(),
        Casting.ReadyFor(State.Actors.Player.DurableId) is { Source: DaggerfallCastSource.Spell or DaggerfallCastSource.DungeonAction } ready ? ready.SpellKey : null,
        _spellResult, ReadSpellSale(), ReadSpellInformation(), ReadSpellMaker(), ReadPotionMaker(), ReadItemMaker(), ReadSummoning(),
        DrawnArt is { } art ? DaggerfallSpellArt.From(art) : null);
    private void ChangeSpell(WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallPlayerUiAction action)
    {
        var result=action.Kind switch
        {
            WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallUiActionKind.SpellReady=>ReadyPlayerSpell(action.Key!),
            _=>Casting.Cancel(State.Actors.Player.DurableId),
        };
        ReportCast(result.Outcome);
    }

    private void ReportCast(DaggerfallCastOutcome outcome)
    {
        _spellResult=CastOutcomeText(outcome);
        Presentation.SetOutcome(_spellResult);
    }

    /// <summary>Player wording for the result of readying, cancelling or releasing the player's spell.</summary>
    internal static string CastOutcomeText(DaggerfallCastOutcome outcome) => outcome switch
    {
        DaggerfallCastOutcome.Ready=>"Spell ready.",
        DaggerfallCastOutcome.Cancelled or DaggerfallCastOutcome.Unready=>"No spell ready.",
        DaggerfallCastOutcome.UnknownSpell or DaggerfallCastOutcome.SourceUnavailable=>"That spell is not known or available.",
        DaggerfallCastOutcome.UnsupportedEffect=>"That spell has unavailable effects.",
        DaggerfallCastOutcome.InsufficientMagicka=>"Not enough magicka.",
        DaggerfallCastOutcome.InvalidTarget or DaggerfallCastOutcome.TargetUnavailable=>"Aim at a valid spell target.",
        DaggerfallCastOutcome.Silenced=>"You cannot cast while silenced.",
        _=>"Spell cast.",
    };

    private string? _spellInfo;

    private (DaggerfallServiceProvider Provider, int Quality)? CurrentSpellProvider(string? revision = null, string service = "buy-spells")
    {
        var npc = _dialogue?.CurrentNpc(revision);
        var building = CurrentInteriorBuilding();
        if (npc is null || building is null || !npc.Services.Contains(service, StringComparer.Ordinal)) return null;
        var source = _site.RequireBuildingSource(_activeProfileKey.Site, new(building.BlockX, building.BlockY, building.Building.Index));
        return (new(npc.DurableId, npc.Site, service), source.Quality);
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
        return new(key, State.Character.IsGrantedSpell(key) ? spell.Name.TrimStart('!') : spell.Name, Policies.DaggerfallMagicCostPolicy.ElementLabel(spell.Element), Policies.DaggerfallMagicCostPolicy.TargetLabel(spell.RangeType),
            spell.Effects.Select(effect => $"{_definitions.Magic.RequireEffectCost(effect).School}: duration {effect.DurationBase} + {effect.DurationMod} per {effect.DurationPerLevel} levels; chance {effect.ChanceBase}% + {effect.ChanceMod}% per {effect.ChancePerLevel} levels; magnitude {effect.MagnitudeBaseLow}–{effect.MagnitudeBaseHigh} + {effect.MagnitudeLevelBase}–{effect.MagnitudeLevelHigh} per {effect.MagnitudePerLevel} levels.").ToArray(),
            spell.RangeType, spell.Element);
    }

    internal void ChangeSpellbook(DaggerfallPlayerUiAction action)
    {
        switch (action.Kind)
        {
            case DaggerfallUiActionKind.SpellSort:
                State.Character.SortSpells(DaggerfallMagicCostPolicy.KnownSpellPoints(_definitions.Magic, State.Actors.Player.Stats, State.Character.IsGrantedSpell)); _spellResult = "Spellbook sorted."; break;
            case DaggerfallUiActionKind.SpellSwap:
                _spellResult = State.Character.SwapSpells(action.Key!, action.Item!) ? "Spell order changed." : "Those spells cannot be swapped.";
                break;
            case DaggerfallUiActionKind.SpellInfo:
                if (State.Character.KnownSpells.Contains(action.Key!) || ReadSpellSale()?.Offers.Any(offer => offer.Key == action.Key) == true)
                    _spellInfo = action.Key;
                else _spellResult = SpellbookOutcomeText("SpellUnavailable");
                break;
            case DaggerfallUiActionKind.SpellDelete:
                // Normalized source record for classic spell identity 92. Cure owns removal;
                // Vampire and lycanthrope grants carry protected source tags from the racial owner.
                string deleted = (action.Key == "spell.085" || State.Character.IsGrantedSpell(action.Key!)) && State.Character.KnownSpells.Contains(action.Key)
                    ? "ProtectedSpell" : !action.Confirm ? "ConfirmationRequired"
                    : State.Character.ForgetSpell(action.Key!) ? "Forgotten" : "UnknownSpell";
                if (deleted == "Forgotten" && _spellInfo == action.Key) _spellInfo = null;
                _spellResult = SpellbookOutcomeText(deleted);
                break;
            case DaggerfallUiActionKind.SpellBuy:
                _spellResult = SpellbookOutcomeText(CurrentSpellProvider(action.Revision) is { } context
                    ? SpellSales.Buy(context.Provider, context.Quality, action.Key!, action.Amount!.Value, action.Confirm).Outcome
                    : "ProviderUnavailable");
                break;
        }
        Presentation.SetOutcome(_spellResult);
    }

    /// <summary>Player wording for spellbook information, forgetting and spell purchase results.</summary>
    internal static string SpellbookOutcomeText(string outcome) => outcome switch
    {
        "Purchased" => "Spell added to your spellbook.", "AlreadyKnown" => "You already know that spell.",
        "PriceChanged" => "The spell price changed. Check the current offer.", "Forgotten" => "Spell forgotten.",
        "ProtectedSpell" => "This transformation spell is removed when its curse is cured.",
        "SpellbookRequired" => "You need a spellbook to buy spells.",
        "ConfirmationRequired" => "Confirm this spellbook change.", "ProviderUnavailable" => "That spell seller is no longer available.",
        "SpellUnavailable" => "That spell is not available here.", "UnknownSpell" => "You do not know that spell.",
        _ => DaggerfallServiceOutcomeText.Common(outcome),
    };

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
                    var preview = SpellMaker.Quote(context.Provider);
                    _spellResult = preview is { Eligible: true } ? "Preview" : preview?.Reason ?? "InvalidSettings";
                }
            }
            catch (JsonException) { _spellResult = "InvalidSettings"; }
        }
        else _spellResult = SpellMaker.Buy(context.Provider, action.Key!, action.Amount!.Value, action.Confirm).Outcome;
        _spellResult = SpellMakerOutcomeText(_spellResult);
        Presentation.SetOutcome(_spellResult);
    }

    /// <summary>Player wording for spellmaker previews, refusals and purchases.</summary>
    internal static string SpellMakerOutcomeText(string outcome) => outcome switch
    {
        "Purchased" => "Custom spell added to your spellbook.",
        "Preview" => "Spell construction preview updated.",
        "DraftChanged" or "PriceChanged" => "Check the current spell construction preview before buying.",
        "SpellbookRequired" => "You need a spellbook to make spells.",
        "ProviderUnavailable" => "This spellmaker is unavailable to you.",
        "InvalidSettings" => "Choose valid supported effects and settings.",
        "InvalidCombination" => "Those effects cannot use that target or element.",
        "IdentityUnavailable" or "IdentityCollision" => "The spellmaker cannot record another spell.",
        _ => DaggerfallServiceOutcomeText.Common(outcome),
    };
}

/// <param name="Icon">The spell's index in the classic spell icon atlas.</param>
/// <param name="School">The school of the spell's first effect, which the spellbook's tabs group by.</param>
internal sealed record DaggerfallKnownSpellView(string Key, string Name, int Cost, bool CanCast = true, int Icon = 0, string School = "");
/// <param name="Art">With refreshed art drawn, the replacement images of the spell icons and tiles.</param>
internal sealed record DaggerfallSpellbookView(DaggerfallKnownSpellView[] Available, string? Ready, string Result,
    DaggerfallSpellSaleView? Sale = null, DaggerfallSpellInformation? Information = null, DaggerfallSpellMakerView? Maker = null, DaggerfallPotionMakerView? PotionMaker = null, DaggerfallItemMakerView? ItemMaker = null, DaggerfallSummoningView? Summoning = null,
    DaggerfallSpellArt? Art = null);

/// <summary>Each classic spell icon atlas cell's, targeting tile's and element tile's replacement image, null where it has none.</summary>
internal sealed record DaggerfallSpellArt(IReadOnlyList<string?> Icons, IReadOnlyList<string?> Targets, IReadOnlyList<string?> Elements)
{
    internal static DaggerfallSpellArt From(DaggerfallArtReplacements art)
    {
        string?[] Images(int count, Func<int, string> media) => [.. Enumerable.Range(0, count).Select(index => art.For(media(index))?.MediaId)];
        return new(Images(DaggerfallSpellConstruction.IconCount, DaggerfallSpellConstruction.IconMedia),
            Images(DaggerfallSpellConstruction.SelectorCount, DaggerfallSpellConstruction.TargetMedia),
            Images(DaggerfallSpellConstruction.SelectorCount, DaggerfallSpellConstruction.ElementMedia));
    }
}

internal sealed record DaggerfallSpellSaleView(string Revision, string Provider, DaggerfallSpellOffer[] Offers);
/// <param name="RangeType">The spell's range type, which names its targeting tile.</param>
/// <param name="ElementType">The spell's element, which names its element tile.</param>
internal sealed record DaggerfallSpellInformation(string Key, string Name, string Element, string Target,
    string[] Details, int RangeType, int ElementType);

internal sealed record DaggerfallSpellMakerView(string Revision, string Provider, DaggerfallSpellMakerEffect[] Effects,
    DaggerfallSpellDraft Draft, DaggerfallSpellConstructionQuote? Quote);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DaggerfallSpellDraft))]
internal partial class DaggerfallSpellDraftJsonContext : JsonSerializerContext;
