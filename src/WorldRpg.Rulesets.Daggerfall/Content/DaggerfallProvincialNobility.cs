using System.Collections.ObjectModel;
using System.Globalization;
using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Role of a canon-seeded member inside a province's ruling noble house.</summary>
internal enum DaggerfallProvincialNobleRole
{
    Ruler = 0,
    Heir = 1,
    Spouse = 2,
    CourtNoble = 3,
}

/// <summary>One canon-seeded provincial noble with cast-compatible identity, title, and capital home placement.</summary>
internal sealed record DaggerfallProvincialNobleMember(
    DaggerfallProvincialNobleRole Role,
    DaggerfallCastIndividualDefinition Cast,
    string? Title);

/// <summary>
/// Deterministic canon-seeded ruling house for one classic province (<see cref="DaggerfallFactionDefinition.Type"/> == 7).
/// Identical across every playthrough (canon seed, independent of <c>GameSeed</c> and <c>WorldSocialSeed</c>) and costs
/// zero save bytes until <c>DaggerfallOfficeLedger</c> records an override.
/// </summary>
internal sealed record DaggerfallProvincialNobleHouse(
    int Region,
    int ProvinceFactionId,
    string RegionName,
    string FamilyId,
    string HouseLabel,
    string RulerTitle,
    DaggerfallSiteId? SeatSite,
    string? SeatBlock,
    int? SeatBuildingIndex,
    bool HasClassicCastRuler,
    IReadOnlyList<DaggerfallProvincialNobleMember> Members)
{
    internal DaggerfallProvincialNobleMember? Ruler =>
        Members.FirstOrDefault(m => m.Role == DaggerfallProvincialNobleRole.Ruler);
}

/// <summary>Index of canon-seeded provincial noble houses and members across all classic Iliac Bay provinces.</summary>
internal sealed class DaggerfallProvincialNobilitySet(
    IReadOnlyDictionary<int, DaggerfallProvincialNobleHouse> byRegion,
    IReadOnlyDictionary<int, DaggerfallProvincialNobleHouse> byProvinceFaction,
    IReadOnlyDictionary<string, DaggerfallCastIndividualDefinition> membersById,
    IReadOnlyDictionary<string, DaggerfallProvincialNobleMember> detailsById,
    IReadOnlyDictionary<int, DaggerfallCastIndividualDefinition> membersByIndividualFaction,
    IReadOnlyDictionary<(DaggerfallSiteId Site, string Block), IReadOnlyList<DaggerfallCastIndividualDefinition>> homedBySiteBlock,
    IReadOnlyDictionary<string, string> dynastyLabels)
{
    internal static DaggerfallProvincialNobilitySet Empty { get; } = new(
        new ReadOnlyDictionary<int, DaggerfallProvincialNobleHouse>(new Dictionary<int, DaggerfallProvincialNobleHouse>()),
        new ReadOnlyDictionary<int, DaggerfallProvincialNobleHouse>(new Dictionary<int, DaggerfallProvincialNobleHouse>()),
        new ReadOnlyDictionary<string, DaggerfallCastIndividualDefinition>(new Dictionary<string, DaggerfallCastIndividualDefinition>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, DaggerfallProvincialNobleMember>(new Dictionary<string, DaggerfallProvincialNobleMember>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<int, DaggerfallCastIndividualDefinition>(new Dictionary<int, DaggerfallCastIndividualDefinition>()),
        new ReadOnlyDictionary<(DaggerfallSiteId, string), IReadOnlyList<DaggerfallCastIndividualDefinition>>(
            new Dictionary<(DaggerfallSiteId, string), IReadOnlyList<DaggerfallCastIndividualDefinition>>()),
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)));

    internal IReadOnlyDictionary<int, DaggerfallProvincialNobleHouse> ByRegion { get; } = byRegion;
    internal IReadOnlyDictionary<int, DaggerfallProvincialNobleHouse> ByProvinceFaction { get; } = byProvinceFaction;
    internal IReadOnlyDictionary<string, DaggerfallCastIndividualDefinition> MembersById { get; } = membersById;
    internal IReadOnlyDictionary<string, DaggerfallProvincialNobleMember> MemberDetailsById { get; } = detailsById;
    internal IReadOnlyList<DaggerfallCastIndividualDefinition> AllMembers { get; } =
        Array.AsReadOnly([.. membersById.Values.OrderBy(c => c.Id, StringComparer.Ordinal)]);

    internal bool TryGetProvinceHouse(int region, out DaggerfallProvincialNobleHouse? house) =>
        ByRegion.TryGetValue(region, out house);

    internal bool TryGetHouseByProvinceFaction(int provinceFactionId, out DaggerfallProvincialNobleHouse? house) =>
        ByProvinceFaction.TryGetValue(provinceFactionId, out house);

    internal bool TryGetMember(string castId, out DaggerfallCastIndividualDefinition? cast) =>
        MembersById.TryGetValue(castId, out cast);

    internal bool TryGetMemberDetail(string castId, out DaggerfallProvincialNobleMember? detail) =>
        MemberDetailsById.TryGetValue(castId, out detail);

    internal bool TryGetMemberByFaction(int individualFactionId, out DaggerfallCastIndividualDefinition? cast) =>
        membersByIndividualFaction.TryGetValue(individualFactionId, out cast);

    internal IEnumerable<DaggerfallCastIndividualDefinition> HomedAt(DaggerfallSiteId site, string blockSourceKey) =>
        homedBySiteBlock.TryGetValue((site, blockSourceKey), out IReadOnlyList<DaggerfallCastIndividualDefinition>? list)
            ? list
            : [];

    internal bool IsCapitalSeatProfile(DaggerfallSiteId site, DaggerfallSiteBuildingId building)
    {
        if (!ByRegion.TryGetValue(site.Region, out DaggerfallProvincialNobleHouse? house)
            || house.SeatSite != site
            || house.SeatBuildingIndex != building.Index)
        {
            return false;
        }
        return true;
    }

    internal string? FormatDynastyLabel(string? familyId) =>
        !string.IsNullOrWhiteSpace(familyId) && dynastyLabels.TryGetValue(familyId, out string? label)
            ? label
            : null;
}

/// <summary>
/// Pure canon-seeded generator for provincial ruling lords and noble houses (#11156).
/// Respects classic cast precedence (#11141 and the four crowns), uses the published cultural name banks and
/// noble spawner visual pools, and assigns home placements on free capital palace/hall slots without colliding
/// with explicit <c>cast.json</c> homes.
/// </summary>
internal static class DaggerfallProvincialNobility
{
    private const ulong CanonSeed = 0x5052_4F56_4E4F_424CUL;
    private const int ProvinceFactionType = 7;
    private const int IndividualFactionType = 4;
    private const int PalaceBuildingType = 16;

    private static readonly (string Title, string Gender)[] RulerRankTable =
    [
        ("King", "Male"),
        ("Queen", "Female"),
        ("Duke", "Male"),
        ("Duchess", "Female"),
        ("Marquis", "Male"),
        ("Marquise", "Female"),
        ("Count", "Male"),
        ("Countess", "Female"),
        ("Baron", "Male"),
        ("Baroness", "Female"),
        ("Lord", "Male"),
        ("Lady", "Female"),
    ];

    internal static DaggerfallProvincialNobilitySet Build(DaggerfallDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Factions.Factions.Count == 0 || definitions.Names.Banks.Count == 0)
            return DaggerfallProvincialNobilitySet.Empty;

        Dictionary<int, DaggerfallProvincialNobleHouse> byRegion = [];
        Dictionary<int, DaggerfallProvincialNobleHouse> byProvince = [];
        Dictionary<string, DaggerfallCastIndividualDefinition> membersById = new(StringComparer.Ordinal);
        Dictionary<string, DaggerfallProvincialNobleMember> detailsById = new(StringComparer.Ordinal);
        Dictionary<int, DaggerfallCastIndividualDefinition> membersByIndividualFaction = [];
        Dictionary<(DaggerfallSiteId Site, string Block), List<DaggerfallCastIndividualDefinition>> homedMap = [];
        Dictionary<string, string> dynastyLabels = new(StringComparer.Ordinal);

        // Collect all classic regions that have a province faction (ProvinceOf picks the real province even when
        // region 60 also carries the imperial meta-faction #350).
        int[] regions =
        [
            .. definitions.Factions.Factions.Values
                .Where(f => f.Type == ProvinceFactionType && f.Region >= 0)
                .Select(f => f.Region)
                .Distinct()
                .Order(),
        ];

        foreach (int region in regions)
        {
            DaggerfallFactionDefinition? province = definitions.Factions.ProvinceOf(region);
            if (province is null)
                continue;

            string regionName = definitions.BuildingNames.RegionName(region);
            int nameBank = definitions.BuildingNames.TryGetNameBank(region, out int resolvedBank) ? resolvedBank : 0;
            string defaultRace = ResolveRegionRace(definitions, province, nameBank);
            (string rulerTitle, string rulerGender) = ResolveRulerRankAndGender(province, region);

            // Detect classic individual children (e.g. Gothryd, Akorithi, Eadwyre, Medora, Gortwog, Baron Shrike, Lady Doryanna Flyte).
            // Check the first published individual child in succession order so an uncast classic ruler (like Anticlere #400
            // Lady Doryanna Flyte) is promoted with a real cast identity rather than skipped in favor of a lower child.
            DaggerfallCastIndividualDefinition? classicCastRuler = null;
            DaggerfallFactionDefinition? uncastClassicRulerFaction = null;
            foreach (int childId in province.Children)
            {
                if (!definitions.Factions.Factions.TryGetValue(childId, out DaggerfallFactionDefinition? child)
                    || child.Type != IndividualFactionType)
                {
                    continue;
                }
                if (definitions.People.TryGetIndividualByFaction(child.Id, out DaggerfallCastIndividualDefinition? foundCast)
                    && foundCast is not null)
                {
                    classicCastRuler = foundCast;
                }
                else
                {
                    uncastClassicRulerFaction = child;
                }
                break;
            }

            string familyId = ResolveProvinceFamilyId(region, classicCastRuler, uncastClassicRulerFaction);
            string? houseSurname = GenerateSurname(definitions, nameBank, $"house/r{region}/surname");
            string houseLabel = ResolveHouseLabel(familyId, regionName, houseSurname);
            dynastyLabels[familyId] = houseLabel;

            // Find the province's capital seat building and its free talkable slots (never colliding with cast.json homes).
            CapitalSeatPlacement? seat = FindCapitalSeat(definitions, region);
            string climateName = seat is not null
                ? definitions.Grids.Climate.GetCell(seat.Site.MapPixelX + 1, seat.Site.MapPixelY).Name
                : "Woodlands";
            List<string> freeSlots = seat is not null
                ? CollectFreeTalkableSlots(definitions, seat)
                : [];
            int nextSlotIndex = 0;

            string? TakeSlot() => nextSlotIndex < freeSlots.Count ? freeSlots[nextSlotIndex++] : null;

            List<DaggerfallProvincialNobleMember> members = [];

            void AddMember(
                DaggerfallProvincialNobleRole role,
                string id,
                int factionId,
                string name,
                string gender,
                string? title,
                string? memberFamilyId,
                bool bindIndividualFaction = false)
            {
                string? slotId = TakeSlot();
                DaggerfallPeopleVisual visual = PickNobleVisual(
                    definitions, region, regionName, climateName, gender, $"{id}:{factionId}");
                DaggerfallCastIndividualDefinition cast = new(
                    Id: id,
                    FactionId: factionId,
                    Name: name,
                    Race: defaultRace,
                    Gender: gender,
                    Archive: visual.Archive,
                    Record: visual.Record,
                    HomeBlock: slotId is not null ? seat!.Block : null,
                    HomeSlotId: slotId,
                    Interaction: "full-dialogue",
                    Harm: "protected",
                    ArchetypeId: "noble",
                    FamilyId: memberFamilyId)
                {
                    HomeSite = slotId is not null ? seat!.Site.Id : null,
                };
                DaggerfallProvincialNobleMember member = new(role, cast, title);
                members.Add(member);
                membersById[id] = cast;
                detailsById[id] = member;
                if (bindIndividualFaction && factionId > 0)
                    membersByIndividualFaction[factionId] = cast;
                if (slotId is not null && seat is not null)
                {
                    (DaggerfallSiteId, string) siteKey = (seat.Site.Id, seat.Block);
                    if (!homedMap.TryGetValue(siteKey, out List<DaggerfallCastIndividualDefinition>? list))
                        homedMap[siteKey] = list = [];
                    list.Add(cast);
                }
            }

            if (classicCastRuler is not null)
            {
                // Classic cast ruler takes precedence; still populate named court nobles (outside the royal dynasty
                // FamilyId so they never hijack royal succession or Tier-2 Great House elevation) so conquest governors
                // (#11143) and court scenes have recurring named crown nobles.
                for (int i = 1; i <= 2; i++)
                {
                    bool female = DrawIndex($"r{region}:classic-noble:{i}:gender", 2) == 1;
                    string nobleGender = female ? "Female" : "Male";
                    string nobleTitle = female ? "Lady" : "Lord";
                    string nobleName = $"{nobleTitle} {GenerateFullName(definitions, nameBank, female, $"r{region}:classic-noble:{i}", surnameOverride: null)}";
                    AddMember(
                        DaggerfallProvincialNobleRole.CourtNoble,
                        $"noble:r{region}:noble-{i}",
                        province.Id,
                        nobleName,
                        nobleGender,
                        nobleTitle,
                        memberFamilyId: null);
                }
            }
            else if (uncastClassicRulerFaction is not null)
            {
                // Classic individual ruler in factions.json without a cast.json row (e.g. Anticlere #400 Lady Doryanna Flyte):
                // give them a real cast identity, palace placement, and named court circle while keeping their classic name!
                bool rulerFemale = uncastClassicRulerFaction.Name.StartsWith("Lady ", StringComparison.OrdinalIgnoreCase)
                    || uncastClassicRulerFaction.Name.StartsWith("Queen ", StringComparison.OrdinalIgnoreCase)
                    || uncastClassicRulerFaction.Name.StartsWith("Duchess ", StringComparison.OrdinalIgnoreCase)
                    || uncastClassicRulerFaction.Name.StartsWith("Baroness ", StringComparison.OrdinalIgnoreCase)
                    || uncastClassicRulerFaction.Name.StartsWith("Countess ", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rulerGender, "Female", StringComparison.Ordinal);
                AddMember(
                    DaggerfallProvincialNobleRole.Ruler,
                    $"noble:r{region}:ruler",
                    uncastClassicRulerFaction.Id,
                    uncastClassicRulerFaction.Name,
                    rulerFemale ? "Female" : "Male",
                    rulerTitle,
                    familyId,
                    bindIndividualFaction: true);

                for (int i = 1; i <= 2; i++)
                {
                    bool female = DrawIndex($"r{region}:uncast-noble:{i}:gender", 2) == 1;
                    string nobleGender = female ? "Female" : "Male";
                    string nobleTitle = female ? "Lady" : "Lord";
                    string nobleName = $"{nobleTitle} {GenerateFullName(definitions, nameBank, female, $"r{region}:uncast-noble:{i}", houseSurname)}";
                    AddMember(
                        DaggerfallProvincialNobleRole.CourtNoble,
                        $"noble:r{region}:noble-{i}",
                        province.Id,
                        nobleName,
                        nobleGender,
                        nobleTitle,
                        familyId);
                }
            }
            else
            {
                // Full canon-seeded provincial noble house: Ruler, Heir, Spouse, and 2 Court Nobles.
                bool rulerFemale = string.Equals(rulerGender, "Female", StringComparison.Ordinal);
                string rulerFullName = GenerateFullName(definitions, nameBank, rulerFemale, $"r{region}:ruler", houseSurname);
                AddMember(
                    DaggerfallProvincialNobleRole.Ruler,
                    $"noble:r{region}:ruler",
                    province.Id,
                    $"{rulerTitle} {rulerFullName}",
                    rulerGender,
                    rulerTitle,
                    familyId);

                bool heirFemale = DrawIndex($"r{region}:heir:gender", 2) == 1;
                string heirTitle = heirFemale ? "Lady" : "Lord";
                string heirFullName = GenerateDistinctFullName(definitions, nameBank, heirFemale, $"r{region}:heir", houseSurname, [rulerFullName]);
                AddMember(
                    DaggerfallProvincialNobleRole.Heir,
                    $"noble:r{region}:heir",
                    province.Id,
                    $"{heirTitle} {heirFullName}",
                    heirFemale ? "Female" : "Male",
                    heirTitle,
                    familyId);

                bool spouseFemale = !rulerFemale;
                string spouseTitle = spouseFemale ? "Lady" : "Lord";
                string spouseFullName = GenerateDistinctFullName(definitions, nameBank, spouseFemale, $"r{region}:spouse", houseSurname, [rulerFullName, heirFullName]);
                AddMember(
                    DaggerfallProvincialNobleRole.Spouse,
                    $"noble:r{region}:spouse",
                    province.Id,
                    $"{spouseTitle} {spouseFullName}",
                    spouseFemale ? "Female" : "Male",
                    spouseTitle,
                    familyId);

                HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase) { rulerFullName, heirFullName, spouseFullName };
                for (int i = 1; i <= 2; i++)
                {
                    bool nobleFemale = DrawIndex($"r{region}:noble-{i}:gender", 2) == 1;
                    string nobleTitle = nobleFemale ? "Lady" : "Lord";
                    string nobleFullName = GenerateDistinctFullName(definitions, nameBank, nobleFemale, $"r{region}:noble-{i}", surnameOverride: null, usedNames);
                    usedNames.Add(nobleFullName);
                    AddMember(
                        DaggerfallProvincialNobleRole.CourtNoble,
                        $"noble:r{region}:noble-{i}",
                        province.Id,
                        $"{nobleTitle} {nobleFullName}",
                        nobleFemale ? "Female" : "Male",
                        nobleTitle,
                        familyId);
                }
            }

            DaggerfallProvincialNobleHouse house = new(
                Region: region,
                ProvinceFactionId: province.Id,
                RegionName: regionName,
                FamilyId: familyId,
                HouseLabel: houseLabel,
                RulerTitle: rulerTitle,
                SeatSite: seat?.Site.Id,
                SeatBlock: seat?.Block,
                SeatBuildingIndex: seat?.BuildingIndex,
                HasClassicCastRuler: classicCastRuler is not null,
                Members: Array.AsReadOnly([.. members]));
            byRegion[region] = house;
            byProvince[province.Id] = house;
        }

        Dictionary<(DaggerfallSiteId, string), IReadOnlyList<DaggerfallCastIndividualDefinition>> readonlyHomed =
            homedMap.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<DaggerfallCastIndividualDefinition>)Array.AsReadOnly([.. kv.Value]));

        return new DaggerfallProvincialNobilitySet(
            new ReadOnlyDictionary<int, DaggerfallProvincialNobleHouse>(byRegion),
            new ReadOnlyDictionary<int, DaggerfallProvincialNobleHouse>(byProvince),
            new ReadOnlyDictionary<string, DaggerfallCastIndividualDefinition>(membersById),
            new ReadOnlyDictionary<string, DaggerfallProvincialNobleMember>(detailsById),
            new ReadOnlyDictionary<int, DaggerfallCastIndividualDefinition>(membersByIndividualFaction),
            new ReadOnlyDictionary<(DaggerfallSiteId, string), IReadOnlyList<DaggerfallCastIndividualDefinition>>(readonlyHomed),
            new ReadOnlyDictionary<string, string>(dynastyLabels));
    }

    private sealed record CapitalSeatPlacement(
        DaggerfallSiteRecord Site,
        string Block,
        int BlockX,
        int BlockY,
        int BuildingIndex,
        int BuildingType);

    private static CapitalSeatPlacement? FindCapitalSeat(DaggerfallDefinitions definitions, int region)
    {
        DaggerfallSiteRecord[] towns =
        [
            .. definitions.Locations.Records
                .Where(site => site.Id.Region == region
                    && site.Exterior is { Blocks.Count: > 0, Buildings.Count: > 0 }
                    && site.Kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage)
                .OrderBy(site => site.Kind)
                .ThenByDescending(site => site.Exterior!.Width * site.Exterior.Height)
                .ThenBy(site => site.Id.Index),
        ];

        // Pass 1: Prefer a capital palace (buildingType == 16) with talkable interior slots on a unique block.
        CapitalSeatPlacement? palace = FindSeatInTowns(definitions, towns, bt => bt == PalaceBuildingType);
        if (palace is not null)
            return palace;

        // Pass 2: Provinces without a palace (e.g. Betony, Dak'fron) seat their court in the capital's main hall/tavern.
        foreach (int preferredType in new[] { 18, 15, 14 })
        {
            CapitalSeatPlacement? hall = FindSeatInTowns(definitions, towns, bt => bt == preferredType);
            if (hall is not null)
                return hall;
        }

        return FindSeatInTowns(definitions, towns, _ => true);
    }

    private static CapitalSeatPlacement? FindSeatInTowns(
        DaggerfallDefinitions definitions,
        DaggerfallSiteRecord[] towns,
        Func<int, bool> matchesBuildingType)
    {
        foreach (DaggerfallSiteRecord town in towns)
        {
            DaggerfallSiteExterior ext = town.Exterior!;
            Dictionary<string, int> blockCounts = new(StringComparer.OrdinalIgnoreCase);
            foreach (DaggerfallSiteBlock block in ext.Blocks)
                blockCounts[block.SourceName] = blockCounts.GetValueOrDefault(block.SourceName) + 1;

            foreach (DaggerfallSiteBuildingSource building in ext.Buildings.Values
                .Where(b => matchesBuildingType(b.Source.BuildingType))
                .OrderBy(b => b.Id.BlockY)
                .ThenBy(b => b.Id.BlockX)
                .ThenBy(b => b.Id.Index))
            {
                DaggerfallSiteBlock? placedBlock = ext.Blocks.FirstOrDefault(
                    blk => blk.X == building.Id.BlockX && blk.Y == building.Id.BlockY);
                if (placedBlock is null || blockCounts.GetValueOrDefault(placedBlock.SourceName) != 1)
                    continue;
                if (!definitions.People.TryGetRmbInteriorSlots(placedBlock.SourceName, building.Id.Index, out DaggerfallPeopleBuildingSlots? slots)
                    || slots is null)
                {
                    continue;
                }
                bool hasTalkable = slots.Slots.Any(s =>
                    string.Equals(s.Interaction, "full-dialogue", StringComparison.Ordinal)
                    && !string.Equals(s.SpawnerId, "town-clutter-person", StringComparison.Ordinal));
                if (!hasTalkable)
                    continue;

                return new CapitalSeatPlacement(
                    town,
                    placedBlock.SourceName,
                    building.Id.BlockX,
                    building.Id.BlockY,
                    building.Id.Index,
                    building.Source.BuildingType);
            }
        }
        return null;
    }

    private static List<string> CollectFreeTalkableSlots(DaggerfallDefinitions definitions, CapitalSeatPlacement seat)
    {
        if (!definitions.People.TryGetRmbInteriorSlots(seat.Block, seat.BuildingIndex, out DaggerfallPeopleBuildingSlots? building)
            || building is null)
        {
            return [];
        }

        HashSet<string> claimed = new(StringComparer.Ordinal);
        foreach (DaggerfallCastIndividualDefinition homed in definitions.People.HomedAt(seat.Site.Id, seat.Block))
        {
            if (!string.IsNullOrWhiteSpace(homed.HomeSlotId))
                claimed.Add(homed.HomeSlotId);
        }

        List<string> free = [];
        foreach (DaggerfallPeopleSlotDefinition slot in building.Slots)
        {
            if (!string.Equals(slot.Interaction, "full-dialogue", StringComparison.Ordinal)
                || string.Equals(slot.SpawnerId, "town-clutter-person", StringComparison.Ordinal))
            {
                continue;
            }
            if (slot.CastId is not null || claimed.Contains(slot.SlotId))
                continue;
            free.Add(slot.SlotId);
        }
        return free;
    }

    private static string ResolveProvinceFamilyId(
        int region,
        DaggerfallCastIndividualDefinition? classicCastRuler,
        DaggerfallFactionDefinition? uncastClassicRuler)
    {
        if (!string.IsNullOrWhiteSpace(classicCastRuler?.FamilyId))
            return classicCastRuler.FamilyId;
        if (region == 21 || uncastClassicRuler?.Id == 400)
            return "fam:dynasty:anticlere";
        if (region == 22)
            return "fam:dynasty:lainlyn";
        return string.Create(CultureInfo.InvariantCulture, $"{DaggerfallHouseholds.DynastyPrefix}r{region}");
    }

    private static string ResolveHouseLabel(string familyId, string regionName, string? houseSurname) => familyId switch
    {
        "fam:dynasty:daggerfall" => "House of Daggerfall",
        "fam:dynasty:wayrest" => "House of Wayrest",
        "fam:dynasty:sentinel" => "House of Sentinel",
        "fam:dynasty:direnni" => "Clan Direnni",
        "fam:dynasty:anticlere" => "House Flyte",
        "fam:dynasty:lainlyn" => "House of Lainlyn",
        _ => !string.IsNullOrWhiteSpace(houseSurname)
            ? $"House {houseSurname} of {regionName}"
            : $"House of {regionName}",
    };

    private static (string Title, string Gender) ResolveRulerRankAndGender(DaggerfallFactionDefinition province, int region)
    {
        if (province.Ruler is >= 1 and <= 12)
            return RulerRankTable[province.Ruler - 1];
        bool female = DrawIndex($"r{region}:fallback-ruler-gender", 2) == 1;
        return female ? ("Lady", "Female") : ("Lord", "Male");
    }

    private static string ResolveRegionRace(DaggerfallDefinitions definitions, DaggerfallFactionDefinition province, int nameBank)
    {
        int raceId = Math.Clamp(nameBank, 0, 7) + 1;
        DaggerfallRaceDefinition? match = definitions.Catalogs.Races.FirstOrDefault(r => r.DonorRaceId == raceId);
        return match?.Id ?? (nameBank == 1 ? "redguard" : "breton");
    }

    private static DaggerfallPeopleVisual PickNobleVisual(
        DaggerfallDefinitions definitions,
        int region,
        string regionName,
        string climateName,
        string gender,
        string key)
    {
        IReadOnlyList<DaggerfallPeoplePoolEntry>? pool = null;
        foreach (string spawnerId in new[] { "town-noble", "town-courtier" })
        {
            if (!definitions.People.Spawners.TryGetValue(spawnerId, out DaggerfallPeopleSpawnerDefinition? spawner))
                continue;
            pool = ResolveSpawnerPool(spawner, region, regionName, climateName);
            if (pool.Count > 0)
                break;
        }
        if (pool is null || pool.Count == 0)
            return string.Equals(gender, "Female", StringComparison.Ordinal)
                ? new DaggerfallPeopleVisual(183, 11, "Female")
                : new DaggerfallPeopleVisual(182, 23, "Male");

        DaggerfallPeoplePoolEntry[] matching = [.. pool.Where(e => string.Equals(e.Gender, gender, StringComparison.OrdinalIgnoreCase))];
        IReadOnlyList<DaggerfallPeoplePoolEntry> source = matching.Length > 0 ? matching : pool;
        int totalWeight = 0;
        foreach (DaggerfallPeoplePoolEntry entry in source)
            totalWeight += Math.Max(1, entry.Weight);
        int pick = DrawIndex($"visual:{key}", totalWeight);
        int cursor = 0;
        foreach (DaggerfallPeoplePoolEntry entry in source)
        {
            cursor += Math.Max(1, entry.Weight);
            if (pick < cursor)
                return new DaggerfallPeopleVisual(entry.Archive, entry.Record, gender);
        }
        DaggerfallPeoplePoolEntry last = source[^1];
        return new DaggerfallPeopleVisual(last.Archive, last.Record, gender);
    }

    private static IReadOnlyList<DaggerfallPeoplePoolEntry> ResolveSpawnerPool(
        DaggerfallPeopleSpawnerDefinition spawner,
        int region,
        string regionName,
        string climateName)
    {
        if (spawner.RegionalPools.TryGetValue(regionName, out IReadOnlyList<DaggerfallPeoplePoolEntry>? byName) && byName.Count > 0)
            return byName;
        if (spawner.RegionalPools.TryGetValue(region.ToString(CultureInfo.InvariantCulture), out IReadOnlyList<DaggerfallPeoplePoolEntry>? byId) && byId.Count > 0)
            return byId;
        if (spawner.ClimatePools.TryGetValue(climateName, out IReadOnlyList<DaggerfallPeoplePoolEntry>? byClimate) && byClimate.Count > 0)
            return byClimate;
        return spawner.DefaultPool;
    }

    private static string GenerateDistinctFullName(
        DaggerfallDefinitions definitions,
        int bank,
        bool female,
        string identity,
        string? surnameOverride,
        IEnumerable<string> existing)
    {
        HashSet<string> used = new(existing, StringComparer.OrdinalIgnoreCase);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string candidate = GenerateFullName(definitions, bank, female, attempt == 0 ? identity : $"{identity}:retry{attempt}", surnameOverride);
            if (!used.Contains(candidate))
                return candidate;
        }
        return GenerateFullName(definitions, bank, female, $"{identity}:final", surnameOverride);
    }

    private static string GenerateFullName(
        DaggerfallDefinitions definitions,
        int bank,
        bool female,
        string identity,
        string? surnameOverride)
    {
        string first = GenerateFirstName(definitions, bank, female, identity);
        string? last = !string.IsNullOrWhiteSpace(surnameOverride)
            ? surnameOverride
            : GenerateSurname(definitions, bank, $"{identity}.last");
        return !string.IsNullOrWhiteSpace(last) ? $"{first} {last}" : first;
    }

    private static string GenerateFirstName(DaggerfallDefinitions definitions, int bank, bool female, string identity)
    {
        int safeBank = bank is >= 0 and <= 7 ? bank : 0;
        if (safeBank != 1)
            return JoinParts(definitions, safeBank, $"{identity}.first", female ? [2, 3] : [0, 1]);
        string first = JoinParts(definitions, safeBank, identity, [0, 1, 2]);
        return female || DrawIndex($"{identity}.suffix-chance", 100) < 75
            ? first + ReadFragment(definitions, safeBank, female ? 4 : 3, $"{identity}.suffix")
            : first;
    }

    private static string? GenerateSurname(DaggerfallDefinitions definitions, int bank, string identity)
    {
        int safeBank = bank is >= 0 and <= 7 ? bank : 0;
        if (safeBank == 1)
            return null;
        if (safeBank == 2)
        {
            string suffix = string.Concat(definitions.Text.Require(new(DaggerfallTextKind.Internal, "nordSurnameImmutableSuffix")).TextRuns);
            return JoinParts(definitions, safeBank, identity, [0, 1]) + suffix;
        }
        return JoinParts(definitions, safeBank, identity, [4, 5]);
    }

    private static string JoinParts(DaggerfallDefinitions definitions, int bank, string identity, int[] sets)
    {
        string result = string.Empty;
        for (int i = 0; i < sets.Length; i++)
            result += ReadFragment(definitions, bank, sets[i], $"{identity}.{i}");
        return result;
    }

    private static string ReadFragment(DaggerfallDefinitions definitions, int bank, int setIndex, string identity)
    {
        DaggerfallNameBankDefinition table = definitions.Names.Banks.Single(value => value.Bank == bank);
        DaggerfallNameSetDefinition set = table.Sets.Single(value => value.Set == setIndex);
        int idx = DrawIndex(identity, set.Keys.Count);
        return string.Concat(definitions.Text.Require(set.Keys[idx]).TextRuns);
    }

    private static int DrawIndex(string key, int count)
    {
        if (count <= 1) return 0;
        ulong hash = StableHash.NonZeroFnv1a64(string.Create(CultureInfo.InvariantCulture, $"daggerfall.canon.nobility:{CanonSeed}:{key}"));
        return checked((int)(hash % (ulong)count));
    }
}
