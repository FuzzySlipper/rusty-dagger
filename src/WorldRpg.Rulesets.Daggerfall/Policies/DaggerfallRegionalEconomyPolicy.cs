using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>Classic Daggerfall merchant and regional-price arithmetic.</summary>
internal static class DaggerfallRegionalEconomyPolicy
{
    internal const int RegionCount = 62;
    internal const int MinimumShopQuality = 0;
    internal const int MaximumShopQuality = 20;
    internal const int MinimumRegionalAdjustment = 250;
    internal const int MaximumRegionalAdjustment = 4000;
    internal const int NeutralRegionalAdjustment = 1000;
    internal const int InitialRegionalAdjustmentMinimum = 750;
    internal const int InitialRegionalAdjustmentMaximum = 1250;
    internal const int MerchantFactionId = 510;
    internal const int ProvinceFactionType = 7;
    internal const int MinimumFactionPower = 1;
    internal const int MaximumFactionPower = 100;
    internal const int MinimumSocialValue = 0;
    internal const int MaximumSocialValue = 100;
    internal const int MinimumReaction = -100;
    internal const int MaximumReaction = 100;

    private const long RandomSeed = 0;
    private const string RandomScope = "daggerfall.regional-economy.v1";

    internal static int ApplyRegionalPriceAdjustment(int cost, int? region, int regionalAdjustment)
    {
        if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost), "A price cannot be negative.");
        if (region is null) return cost;
        RequireRegion(region.Value);
        RequireAdjustment(regionalAdjustment);
        long adjusted = checked((long)cost * regionalAdjustment / NeutralRegionalAdjustment);
        return checked((int)Math.Max(1L, adjusted));
    }

    /// <summary>
    /// Donor FormulaHelper.CalculateCost; its optional condition percentage is retained but unused
    /// exactly as in the donor. The actual quote owner applies condition from current instance data.
    /// </summary>
    internal static int CalculateCost(int baseValue, int shopQuality, int? region, int regionalAdjustment, int conditionPercentage = -1)
    {
        RequireShopQuality(shopQuality);
        if (baseValue < 0) throw new ArgumentOutOfRangeException(nameof(baseValue), "An item value cannot be negative.");

        int cost = ApplyRegionalPriceAdjustment(Math.Max(1, baseValue), region, regionalAdjustment);
        long qualityScaled = checked((long)cost * (shopQuality - 10) / 100L);
        long shopCost = checked(2L * checked(qualityScaled + cost));
        return checked((int)shopCost);
    }

    /// <summary>
    /// Donor FormulaHelper.CalculateTradePrice using the classic Q8 operations in their original
    /// order. The bounded reaction adjustment is a task-required extension to player personality.
    /// </summary>
    internal static int CalculateTradePrice(int cost, int shopQuality, bool selling,
        int playerMercantile, int playerPersonality, int reaction)
    {
        if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
        RequireShopQuality(shopQuality);
        RequireSocialValue(playerMercantile, nameof(playerMercantile));
        RequireSocialValue(playerPersonality, nameof(playerPersonality));
        if (reaction is < MinimumReaction or > MaximumReaction)
            throw new ArgumentOutOfRangeException(nameof(reaction), $"Trade reaction must be in [{MinimumReaction}, {MaximumReaction}].");

        int effectivePersonality = Math.Clamp(checked(playerPersonality + reaction), MinimumSocialValue, MaximumSocialValue);
        int merchantLevel = checked(5 * (shopQuality - 10) + 50);
        long merchantQ8 = checked(((long)merchantLevel << 8) / 200 + 128);
        long amount;

        if (selling)
        {
            long merchantSkillDelta = checked(((((100L - merchantLevel) << 8) / 200 + 128)
                * (((long)playerMercantile << 8) / 200 + 128)) >> 8);
            long merchantPersonalityDelta = checked(((((100L - merchantLevel) << 8) / 200 + 128)
                * (((long)effectivePersonality << 8) / 200 + 128)) >> 8);
            long skillWeight = checked(179L * merchantSkillDelta) >> 8;
            long personalityWeight = checked(51L * merchantPersonalityDelta) >> 8;
            amount = checked(checked(skillWeight + personalityWeight) * cost) >> 8;
        }
        else
        {
            long playerSkillComplement = checked((((100L - playerMercantile) << 8) / 200 + 128));
            long playerPersonalityComplement = checked((((100L - effectivePersonality) << 8) / 200 + 128));
            long merchantSkillDelta = checked(merchantQ8 * playerSkillComplement) >> 8;
            long merchantPersonalityDelta = checked(merchantQ8 * playerPersonalityComplement) >> 8;
            merchantPersonalityDelta = checked(merchantPersonalityDelta << 6);
            long skillWeight = checked(192L * merchantSkillDelta) >> 8;
            long personalityWeight = merchantPersonalityDelta >> 8;
            amount = checked(checked(skillWeight + personalityWeight) * cost) >> 8;
        }

        return checked((int)amount);
    }

    internal static int[] RandomizeInitialRegionalPrices(IRandomService random, string key)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        int[] factors = new int[RegionCount];
        for (int region = 0; region < factors.Length; region++)
        {
            factors[region] = DrawInclusive(random, $"{key}:initial:region:{region}",
                InitialRegionalAdjustmentMinimum, InitialRegionalAdjustmentMaximum);
        }
        return factors;
    }

    /// <summary>Applies one donor daily roll; the random value is supplied by Engine Random.</summary>
    internal static int UpdateRegionalPrice(int currentAdjustment, int merchantPower, int provincePower, int roll)
    {
        RequireAdjustment(currentAdjustment);
        RequireFactionPower(merchantPower, nameof(merchantPower));
        RequireFactionPower(provincePower, nameof(provincePower));
        if (roll is < 0 or > 99) throw new ArgumentOutOfRangeException(nameof(roll), "The donor daily roll is inclusive 0..99.");

        int chanceOfRise = checked(((merchantPower - provincePower) / 5)
            + 50 - ((currentAdjustment - NeutralRegionalAdjustment) / 25));
        long next = roll >= chanceOfRise
            ? checked(49L * currentAdjustment / 50)
            : checked(51L * currentAdjustment / 50);
        return checked((int)Math.Clamp(next, MinimumRegionalAdjustment, MaximumRegionalAdjustment));
    }

    internal static int DrawDailyRoll(IRandomService random, long day, int region)
    {
        ArgumentNullException.ThrowIfNull(random);
        RequireRegion(region);
        return DrawInclusive(random, $"day:{day}:region:{region}", 0, 99);
    }

    /// <summary>Updates every retained regional factor once, for one boundary in the shared calendar.</summary>
    internal static int[] UpdateRegionalPrices(IReadOnlyList<int> currentFactors, DaggerfallFactionsSet factions,
        IRandomService random, long day, IReadOnlyDictionary<int, int>? factionPowers = null)
    {
        ArgumentNullException.ThrowIfNull(currentFactors);
        ArgumentNullException.ThrowIfNull(factions);
        ArgumentNullException.ThrowIfNull(random);
        if (currentFactors.Count != RegionCount)
            throw new ArgumentException($"Regional prices require exactly {RegionCount} factors.", nameof(currentFactors));
        if (factionPowers is not null)
        {
            foreach ((int factionId, int power) in factionPowers)
            {
                if (!factions.Factions.ContainsKey(factionId))
                    throw new ArgumentException($"Faction power override names unpublished faction {factionId}.", nameof(factionPowers));
                RequireFactionPower(power, nameof(factionPowers));
            }
        }

        int[] updated = new int[RegionCount];
        for (int region = 0; region < RegionCount; region++)
        {
            int adjustment = currentFactors[region];
            RequireAdjustment(adjustment);
            updated[region] = adjustment;
        }

        if (!factions.Factions.TryGetValue(MerchantFactionId, out DaggerfallFactionDefinition? merchants))
            return updated;
        int merchantPower = Power(merchants, factionPowers);
        for (int region = 0; region < updated.Length; region++)
        {
            int? provinceId = ResolveProvinceFactionId(factions.Factions, region);
            if (provinceId is not int id || !factions.Factions.TryGetValue(id, out DaggerfallFactionDefinition? province))
                continue;
            updated[region] = UpdateRegionalPrice(updated[region], merchantPower, Power(province, factionPowers),
                DrawDailyRoll(random, day, region));
        }
        return updated;
    }

    internal static int? ResolveProvinceFactionId(IReadOnlyDictionary<int, DaggerfallFactionDefinition> factions, int region)
    {
        ArgumentNullException.ThrowIfNull(factions);
        RequireRegion(region);

        int? globalProvince = null;
        foreach (DaggerfallFactionDefinition faction in factions.Values)
        {
            if (faction.Type != ProvinceFactionType) continue;
            if (faction.Region == region) return faction.Id;
            if (faction.Region == -1) globalProvince = faction.Id;
        }
        return globalProvince;
    }

    internal static bool IsHighPrice(int adjustment)
    {
        RequireAdjustment(adjustment);
        return adjustment > 2000;
    }

    internal static bool IsLowPrice(int adjustment)
    {
        RequireAdjustment(adjustment);
        return adjustment < 500;
    }

    internal static void RequireRegion(int region)
    {
        if (region is < 0 or >= RegionCount) throw new ArgumentOutOfRangeException(nameof(region), $"Region must be in [0, {RegionCount - 1}].");
    }

    internal static void RequireAdjustment(int adjustment)
    {
        if (adjustment is < MinimumRegionalAdjustment or > MaximumRegionalAdjustment)
            throw new ArgumentOutOfRangeException(nameof(adjustment), $"Regional adjustment must be in [{MinimumRegionalAdjustment}, {MaximumRegionalAdjustment}].");
    }

    private static void RequireShopQuality(int shopQuality)
    {
        if (shopQuality is < MinimumShopQuality or > MaximumShopQuality)
            throw new ArgumentOutOfRangeException(nameof(shopQuality), $"Shop quality must be in [{MinimumShopQuality}, {MaximumShopQuality}].");
    }

    private static void RequireSocialValue(int value, string parameter)
    {
        if (value is < MinimumSocialValue or > MaximumSocialValue)
            throw new ArgumentOutOfRangeException(parameter, $"Trade skill and personality must be in [{MinimumSocialValue}, {MaximumSocialValue}].");
    }

    private static void RequireFactionPower(int value, string parameter)
    {
        if (value is < MinimumFactionPower or > MaximumFactionPower)
            throw new ArgumentOutOfRangeException(parameter, $"Faction power must be in [{MinimumFactionPower}, {MaximumFactionPower}].");
    }

    private static int Power(DaggerfallFactionDefinition faction, IReadOnlyDictionary<int, int>? overrides)
    {
        int power = overrides is not null && overrides.TryGetValue(faction.Id, out int current) ? current : faction.Power;
        RequireFactionPower(power, $"faction {faction.Id}");
        return power;
    }

    private static int DrawInclusive(IRandomService random, string key, int minimum, int maximum)
    {
        long value = random.DrawKeyed(new KeyedRngRequest(RandomSeed, RandomScope, key, minimum, maximum)).Value;
        if (value < minimum || value > maximum)
            throw new InvalidOperationException($"Engine Random returned {value} outside [{minimum}, {maximum}] for '{key}'.");
        return checked((int)value);
    }
}
