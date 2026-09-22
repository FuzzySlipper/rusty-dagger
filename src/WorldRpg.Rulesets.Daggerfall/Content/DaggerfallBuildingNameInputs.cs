namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Daggerfall-owned naming context imported from the donor's exact regionRaces table. It is kept as
/// authored input because province faction race is not equivalent to the donor's region name bank.
/// </summary>
internal sealed class DaggerfallBuildingNameInputs(IReadOnlyList<int> regionNameBanks)
{
    internal IReadOnlyList<int> RegionNameBanks { get; } = [.. regionNameBanks];

    internal bool TryGetNameBank(int region, out int bank)
    {
        if (region is >= 0 and < 62 && RegionNameBanks.Count == 62)
        {
            bank = RegionNameBanks[region];
            return bank is 0 or 1;
        }

        bank = default;
        return false;
    }
}
