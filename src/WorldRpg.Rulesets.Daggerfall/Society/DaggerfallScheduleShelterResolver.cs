using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One deterministic exterior or off-hours closed-building NPC scheduled to visit an interior building.</summary>
internal sealed record DaggerfallScheduledVisitor(
    long DurableId,
    DaggerfallWorldProfileKey HomeProfile,
    string HomeSlotId,
    DaggerfallNpcKind Kind,
    DaggerfallNpcAppearance Appearance,
    string Role,
    IReadOnlyList<string> Services,
    string? FamilyId,
    string? SpawnerId,
    ActorHarmPolicyKind Harm,
    ActorInteractionPolicyKind Interaction,
    string? DisplayName,
    DaggerfallScheduleDecision Decision,
    bool IsExteriorWalker,
    DaggerfallSiteBuildingId DestinationBuilding);

/// <summary>Canonical schedule readout for one interior static NPC at a site (at their home post, sheltering at home, or visiting another building).</summary>
internal sealed record DaggerfallScheduledInteriorResident(
    long DurableId,
    DaggerfallSiteBuildingId HomeBuildingId,
    string HomeSlotId,
    string Role,
    string? DisplayName,
    DaggerfallScheduleDecision Decision,
    DaggerfallSiteBuildingId? VisitingBuildingId,
    bool TavernOverflowed);

/// <summary>
/// Pure town-wide shelter and evening-tavern assignment snapshot for one site at one instant.
/// Guarantees every NPC has at most one active location (zero duplicate bodies across exterior, tavern,
/// temple, residential home, and closed shop interiors).
/// </summary>
internal sealed class DaggerfallTownShelterAssignment
{
    private static readonly IReadOnlyList<DaggerfallScheduledVisitor> EmptyVisitors = [];
    private static readonly IReadOnlySet<long> EmptySuppressed = new HashSet<long>();
    private static readonly IReadOnlyList<DaggerfallScheduledInteriorResident> EmptyResidents = [];

    internal static DaggerfallTownShelterAssignment Empty { get; } = new(
        new Dictionary<DaggerfallSiteBuildingId, IReadOnlyList<DaggerfallScheduledVisitor>>(),
        new Dictionary<DaggerfallSiteBuildingId, IReadOnlySet<long>>(),
        new Dictionary<long, DaggerfallSiteBuildingId>(),
        new HashSet<long>(),
        EmptyResidents);

    internal DaggerfallTownShelterAssignment(
        IReadOnlyDictionary<DaggerfallSiteBuildingId, IReadOnlyList<DaggerfallScheduledVisitor>> visitorsByBuilding,
        IReadOnlyDictionary<DaggerfallSiteBuildingId, IReadOnlySet<long>> suppressedStaticSlotsByBuilding,
        IReadOnlyDictionary<long, DaggerfallSiteBuildingId> assignedBuildingByActor,
        IReadOnlySet<long> offStageOverflowActorIds,
        IReadOnlyList<DaggerfallScheduledInteriorResident>? interiorResidents = null)
    {
        VisitorsByBuilding = visitorsByBuilding;
        SuppressedStaticSlotsByBuilding = suppressedStaticSlotsByBuilding;
        AssignedBuildingByActor = assignedBuildingByActor;
        OffStageOverflowActorIds = offStageOverflowActorIds;
        InteriorResidents = interiorResidents ?? EmptyResidents;
    }

    internal IReadOnlyDictionary<DaggerfallSiteBuildingId, IReadOnlyList<DaggerfallScheduledVisitor>> VisitorsByBuilding { get; }
    internal IReadOnlyDictionary<DaggerfallSiteBuildingId, IReadOnlySet<long>> SuppressedStaticSlotsByBuilding { get; }
    internal IReadOnlyDictionary<long, DaggerfallSiteBuildingId> AssignedBuildingByActor { get; }
    internal IReadOnlySet<long> OffStageOverflowActorIds { get; }
    internal IReadOnlyList<DaggerfallScheduledInteriorResident> InteriorResidents { get; }

    internal IReadOnlyList<DaggerfallScheduledVisitor> VisitorsFor(DaggerfallSiteBuildingId buildingId) =>
        VisitorsByBuilding.TryGetValue(buildingId, out IReadOnlyList<DaggerfallScheduledVisitor>? list)
            ? list
            : EmptyVisitors;

    internal bool IsStaticSlotScheduledAway(DaggerfallSiteBuildingId buildingId, long durableId) =>
        SuppressedStaticSlotsByBuilding.TryGetValue(buildingId, out IReadOnlySet<long>? set)
        && set.Contains(durableId);

    internal IReadOnlySet<long> SuppressedSlotsFor(DaggerfallSiteBuildingId buildingId) =>
        SuppressedStaticSlotsByBuilding.TryGetValue(buildingId, out IReadOnlySet<long>? set)
            ? set
            : EmptySuppressed;
}

/// <summary>
/// Pure deterministic town-wide sheltering and off-hours tavern mixer resolver (#11085 / #11104).
/// Assigns sheltering exterior walkers and off-hours closed-building proprietors/clerks/guild members
/// to local taverns, temples, and #11084 family residential homes without loading other buildings' 3D profiles,
/// enforcing per-building capacity caps, #11084 neighbor/regular priority, home overflow, and zero duplicate bodies.
/// </summary>
internal static class DaggerfallScheduleShelterResolver
{
    private const int TempleBuildingType = 14;
    private const int TavernBuildingType = 15;
    private const float RmbBlockSide = DaggerfallWorldGrid.HorizontalSize / 8f;
    private const float MinStandSeparationMeters = 1.2f;
    private const float MinDoorClearanceMeters = 1.6f;

    internal const int DefaultTavernShelterCapacity = 6;
    internal const int DefaultTempleShelterCapacity = 4;
    internal const int DefaultResidenceShelterCapacity = 3;

    internal static bool IsResidenceBuildingType(int buildingType) =>
        buildingType is >= 17 and <= 23;

    internal static bool IsShelterDestinationBuildingType(int buildingType) =>
        buildingType is TavernBuildingType or TempleBuildingType or (>= 17 and <= 23);

    /// <summary>
    /// Canonical deterministic shelter slot capacity for a building from town metadata alone so every interior
    /// in town agrees on the exact same town-wide assignment without loading sibling buildings' 3D profiles.
    /// </summary>
    internal static int DefaultCapacityForBuilding(DaggerfallSiteBuildingSource building)
    {
        ArgumentNullException.ThrowIfNull(building);
        int bt = building.Source.BuildingType;
        if (bt == TavernBuildingType)
            return DefaultTavernShelterCapacity;
        if (bt == TempleBuildingType)
            return DefaultTempleShelterCapacity;
        if (IsResidenceBuildingType(bt))
            return DefaultResidenceShelterCapacity;
        return 0;
    }

    internal static DaggerfallTownShelterAssignment ResolveTown(
        ulong worldSocialSeed,
        DaggerfallDefinitions definitions,
        DaggerfallSiteRecord site,
        IReadOnlyList<DaggerfallSiteBuildingSource> buildings,
        IReadOnlyList<DaggerfallPopulationPlacement> exteriorPlacements,
        int minuteOfDay,
        DaggerfallWeatherKind weather,
        DaggerfallWeatherShelterElapsed weatherElapsed,
        int holidayId = 0,
        Func<long, bool>? isActiveQuestor = null,
        Func<long, bool>? isOfficeHolder = null,
        Func<long, DaggerfallWorldProfileKey, bool>? isHomeSlotSuppressed = null,
        Func<DaggerfallSiteBuildingSource, int>? capacityForBuilding = null,
        IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite>? availableBillboards = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(exteriorPlacements);

        Func<long, bool> checkQuestor = isActiveQuestor ?? (_ => false);
        Func<long, bool> checkOffice = isOfficeHolder ?? (_ => false);
        Func<long, DaggerfallWorldProfileKey, bool> checkSuppressed = isHomeSlotSuppressed ?? ((_, _) => false);
        Func<DaggerfallSiteBuildingSource, int> readCapacity = capacityForBuilding ?? DefaultCapacityForBuilding;

        DaggerfallSiteBuildingSource[] orderedBuildings =
        [
            .. buildings
                .OrderBy(b => b.Id.BlockY)
                .ThenBy(b => b.Id.BlockX)
                .ThenBy(b => b.Id.Index),
        ];

        List<DaggerfallSiteBuildingSource> taverns = [];
        List<DaggerfallSiteBuildingSource> temples = [];
        List<DaggerfallSiteBuildingSource> residences = [];
        foreach (DaggerfallSiteBuildingSource building in orderedBuildings)
        {
            int bt = building.Source.BuildingType;
            if (bt == TavernBuildingType)
                taverns.Add(building);
            else if (bt == TempleBuildingType && !IsGuildFaction(definitions, building.Source.FactionId))
                temples.Add(building);
            else if (IsResidenceBuildingType(bt))
                residences.Add(building);
        }

        if (orderedBuildings.Length == 0 && exteriorPlacements.Count == 0)
            return DaggerfallTownShelterAssignment.Empty;

        bool hasShelterDestinations = taverns.Count > 0 || temples.Count > 0 || residences.Count > 0;
        Dictionary<DaggerfallSiteBuildingId, int> remainingCapacity = [];
        foreach (DaggerfallSiteBuildingSource b in taverns.Concat(temples).Concat(residences))
            remainingCapacity[b.Id] = Math.Max(0, readCapacity(b));

        List<TavernApplicant> tavernApplicants = [];
        List<HomeShelterCandidate> homeCandidates = [];
        List<(long DurableId, DaggerfallSiteBuildingId HomeBuildingId, string HomeSlotId, string Role, string? DisplayName, DaggerfallScheduleDecision Decision)> pendingInteriorResidents = [];
        HashSet<long> seenActors = [];

        DaggerfallWorldProfileKey exteriorProfile = DaggerfallWorldProfileIds.Exterior(site.Id);
        string? climateRace = DaggerfallSourcePopulation.TryResolveClimateRace(definitions, site, out _);
        int totalPop = exteriorPlacements.Count(placement => placement.CrackdownGuard == 0);

        // Phase A: Exterior source-population walkers currently scheduled indoors (tavern or home/storm shelter).
        if (hasShelterDestinations && climateRace is not null && totalPop > 0)
        {
            foreach (DaggerfallPopulationPlacement placement in exteriorPlacements
                .OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                if (placement.IsGuard)
                    continue;
                if (placement.FactionId != 0 && !definitions.Factions.Factions.ContainsKey(placement.FactionId))
                    continue;
                if (availableBillboards is not null
                    && !availableBillboards.ContainsKey((placement.BillboardArchive, placement.BillboardRecord)))
                    continue;

                string slotKey = $"{exteriorProfile.LogicalId}/{placement.Id}";
                long durableId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, slotKey);
                if (!seenActors.Add(durableId))
                    continue;
                if (checkSuppressed(durableId, exteriorProfile))
                    continue;

                DaggerfallScheduleDecision decision = DaggerfallNpcSchedulePolicy.EvaluateExteriorPlacement(
                    worldSocialSeed,
                    site.Id,
                    placement.Id,
                    placement.IsGuard,
                    totalPop,
                    definitions.ActorCatalog,
                    minuteOfDay,
                    weather,
                    weatherElapsedMinutes: weatherElapsed.PrecipElapsedMinutes,
                    isActiveQuestor: checkQuestor(durableId),
                    isOfficeHolder: checkOffice(durableId),
                    severeWeatherElapsedMinutes: weatherElapsed.SevereElapsedMinutes);

                if (decision.Placement == DaggerfallSchedulePlacement.ExteriorStreet || decision.Pinned)
                    continue;

                (DaggerfallNpcAppearance look, string role, IReadOnlyList<string> services) =
                    DaggerfallSourcePopulation.ResolvePlacementIdentity(definitions, placement, climateRace);

                int blockX = (int)MathF.Floor(placement.Position.X / RmbBlockSide);
                int blockY = (int)MathF.Floor(-placement.Position.Z / RmbBlockSide);
                ulong rank = StableHash.NonZeroFnv1a64(string.Create(CultureInfo.InvariantCulture,
                    $"daggerfall.shelter.rank:{worldSocialSeed}:{site.Id.Region}:{site.Id.Index}:{durableId}"));

                CandidatePayload payload = new(
                    durableId,
                    exteriorProfile,
                    placement.Id,
                    DaggerfallNpcKind.Civilian,
                    look,
                    role,
                    services,
                    placement.FamilyId,
                    SpawnerId: null,
                    ActorHarmPolicyKind.LawProtectedCivilian,
                    ActorInteractionPolicyKind.FullDialogue,
                    DisplayName: null,
                    decision,
                    IsExteriorWalker: true,
                    HomeBuildingId: null,
                    blockX,
                    blockY,
                    rank);

                if (decision.Placement == DaggerfallSchedulePlacement.InteriorBuilding
                    && decision.BuildingTarget == DaggerfallScheduleBuildingTarget.Tavern
                    && taverns.Count > 0)
                {
                    (DaggerfallSiteBuildingSource preferredTavern, int priorityTier) = SelectPreferredTavern(
                        worldSocialSeed, site.Id, payload.FamilyId, placement.Id, blockX, blockY, taverns);
                    tavernApplicants.Add(new(payload, preferredTavern.Id, priorityTier));
                }
                else
                {
                    homeCandidates.Add(new(payload));
                }
            }
        }

        // Phase B: Interior static NPCs (canonical schedule decisions for every building's residents, plus
        // off-hours evening tavern mixer applications for closed-shop proprietors, court clerks/nobles, and guild members).
        if (orderedBuildings.Length > 0)
        {
            string regionName = definitions.BuildingNames.RegionName(site.Id.Region);
            string climateName = definitions.Grids.Climate.GetCell(site.MapPixelX + 1, site.MapPixelY).Name;
            int townBuckets = DaggerfallHouseholds.TownHouseholdBucketCount(site);
            int hour = DaggerfallNpcSchedulePolicy.NormalizeMinute(minuteOfDay) / DaggerfallCalendar.MinutesPerHour;
            int minute = DaggerfallNpcSchedulePolicy.NormalizeMinute(minuteOfDay) % DaggerfallCalendar.MinutesPerHour;
            DaggerfallActorScheduleProfile alwaysStationedProfile =
                definitions.ActorCatalog.Schedules[DaggerfallNpcSchedulePolicy.AlwaysStationedScheduleId];

            foreach (DaggerfallSiteBuildingSource building in orderedBuildings)
            {
                int bt = building.Source.BuildingType;
                if (!definitions.People.TryGetRmbInteriorSlots(building.Source.Id.SourceKey, building.Source.Id.Index, out DaggerfallPeopleBuildingSlots? slots)
                    || slots is null || slots.Slots.Count == 0)
                    continue;

                bool canVisitTavern = taverns.Count > 0 && bt != TavernBuildingType && !IsResidenceBuildingType(bt);

                Dictionary<string, DaggerfallCastIndividualDefinition>? homedCastBySlot = null;
                foreach (DaggerfallCastIndividualDefinition cast in definitions.AllCastAndNobles())
                {
                    if (!string.IsNullOrWhiteSpace(cast.HomeSlotId)
                        && string.Equals(cast.HomeBlock, building.Source.Id.SourceKey, StringComparison.OrdinalIgnoreCase)
                        && (cast.HomeSite is null || cast.HomeSite == site.Id))
                    {
                        homedCastBySlot ??= new Dictionary<string, DaggerfallCastIndividualDefinition>(StringComparer.Ordinal);
                        homedCastBySlot.TryAdd(cast.HomeSlotId, cast);
                    }
                }

                DaggerfallWorldProfileKey buildingProfile = DaggerfallWorldProfileIds.Interior(site.Id, building.Id);
                int talkableOrdinal = 0;
                foreach (DaggerfallPeopleSlotDefinition slot in slots.Slots)
                {
                    if (!string.Equals(slot.Interaction, "full-dialogue", StringComparison.Ordinal)
                        || string.Equals(slot.SpawnerId, "town-clutter-person", StringComparison.Ordinal))
                        continue;

                    int ordinal = talkableOrdinal++;
                    string placementId = string.Create(CultureInfo.InvariantCulture, $"person/{ordinal}");

                    int factionId = slot.FactionId ?? 0;
                    if (slot.DeriveRegionalFaction && factionId == 0)
                        factionId = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, site.Id.Region)?.Id ?? 0;

                    DaggerfallCastIndividualDefinition? castIndividual = null;
                    if (!string.IsNullOrWhiteSpace(slot.CastId)
                        && definitions.TryGetCastOrNoble(slot.CastId, out DaggerfallCastIndividualDefinition? explicitCast))
                    {
                        castIndividual = explicitCast;
                    }
                    if (castIndividual is null && homedCastBySlot is not null)
                        homedCastBySlot.TryGetValue(slot.SlotId, out castIndividual);
                    if (castIndividual is null && factionId > 0
                        && definitions.TryGetCastOrNobleByFaction(factionId, out DaggerfallCastIndividualDefinition? factionCast))
                    {
                        castIndividual = factionCast;
                    }

                    bool isCastOrHomed = slot.CastId is not null || castIndividual is not null;
                    long durableId = castIndividual is not null
                        ? DaggerfallPeopleSlots.CastIndividualIdentity(castIndividual.Id)
                        : DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{buildingProfile.LogicalId}/{placementId}");
                    if (!seenActors.Add(durableId))
                        continue;
                    if (checkSuppressed(durableId, buildingProfile))
                        continue;

                    bool isChild = string.Equals(slot.SpawnerId, "town-child", StringComparison.Ordinal);

                    DaggerfallActorSpawnerSpec? spec = !string.IsNullOrWhiteSpace(slot.SpawnerId)
                        && definitions.ActorCatalog.Specs.TryGetValue(slot.SpawnerId, out DaggerfallActorSpawnerSpec? foundSpec)
                        ? foundSpec
                        : null;

                    DaggerfallScheduleDecision decision = !isCastOrHomed && !isChild && spec is not null
                        ? DaggerfallNpcSchedulePolicy.EvaluateSpec(
                            worldSocialSeed,
                            site.Id,
                            placementId,
                            spec,
                            definitions.ActorCatalog,
                            hour,
                            minute,
                            weather,
                            buildingType: bt,
                            holidayId: holidayId,
                            weatherElapsedMinutes: weatherElapsed.PrecipElapsedMinutes,
                            isActiveQuestor: checkQuestor(durableId),
                            isOfficeHolder: checkOffice(durableId),
                            severeWeatherElapsedMinutes: weatherElapsed.SevereElapsedMinutes)
                        : DaggerfallNpcSchedulePolicy.Evaluate(
                            worldSocialSeed,
                            site.Id,
                            placementId,
                            alwaysStationedProfile,
                            minuteOfDay,
                            weather,
                            buildingType: bt,
                            holidayId: holidayId,
                            weatherElapsedMinutes: weatherElapsed.PrecipElapsedMinutes,
                            isActiveQuestor: checkQuestor(durableId),
                            isOfficeHolder: checkOffice(durableId),
                            severeWeatherElapsedMinutes: weatherElapsed.SevereElapsedMinutes);

                    DaggerfallFactionDefinition? sourceFaction = definitions.Factions.Factions.GetValueOrDefault(factionId);
                    (string role, IReadOnlyList<string> services) = DaggerfallNpcServiceFacts.Resolve(
                        definitions, sourceFaction, bt, building.Source.FactionId, "person");

                    string residentSlotId = castIndividual is not null ? $"cast:{castIndividual.Id}" : placementId;
                    pendingInteriorResidents.Add((durableId, building.Id, residentSlotId, role, castIndividual?.Name, decision));

                    if (!canVisitTavern
                        || isCastOrHomed
                        || isChild
                        || spec is null
                        || string.IsNullOrWhiteSpace(slot.SpawnerId)
                        || decision.Pinned
                        || decision.Placement != DaggerfallSchedulePlacement.InteriorBuilding
                        || decision.BuildingTarget != DaggerfallScheduleBuildingTarget.Tavern)
                        continue;

                    if (!definitions.People.Spawners.TryGetValue(slot.SpawnerId, out DaggerfallPeopleSpawnerDefinition? spawner))
                        continue;

                    string slotKey = DaggerfallPeopleSlots.InteriorBuildingSlotKey(building.Id.BlockX, building.Id.BlockY, slot.SlotId);
                    DaggerfallResolvedPeopleSlot resolved = DaggerfallPeopleSlots.ResolveSlot(
                        definitions.People, definitions.Factions, worldSocialSeed, site.Id, slotKey, slot,
                        regionKey: regionName, climateKey: climateName);
                    if (availableBillboards is not null
                        && !availableBillboards.ContainsKey((resolved.Visual.Archive, resolved.Visual.Record)))
                        continue;

                    string race = climateRace
                        ?? (definitions.BuildingNames.TryGetNameBank(site.Id.Region, out int bank) && bank == 1 ? "redguard" : "breton");
                    int seededName = unchecked((int)(resolved.SlotSeed & 0x7FFF_FFFFUL));
                    DaggerfallNpcAppearance look = new(
                        race,
                        resolved.Visual.Gender,
                        resolved.Visual.Archive,
                        resolved.Visual.Record,
                        seededName == 0 ? 1 : seededName,
                        factionId);
                    string? familyId = DaggerfallHouseholds.ResolveForPlacedSlot(
                        worldSocialSeed,
                        site.Id,
                        bt,
                        building.Id.BlockX,
                        building.Id.BlockY,
                        building.Id.Index,
                        slotKey,
                        spawner.SocialGrouping,
                        townBucketCount: townBuckets);
                    ActorHarmPolicyKind harm = DaggerfallPeopleSlots.ParseHarmPolicy(resolved.Harm, ActorHarmPolicyKind.Protected);
                    ActorInteractionPolicyKind interaction = DaggerfallPeopleSlots.ParseInteractionPolicy(resolved.Interaction);
                    ulong rank = StableHash.NonZeroFnv1a64(string.Create(CultureInfo.InvariantCulture,
                        $"daggerfall.shelter.rank:{worldSocialSeed}:{site.Id.Region}:{site.Id.Index}:{durableId}"));

                    CandidatePayload payload = new(
                        durableId,
                        buildingProfile,
                        placementId,
                        DaggerfallNpcKind.Static,
                        look,
                        role,
                        services,
                        familyId,
                        resolved.SpawnerId,
                        harm,
                        interaction,
                        DisplayName: null,
                        decision,
                        IsExteriorWalker: false,
                        HomeBuildingId: building.Id,
                        building.Id.BlockX,
                        building.Id.BlockY,
                        rank);

                    (DaggerfallSiteBuildingSource preferredTavern, int priorityTier) = SelectPreferredTavern(
                        worldSocialSeed, site.Id, familyId, placementId, building.Id.BlockX, building.Id.BlockY, taverns);
                    tavernApplicants.Add(new(payload, preferredTavern.Id, priorityTier));
                }
            }
        }

        Dictionary<DaggerfallSiteBuildingId, List<DaggerfallScheduledVisitor>> admittedByBuilding = [];
        Dictionary<DaggerfallSiteBuildingId, HashSet<long>> suppressedStaticSlots = [];
        Dictionary<long, DaggerfallSiteBuildingId> assignedBuildingByActor = [];
        HashSet<long> offStageOverflow = [];

        void Admit(CandidatePayload candidate, DaggerfallSiteBuildingId destinationBuilding)
        {
            if (!admittedByBuilding.TryGetValue(destinationBuilding, out List<DaggerfallScheduledVisitor>? list))
            {
                list = [];
                admittedByBuilding[destinationBuilding] = list;
            }
            list.Add(new DaggerfallScheduledVisitor(
                candidate.DurableId,
                candidate.HomeProfile,
                candidate.HomeSlotId,
                candidate.Kind,
                candidate.Appearance,
                candidate.Role,
                candidate.Services,
                candidate.FamilyId,
                candidate.SpawnerId,
                candidate.Harm,
                candidate.Interaction,
                candidate.DisplayName,
                candidate.Decision,
                candidate.IsExteriorWalker,
                destinationBuilding));
            assignedBuildingByActor[candidate.DurableId] = destinationBuilding;
            remainingCapacity[destinationBuilding] = Math.Max(0, remainingCapacity[destinationBuilding] - 1);

            if (!candidate.IsExteriorWalker && candidate.HomeBuildingId is { } homeBuilding)
            {
                if (!suppressedStaticSlots.TryGetValue(homeBuilding, out HashSet<long>? set))
                {
                    set = [];
                    suppressedStaticSlots[homeBuilding] = set;
                }
                set.Add(candidate.DurableId);
            }
        }

        // Phase C: Admit tavern applicants per preferred tavern up to capacity (preferring #11084 regulars/neighbors).
        // Exterior walkers that overflow a full tavern spill into Phase D (#11084 family residential home / OffStage);
        // off-hours closed-building proprietors that overflow simply remain at home (not suppressed).
        HashSet<long> overflowedInteriorTavernApplicants = [];
        foreach (IGrouping<DaggerfallSiteBuildingId, TavernApplicant> group in tavernApplicants
            .GroupBy(a => a.PreferredTavern)
            .OrderBy(g => g.Key.BlockY)
            .ThenBy(g => g.Key.BlockX)
            .ThenBy(g => g.Key.Index))
        {
            foreach (TavernApplicant applicant in group
                .OrderBy(a => a.PriorityTier)
                .ThenBy(a => a.Candidate.Rank)
                .ThenBy(a => a.Candidate.DurableId))
            {
                if (remainingCapacity.TryGetValue(group.Key, out int cap) && cap > 0)
                {
                    Admit(applicant.Candidate, group.Key);
                }
                else if (applicant.Candidate.IsExteriorWalker)
                {
                    homeCandidates.Add(new(applicant.Candidate));
                }
                else
                {
                    overflowedInteriorTavernApplicants.Add(applicant.Candidate.DurableId);
                }
            }
        }

        // Phase D: Route sheltering exterior walkers and tavern overflow to local temples (small storm slice)
        // or their deterministic #11084 family residential building, with remaining overflow marked OffStage.
        bool isStormOrRain = DaggerfallWeatherShelterClock.IsPrecipitating(weather);
        foreach (HomeShelterCandidate homeCandidate in homeCandidates
            .OrderBy(c => c.Candidate.Rank)
            .ThenBy(c => c.Candidate.DurableId))
        {
            CandidatePayload candidate = homeCandidate.Candidate;

            if (isStormOrRain && temples.Count > 0 && candidate.Rank % 6UL == 0UL)
            {
                DaggerfallSiteBuildingSource temple = SelectNearestBuilding(
                    worldSocialSeed, site.Id, candidate.FamilyId, candidate.HomeSlotId, candidate.BlockX, candidate.BlockY, temples);
                if (remainingCapacity.TryGetValue(temple.Id, out int templeCap) && templeCap > 0)
                {
                    Admit(candidate, temple.Id);
                    continue;
                }
            }

            if (residences.Count > 0)
            {
                DaggerfallSiteBuildingSource residence = SelectFamilyResidence(
                    worldSocialSeed, site.Id, candidate.FamilyId, candidate.HomeSlotId, residences);
                if (remainingCapacity.TryGetValue(residence.Id, out int homeCap) && homeCap > 0)
                {
                    Admit(candidate, residence.Id);
                    continue;
                }
            }

            offStageOverflow.Add(candidate.DurableId);
        }

        Dictionary<DaggerfallSiteBuildingId, IReadOnlyList<DaggerfallScheduledVisitor>> readonlyVisitors = [];
        foreach ((DaggerfallSiteBuildingId bId, List<DaggerfallScheduledVisitor> list) in admittedByBuilding)
            readonlyVisitors[bId] = list;

        Dictionary<DaggerfallSiteBuildingId, IReadOnlySet<long>> readonlySuppressed = [];
        foreach ((DaggerfallSiteBuildingId bId, HashSet<long> set) in suppressedStaticSlots)
            readonlySuppressed[bId] = set;

        DaggerfallScheduledInteriorResident[] interiorResidents =
        [
            .. pendingInteriorResidents.Select(r =>
            {
                DaggerfallSiteBuildingId? visiting = assignedBuildingByActor.TryGetValue(r.DurableId, out DaggerfallSiteBuildingId dest)
                    ? dest
                    : null;
                bool tavernOverflowed = visiting is null && overflowedInteriorTavernApplicants.Contains(r.DurableId);
                return new DaggerfallScheduledInteriorResident(
                    r.DurableId,
                    r.HomeBuildingId,
                    r.HomeSlotId,
                    r.Role,
                    r.DisplayName,
                    r.Decision,
                    visiting,
                    tavernOverflowed);
            }),
        ];

        return new DaggerfallTownShelterAssignment(
            new ReadOnlyDictionary<DaggerfallSiteBuildingId, IReadOnlyList<DaggerfallScheduledVisitor>>(readonlyVisitors),
            new ReadOnlyDictionary<DaggerfallSiteBuildingId, IReadOnlySet<long>>(readonlySuppressed),
            new ReadOnlyDictionary<long, DaggerfallSiteBuildingId>(assignedBuildingByActor),
            offStageOverflow,
            interiorResidents);
    }

    /// <summary>
    /// Computes up to <paramref name="maxCount"/> clean, non-overlapping interior stand poses for scheduled visitors
    /// inside a loaded interior profile, preferring real interior markers clear of doors and static NPCs.
    /// </summary>
    internal static IReadOnlyList<(WorldPoint Position, float HeadingRadians)> ResolveInteriorStandPoints(
        DaggerfallSiteProfile destination,
        int maxCount,
        IEnumerable<WorldPoint>? extraOccupied = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (maxCount <= 0)
            return [];

        List<WorldPoint> occupied = [];
        foreach (DaggerfallStaticNpcPlacement staticNpc in destination.StaticNpcs)
            occupied.Add(staticNpc.Position);
        foreach (var fixedActor in destination.Project.Actors.Values)
            occupied.Add(fixedActor.Position);
        if (extraOccupied is not null)
        {
            foreach (WorldPoint livePos in extraOccupied)
                occupied.Add(livePos);
        }

        List<WorldPoint> doors = [];
        if (destination.InteriorBuilding?.EntryFrame is { } entryFrame)
        {
            foreach (DaggerfallBuildingEntryDoor door in entryFrame.Doors)
                doors.Add(door.Position);
        }
        foreach (DaggerfallSitePortal portal in destination.Portals)
            doors.Add(portal.Position);
        if (destination.Project.PlayerPosition is WorldPoint playerStart)
            doors.Add(playerStart);

        List<(WorldPoint Position, float HeadingRadians)> result = [];
        WorldPoint roomFocus = destination.InteriorBuilding?.EntryFrame?.Markers.FirstOrDefault()
            ?? destination.Project.PlayerPosition
            ?? default;

        bool IsClear(WorldPoint candidate)
        {
            if (!float.IsFinite(candidate.X) || !float.IsFinite(candidate.Y) || !float.IsFinite(candidate.Z))
                return false;
            foreach (WorldPoint occ in occupied)
            {
                if (candidate.HorizontalDistanceTo(occ) < MinStandSeparationMeters
                    && MathF.Abs(candidate.Y - occ.Y) < 2.0f)
                    return false;
            }
            foreach (WorldPoint door in doors)
            {
                if (candidate.HorizontalDistanceTo(door) < MinDoorClearanceMeters
                    && MathF.Abs(candidate.Y - door.Y) < 2.0f)
                    return false;
            }
            foreach ((WorldPoint chosen, _) in result)
            {
                if (candidate.HorizontalDistanceTo(chosen) < MinStandSeparationMeters
                    && MathF.Abs(candidate.Y - chosen.Y) < 2.0f)
                    return false;
            }
            return true;
        }

        float FaceToward(WorldPoint from, WorldPoint toward)
        {
            Vector3 delta = new(toward.X - from.X, 0f, toward.Z - from.Z);
            return delta.LengthSquared() > 1e-4f ? ActorHeading.Yaw(delta) : 0f;
        }

        void TryAdd(WorldPoint candidate)
        {
            if (result.Count >= maxCount || !IsClear(candidate))
                return;
            result.Add((candidate, FaceToward(candidate, roomFocus)));
        }

        // 1. Prefer real interior entry-frame markers.
        if (destination.InteriorBuilding?.EntryFrame?.Markers is { Count: > 0 } markers)
        {
            foreach (WorldPoint marker in markers)
                TryAdd(marker);
        }

        // 2. Then non-portal quest/interior markers.
        foreach (DaggerfallSiteMarker marker in destination.QuestMarkers)
        {
            if (marker.Kind != DaggerfallSiteMarkerKind.Portal)
                TryAdd(marker.Position);
        }

        // 3. Deterministic ring offsets around interior anchors / room center if more slots are needed.
        List<WorldPoint> anchors = [];
        if (destination.InteriorBuilding?.EntryFrame?.Markers is { Count: > 0 } frameMarkers)
            anchors.AddRange(frameMarkers);
        if (destination.StaticNpcs.Count > 0)
            anchors.Add(destination.StaticNpcs[0].Position);
        if (destination.Project.PlayerPosition is WorldPoint start)
            anchors.Add(start);
        if (anchors.Count == 0)
            anchors.Add(default);

        ReadOnlySpan<float> radii = [1.8f, 2.8f, 3.8f];
        const int angleSteps = 8;
        foreach (WorldPoint anchor in anchors)
        {
            if (result.Count >= maxCount)
                break;
            foreach (float radius in radii)
            {
                if (result.Count >= maxCount)
                    break;
                for (int step = 0; step < angleSteps && result.Count < maxCount; step++)
                {
                    float angle = step * (MathF.Tau / angleSteps);
                    WorldPoint candidate = new(
                        anchor.X + MathF.Sin(angle) * radius,
                        anchor.Y,
                        anchor.Z - MathF.Cos(angle) * radius);
                    TryAdd(candidate);
                }
            }
        }

        return result;
    }

    private static (DaggerfallSiteBuildingSource Tavern, int PriorityTier) SelectPreferredTavern(
        ulong worldSocialSeed,
        DaggerfallSiteId siteId,
        string? familyId,
        string slotId,
        int blockX,
        int blockY,
        IReadOnlyList<DaggerfallSiteBuildingSource> taverns)
    {
        // #11084 household regulars have a deterministic regular tavern tied to their FamilyId;
        // same-block neighbors and household regulars get Tier 0 priority when a tavern fills.
        int? regularIndex = TryParseHouseholdBucket(familyId, out int bucket)
            ? bucket % taverns.Count
            : null;

        DaggerfallSiteBuildingSource best = taverns[0];
        int bestBlockDist = int.MaxValue;
        ulong bestTie = ulong.MaxValue;
        bool bestIsRegular = false;

        for (int i = 0; i < taverns.Count; i++)
        {
            DaggerfallSiteBuildingSource tavern = taverns[i];
            int dist = Math.Abs(blockX - tavern.Id.BlockX) + Math.Abs(blockY - tavern.Id.BlockY);
            bool isRegular = regularIndex is int reg && reg == i;
            // Household regular tavern wins ties and beats adjacent blocks by 1 step so kin gather together.
            int effectiveDist = isRegular ? Math.Max(0, dist - 1) : dist;
            ulong tie = StableHash.NonZeroFnv1a64(string.Create(CultureInfo.InvariantCulture,
                $"daggerfall.tavern.pref:{worldSocialSeed}:{siteId.Region}:{siteId.Index}:{familyId ?? slotId}:{tavern.Id.BlockX}:{tavern.Id.BlockY}:{tavern.Id.Index}"));

            if (effectiveDist < bestBlockDist || (effectiveDist == bestBlockDist && tie < bestTie))
            {
                best = tavern;
                bestBlockDist = effectiveDist;
                bestTie = tie;
                bestIsRegular = isRegular;
            }
        }

        int actualDist = Math.Abs(blockX - best.Id.BlockX) + Math.Abs(blockY - best.Id.BlockY);
        int priorityTier = (actualDist == 0 || bestIsRegular) ? 0
            : actualDist <= 1 ? 1
            : 2;
        return (best, priorityTier);
    }

    private static DaggerfallSiteBuildingSource SelectNearestBuilding(
        ulong worldSocialSeed,
        DaggerfallSiteId siteId,
        string? familyId,
        string slotId,
        int blockX,
        int blockY,
        IReadOnlyList<DaggerfallSiteBuildingSource> candidates)
    {
        DaggerfallSiteBuildingSource best = candidates[0];
        int bestDist = int.MaxValue;
        ulong bestTie = ulong.MaxValue;
        foreach (DaggerfallSiteBuildingSource candidate in candidates)
        {
            int dist = Math.Abs(blockX - candidate.Id.BlockX) + Math.Abs(blockY - candidate.Id.BlockY);
            ulong tie = StableHash.NonZeroFnv1a64(string.Create(CultureInfo.InvariantCulture,
                $"daggerfall.shelter.near:{worldSocialSeed}:{siteId.Region}:{siteId.Index}:{familyId ?? slotId}:{candidate.Id.BlockX}:{candidate.Id.BlockY}:{candidate.Id.Index}"));
            if (dist < bestDist || (dist == bestDist && tie < bestTie))
            {
                best = candidate;
                bestDist = dist;
                bestTie = tie;
            }
        }
        return best;
    }

    /// <summary>
    /// Deterministically maps a #11084 household (<c>fam:local:r{R}:s{S}:h{bucket}</c>) to a single real
    /// residential building in town so kin share the same family home when sheltering or overflowing a full tavern.
    /// </summary>
    internal static DaggerfallSiteBuildingSource SelectFamilyResidence(
        ulong worldSocialSeed,
        DaggerfallSiteId siteId,
        string? familyId,
        string slotId,
        IReadOnlyList<DaggerfallSiteBuildingSource> residences)
    {
        if (TryParseHouseholdBucket(familyId, out int bucket))
            return residences[bucket % residences.Count];
        ulong hash = StableHash.NonZeroFnv1a64(string.Create(CultureInfo.InvariantCulture,
            $"daggerfall.shelter.home:{worldSocialSeed}:{siteId.Region}:{siteId.Index}:{familyId ?? slotId}"));
        return residences[(int)(hash % (ulong)residences.Count)];
    }

    private static bool TryParseHouseholdBucket(string? familyId, out int bucket)
    {
        bucket = 0;
        if (string.IsNullOrWhiteSpace(familyId))
            return false;
        int marker = familyId.LastIndexOf(":h", StringComparison.Ordinal);
        if (marker < 0 || marker + 2 >= familyId.Length)
            return false;
        return int.TryParse(familyId.AsSpan(marker + 2), NumberStyles.None, CultureInfo.InvariantCulture, out bucket)
            && bucket >= 0;
    }

    private static bool IsGuildFaction(DaggerfallDefinitions definitions, int factionId) =>
        factionId > 0
        && definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction)
        && faction.Type is 2 or 3 or 4 or 7 or 10;

    private sealed record CandidatePayload(
        long DurableId,
        DaggerfallWorldProfileKey HomeProfile,
        string HomeSlotId,
        DaggerfallNpcKind Kind,
        DaggerfallNpcAppearance Appearance,
        string Role,
        IReadOnlyList<string> Services,
        string? FamilyId,
        string? SpawnerId,
        ActorHarmPolicyKind Harm,
        ActorInteractionPolicyKind Interaction,
        string? DisplayName,
        DaggerfallScheduleDecision Decision,
        bool IsExteriorWalker,
        DaggerfallSiteBuildingId? HomeBuildingId,
        int BlockX,
        int BlockY,
        ulong Rank);

    private sealed record TavernApplicant(
        CandidatePayload Candidate,
        DaggerfallSiteBuildingId PreferredTavern,
        int PriorityTier);

    private sealed record HomeShelterCandidate(CandidatePayload Candidate);
}
