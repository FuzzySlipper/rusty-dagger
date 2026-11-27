using System.Globalization;
using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Social reach of a resolved <c>FamilyId</c>.</summary>
internal enum DaggerfallHouseholdScope
{
    None,
    Local,
    GreatHouse,
    Dynasty,
}

/// <summary>
/// Pure deterministic household, Great House, and cultural surname resolver over <c>WorldSocialSeed</c>,
/// <see cref="DaggerfallSiteId"/>, building type, slot identity, and <see cref="ActorSocialGrouping"/>.
/// Produces zero save bytes for untouched actors because every live actor derives the exact same baseline
/// <c>FamilyId</c> on materialization.
/// </summary>
internal static class DaggerfallHouseholds
{
    internal const string LocalPrefix = "fam:local:";
    internal const string GreatHousePrefix = "fam:house:";
    internal const string DynastyPrefix = "fam:dynasty:";

    internal static bool IsValidFamilyId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !char.IsAsciiLetterLower(value[0]) && !char.IsAsciiDigit(value[0]))
            return false;
        foreach (char c in value)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is ':' or '.' or '_' or '-'))
                return false;
        }
        return true;
    }

    internal static DaggerfallHouseholdScope ScopeOf(string? familyId)
    {
        if (string.IsNullOrWhiteSpace(familyId)) return DaggerfallHouseholdScope.None;
        if (familyId.StartsWith(DynastyPrefix, StringComparison.Ordinal)) return DaggerfallHouseholdScope.Dynasty;
        if (familyId.StartsWith(GreatHousePrefix, StringComparison.Ordinal)) return DaggerfallHouseholdScope.GreatHouse;
        return DaggerfallHouseholdScope.Local;
    }

    internal static bool IsLocal(string? familyId) => ScopeOf(familyId) == DaggerfallHouseholdScope.Local;
    internal static bool IsGreatHouse(string? familyId) => ScopeOf(familyId) == DaggerfallHouseholdScope.GreatHouse;
    internal static bool IsDynasty(string? familyId) => ScopeOf(familyId) == DaggerfallHouseholdScope.Dynasty;
    internal static bool IsProvinceWide(string? familyId) => ScopeOf(familyId) is DaggerfallHouseholdScope.GreatHouse or DaggerfallHouseholdScope.Dynasty;

    /// <summary>Classic Daggerfall residences and family-run shops where occupants form a building household.</summary>
    internal static bool IsResidenceOrFamilyShop(int buildingType) =>
        buildingType is 0 or 1 or 2 or 4 or 5 or 6 or 7 or 8 or 9 or 12 or 13 or (>= 17 and <= 23);

    internal static bool IsBank(int buildingType) => buildingType == 3;
    internal static bool IsInstitutional(int buildingType) => buildingType is 10 or 11 or 14;
    internal static bool IsTavern(int buildingType) => buildingType == 15;
    internal static bool IsPalace(int buildingType) => buildingType == 16;

    /// <summary>Each province has 2..4 deterministic Great Houses seeded from <paramref name="worldSocialSeed"/>.</summary>
    internal static int GreatHouseCountForRegion(ulong worldSocialSeed, int regionIndex)
    {
        ulong hash = StableHash.NonZeroFnv1a64(
            string.Create(CultureInfo.InvariantCulture, $"daggerfall.great-house-count:{worldSocialSeed}:{regionIndex}"));
        return 2 + (int)(hash % 3UL);
    }

    internal static string GreatHouseId(int regionIndex, int houseIndex) =>
        string.Create(CultureInfo.InvariantCulture, $"{GreatHousePrefix}r{regionIndex}:{houseIndex}");

    internal static string BuildingHouseholdId(DaggerfallSiteId site, int blockX, int blockY, int buildingIndex) =>
        string.Create(CultureInfo.InvariantCulture, $"{LocalPrefix}r{site.Region}:s{site.Index}:x{blockX}y{blockY}b{buildingIndex}");

    internal static int TownHouseholdBucketCount(DaggerfallSiteRecord? site) =>
        site?.Exterior is { Blocks.Count: > 0 } exterior
            ? Math.Clamp(exterior.Blocks.Count, 4, 24)
            : 8;

    internal static string TownPoolHouseholdId(ulong worldSocialSeed, DaggerfallSiteId site, string slotKey, int bucketCount = 8)
    {
        int clamped = Math.Max(1, bucketCount);
        ulong hash = StableHash.NonZeroFnv1a64(
            string.Create(CultureInfo.InvariantCulture, $"daggerfall.town-household:{worldSocialSeed}:{site.Region}:{site.Index}:{slotKey}"));
        int bucket = (int)(hash % (ulong)clamped);
        return string.Create(CultureInfo.InvariantCulture, $"{LocalPrefix}r{site.Region}:s{site.Index}:h{bucket}");
    }

    /// <summary>
    /// Resolves the deterministic baseline <c>FamilyId</c> for a placed static NPC slot.
    /// Enforces building-type guards so guildhalls, temples, libraries, and palace courts never collapse
    /// unrelated people into one building-wide family.
    /// </summary>
    internal static string? ResolveForPlacedSlot(
        ulong worldSocialSeed,
        DaggerfallSiteId site,
        int? buildingType,
        int? blockX,
        int? blockY,
        int? buildingIndex,
        string slotKey,
        ActorSocialGrouping grouping,
        string? authoredFamilyId = null,
        int townBucketCount = 8)
    {
        if (!string.IsNullOrWhiteSpace(authoredFamilyId))
            return authoredFamilyId;
        if (grouping is ActorSocialGrouping.None or ActorSocialGrouping.FactionChapter)
            return null;
        if (buildingType is int instType && IsInstitutional(instType))
            return null;

        switch (grouping)
        {
            case ActorSocialGrouping.NobleHouse:
            {
                int houseCount = GreatHouseCountForRegion(worldSocialSeed, site.Region);
                ulong hash = StableHash.NonZeroFnv1a64(
                    string.Create(CultureInfo.InvariantCulture, $"daggerfall.noble-house:{worldSocialSeed}:{site.Region}:{site.Index}:{slotKey}"));
                int houseIndex = (int)(hash % (ulong)houseCount);
                return GreatHouseId(site.Region, houseIndex);
            }
            case ActorSocialGrouping.TavernStaff:
                if (buildingType is int staffType && (IsTavern(staffType) || IsResidenceOrFamilyShop(staffType))
                    && blockX is int sbx && blockY is int sby && buildingIndex is int sbi)
                    return BuildingHouseholdId(site, sbx, sby, sbi);
                return TownPoolHouseholdId(worldSocialSeed, site, slotKey, townBucketCount);
            case ActorSocialGrouping.TavernPatron:
                return TownPoolHouseholdId(worldSocialSeed, site, slotKey, townBucketCount);
            case ActorSocialGrouping.BuildingHousehold:
                if (buildingType is int bt)
                {
                    if (IsResidenceOrFamilyShop(bt) && blockX is int bx && blockY is int by && buildingIndex is int bi)
                        return BuildingHouseholdId(site, bx, by, bi);
                    // Non-residential buildings (palace, tavern, bank) never bind building-household roles into one family.
                    return TownPoolHouseholdId(worldSocialSeed, site, slotKey, townBucketCount);
                }
                return TownPoolHouseholdId(worldSocialSeed, site, slotKey, townBucketCount);
            default:
                return null;
        }
    }

    /// <summary>Resolves the deterministic baseline <c>FamilyId</c> for an exterior walking civilian.</summary>
    internal static string? ResolveForExteriorWalker(
        ulong worldSocialSeed,
        DaggerfallSiteId site,
        string slotKey,
        bool isGuard,
        int townBucketCount = 8) =>
        isGuard ? null : TownPoolHouseholdId(worldSocialSeed, site, slotKey, townBucketCount);

    /// <summary>Maps a Daggerfall race slug to its classic <c>DaggerfallNames</c> bank index (0..7), or null if non-standard.</summary>
    internal static int? RaceNameBank(string? race) => race?.ToLowerInvariant() switch
    {
        "breton" => 0,
        "redguard" => 1,
        "nord" => 2,
        "dark-elf" => 3,
        "high-elf" => 4,
        "wood-elf" => 5,
        "khajiit" => 6,
        "argonian" => 7,
        _ => null,
    };

    /// <summary>
    /// Race/culture guard: a household member shares the household's seeded surname only when their cultural
    /// name bank matches the household's bank and that bank uses surnames (classic bank 1 Redguards use mononyms).
    /// </summary>
    internal static bool ShouldShareSurname(string? race, int householdBank, string? familyId)
    {
        if (string.IsNullOrWhiteSpace(familyId) || IsDynasty(familyId) || householdBank == 1)
            return false;
        if (RaceNameBank(race) is int raceBank && raceBank != householdBank)
            return false;
        return true;
    }

    internal static string SurnameIdentity(ulong worldSocialSeed, string familyId) =>
        string.Create(CultureInfo.InvariantCulture, $"household/surname/{worldSocialSeed}/{familyId}");

    internal static bool TryParseRegion(string? familyId, out int region)
    {
        region = default;
        if (string.IsNullOrWhiteSpace(familyId)) return false;
        switch (familyId)
        {
            case "fam:dynasty:daggerfall":
            case "fam:house:bridwell":
                region = 17;
                return true;
            case "fam:dynasty:wayrest":
                region = 23;
                return true;
            case "fam:dynasty:sentinel":
                region = 20;
                return true;
            case "fam:dynasty:direnni":
                region = 9;
                return true;
            case "fam:dynasty:anticlere":
                region = 21;
                return true;
            case "fam:dynasty:lainlyn":
                region = 22;
                return true;
        }
        ReadOnlySpan<char> span = familyId.AsSpan();
        if (span.StartsWith(LocalPrefix, StringComparison.Ordinal))
            span = span[LocalPrefix.Length..];
        else if (span.StartsWith(GreatHousePrefix, StringComparison.Ordinal))
            span = span[GreatHousePrefix.Length..];
        else if (span.StartsWith(DynastyPrefix, StringComparison.Ordinal))
            span = span[DynastyPrefix.Length..];
        else
            return false;
        if (span.Length < 2 || span[0] != 'r') return false;
        int colon = span.IndexOf(':');
        ReadOnlySpan<char> num = colon >= 0 ? span[1..colon] : span[1..];
        return int.TryParse(num, NumberStyles.None, CultureInfo.InvariantCulture, out region);
    }

    internal static bool TryParseLocalSite(string? familyId, out DaggerfallSiteId site)
    {
        site = default;
        if (string.IsNullOrWhiteSpace(familyId) || !familyId.StartsWith(LocalPrefix, StringComparison.Ordinal))
            return false;
        string[] parts = familyId[LocalPrefix.Length..].Split(':');
        if (parts.Length < 2 || !parts[0].StartsWith('r') || !parts[1].StartsWith('s'))
            return false;
        if (!int.TryParse(parts[0][1..], NumberStyles.None, CultureInfo.InvariantCulture, out int region)
            || !int.TryParse(parts[1][1..], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            return false;
        site = new DaggerfallSiteId(region, index);
        return true;
    }

    internal static string? FormatLabel(
        string? familyId,
        DaggerfallDefinitions? definitions,
        Func<int, string, string>? surnameForBank,
        Func<int, string, string>? firstNameForBank,
        ulong worldSocialSeed)
    {
        if (string.IsNullOrWhiteSpace(familyId)) return null;
        switch (familyId)
        {
            case "fam:dynasty:daggerfall": return "House of Daggerfall";
            case "fam:dynasty:wayrest": return "House of Wayrest";
            case "fam:dynasty:sentinel": return "House of Sentinel";
            case "fam:dynasty:direnni": return "Clan Direnni";
            case "fam:dynasty:anticlere": return "House Flyte";
            case "fam:dynasty:lainlyn": return "House of Lainlyn";
            case "fam:house:bridwell": return "House Bridwell";
        }
        string? regionName = definitions is not null && TryParseRegion(familyId, out int region)
            ? definitions.BuildingNames.RegionName(region)
            : null;
        string? houseSurname = null;
        if (definitions is not null && TryParseRegion(familyId, out int bankRegion)
            && definitions.BuildingNames.TryGetNameBank(bankRegion, out int bank))
        {
            string identity = SurnameIdentity(worldSocialSeed, familyId);
            houseSurname = surnameForBank?.Invoke(bank, identity);
            if (string.IsNullOrWhiteSpace(houseSurname))
                houseSurname = firstNameForBank?.Invoke(bank, identity);
        }

        if (IsDynasty(familyId))
        {
            if (!string.IsNullOrWhiteSpace(houseSurname))
                return !string.IsNullOrWhiteSpace(regionName) && regionName != DaggerfallRegionNames.Unnamed
                    ? $"House {houseSurname} of {regionName}"
                    : $"House {houseSurname}";
            return !string.IsNullOrWhiteSpace(regionName) && regionName != DaggerfallRegionNames.Unnamed
                ? $"House of {regionName}"
                : familyId;
        }

        if (IsGreatHouse(familyId))
        {
            string title = !string.IsNullOrWhiteSpace(houseSurname) ? $"House {houseSurname}" : familyId;
            return !string.IsNullOrWhiteSpace(regionName) && regionName != DaggerfallRegionNames.Unnamed
                ? $"{title} ({regionName})"
                : title;
        }
        if (IsLocal(familyId))
        {
            if (!string.IsNullOrWhiteSpace(houseSurname))
                return $"{houseSurname} Household";
            return !string.IsNullOrWhiteSpace(regionName) && regionName != DaggerfallRegionNames.Unnamed
                ? $"Local Household ({regionName})"
                : "Local Household";
        }
        return familyId;
    }
}
