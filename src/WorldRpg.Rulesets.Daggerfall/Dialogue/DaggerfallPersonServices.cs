namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One service a person offers, as the person screen's chooser names it.</summary>
internal sealed record DaggerfallPersonService(string Id, string Label);

/// <summary>
/// The services a person offers in conversation, in the donor's service-window order: talk first, then
/// the trade, training, food and drink, spells for sale, the makers and Daedric summoning. Each is listed
/// only when its own page has something to draw, so the chooser never offers an empty page. Temple
/// donation, curing, banking and the guild topics are asked about in talk and are not separate pages.
/// </summary>
internal static class DaggerfallPersonServices
{
    internal const string Chooser = "services";
    internal const string Talk = "talk";
    internal const string Trade = "trade";
    internal const string Training = "training";
    internal const string Tavern = "tavern";
    internal const string Spells = "spells";
    internal const string ItemMaker = "item-maker";
    internal const string PotionMaker = "potion-maker";
    internal const string SpellMaker = "spell-maker";
    internal const string Summoning = "summoning";

    internal static readonly IReadOnlyList<DaggerfallPersonService> TalkOnly = [new(Talk, "Talk")];

    internal static IReadOnlyList<DaggerfallPersonService> For(DaggerfallMerchantView? merchant, bool training, bool tavern,
        bool spells, bool itemMaker, bool potionMaker, bool spellMaker, bool summoning)
    {
        List<DaggerfallPersonService> services = [.. TalkOnly];
        if (merchant is not null && TradeLabel(merchant) is { } trade) services.Add(new(Trade, trade));
        if (training) services.Add(new(Training, "Training"));
        if (tavern) services.Add(new(Tavern, "Food and drink"));
        if (spells) services.Add(new(Spells, "Buy spells"));
        if (itemMaker) services.Add(new(ItemMaker, "Make magic items"));
        if (potionMaker) services.Add(new(PotionMaker, "Make potions"));
        if (spellMaker) services.Add(new(SpellMaker, "Make spells"));
        if (summoning) services.Add(new(Summoning, "Summon a Daedra"));
        return services;
    }

    /// <summary>The trade's name from what this provider deals in, as "Buy, sell and repair"; null when it deals in nothing.</summary>
    private static string? TradeLabel(DaggerfallMerchantView merchant)
    {
        string[] parts = [.. new[] { (merchant.CanBuy, "buy"), (merchant.CanSell, "sell"), (merchant.CanRepair, "repair"), (merchant.CanIdentify, "identify") }
            .Where(part => part.Item1).Select(part => part.Item2)];
        if (parts.Length == 0) return null;
        string joined = parts.Length == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";
        return char.ToUpperInvariant(joined[0]) + joined[1..];
    }
}
