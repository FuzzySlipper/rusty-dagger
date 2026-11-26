using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Cross-boundary tavern/home sheltering admission, off-hours closed-shop tavern mixer,
/// per-tavern capacity caps with #11084 household home overflow, and off-frustum live grace (#11085 / #11104).
/// </summary>
public sealed class DaggerfallScheduleShelterTests
{
    [Fact]
    public void Cross_boundary_storm_sheltering_preserves_identity_social_state_zero_duplicates_and_zero_save_bytes_for_untouched_visitors()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord charing = definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallSiteBuildingSource tavernBuilding = charing.Exterior!.Buildings.Values
            .OrderBy(b => b.Id.BlockY).ThenBy(b => b.Id.BlockX).ThenBy(b => b.Id.Index)
            .First(b => b.Source.BuildingType == 15);

        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile tavern = Assembled(DaggerfallWorldProfileIds.Interior(Charing, tavernBuilding.Id));
        DaggerfallSiteProfiles profiles = new([exterior, tavern]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, tavern);
        SpatialFake spatial = SpatialFake.Create(exterior.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, exterior, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        session.AdvanceElapsedTime(8 * 60 * 60); // midday sunny

        // Determine which real exterior walker will shelter in this specific tavern during a settled thunderstorm.
        DaggerfallCalendar middayCal = session.Calendar;
        DaggerfallTownShelterAssignment stormPlan = DaggerfallScheduleShelterResolver.ResolveTown(
            session.WorldSocialSeed,
            definitions,
            charing,
            [.. charing.Exterior.Buildings.Values],
            exterior.Population,
            minuteOfDay: checked(middayCal.Hour * 60 + middayCal.Minute),
            weather: DaggerfallWeatherKind.Thunder,
            weatherElapsed: DaggerfallWeatherShelterElapsed.Settled,
            availableBillboards: tavern.BillboardSprites);
        IReadOnlyList<DaggerfallScheduledVisitor> plannedTavernVisitors = stormPlan.VisitorsFor(tavernBuilding.Id);
        Assert.NotEmpty(plannedTavernVisitors);
        Assert.InRange(plannedTavernVisitors.Count, 1, DaggerfallScheduleShelterResolver.DefaultTavernShelterCapacity);

        DaggerfallScheduledVisitor targetVisitor = plannedTavernVisitors.First(v => v.IsExteriorWalker);
        long targetId = targetVisitor.DurableId;

        // Walk the player to this townsperson's street block so they are within the 120m outdoor admission range.
        DaggerfallPopulationPlacement targetPlacement = exterior.Population.Single(p => p.Id == targetVisitor.HomeSlotId);
        WorldPoint nearTargetLocal = session.Exterior.ProfileToLocal(targetPlacement.Position);
        session.State.PlayerControl.Restore(nearTargetLocal, session.State.PlayerControl.Motion);
        session.Population.ReconcileImmediate();

        // Meet this walker outdoors and give them social state (Met, DispositionDelta, Vouch), leaving all others untouched.
        Assert.True(session.State.Actors.TryGet(targetId, out ActorState? outdoorActor));
        ActorInstanceIdentity outdoorIdentity = outdoorActor!.Actor.Get<ActorInstanceIdentity>();
        string? outdoorFamily = outdoorActor.Actor.Get<ActorOverlayComponent>().FamilyId;
        Assert.Equal(targetVisitor.FamilyId, outdoorFamily);

        ActorOverlayComponent outdoorOverlay = outdoorActor.Actor.Get<ActorOverlayComponent>();
        outdoorOverlay.Met = true;
        ActorVouchRecord vouch = new(
            ReferrerDurableId: 777_001L,
            ReferrerFactionId: 0,
            Kind: ActorVouchKind.Contact,
            IssuedDay: checked((int)middayCal.DayNumber),
            ReferrerName: "Aldric");
        outdoorOverlay.RecordVouch(vouch);
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            targetId,
            outdoorActor.Actor.Get<ActorOriginComponent>().Origin,
            Social: DaggerfallSessionPersistence.ToSocialDiff(session.State.Npcs.Require(targetId)) with
            {
                Met = true,
                DispositionDelta = 12,
                Vouches = [vouch],
            }));
        WorldPoint outdoorBaselinePos = outdoorActor.Actor.Get<ActorOriginComponent>().BaselinePose;

        // Settled thunderstorm hits -> exterior street walkers shelter indoors.
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Thunder);
        session.ResetScheduleWeatherBaseline();
        session.Population.ReconcileImmediate();
        Assert.Equal(DaggerfallNpcPresence.Hidden, session.State.Npcs.Require(targetId).Presence);

        // Enter the local tavern: exact same DurableId, ActorInstanceIdentity, Met, DispositionDelta, Vouches, and FamilyId appear!
        Assert.True(session.TryTransitionTo(tavern.ProfileKey));
        Assert.True(session.State.Actors.TryGet(targetId, out ActorState? indoorActor));
        ActorInstanceIdentity indoorIdentity = indoorActor!.Actor.Get<ActorInstanceIdentity>();
        Assert.Equal(outdoorIdentity.SpawnerId, indoorIdentity.SpawnerId);
        Assert.Equal(outdoorIdentity.TemplateId, indoorIdentity.TemplateId);
        Assert.Equal(outdoorIdentity.Race, indoorIdentity.Race);
        Assert.Equal(outdoorIdentity.Gender, indoorIdentity.Gender);
        Assert.Equal(outdoorIdentity.FactionId, indoorIdentity.FactionId);
        ActorOverlayComponent indoorOverlay = indoorActor.Actor.Get<ActorOverlayComponent>();
        Assert.True(indoorOverlay.Met);
        Assert.Equal(outdoorFamily, indoorOverlay.FamilyId);
        Assert.Contains(indoorOverlay.Vouches, v => v.ReferrerDurableId == vouch.ReferrerDurableId && v.ReferrerName == "Aldric");
        Assert.True(session.State.ActorLedger.TryGet(targetId, out ActorLedgerEntry? indoorLedger));
        Assert.Equal(12, indoorLedger!.Social?.DispositionDelta);

        // Untouched scheduled visitors are materialized too, yet write ZERO save bytes!
        long[] untouchedVisitorIds = [.. plannedTavernVisitors.Where(v => v.DurableId != targetId).Select(v => v.DurableId)];
        Assert.NotEmpty(untouchedVisitorIds);
        foreach (long untouchedId in untouchedVisitorIds)
            Assert.True(session.State.Actors.TryGet(untouchedId, out _), $"Expected untouched visitor {untouchedId} to be live in tavern.");

        RulesetSavePayload indoorSaveBytes = session.CaptureSave();
        DaggerfallSavePayload indoorSave = DaggerfallSavePayload.Read(indoorSaveBytes);
        foreach (long untouchedId in untouchedVisitorIds)
        {
            Assert.DoesNotContain(indoorSave.ActorLedger, e => e.DurableId == untouchedId);
            Assert.DoesNotContain(indoorSave.ActorInventories, i => i.EntityId == untouchedId);
        }

        // The touched walker saves ONLY sparse Social/Referral diff (Location == null, Pose == null — NOT permanently locked to tavern!).
        ActorLedgerEntry savedTarget = Assert.Single(indoorSave.ActorLedger, e => e.DurableId == targetId);
        Assert.Null(savedTarget.Location);
        Assert.Null(savedTarget.Pose);
        Assert.True(savedTarget.Social?.Met);
        Assert.Equal(12, savedTarget.Social?.DispositionDelta);

        // Save/load while inside the tavern restores both touched and untouched scheduled visitors cleanly.
        using DaggerfallSession restoredInTavern = DaggerfallSession.Restore(engine.Context, composition, indoorSaveBytes);
        Assert.True(restoredInTavern.State.Actors.TryGet(targetId, out ActorState? restoredIndoor));
        Assert.True(restoredIndoor!.Actor.Get<ActorOverlayComponent>().Met);
        Assert.Equal(outdoorFamily, restoredIndoor.Actor.Get<ActorOverlayComponent>().FamilyId);
        Assert.Contains(restoredIndoor.Actor.Get<ActorOverlayComponent>().Vouches, v => v.ReferrerDurableId == vouch.ReferrerDurableId);
        foreach (long untouchedId in untouchedVisitorIds)
            Assert.True(restoredInTavern.State.Actors.TryGet(untouchedId, out _));

        // Weather clears to Sunny; step back outside -> exact same DurableId returns outdoors with zero duplicates and intact home coordinates!
        restoredInTavern.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        restoredInTavern.ResetScheduleWeatherBaseline();
        Assert.True(restoredInTavern.TryTransitionTo(exterior.ProfileKey));
        ActorState returnedOutdoor = Assert.Single(restoredInTavern.State.Actors.All, a => a.DurableId == targetId);
        Assert.Single(restoredInTavern.State.Npcs.All, n => n.DurableId == targetId);
        Assert.True(returnedOutdoor.Actor.Get<ActorOverlayComponent>().Met);
        Assert.Equal(outdoorFamily, returnedOutdoor.Actor.Get<ActorOverlayComponent>().FamilyId);
        Assert.Contains(returnedOutdoor.Actor.Get<ActorOverlayComponent>().Vouches, v => v.ReferrerDurableId == vouch.ReferrerDurableId);
        // Must stay at exterior home coordinates rather than getting overwritten by the tavern's interior stand point.
        Assert.Equal(outdoorBaselinePos, returnedOutdoor.Actor.Get<ActorOriginComponent>().BaselinePose);
    }

    [Fact]
    public void Off_hours_tavern_mixer_routes_closed_shop_proprietors_suppresses_closed_shop_and_pins_open_questor_and_office_holders()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord town = definitions.Locations.Records.Single(r => r.Id.Region == 17 && r.Name == "Daggerfall" && r.Exterior is not null);
        DaggerfallSiteBuildingSource[] buildings = [.. town.Exterior!.Buildings.Values];

        // 1. Midday (12:00): all regular shops are open; ZERO open-hours shopkeepers leave their counters for taverns.
        DaggerfallTownShelterAssignment midday = DaggerfallScheduleShelterResolver.ResolveTown(
            worldSocialSeed: 42UL,
            definitions,
            town,
            buildings,
            exteriorPlacements: [],
            minuteOfDay: 12 * 60,
            weather: DaggerfallWeatherKind.Sunny,
            weatherElapsed: DaggerfallWeatherShelterElapsed.Settled);
        Assert.Empty(midday.SuppressedStaticSlotsByBuilding);
        Assert.Empty(midday.AssignedBuildingByActor);

        // 2. Evening mixer (20:30): closed shops (Armorer/Clothing/Gem/Bank/Palace/etc.) route a deterministic slice
        //    to local taverns, suppressing their home counter slots so zero duplicate bodies can exist!
        DaggerfallTownShelterAssignment evening = DaggerfallScheduleShelterResolver.ResolveTown(
            worldSocialSeed: 42UL,
            definitions,
            town,
            buildings,
            exteriorPlacements: [],
            minuteOfDay: 20 * 60 + 30,
            weather: DaggerfallWeatherKind.Sunny,
            weatherElapsed: DaggerfallWeatherShelterElapsed.Settled);
        Assert.NotEmpty(evening.SuppressedStaticSlotsByBuilding);
        Assert.NotEmpty(evening.AssignedBuildingByActor);

        // Verify zero duplicate actor assignments across every building in town.
        long[] allAssignedIds = [.. evening.VisitorsByBuilding.Values.SelectMany(list => list).Select(v => v.DurableId)];
        Assert.Equal(allAssignedIds.Length, allAssignedIds.Distinct().Count());
        foreach (DaggerfallScheduledVisitor visitor in evening.VisitorsByBuilding.Values.SelectMany(v => v))
        {
            Assert.False(visitor.IsExteriorWalker);
            Assert.Equal(DaggerfallScheduleBuildingTarget.Tavern, visitor.Decision.BuildingTarget);
            // Every off-hours static visitor in a tavern must be suppressed from its own home building.
            Assert.Contains(evening.SuppressedStaticSlotsByBuilding.Values, set => set.Contains(visitor.DurableId));
        }

        // 3. Pinning exclusions: marking a closed-building visitor as an active questor or #11087 office holder
        //    must keep them pinned at their home post (not in tavern, not suppressed from home building).
        DaggerfallScheduledVisitor[] picked = [.. evening.VisitorsByBuilding.Values.SelectMany(v => v).Take(2)];
        Assert.Equal(2, picked.Length);
        long questorId = picked[0].DurableId;
        long officeHolderId = picked[1].DurableId;

        DaggerfallTownShelterAssignment pinnedEvening = DaggerfallScheduleShelterResolver.ResolveTown(
            worldSocialSeed: 42UL,
            definitions,
            town,
            buildings,
            exteriorPlacements: [],
            minuteOfDay: 20 * 60 + 30,
            weather: DaggerfallWeatherKind.Sunny,
            weatherElapsed: DaggerfallWeatherShelterElapsed.Settled,
            isActiveQuestor: id => id == questorId,
            isOfficeHolder: id => id == officeHolderId);
        Assert.False(pinnedEvening.AssignedBuildingByActor.ContainsKey(questorId));
        Assert.False(pinnedEvening.AssignedBuildingByActor.ContainsKey(officeHolderId));
        Assert.DoesNotContain(pinnedEvening.SuppressedStaticSlotsByBuilding.Values, set => set.Contains(questorId));
        Assert.DoesNotContain(pinnedEvening.SuppressedStaticSlotsByBuilding.Values, set => set.Contains(officeHolderId));

        // 4. Real session crossing check: pick one closed building in Charing that sends a proprietor to the tavern at 20:30,
        //    enter the tavern -> proprietor is live there; enter the closed shop -> proprietor is suppressed (zero duplicates!).
        DaggerfallSiteRecord charing = definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallSiteBuildingSource[] charingBuildings = [.. charing.Exterior!.Buildings.Values];
        // Find a minute in 19:30..22:30 where at least one Charing closed-building slot visits a tavern.
        DaggerfallTownShelterAssignment? charingEvening = null;
        int eveningMinute = 20 * 60 + 30;
        for (int m = 19 * 60 + 45; m <= 22 * 60 + 15; m += 15)
        {
            DaggerfallTownShelterAssignment probe = DaggerfallScheduleShelterResolver.ResolveTown(
                worldSocialSeed: 0UL, definitions, charing, charingBuildings, [], m,
                DaggerfallWeatherKind.Sunny, DaggerfallWeatherShelterElapsed.Settled);
            if (probe.SuppressedStaticSlotsByBuilding.Count > 0)
            {
                charingEvening = probe;
                eveningMinute = m;
                break;
            }
        }
        Assert.NotNull(charingEvening);
        KeyValuePair<DaggerfallSiteBuildingId, IReadOnlySet<long>> suppressedShop = charingEvening!.SuppressedStaticSlotsByBuilding.First();
        long awayProprietorId = suppressedShop.Value.First();
        DaggerfallSiteBuildingId destTavernId = charingEvening.AssignedBuildingByActor[awayProprietorId];

        DaggerfallSiteProfile shopProfile = Assembled(DaggerfallWorldProfileIds.Interior(Charing, suppressedShop.Key));
        DaggerfallSiteProfile tavernProfile = Assembled(DaggerfallWorldProfileIds.Interior(Charing, destTavernId));
        DaggerfallSiteProfiles profiles = new([shopProfile, tavernProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, shopProfile);
        PopulateContent(content, tavernProfile);
        SpatialFake spatial = SpatialFake.Create(shopProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, shopProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallCalendar startCal = session.Calendar;
        long deltaSeconds = checked((eveningMinute - (startCal.Hour * 60 + startCal.Minute)) * 60L);
        if (deltaSeconds <= 0) deltaSeconds += 24L * 3600L;
        session.AdvanceElapsedTime(deltaSeconds);

        // Re-enter the tavern at eveningMinute: proprietor is live in tavern.
        Assert.True(session.TryTransitionTo(tavernProfile.ProfileKey));
        Assert.True(session.State.Actors.TryGet(awayProprietorId, out _));

        // Enter the closed shop at the same eveningMinute: proprietor is suppressed behind the counter (zero duplicates!).
        Assert.True(session.TryTransitionTo(shopProfile.ProfileKey));
        Assert.False(session.State.Actors.TryGet(awayProprietorId, out _));
    }

    [Fact]
    public void Per_tavern_shelter_capacity_cap_prioritizes_household_regulars_and_overflows_to_family_home_and_offstage()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord charing = definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallSiteBuildingSource tavern = charing.Exterior!.Buildings.Values.First(b => b.Source.BuildingType == 15);
        DaggerfallSiteBuildingSource[] residences = [.. charing.Exterior.Buildings.Values
            .Where(b => DaggerfallScheduleShelterResolver.IsResidenceBuildingType(b.Source.BuildingType))
            .OrderBy(b => b.Id.BlockY).ThenBy(b => b.Id.BlockX).ThenBy(b => b.Id.Index)
            .Take(2)];
        Assert.Equal(2, residences.Length);

        DaggerfallSiteBuildingSource[] tinyTown = [tavern, residences[0], residences[1]];
        const int tavernCap = 4;
        const int homeCap = 3;

        // 64 exterior storm refugees: half on the tavern's own block (same-block neighbors / regulars),
        // half 5 blocks away; assigned across household buckets h0..h3.
        DaggerfallPopulationPlacement[] refugees =
        [
            .. Enumerable.Range(0, 64).Select(i =>
            {
                bool isSameBlock = i < 32;
                int bx = isSameBlock ? tavern.Id.BlockX : tavern.Id.BlockX + 5;
                int by = isSameBlock ? tavern.Id.BlockY : tavern.Id.BlockY + 5;
                float x = bx * 128f + 10f + (i % 16);
                float z = -(by * 128f + 10f);
                string familyId = $"fam:local:r{Charing.Region}:s{Charing.Index}:h{i % 4}";
                return new DaggerfallPopulationPlacement(
                    $"population/storm-cap/dynamic/{i:D3}",
                    new WorldPoint(x, 0f, z),
                    210,
                    4,
                    0,
                    (byte)(i % 2 == 1 ? 0x20 : 0),
                    (ushort)(2000 + i))
                {
                    IsGuard = false,
                    FamilyId = familyId,
                };
            }),
        ];

        DaggerfallTownShelterAssignment assignment = DaggerfallScheduleShelterResolver.ResolveTown(
            worldSocialSeed: 77UL,
            definitions,
            charing,
            tinyTown,
            refugees,
            minuteOfDay: 12 * 60,
            weather: DaggerfallWeatherKind.Thunder,
            weatherElapsed: DaggerfallWeatherShelterElapsed.Settled,
            capacityForBuilding: b => b.Source.BuildingType == 15 ? tavernCap : homeCap);

        IReadOnlyList<DaggerfallScheduledVisitor> inTavern = assignment.VisitorsFor(tavern.Id);
        IReadOnlyList<DaggerfallScheduledVisitor> inHouse0 = assignment.VisitorsFor(residences[0].Id);
        IReadOnlyList<DaggerfallScheduledVisitor> inHouse1 = assignment.VisitorsFor(residences[1].Id);

        // 1. Tavern strictly capped at <= tavernCap (4), residences strictly capped at <= homeCap (3).
        Assert.Equal(tavernCap, inTavern.Count);
        Assert.InRange(inHouse0.Count, 1, homeCap);
        Assert.InRange(inHouse1.Count, 1, homeCap);

        // 2. Same-block neighbors / regulars are prioritized ahead of distant-block non-regulars in the tavern.
        //    (Note: with 1 tavern, household bucket % 1 == 0 makes everyone Tier 0 unless non-family, so verify
        //    a second test with 2 taverns below as well; here verify zero overflow stacking and disjoint sets.)
        HashSet<long> allInBuildings = [.. assignment.AssignedBuildingByActor.Keys];
        Assert.Equal(inTavern.Count + inHouse0.Count + inHouse1.Count, allInBuildings.Count);
        Assert.Empty(allInBuildings.Intersect(assignment.OffStageOverflowActorIds));
        Assert.Equal(64, allInBuildings.Count + assignment.OffStageOverflowActorIds.Count);

        // 3. Household affinity: every resident placed in House 0 shares even household buckets (h0/h2 -> % 2 == 0),
        //    and every resident placed in House 1 shares odd household buckets (h1/h3 -> % 2 == 1).
        foreach (DaggerfallScheduledVisitor v in inHouse0)
            Assert.True(v.FamilyId!.EndsWith(":h0", StringComparison.Ordinal) || v.FamilyId.EndsWith(":h2", StringComparison.Ordinal));
        foreach (DaggerfallScheduledVisitor v in inHouse1)
            Assert.True(v.FamilyId!.EndsWith(":h1", StringComparison.Ordinal) || v.FamilyId.EndsWith(":h3", StringComparison.Ordinal));

        // 4. Neighbor / regular priority check: with 2 taverns, same-block neighbors of Tavern A beat distant non-regulars.
        DaggerfallSiteBuildingSource tavernA = tavern with { Id = new(0, 0, 0) };
        DaggerfallSiteBuildingSource tavernB = tavern with { Id = new(4, 4, 1) };
        DaggerfallPopulationPlacement[] mixedNeighbors =
        [
            // 20 same-block neighbors (block 0,0, h0 regular for tavernA -> ~8 tavern applicants)
            // + 20 distant non-regulars (block 1,1, h1 regular for tavernB -> ~8 tavern applicants for tavernA).
            .. Enumerable.Range(0, 40).Select(i =>
            {
                bool isLocalNeighbor = i < 20;
                int bx = isLocalNeighbor ? 0 : 1;
                int by = isLocalNeighbor ? 0 : 1;
                return new DaggerfallPopulationPlacement(
                    $"population/priority/dynamic/{i:D3}",
                    new WorldPoint(bx * 128f + 5f + (i % 16), 0f, -(by * 128f + 5f)),
                    210,
                    4,
                    0,
                    0,
                    (ushort)(3000 + i))
                {
                    FamilyId = $"fam:local:r{Charing.Region}:s{Charing.Index}:h{(isLocalNeighbor ? 0 : 1)}",
                };
            }),
        ];
        DaggerfallTownShelterAssignment priorityPlan = DaggerfallScheduleShelterResolver.ResolveTown(
            worldSocialSeed: 77UL,
            definitions,
            charing,
            [tavernA, tavernB],
            mixedNeighbors,
            minuteOfDay: 12 * 60,
            weather: DaggerfallWeatherKind.Thunder,
            weatherElapsed: DaggerfallWeatherShelterElapsed.Settled,
            capacityForBuilding: _ => 4);
        IReadOnlyList<DaggerfallScheduledVisitor> tavernAVisitors = priorityPlan.VisitorsFor(tavernA.Id);
        Assert.Equal(4, tavernAVisitors.Count);
        Assert.All(tavernAVisitors, v => Assert.True(v.FamilyId!.EndsWith(":h0", StringComparison.Ordinal),
            "Expected all 4 same-block household regulars to win Tavern A slots ahead of distant non-regulars."));
    }

    [Fact]
    public void Off_frustum_and_door_proximity_live_grace_prevents_same_frame_popout_while_immediate_reconcile_hides_immediately()
    {
        DaggerfallSiteRecord town = TestPayload.Definitions.Locations.Records.Single(s => s.Id.Region == 17 && s.Name == "Daggerfall" && s.Exterior is not null);
        // Player at origin facing -Z (YawRadians = 0):
        //   001: 10m directly in front (0, 0, -10) -> VISIBLE in camera frustum (< 25m)
        //   002: 10m directly behind (0, 0, +10)   -> BEHIND camera (out of frustum)
        //   003: 35m in front (0, 0, -35)          -> TOO FAR (> 25m frustum grace range)
        DaggerfallPopulationPlacement[] placements =
        [
            new("population/grace/dynamic/001", new WorldPoint(0f, 0f, -10f), 210, 4, 0, 0, 1001),
            new("population/grace/dynamic/002", new WorldPoint(0f, 0f, 10f), 210, 4, 0, 0, 1002),
            new("population/grace/dynamic/003", new WorldPoint(0f, 0f, -35f), 210, 4, 0, 0, 1003),
        ];

        using CivilianStage stage = new(town, placements);
        // Attach a real exterior doorway portal nearby at (6, 0, -12) so the retiring walker targets it.
        DaggerfallSitePortal doorway = new("door/tavern", new WorldPoint(6f, 0f, -12f), 1.5f, "interior/17/0/0/0/0");
        DaggerfallSiteProfile profileWithDoor = new(
            stage.Profile.Project,
            stage.Profile.Geometry,
            stage.Profile.WorldAppearance,
            stage.Profile.InitialLook,
            stage.Profile.Materials,
            stage.Profile.ActorSprites,
            stage.Profile.MobileSprites,
            stage.Profile.Audio,
            stage.Profile.ClassicPresentation,
            stage.Profile.Site,
            profileKind: DaggerfallWorldProfileKind.Exterior,
            logicalProfileId: stage.Profile.ProfileKey.LogicalId,
            portals: [doorway],
            billboardSprites: stage.Profile.BillboardSprites,
            population: placements);

        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), yawRadians: 0f, pitchRadians: 0f);
        bool outdoorsAllowed = true;
        DaggerfallSourcePopulation population = new(
            stage.Npcs,
            stage.Actors,
            player,
            TestPayload.Definitions,
            DaggerfallCivilianWanderTuning.Classic,
            stage.Civilians,
            () => profileWithDoor,
            () => profileWithDoor.ProfileKey,
            () => stage.Appearance,
            () => true,
            () => true,
            p => p,
            p => p,
            _ => town,
            () => true,
            () => false,
            _ => false,
            (actor, _, _, dt) =>
            {
                // Move actor 2 m/s toward its staged retirement goal during tests.
                if (actor.Wander.Route is { Count: > 0 } route)
                {
                    WorldPoint target = route[0];
                    Vector3 delta = new(target.X - actor.Position.X, 0f, target.Z - actor.Position.Z);
                    float len = delta.Length();
                    if (len > 1e-3f)
                    {
                        float step = MathF.Min(len, 2f * dt);
                        Vector3 next = actor.Position.ToVector() + (delta / len) * step;
                        actor.ApplyPose(new ActorPose(WorldPoint.From(next), ActorHeading.Yaw(delta)));
                    }
                }
            },
            _ => { },
            isPlacementScheduledOutdoors: (_, _, _, _) => outdoorsAllowed);

        // 1. Midday sunny: all 3 walkers materialize.
        population.ReconcileImmediate();
        long visibleId = stage.Npcs.All.Single(n => n.StableKey == "population/grace/dynamic/001").DurableId;
        long behindId = stage.Npcs.All.Single(n => n.StableKey == "population/grace/dynamic/002").DurableId;
        long farId = stage.Npcs.All.Single(n => n.StableKey == "population/grace/dynamic/003").DurableId;
        Assert.Equal(DaggerfallNpcPresence.Active, stage.Npcs.Require(visibleId).Presence);
        Assert.Equal(DaggerfallNpcPresence.Active, stage.Npcs.Require(behindId).Presence);
        Assert.Equal(DaggerfallNpcPresence.Active, stage.Npcs.Require(farId).Presence);

        // 2. Live tick flips schedule indoors: behind & far (>25m) hide immediately; visible walker (<25m in front)
        //    enters live grace (zero same-frame pop-out!) and stages toward the nearby doorway portal!
        outdoorsAllowed = false;
        population.Reconcile(immediate: false);
        Assert.Equal(DaggerfallNpcPresence.Hidden, stage.Npcs.Require(behindId).Presence);
        Assert.Equal(DaggerfallNpcPresence.Hidden, stage.Npcs.Require(farId).Presence);
        Assert.Equal(DaggerfallNpcPresence.Active, stage.Npcs.Require(visibleId).Presence);
        Assert.True(population.IsRetiring(visibleId));
        ActorState visibleActor = stage.Actors.Get(visibleId);
        Assert.NotNull(visibleActor.Wander.Route);
        Assert.Equal(doorway.Position, visibleActor.Wander.Route![0]);

        // 3. Stepping moves the retiring walker toward the doorway; once within RetireDoorArrivalMeters (2.5m), it hides cleanly!
        for (ulong step = 1; step <= 6; step++)
            population.Step(step, 0.5f);
        Assert.False(population.IsRetiring(visibleId));
        Assert.Equal(DaggerfallNpcPresence.Hidden, stage.Npcs.Require(visibleId).Presence);
        Assert.Null(visibleActor.Wander.Route);

        // 4. Contrast with immediate reconcile (scene load / rest / fast-forward): even a walker 10m right in front of
        //    the player hides immediately with zero grace delay.
        outdoorsAllowed = true;
        visibleActor.ApplyPose(new ActorPose(new WorldPoint(0f, 0f, -10f), 0f));
        population.ReconcileImmediate();
        Assert.Equal(DaggerfallNpcPresence.Active, stage.Npcs.Require(visibleId).Presence);

        outdoorsAllowed = false;
        population.ReconcileImmediate();
        Assert.False(population.IsRetiring(visibleId));
        Assert.Equal(DaggerfallNpcPresence.Hidden, stage.Npcs.Require(visibleId).Presence);
    }

    [Fact]
    public void First_encounter_indoors_preserves_home_social_binding_survives_save_load_and_retires_on_subsequent_outdoor_shelter_flip()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord charing = definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallSiteBuildingSource[] charingBuildings = [.. charing.Exterior!.Buildings.Values];
        DaggerfallSiteBuildingSource tavernBuilding = charingBuildings
            .OrderBy(b => b.Id.BlockY).ThenBy(b => b.Id.BlockX).ThenBy(b => b.Id.Index)
            .First(b => b.Source.BuildingType == 15);

        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile tavern = Assembled(DaggerfallWorldProfileIds.Interior(Charing, tavernBuilding.Id));
        DaggerfallSiteProfiles profiles = new([exterior, tavern]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, tavern);
        SpatialFake spatial = SpatialFake.Create(tavern.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, tavern, DaggerfallTuning.Defaults) { Profiles = profiles };

        // 1. Start directly INSIDE the tavern during a settled midday storm (never visited the exterior first!).
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Thunder);
        session.AdvanceElapsedTime(12 * 60 * 60); // midday settled storm
        session.ResetScheduleWeatherBaseline();
        session.ReconcileInteriorScheduledVisitors(tavern, session.Sites.Projection.Appearance);

        DaggerfallTownShelterAssignment stormPlan = session.ResolveTownShelterAssignment(Charing, tavern.BillboardSprites);
        DaggerfallScheduledVisitor walkerVisitor = stormPlan.VisitorsFor(tavernBuilding.Id).First(v => v.IsExteriorWalker);
        long walkerId = walkerVisitor.DurableId;

        Assert.True(session.State.Actors.TryGet(walkerId, out ActorState? indoorWalker));
        DaggerfallNpc indoorNpc = session.State.Npcs.Require(walkerId);
        // Social origin must be the canonical exterior town binding (empty building, null ProfileId), NOT the tavern!
        DaggerfallNpcSite expectedExteriorSite = new(Charing.Region, charing.Name, string.Empty);
        Assert.Equal(expectedExteriorSite, indoorNpc.Site);
        Assert.Equal(tavern.ProfileKey, indoorNpc.Profile);

        // Meet this walker for the first time indoors and persist via save/load.
        indoorWalker!.Actor.Get<ActorOverlayComponent>().Met = true;
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            walkerId,
            indoorWalker.Actor.Get<ActorOriginComponent>().Origin,
            Social: DaggerfallSessionPersistence.ToSocialDiff(indoorNpc) with { Met = true, DispositionDelta = 8 }));

        RulesetSavePayload saveBytes = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saveBytes);
        Assert.Equal(expectedExteriorSite, restored.State.Npcs.Require(walkerId).Site);

        // 2. Weather clears to Sunny; walk out to the exterior near this walker -> active outdoors with Met + home binding.
        restored.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        restored.ResetScheduleWeatherBaseline();
        Assert.True(restored.TryTransitionTo(exterior.ProfileKey));
        DaggerfallPopulationPlacement placement = exterior.Population.Single(p => p.Id == walkerVisitor.HomeSlotId);
        restored.State.PlayerControl.Restore(restored.Exterior.ProfileToLocal(placement.Position), restored.State.PlayerControl.Motion);
        restored.Population.ReconcileImmediate();

        Assert.True(restored.State.Actors.TryGet(walkerId, out ActorState? outdoorWalker));
        Assert.True(outdoorWalker!.Actor.Get<ActorOverlayComponent>().Met);
        Assert.Equal(DaggerfallNpcPresence.Active, restored.State.Npcs.Require(walkerId).Presence);
        Assert.Equal(expectedExteriorSite, restored.State.Npcs.Require(walkerId).Site);

        // 3. Weather flips back to settled Thunder outdoors -> SourcePopulation recognizes them via home Site and hides them!
        restored.LocalWeather.State.Force(DaggerfallWeatherKind.Thunder);
        restored.ResetScheduleWeatherBaseline();
        restored.Population.ReconcileImmediate();
        Assert.Equal(DaggerfallNpcPresence.Hidden, restored.State.Npcs.Require(walkerId).Presence);

        // 4. First-encountered off-hours closed-shop proprietor inside a tavern must ALSO carry their home shop's social binding!
        DaggerfallTownShelterAssignment? charingEvening = null;
        int eveningMinute = 20 * 60 + 30;
        for (int m = 19 * 60 + 45; m <= 22 * 60 + 15; m += 15)
        {
            DaggerfallTownShelterAssignment probe = DaggerfallScheduleShelterResolver.ResolveTown(
                session.WorldSocialSeed, definitions, charing, charingBuildings, [], m,
                DaggerfallWeatherKind.Sunny, DaggerfallWeatherShelterElapsed.Settled);
            if (probe.SuppressedStaticSlotsByBuilding.Count > 0)
            {
                charingEvening = probe;
                eveningMinute = m;
                break;
            }
        }
        Assert.NotNull(charingEvening);
        KeyValuePair<DaggerfallSiteBuildingId, IReadOnlySet<long>> homeShopEntry = charingEvening!.SuppressedStaticSlotsByBuilding.First();
        long proprietorId = homeShopEntry.Value.First();
        DaggerfallSiteBuildingId mixerTavernId = charingEvening.AssignedBuildingByActor[proprietorId];
        DaggerfallSiteProfile mixerTavern = Assembled(DaggerfallWorldProfileIds.Interior(Charing, mixerTavernId));
        DaggerfallSiteProfiles mixerProfiles = new([mixerTavern]);
        List<string> mixerReleases = [];
        ContentFake mixerContent = new(mixerReleases);
        PopulateContent(mixerContent, mixerTavern);
        SpatialFake mixerSpatial = SpatialFake.Create(mixerTavern.Geometry.Spatial[0].Sha256, mixerReleases);
        EngineContextFake mixerEngine = EngineContextFake.Create(mixerContent, mixerSpatial.Service, new AppearanceFake(mixerReleases));
        DaggerfallSessionComposition mixerComp = new(definitions, mixerTavern, DaggerfallTuning.Defaults) { Profiles = mixerProfiles };

        using DaggerfallSession mixerSession = DaggerfallSession.StartNew(mixerEngine.Context, mixerComp);
        DaggerfallCalendar startCal = mixerSession.Calendar;
        long deltaSeconds = checked((eveningMinute - (startCal.Hour * 60 + startCal.Minute)) * 60L);
        if (deltaSeconds <= 0) deltaSeconds += 24L * 3600L;
        mixerSession.AdvanceElapsedTime(deltaSeconds);
        mixerSession.ReconcileInteriorScheduledVisitors(mixerTavern, mixerSession.Sites.Projection.Appearance);

        Assert.True(mixerSession.State.Actors.TryGet(proprietorId, out _));
        DaggerfallNpc proprietorNpc = mixerSession.State.Npcs.Require(proprietorId);
        DaggerfallWorldProfileKey expectedShopProfile = DaggerfallWorldProfileIds.Interior(Charing, homeShopEntry.Key);
        DaggerfallNpcSite expectedShopSite = new(Charing.Region, charing.Name,
            $"{homeShopEntry.Key.BlockX}/{homeShopEntry.Key.BlockY}/{homeShopEntry.Key.Index}",
            expectedShopProfile.LogicalId);
        Assert.Equal(expectedShopSite, proprietorNpc.Site);
        Assert.Equal(mixerTavern.ProfileKey, proprietorNpc.Profile);
    }
}
