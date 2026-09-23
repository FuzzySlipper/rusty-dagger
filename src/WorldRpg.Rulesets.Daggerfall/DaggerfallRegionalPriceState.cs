using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Resolved regional market factors and the last calendar day whose update was applied.</summary>
internal sealed record DaggerfallRegionalPriceSave(long LastAdvancedDay, int[] Factors)
{
    internal DaggerfallRegionalPriceSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Factors);
        if (Factors.Length != DaggerfallRegionalEconomyPolicy.RegionCount)
            throw new ArgumentException($"Regional prices require exactly {DaggerfallRegionalEconomyPolicy.RegionCount} factors.", nameof(Factors));
        foreach (int factor in Factors)
            DaggerfallRegionalEconomyPolicy.RequireAdjustment(factor);
        return this;
    }
}

/// <summary>
/// Current Daggerfall market factors. It uses authored faction power unless a caller supplies the
/// canonical live-power values, and advances only from the shared Daggerfall calendar's day number.
/// </summary>
internal sealed class DaggerfallRegionalPriceState
{
    private readonly DaggerfallFactionsSet _factions;
    private readonly IRandomService _random;
    private readonly int[] _factors;
    private long _lastAdvancedDay;

    internal DaggerfallRegionalPriceState(DaggerfallFactionsSet factions, IRandomService random, long currentDay,
        DaggerfallRegionalPriceSave? restored = null, string randomizationKey = "new-game")
    {
        _factions = factions ?? throw new ArgumentNullException(nameof(factions));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        ValidateRegions(factions);

        if (restored is not null)
        {
            restored.Validate();
            if (restored.LastAdvancedDay > currentDay)
                throw new ArgumentException("Saved regional prices cannot be advanced beyond the restored calendar.", nameof(restored));
            _lastAdvancedDay = restored.LastAdvancedDay;
            _factors = (int[])restored.Factors.Clone();
        }
        else
        {
            _lastAdvancedDay = currentDay;
            _factors = DaggerfallRegionalEconomyPolicy.RandomizeInitialRegionalPrices(_random, randomizationKey);
        }
    }

    internal long LastAdvancedDay => _lastAdvancedDay;

    internal int AdjustmentForRegion(int region)
    {
        DaggerfallRegionalEconomyPolicy.RequireRegion(region);
        return _factors[region];
    }

    internal IReadOnlyList<int> Factors => Array.AsReadOnly((int[])_factors.Clone());

    internal bool PricesAreHigh(int region) => DaggerfallRegionalEconomyPolicy.IsHighPrice(AdjustmentForRegion(region));

    internal bool PricesAreLow(int region) => DaggerfallRegionalEconomyPolicy.IsLowPrice(AdjustmentForRegion(region));

    /// <summary>
    /// Applies one source-derived market roll per region for every elapsed day. When no mutable
    /// faction-power owner supplies overrides, the admitted faction catalog's filed power is used.
    /// </summary>
    internal void AdvanceToDay(long day, IReadOnlyDictionary<int, int>? factionPowers = null)
    {
        if (day < _lastAdvancedDay)
            throw new ArgumentOutOfRangeException(nameof(day), "Regional prices cannot move backward from their last applied calendar day.");
        int[] updatedFactors = (int[])_factors.Clone();
        long nextDay = _lastAdvancedDay;
        while (nextDay < day)
        {
            nextDay = checked(nextDay + 1);
            updatedFactors = DaggerfallRegionalEconomyPolicy.UpdateRegionalPrices(updatedFactors, _factions, _random, nextDay, factionPowers);
        }
        Array.Copy(updatedFactors, _factors, _factors.Length);
        _lastAdvancedDay = day;
    }

    internal DaggerfallRegionalPriceSave Capture() => new(_lastAdvancedDay, (int[])_factors.Clone());

    private static void ValidateRegions(DaggerfallFactionsSet factions)
    {
        if (factions.Regions.Count != DaggerfallRegionalEconomyPolicy.RegionCount)
            throw new ArgumentException($"The faction catalog must admit all {DaggerfallRegionalEconomyPolicy.RegionCount} classic regions.", nameof(factions));
        for (int region = 0; region < DaggerfallRegionalEconomyPolicy.RegionCount; region++)
            if (!factions.Regions.ContainsKey(region))
                throw new ArgumentException($"The faction catalog does not admit classic region {region}.", nameof(factions));
    }
}
