using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallSpellDraft(string Name, int Element, int RangeType, int Icon,
    DaggerfallSpellEffectDefinition[] Effects);
internal sealed record DaggerfallSpellMakerEffect(string Key, int Type, int SubType, string School,
    bool Duration, bool Chance, bool Magnitude, int Targets, int Elements);
internal sealed record DaggerfallSpellConstructionQuote(string Key, DaggerfallSpellDraft Draft,
    int Gold, int SpellPoints, bool Eligible, string? Reason);

/// <summary>Classic construction settings over compiled effects and the common normalized catalog.</summary>
internal static class DaggerfallSpellConstruction
{
    internal static DaggerfallSpellMakerEffect[] Effects(DaggerfallMagicCatalogSet magic, DaggerfallEffectCatalog effects) =>
        [.. effects.SpellDefinitions.Where(definition => definition.Spell is { SpellMaker: true } spell
            && magic.EffectCosts.ContainsKey((spell.Type, spell.SubType)))
        .Select(definition =>
        {
            var binding = definition.Spell!;
            var costs = magic.RequireEffectCost(new(definition.Key, binding.Type, binding.SubType,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1));
            return new DaggerfallSpellMakerEffect(definition.Key, binding.Type, binding.SubType, costs.School,
                costs.RegularComponents?.Duration is not null, costs.RegularComponents?.Chance is not null,
                costs.RegularComponents?.Magnitude is not null, (int)binding.AllowedTargets, (int)binding.AllowedElements);
        }).OrderBy(effect => effect.School, StringComparer.Ordinal).ThenBy(effect => effect.Key, StringComparer.Ordinal)];

    internal static void ValidateDefinition(DaggerfallMagicCatalogSet magic, DaggerfallSpellDefinition spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        if (!spell.IsCustom || string.IsNullOrWhiteSpace(spell.Key) || spell.Identity != -1 || spell.IdentityShared
            || spell.IsPlayerCreated && (!spell.Key.StartsWith("custom-spell.", StringComparison.Ordinal) || spell.SpellsForSale))
            throw new ArgumentException("A constructed spell must retain its custom identity and sale eligibility.");
        if (spell.IsPlayerCreated && (!long.TryParse(spell.Key["custom-spell.".Length..],
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var identity) || identity <= 0))
            throw new ArgumentException("A player-created spell requires a positive durable identity.");
        ArgumentNullException.ThrowIfNull(spell.Effects);
        try { ValidateDraft(magic, new(spell.Name, spell.Element, spell.RangeType, spell.Icon, [.. spell.Effects])); }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        { throw new ArgumentException($"Constructed spell '{spell.Key}' has invalid effect settings.", exception); }
    }

    internal static void ValidateDraft(DaggerfallMagicCatalogSet magic, DaggerfallSpellDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrEmpty(draft.Name) || draft.Name.Length > 31
            || draft.Element is < 0 or > 4 || draft.RangeType is < 0 or > 4 || draft.Icon is < 0 or > 68
            || draft.Effects is null || draft.Effects.Length is < 1 or > 3)
            throw new ArgumentException("Name, icon, target, element and one to three effects are required.");
        foreach (var setting in draft.Effects)
        {
            ArgumentNullException.ThrowIfNull(setting);
            var components = magic.RequireEffectCost(setting).RegularComponents
                ?? throw new ArgumentException("This effect has no construction settings.");
            if (components.Duration is not null && (!Range(setting.DurationBase, 60) || !Range(setting.DurationMod, 60) || !Range(setting.DurationPerLevel, 20))
                || components.Chance is not null && (!Range(setting.ChanceBase, 100) || !Range(setting.ChanceMod, 100) || !Range(setting.ChancePerLevel, 20))
                || components.Magnitude is not null && (!Range(setting.MagnitudeBaseLow, 100) || !Range(setting.MagnitudeBaseHigh, 100)
                    || !Range(setting.MagnitudeLevelBase, 100) || !Range(setting.MagnitudeLevelHigh, 100) || !Range(setting.MagnitudePerLevel, 20)
                    || setting.MagnitudeBaseLow > setting.MagnitudeBaseHigh || setting.MagnitudeLevelBase > setting.MagnitudeLevelHigh))
                throw new ArgumentException("Construction settings are outside the classic editor ranges.");
            if (new[] { setting.DurationBase, setting.DurationMod, setting.DurationPerLevel, setting.ChanceBase,
                setting.ChanceMod, setting.ChancePerLevel, setting.MagnitudeBaseLow, setting.MagnitudeBaseHigh,
                setting.MagnitudeLevelBase, setting.MagnitudeLevelHigh, setting.MagnitudePerLevel }.Any(value => value is < 0 or > 100))
                throw new ArgumentException("Construction settings contain an invalid value.");
        }
    }

    private static bool Range(int value, int maximum) => value >= 1 && value <= maximum;
}

/// <summary>The spellmaker's current draft; payment and completed definitions use the existing session owners.</summary>
internal sealed class DaggerfallSpellMaker(DaggerfallDefinitions definitions, DaggerfallState state,
    Func<DaggerfallCalendar> calendar)
{
    private DaggerfallSpellDraft? _draft;
    private long _draftRevision;
    internal DaggerfallSpellDraft Draft => _draft ?? new("", 4, 0, 1, []);
    internal string Revision => _draftRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal DaggerfallSpellMakerEffect[] Effects => DaggerfallSpellConstruction.Effects(definitions.Magic, state.Effects.Catalog);

    internal void SetDraft(DaggerfallSpellDraft draft)
    {
        _draft = draft with { Effects = [.. draft.Effects] };
        _draftRevision = checked(_draftRevision + 1);
    }

    internal DaggerfallSpellConstructionQuote? Quote(DaggerfallServiceProvider provider)
    {
        if (_draft is null) return null;
        try
        {
            if (!CanUse(provider)) return new(Revision, _draft, 0, 0, false, "ProviderUnavailable");
            DaggerfallSpellConstruction.ValidateDraft(definitions.Magic, _draft);
            int allowedElements = (int)DaggerfallMagicAllowedElements.Magic;
            foreach (var setting in _draft.Effects)
            {
                var option = Effects.FirstOrDefault(option => option.Type == setting.Type && option.SubType == setting.SubType);
                if (option is null || (option.Targets & (1 << _draft.RangeType)) == 0)
                    return new(Revision, _draft, 0, 0, false, "InvalidCombination");
                allowedElements |= option.Elements;
            }
            if ((allowedElements & (1 << _draft.Element)) == 0) return new(Revision, _draft, 0, 0, false, "InvalidCombination");
            var skills = Effects.Select(option => option.School).Distinct(StringComparer.Ordinal).ToDictionary(school => school,
                school => Math.Clamp(DaggerfallCasting.Read(state.Actors.Player.Stats, school), 0, 100));
            var cost = DaggerfallMagicCostPolicy.CalculateTotalEffectCosts(definitions.Magic, _draft.Effects,
                DaggerfallMagicCostPolicy.TargetForRangeType(_draft.RangeType), skills);
            return new(Revision, _draft, cost.Gold, cost.SpellPoints, true, null);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or InvalidOperationException)
        { return new(Revision, _draft, 0, 0, false, "InvalidSettings"); }
    }

    internal DaggerfallSpellSaleResult Buy(DaggerfallServiceProvider provider, string quotedRevision, ulong quotedGold, bool confirm)
    {
        if (!confirm) return new(false, "ConfirmationRequired");
        if (quotedRevision != Revision) return new(false, "DraftChanged");
        var quote = Quote(provider);
        if (quote is null || !quote.Eligible) return new(false, quote?.Reason ?? "InvalidSettings");
        if ((ulong)quote.Gold != quotedGold) return new(false, "PriceChanged");
        if (!state.Inventory.Read().UniqueItems.Any(item => definitions.RequireItem(new(item.Definition.Value)).Template?.Index == 132))
            return new(false, "SpellbookRequired");
        var guild = GuildFor(provider)!;
        guild.TryGetService(DaggerfallConcreteGuildService.MakeSpells, out var policy);
        var paymentQuote = state.Services.Quote(new($"spellmaker-{Revision}", provider),
            new(policy.RequiresMembership, policy.MinimumRank ?? 0, guild.FactionId), new(quotedGold));
        if (paymentQuote.Quote is null) return new(false, paymentQuote.Outcome.Denial.ToString());
        long lastIdentity = definitions.Magic.Spells.Values.Where(spell => spell.IsPlayerCreated)
            .Select(spell => long.Parse(spell.Key["custom-spell.".Length..], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        if (lastIdentity == long.MaxValue) return new(false, "IdentityUnavailable");
        long identity = lastIdentity + 1;
        var spell = new DaggerfallSpellDefinition($"custom-spell.{identity}", -1, false, _draft!.Name, _draft.Element,
            _draft.RangeType, quote.SpellPoints, _draft.Icon, [.. _draft.Effects]) { IsCustom = true, IsPlayerCreated = true, SpellsForSale = false };
        DaggerfallSpellConstruction.ValidateDefinition(definitions.Magic, spell);
        if (definitions.Magic.Spells.ContainsKey(spell.Key)) return new(false, "IdentityCollision");
        var payment = state.Services.Commit(paymentQuote.Quote);
        if (!payment.Accepted) return new(false, payment.Denial.ToString());
        definitions.Magic.AddConstructedSpell(spell);
        state.Character.LearnSpell(spell.Key);
        // The next revision cannot resubmit this completed purchase, including zero-gold constructions.
        _draft = null;
        _draftRevision = checked(_draftRevision + 1);
        return new(true, "Purchased", payment.PaidGold);
    }

    private DaggerfallConcreteGuildDefinition? GuildFor(DaggerfallServiceProvider provider)
    {
        DaggerfallNpc npc;
        try { npc = state.Npcs.Require(provider.NpcId); }
        catch (InvalidOperationException) { return null; }
        return DaggerfallConcreteGuildCatalog.All.FirstOrDefault(guild => guild.TryGetService(DaggerfallConcreteGuildService.MakeSpells, out var service)
            && service.ProviderFactionId == npc.Appearance.FactionId);
    }

    private bool CanUse(DaggerfallServiceProvider provider) => GuildFor(provider) is { } guild
        && state.ConcreteGuildServices.Evaluate(guild.FactionId, DaggerfallConcreteGuildService.MakeSpells,
            checked((int)calendar().DayNumber), new(provider)).CanUse;
}
