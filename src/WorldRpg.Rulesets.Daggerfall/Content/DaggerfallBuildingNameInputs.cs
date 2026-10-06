namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Daggerfall-owned naming context imported from the donor's exact regionRaces table. It is kept as
/// authored input because province faction race is not equivalent to the donor's region name bank.
/// </summary>
internal sealed class DaggerfallBuildingNameInputs(IReadOnlyList<int> regionNameBanks, IReadOnlyList<string>? regionNames = null)
{
    internal IReadOnlyList<int> RegionNameBanks { get; } = [.. regionNameBanks];
    internal IReadOnlyList<string> RegionNames { get; } = [.. regionNames ?? []];

    /// <summary>The player name of a source region, from the published region names.</summary>
    internal string RegionName(int region) => DaggerfallRegionNames.Name(RegionNames, region);

    internal bool TryGetNameBank(int region, out int bank)
    {
        if (region >= 0 && region < RegionNameBanks.Count)
        {
            bank = RegionNameBanks[region];
            return bank is 0 or 1;
        }

        bank = default;
        return false;
    }
}

/// <summary>Player names for source region indices, with one wording for an index the pack does not name.</summary>
internal static class DaggerfallRegionNames
{
    internal const string Unnamed = "an unnamed region";

    internal static string Name(IReadOnlyList<string> names, int region) =>
        region >= 0 && region < names.Count && !string.IsNullOrWhiteSpace(names[region]) ? names[region] : Unnamed;
}
