using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallSpellOffer(string Key, string Name, int CastingCost, ulong Price, bool Known);
internal sealed record DaggerfallSpellSaleResult(bool Accepted, string Outcome, ulong Paid = 0);

/// <summary>Spell sales over canonical guild admission, casting prices, currency and known spells.</summary>
internal sealed class DaggerfallSpellSales(DaggerfallDefinitions definitions, DaggerfallState state,
    Func<string, int?> castingCost, Func<DaggerfallCalendar> calendar)
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
        return definitions.Magic.Spells.Values.OrderBy(spell => spell.Name, StringComparer.Ordinal)
            .Select(spell => Offer(spell.Key, quality)).OfType<DaggerfallSpellOffer>().ToArray();
    }

    internal DaggerfallSpellSaleResult Buy(DaggerfallServiceProvider provider, int quality, string key, ulong quotedPrice, bool confirm)
    {
        if (!confirm) return new(false, "ConfirmationRequired");
        if (!CanUse(provider)) return new(false, "ProviderUnavailable");
        var offer = Offer(key, quality);
        if (offer is null) return new(false, "SpellUnavailable");
        if (offer.Known) return new(false, "AlreadyKnown");
        if (offer.Price != quotedPrice) return new(false, "PriceChanged");
        if (!HasSpellbook()) return new(false, "SpellbookRequired");
        var guild = GuildForProvider(state.Npcs.Require(provider.NpcId).Appearance.FactionId)!;
        guild.TryGetService(DaggerfallConcreteGuildService.BuySpells, out var policy);
        var quote = state.Services.Quote(new($"spell-sale-{checked(++_nextRequest)}", provider),
            new(policy.RequiresMembership, policy.MinimumRank ?? 0, guild.FactionId), new(offer.Price));
        if (quote.Quote is null) return new(false, quote.Outcome.Denial.ToString());
        var payment = state.Services.Commit(quote.Quote);
        if (!payment.Accepted) return new(false, payment.Denial.ToString());
        // All definition, membership and duplicate checks preceded payment in this admitted update.
        state.Character.LearnSpell(key);
        return new(true, "Purchased", payment.PaidGold);
    }

    private bool CanUse(DaggerfallServiceProvider provider)
    {
        DaggerfallNpc npc;
        try { npc = state.Npcs.Require(provider.NpcId); }
        catch (InvalidOperationException) { return false; }
        var guild = GuildForProvider(npc.Appearance.FactionId);
        return guild is not null && state.ConcreteGuildServices.Evaluate(guild.FactionId,
            DaggerfallConcreteGuildService.BuySpells, checked((int)calendar().DayNumber), new(provider)).CanUse;
    }

    private DaggerfallSpellOffer? Offer(string key, int quality)
    {
        if (!definitions.Magic.Spells.TryGetValue(key, out var spell) || castingCost(key) is not int cost) return null;
        int presented = checked(cost * 4);
        if (calendar().GetHolidayId(0) == WitchesFestival) presented = Math.Max(1, presented >> 1);
        var stats = state.Actors.Player.Stats;
        int price = DaggerfallRegionalEconomyPolicy.CalculateTradePrice(presented, quality, false,
            Math.Clamp(stats.GetStat(StatId.Parse("mercantile")).ValueInt, 0, 100),
            Math.Clamp(stats.GetStat(StatId.Parse("personality")).ValueInt, 0, 100), 0);
        return new(key, spell.Name, cost, checked((ulong)price), state.Character.KnownSpells.Contains(key));
    }

    private bool HasSpellbook() => state.Inventory.Read().UniqueItems.Any(item =>
        definitions.RequireItem(new(item.Definition.Value)).Template?.Index == 132);
}
