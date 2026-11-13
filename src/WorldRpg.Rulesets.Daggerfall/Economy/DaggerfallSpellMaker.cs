using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The spellmaker's current draft over Magic's construction rules; payment and completed definitions use the
/// existing owners. A guild service, so Economy's, as the item maker is.
/// </summary>
internal sealed class DaggerfallSpellMaker(DaggerfallDefinitions definitions, DaggerfallEffectCatalog effects,
    DaggerfallNpcRegistry npcs, DaggerfallConcreteGuildServiceRuntime guildServices, DaggerfallServiceTransactions services,
    DaggerfallCharacterState character, MechanicsInventoryCoordinator inventory, StatsComponent playerStats,
    Func<DaggerfallCalendar> calendar)
{
    private DaggerfallSpellDraft? _draft;
    private long _draftRevision;
    internal DaggerfallSpellDraft Draft => _draft ?? new("", 4, 0, 1, []);
    internal string Revision => _draftRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal DaggerfallSpellMakerEffect[] Effects => DaggerfallSpellConstruction.Effects(definitions.Magic, effects);

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
                school => Math.Clamp(DaggerfallCasting.Read(playerStats, school), 0, 100));
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
        if (!inventory.Read().UniqueItems.Any(item => definitions.RequireItem(new(item.Definition.Value)).Template?.Index == 132))
            return new(false, "SpellbookRequired");
        var guild = GuildFor(provider)!;
        guild.TryGetService(DaggerfallConcreteGuildService.MakeSpells, out var policy);
        var paymentQuote = services.Quote(new($"spellmaker-{Revision}", provider),
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
        var payment = services.Commit(paymentQuote.Quote);
        if (!payment.Accepted) return new(false, payment.Denial.ToString());
        definitions.Magic.AddConstructedSpell(spell);
        character.LearnSpell(spell.Key);
        // The next revision cannot resubmit this completed purchase, including zero-gold constructions.
        _draft = null;
        _draftRevision = checked(_draftRevision + 1);
        return new(true, "Purchased", payment.PaidGold);
    }

    private DaggerfallConcreteGuildDefinition? GuildFor(DaggerfallServiceProvider provider)
    {
        DaggerfallNpc npc;
        try { npc = npcs.Require(provider.NpcId); }
        catch (InvalidOperationException) { return null; }
        return DaggerfallConcreteGuildCatalog.All.FirstOrDefault(guild => guild.TryGetService(DaggerfallConcreteGuildService.MakeSpells, out var service)
            && service.ProviderFactionId == npc.Appearance.FactionId);
    }

    private bool CanUse(DaggerfallServiceProvider provider) => GuildFor(provider) is { } guild
        && guildServices.Evaluate(guild.FactionId, DaggerfallConcreteGuildService.MakeSpells,
            checked((int)calendar().DayNumber), new(provider)).CanUse;
}
