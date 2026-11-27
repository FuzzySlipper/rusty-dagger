using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallSpellOffer(string Key, string Name, int CastingCost, ulong Price, bool Known,
    DaggerfallPriceBreakdown? PriceBreakdown = null);
internal sealed record DaggerfallSpellSaleResult(bool Accepted, string Outcome, ulong Paid = 0);

/// <summary>
/// Spell sales over canonical guild admission, casting prices, currency and known spells. A guild service, so
/// Economy's, as the item maker is.
/// </summary>
internal sealed class DaggerfallSpellSales(DaggerfallDefinitions definitions, DaggerfallNpcRegistry npcs,
    DaggerfallConcreteGuildServiceRuntime guildServices, DaggerfallServiceTransactions services,
    DaggerfallCharacterState character, MechanicsInventoryCoordinator inventory, StatsComponent playerStats,
    Func<string, int?> castingCost, Func<DaggerfallCalendar> calendar,
    Func<DaggerfallNpc, int>? tradeReaction = null, Func<int, int, int>? magicServicePrice = null,
    Func<DaggerfallNpc, DaggerfallTradeReactionBreakdown>? tradeReactionBreakdown = null,
    Func<int, int, string, (int Price, DaggerfallPriceBreakdown Breakdown)>? magicServiceQuote = null)
{
    // DFLocation.Holidays.Witches_Festival, whose date is owned by the existing calendar.
    private const int WitchesFestival = 43;
    private long _nextRequest;

    internal static DaggerfallConcreteGuildDefinition? GuildForProvider(int faction) =>
        DaggerfallConcreteGuildCatalog.All.FirstOrDefault(guild => guild.TryGetService(DaggerfallConcreteGuildService.BuySpells, out var service)
            && service.ProviderFactionId == faction);

    internal DaggerfallSpellOffer[] Offers(DaggerfallServiceProvider provider, int quality)
    {
        if (!CanUse(provider)) return [];
        DaggerfallTradeReactionBreakdown reaction = ResolveReactionBreakdown(provider);
        return definitions.Magic.Spells.Values.Where(spell => spell.SpellsForSale).OrderBy(spell => spell.Name, StringComparer.Ordinal)
            .Select(spell => Offer(spell.Key, quality, reaction, provider.Site.Region)).OfType<DaggerfallSpellOffer>().ToArray();
    }

    internal DaggerfallSpellSaleResult Buy(DaggerfallServiceProvider provider, int quality, string key, ulong quotedPrice, bool confirm)
    {
        if (!confirm) return new(false, "ConfirmationRequired");
        if (!CanUse(provider)) return new(false, "ProviderUnavailable");
        var offer = Offer(key, quality, ResolveReactionBreakdown(provider), provider.Site.Region);
        if (offer is null) return new(false, "SpellUnavailable");
        if (offer.Known) return new(false, "AlreadyKnown");
        if (offer.Price != quotedPrice) return new(false, "PriceChanged");
        if (!HasSpellbook()) return new(false, "SpellbookRequired");
        var guild = GuildForProvider(npcs.Require(provider.NpcId).Appearance.FactionId)!;
        guild.TryGetService(DaggerfallConcreteGuildService.BuySpells, out var policy);
        var quote = services.Quote(new($"spell-sale-{checked(++_nextRequest)}", provider),
            new(policy.RequiresMembership, policy.MinimumRank ?? 0, guild.FactionId), new(offer.Price));
        if (quote.Quote is null) return new(false, quote.Outcome.Denial.ToString());
        var payment = services.Commit(quote.Quote);
        if (!payment.Accepted) return new(false, payment.Denial.ToString());
        // All definition, membership and duplicate checks preceded payment in this admitted update.
        character.LearnSpell(key);
        return new(true, "Purchased", payment.PaidGold);
    }

    private bool CanUse(DaggerfallServiceProvider provider)
    {
        DaggerfallNpc npc;
        try { npc = npcs.Require(provider.NpcId); }
        catch (InvalidOperationException) { return false; }
        var guild = GuildForProvider(npc.Appearance.FactionId);
        return guild is not null && guildServices.Evaluate(guild.FactionId,
            DaggerfallConcreteGuildService.BuySpells, checked((int)calendar().DayNumber), new(provider)).CanUse;
    }

    private DaggerfallTradeReactionBreakdown ResolveReactionBreakdown(DaggerfallServiceProvider provider)
    {
        if (!npcs.TryGet(provider.NpcId, out DaggerfallNpc? npc))
            return DaggerfallTradeReactionBreakdown.Plain(0);
        if (tradeReactionBreakdown is not null)
            return tradeReactionBreakdown(npc);
        int raw = tradeReaction is not null
            ? Math.Clamp(tradeReaction(npc), DaggerfallRegionalEconomyPolicy.MinimumReaction, DaggerfallRegionalEconomyPolicy.MaximumReaction)
            : 0;
        return DaggerfallTradeReactionBreakdown.Plain(raw);
    }

    private DaggerfallSpellOffer? Offer(string key, int quality, DaggerfallTradeReactionBreakdown reaction, int region)
    {
        if (!definitions.Magic.Spells.TryGetValue(key, out var spell) || !spell.SpellsForSale || castingCost(key) is not int cost) return null;
        int basePresented = checked(cost * 4);
        int presented = basePresented;
        List<DaggerfallPriceLine> steps = [];
        int running = basePresented;
        if (calendar().GetHolidayId(0) == WitchesFestival)
        {
            presented = Math.Max(1, presented >> 1);
            if (presented != running)
            {
                steps.Add(new("Witches Festival", presented - running, "\u221250%"));
                running = presented;
            }
        }
        int mercantile = Math.Clamp(playerStats.GetStat(StatId.Parse("mercantile")).ValueInt, 0, 100);
        int personality = Math.Clamp(playerStats.GetStat(StatId.Parse("personality")).ValueInt, 0, 100);
        int clampedReaction = Math.Clamp(reaction.Total, DaggerfallRegionalEconomyPolicy.MinimumReaction, DaggerfallRegionalEconomyPolicy.MaximumReaction);

        int atNeutral = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(presented, quality, false, mercantile, personality, 0);
        if (atNeutral != running)
        {
            steps.Add(new("Mercantile & haggling", atNeutral - running));
            running = atNeutral;
        }

        int unclamped = reaction.BaseSocialReaction;
        int clampedStanding = Math.Clamp(unclamped, DaggerfallRegionalEconomyPolicy.MinimumReaction, DaggerfallRegionalEconomyPolicy.MaximumReaction);
        int atStanding = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(presented, quality, false, mercantile, personality, clampedStanding);
        if (atStanding != running)
        {
            string standingLabel = clampedStanding >= 0
                ? $"Standing with provider (+{clampedStanding})"
                : $"Standing with provider (\u2212{Math.Abs(clampedStanding)})";
            steps.Add(new(standingLabel, atStanding - running));
            running = atStanding;
        }

        foreach (DaggerfallReactionShiftCause shift in reaction.OfficeShifts)
        {
            unclamped += shift.Delta;
            int clampedNext = Math.Clamp(unclamped, DaggerfallRegionalEconomyPolicy.MinimumReaction, DaggerfallRegionalEconomyPolicy.MaximumReaction);
            int nextPrice = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(presented, quality, false, mercantile, personality, clampedNext);
            if (nextPrice != running)
            {
                steps.Add(new(shift.Label, nextPrice - running));
                running = nextPrice;
            }
        }

        int beforeMagicCondition = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(presented, quality, false, mercantile, personality, clampedReaction);
        if (beforeMagicCondition != running)
        {
            steps.Add(new("Standing adjustment", beforeMagicCondition - running));
            running = beforeMagicCondition;
        }

        int price = beforeMagicCondition;
        if (magicServiceQuote is not null)
        {
            (int adjusted, DaggerfallPriceBreakdown magicBreakdown) = magicServiceQuote(region, beforeMagicCondition, "Spell price");
            foreach (DaggerfallPriceLine step in magicBreakdown.Steps)
                steps.Add(step);
            price = adjusted;
        }
        else if (magicServicePrice is not null)
        {
            int adjusted = magicServicePrice(region, beforeMagicCondition);
            if (adjusted != beforeMagicCondition)
                steps.Add(new("Magic service surcharge", adjusted - beforeMagicCondition));
            price = adjusted;
        }

        DaggerfallPriceBreakdown breakdown = new("Base spell price", basePresented, steps, price);
        return new(key, spell.Name, cost, checked((ulong)price), character.KnownSpells.Contains(key), breakdown);
    }

    private bool HasSpellbook() => inventory.Read().UniqueItems.Any(item =>
        definitions.RequireItem(new(item.Definition.Value)).Template?.Index == 132);
}
