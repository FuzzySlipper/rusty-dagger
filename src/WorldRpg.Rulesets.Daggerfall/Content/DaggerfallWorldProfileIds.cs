using System.Globalization;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The deterministic identity of every profile a catalog location can have: its exterior, its dungeon
/// and each building interior its exterior blocks declare. Saves, return destinations, discoveries, action
/// graphs and authored overrides all name a profile by this id, so a location resolves to the same profile
/// whether an authored closure or the per-block assembly provides it.
/// </summary>
internal static class DaggerfallWorldProfileIds
{
    /// <summary><c>{region}/{index}/exterior</c>.</summary>
    internal static DaggerfallWorldProfileKey Exterior(DaggerfallSiteId site) =>
        new(site, DaggerfallWorldProfileKind.Exterior, $"{Prefix(site)}/exterior");

    /// <summary><c>{region}/{index}/dungeon</c>.</summary>
    internal static DaggerfallWorldProfileKey Dungeon(DaggerfallSiteId site) =>
        new(site, DaggerfallWorldProfileKind.Dungeon, $"{Prefix(site)}/dungeon");

    /// <summary><c>{region}/{index}/interior-{blockX}-{blockY}-{building}</c>.</summary>
    internal static DaggerfallWorldProfileKey Interior(DaggerfallSiteId site, DaggerfallSiteBuildingId building) =>
        new(site, DaggerfallWorldProfileKind.Interior,
            string.Create(CultureInfo.InvariantCulture, $"{Prefix(site)}/interior-{building.BlockX}-{building.BlockY}-{building.Index}"));

    /// <summary>Reads a profile id back to its location, kind and, for an interior, its building.</summary>
    internal static bool TryParse(string id, out DaggerfallWorldProfileKey key) => TryParse(id, out key, out _);

    internal static bool TryParse(string id, out DaggerfallWorldProfileKey key, out DaggerfallSiteBuildingId? building)
    {
        key = default;
        building = null;
        if (string.IsNullOrWhiteSpace(id)) return false;
        string[] parts = id.Split('/');
        if (parts.Length != 3 || !Number(parts[0], out int region) || !Number(parts[1], out int index)) return false;
        DaggerfallSiteId site = new(region, index);
        if (parts[2] == "exterior") key = Exterior(site);
        else if (parts[2] == "dungeon") key = Dungeon(site);
        else if (parts[2].StartsWith("interior-", StringComparison.Ordinal)
            && parts[2]["interior-".Length..].Split('-') is [string x, string y, string slot]
            && Number(x, out int blockX) && Number(y, out int blockY) && Number(slot, out int buildingIndex))
        {
            building = new DaggerfallSiteBuildingId(blockX, blockY, buildingIndex);
            key = Interior(site, building.Value);
        }
        else return false;
        // A parsed id must print back exactly, so one profile has one spelling.
        return StringComparer.Ordinal.Equals(key.LogicalId, id);
    }

    private static string Prefix(DaggerfallSiteId site) => string.Create(CultureInfo.InvariantCulture, $"{site.Region}/{site.Index}");

    private static bool Number(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
