using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The spellbook frame: the spells the player knows and the one ready, with the guild's spell sale, spellmaker, the
/// makers' and summoning panes beside them, and the actions that change them, worded for the player.
/// </summary>
/// <param name="actors">The actors, whose player knows and casts the spells.</param>
/// <param name="drawnArt">The refreshed art set drawn, or null for the classic one.</param>
internal sealed class DaggerfallSpellbookPanel(DaggerfallCharacterState character, ActorsState actors, DaggerfallCasting casting,
    DaggerfallSpellRelease release, DaggerfallDefinitions definitions, DaggerfallSpellSales sales, DaggerfallSpellMaker maker,
    DaggerfallServiceCounter counter, DaggerfallMakerPanels makers, DaggerfallSummoningPanel summoning,
    Func<DaggerfallArtReplacements?> drawnArt, PresentationState presentation)
{
    private string _spellResult="";
    internal DaggerfallSpellbookView Read() => new(
        character.KnownSpells.Select(key =>
        {
            int? cost = casting.AvailableSpellCost(actors.Player.DurableId, key);
            DaggerfallSpellDefinition spell = definitions.Magic.Spells[key];
            string school = spell.Effects.Count == 0 ? string.Empty : definitions.Magic.RequireEffectCost(spell.Effects[0]).School;
            return new DaggerfallKnownSpellView(key, character.IsGrantedSpell(key) ? spell.Name.TrimStart('!') : spell.Name, cost ?? 0, cost is not null,
                Math.Clamp(spell.Icon, 0, DaggerfallSpellConstruction.IconCount - 1), school);
        }).ToArray(),
        casting.ReadyFor(actors.Player.DurableId) is { Source: DaggerfallCastSource.Spell or DaggerfallCastSource.DungeonAction } ready ? ready.SpellKey : null,
        _spellResult, ReadSpellSale(), ReadSpellInformation(), ReadSpellMaker(), makers.ReadPotionMaker(), makers.ReadItemMaker(), summoning.Read(),
        drawnArt() is { } art ? DaggerfallSpellArt.From(art) : null);
    internal void ChangeSpell(DaggerfallPlayerUiAction action)
    {
        var result=action.Kind switch
        {
            DaggerfallUiActionKind.SpellReady=>release.ReadyPlayerSpell(action.Key!),
            _=>casting.Cancel(actors.Player.DurableId),
        };
        ReportCast(result.Outcome);
    }

    internal void ReportCast(DaggerfallCastOutcome outcome)
    {
        _spellResult=CastOutcomeText(outcome);
        presentation.SetOutcome(_spellResult);
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

    internal DaggerfallSpellSaleView? ReadSpellSale()
    {
        if (counter.Provider() is not { } context) return null;
        var offers = sales.Offers(context.Provider, context.Quality);
        return offers.Length == 0 ? null : new(counter.Page.Revision,
            counter.Page.TargetLabel, offers);
    }

    private DaggerfallSpellInformation? ReadSpellInformation()
    {
        if (_spellInfo is not { } key || !definitions.Magic.Spells.TryGetValue(key, out var spell)) return null;
        return new(key, character.IsGrantedSpell(key) ? spell.Name.TrimStart('!') : spell.Name, DaggerfallMagicCostPolicy.ElementLabel(spell.Element), DaggerfallMagicCostPolicy.TargetLabel(spell.RangeType),
            spell.Effects.Select(effect => $"{definitions.Magic.RequireEffectCost(effect).School}: duration {effect.DurationBase} + {effect.DurationMod} per {effect.DurationPerLevel} levels; chance {effect.ChanceBase}% + {effect.ChanceMod}% per {effect.ChancePerLevel} levels; magnitude {effect.MagnitudeBaseLow}–{effect.MagnitudeBaseHigh} + {effect.MagnitudeLevelBase}–{effect.MagnitudeLevelHigh} per {effect.MagnitudePerLevel} levels.").ToArray(),
            spell.RangeType, spell.Element);
    }

    internal void ChangeSpellbook(DaggerfallPlayerUiAction action)
    {
        switch (action.Kind)
        {
            case DaggerfallUiActionKind.SpellSort:
                character.SortSpells(DaggerfallMagicCostPolicy.KnownSpellPoints(definitions.Magic, actors.Player.Stats, character.IsGrantedSpell)); _spellResult = "Spellbook sorted."; break;
            case DaggerfallUiActionKind.SpellSwap:
                _spellResult = character.SwapSpells(action.Key!, action.Item!) ? "Spell order changed." : "Those spells cannot be swapped.";
                break;
            case DaggerfallUiActionKind.SpellInfo:
                if (character.KnownSpells.Contains(action.Key!) || ReadSpellSale()?.Offers.Any(offer => offer.Key == action.Key) == true)
                    _spellInfo = action.Key;
                else _spellResult = SpellbookOutcomeText("SpellUnavailable");
                break;
            case DaggerfallUiActionKind.SpellDelete:
                // Normalized source record for classic spell identity 92. Cure owns removal;
                // Vampire and lycanthrope grants carry protected source tags from the racial owner.
                string deleted = (action.Key == "spell.085" || character.IsGrantedSpell(action.Key!)) && character.KnownSpells.Contains(action.Key)
                    ? "ProtectedSpell" : !action.Confirm ? "ConfirmationRequired"
                    : character.ForgetSpell(action.Key!) ? "Forgotten" : "UnknownSpell";
                if (deleted == "Forgotten" && _spellInfo == action.Key) _spellInfo = null;
                _spellResult = SpellbookOutcomeText(deleted);
                break;
            case DaggerfallUiActionKind.SpellBuy:
                _spellResult = SpellbookOutcomeText(counter.Provider(action.Revision) is { } context
                    ? sales.Buy(context.Provider, context.Quality, action.Key!, action.Amount!.Value, action.Confirm).Outcome
                    : "ProviderUnavailable");
                break;
        }
        presentation.SetOutcome(_spellResult);
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
        if (counter.Provider(service: "make-spells") is not { } context) return null;
        return new(counter.Page.Revision, counter.Page.TargetLabel,
            maker.Effects, maker.Draft, maker.Quote(context.Provider));
    }

    internal void ChangeSpellMaker(DaggerfallPlayerUiAction action)
    {
        if (counter.Provider(action.Revision, "make-spells") is not { } context)
            _spellResult = "ProviderUnavailable";
        else if (action.Kind == DaggerfallUiActionKind.SpellMakerDraft)
        {
            try
            {
                var draft = JsonSerializer.Deserialize(action.Text!, DaggerfallSpellDraftJsonContext.Default.DaggerfallSpellDraft);
                if (draft is null || draft.Name is null || draft.Effects is null) _spellResult = "InvalidSettings";
                else
                {
                    maker.SetDraft(draft);
                    var preview = maker.Quote(context.Provider);
                    _spellResult = preview is { Eligible: true } ? "Preview" : preview?.Reason ?? "InvalidSettings";
                }
            }
            catch (JsonException) { _spellResult = "InvalidSettings"; }
        }
        else _spellResult = maker.Buy(context.Provider, action.Key!, action.Amount!.Value, action.Confirm).Outcome;
        _spellResult = SpellMakerOutcomeText(_spellResult);
        presentation.SetOutcome(_spellResult);
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
