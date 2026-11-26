using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Time-jump and catch-up schedule invariants across rest, wait, fast-forward, expedition arrival,
/// and storm-to-clear recovery with zero orphaned actors, zero duplicate bodies, and zero save bytes
/// for untouched NPCs (#11085 / #11108).
/// </summary>
public sealed class DaggerfallScheduleCatchUpTests
{
    [Fact]
    public void Rest_and_wait_inside_tavern_reconciles_scheduled_visitors_without_orphans_duplicates_or_untouched_save_bytes()
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
        SpatialFake spatial = SpatialFake.Create(tavern.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, tavern, DaggerfallTuning.Defaults) { Profiles = profiles };

        // 1. Start inside tavern and jump to evening mixer (20:30, sunny): scheduled visitors appear.
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        AdvanceToMinuteOfDay(session, 20 * 60 + 30);

        DaggerfallTownShelterAssignment eveningPlan = session.ResolveTownShelterAssignment(Charing, tavern.BillboardSprites);
        IReadOnlyList<DaggerfallScheduledVisitor> eveningVisitors = eveningPlan.VisitorsFor(tavernBuilding.Id);
        Assert.NotEmpty(eveningVisitors);
        foreach (DaggerfallScheduledVisitor visitor in eveningVisitors)
            Assert.True(session.State.Actors.TryGet(visitor.DurableId, out _), $"Expected evening visitor {visitor.DurableId} in tavern.");

        // Meet one exterior walker visitor and leave all other evening visitors untouched.
        DaggerfallScheduledVisitor metVisitor = eveningVisitors.First(v => v.IsExteriorWalker);
        long metId = metVisitor.DurableId;
        long[] untouchedIds = [.. eveningVisitors.Where(v => v.DurableId != metId).Select(v => v.DurableId)];
        Assert.NotEmpty(untouchedIds);

        ActorState metActor = session.State.Actors.Get(metId);
        metActor.Actor.Get<ActorOverlayComponent>().Met = true;
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            metId,
            metActor.Actor.Get<ActorOriginComponent>().Origin,
            Social: DaggerfallSessionPersistence.ToSocialDiff(session.State.Npcs.Require(metId)) with
            {
                Met = true,
                DispositionDelta = 10,
            }));

        // 2. Rest inside tavern overnight via ApplyRest (13 hours -> ~09:30 next morning):
        //    ALL evening visitors must be unloaded cleanly (zero orphaned actors!), untouched visitors write 0 save bytes,
        //    and the met visitor preserves its sparse Social diff without permanent AtSite relocation.
        DaggerfallRestResult restResult = session.ApplyRest(
            new DaggerfallRestRequest(DaggerfallRestMode.Timed, Hours: 13),
            new DaggerfallRestEligibility(Allowed: true),
            seconds =>
            {
                DaggerfallCalendarAdvance adv = session.AdvanceElapsedTime(seconds, resting: true, idleFatigue: false, completeTimeIncrease: false);
                return new DaggerfallRestTimeAdvance(seconds, adv.AppliedSeconds);
            });
        Assert.True(restResult.Accepted);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        AdvanceToMinuteOfDay(session, 10 * 60);

        foreach (DaggerfallScheduledVisitor visitor in eveningVisitors)
            Assert.False(session.State.Actors.TryGet(visitor.DurableId, out _), $"Stale evening visitor {visitor.DurableId} remained orphaned after resting to sunny morning.");

        DaggerfallSavePayload afterMorningRest = DaggerfallSavePayload.Read(session.CaptureSave());
        foreach (long untouchedId in untouchedIds)
        {
            Assert.DoesNotContain(afterMorningRest.ActorLedger, e => e.DurableId == untouchedId);
            Assert.DoesNotContain(afterMorningRest.ActorInventories, i => i.EntityId == untouchedId);
        }
        ActorLedgerEntry savedMet = Assert.Single(afterMorningRest.ActorLedger, e => e.DurableId == metId);
        Assert.True(savedMet.Social?.Met);
        Assert.Equal(10, savedMet.Social?.DispositionDelta);
        Assert.Null(savedMet.Location);
        Assert.Null(savedMet.Pose);

        // 3. Wait inside the same tavern until 20:30 again: evening visitors cleanly re-materialize with zero duplicates.
        AdvanceToMinuteOfDay(session, 20 * 60 + 30);
        foreach (DaggerfallScheduledVisitor visitor in eveningVisitors)
            Assert.Single(session.State.Actors.All, a => a.DurableId == visitor.DurableId);
        Assert.True(session.State.Actors.Get(metId).Actor.Get<ActorOverlayComponent>().Met);

        // 4. Advance to midday sunny and step outside: met visitor returns outdoors cleanly with zero duplicates.
        AdvanceToMinuteOfDay(session, 12 * 60);
        Assert.True(session.TryTransitionTo(exterior.ProfileKey));
        DaggerfallPopulationPlacement placement = exterior.Population.Single(p => p.Id == metVisitor.HomeSlotId);
        session.State.PlayerControl.Restore(session.Exterior.ProfileToLocal(placement.Position), session.State.PlayerControl.Motion);
        session.Population.ReconcileImmediate();

        ActorState outdoorMet = Assert.Single(session.State.Actors.All, a => a.DurableId == metId);
        Assert.Single(session.State.Npcs.All, n => n.DurableId == metId);
        Assert.True(outdoorMet.Actor.Get<ActorOverlayComponent>().Met);
    }

    [Fact]
    public void Waiting_inside_closed_shop_across_opening_hours_restores_and_suppresses_proprietor_cleanly()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord charing = definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallSiteBuildingSource[] buildings = [.. charing.Exterior!.Buildings.Values];

        DaggerfallTownShelterAssignment? charingEvening = null;
        int eveningMinute = 20 * 60 + 30;
        for (int m = 19 * 60 + 45; m <= 22 * 60 + 15; m += 15)
        {
            DaggerfallTownShelterAssignment probe = DaggerfallScheduleShelterResolver.ResolveTown(
                0UL, definitions, charing, buildings, [], m,
                DaggerfallWeatherKind.Sunny, DaggerfallWeatherShelterElapsed.Settled);
            if (probe.SuppressedStaticSlotsByBuilding.Count > 0)
            {
                charingEvening = probe;
                eveningMinute = m;
                break;
            }
        }
        Assert.NotNull(charingEvening);
        KeyValuePair<DaggerfallSiteBuildingId, IReadOnlySet<long>> shopEntry = charingEvening!.SuppressedStaticSlotsByBuilding.First();
        long proprietorId = shopEntry.Value.First();
        DaggerfallSiteProfile shopProfile = Assembled(DaggerfallWorldProfileIds.Interior(Charing, shopEntry.Key));
        DaggerfallSiteProfiles profiles = new([shopProfile]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, shopProfile);
        SpatialFake spatial = SpatialFake.Create(shopProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, shopProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);

        // 1. Wait to evening mixer minute: closed-shop proprietor departs for the tavern (unloaded, 0 save bytes).
        AdvanceToMinuteOfDay(session, eveningMinute);
        Assert.False(session.State.Actors.TryGet(proprietorId, out _));
        Assert.DoesNotContain(DaggerfallSavePayload.Read(session.CaptureSave()).ActorLedger, e => e.DurableId == proprietorId);

        // 2. Wait until midday open hours (12:00): proprietor returns behind the counter with zero duplicates & 0 save bytes.
        AdvanceToMinuteOfDay(session, 12 * 60);
        Assert.Single(session.State.Actors.All, a => a.DurableId == proprietorId);
        Assert.DoesNotContain(DaggerfallSavePayload.Read(session.CaptureSave()).ActorLedger, e => e.DurableId == proprietorId);

        // 3. Wait back to evening mixer minute: proprietor departs again cleanly.
        AdvanceToMinuteOfDay(session, eveningMinute);
        Assert.False(session.State.Actors.TryGet(proprietorId, out _));
    }

    [Fact]
    public void Expedition_arrival_and_storm_to_clear_recovery_reconcile_exterior_schedules_immediately_with_zero_save_bytes()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfiles profiles = new([exterior]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, exterior, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        AdvanceToMinuteOfDay(session, 12 * 60);

        DaggerfallNpc[] middayActive = [.. session.State.Npcs.All
            .Where(n => DaggerfallCivilians.IsSourcePopulation(n) && n.Presence == DaggerfallNpcPresence.Active)];
        Assert.NotEmpty(middayActive);
        long[] middayWalkerIds = [.. middayActive.Select(n => n.DurableId)];
        Assert.Equal(middayWalkerIds.Length, middayWalkerIds.Distinct().Count());

        // 1. Storm onset + elapsed fast-forward reconciles immediately (no lingering retiring walkers!).
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Thunder);
        session.AdvanceElapsedTime(20 * 60); // 20-minute elapsed jump settles storm immediately
        foreach (long id in middayWalkerIds)
            Assert.False(session.Population.IsRetiring(id));

        DaggerfallNpc[] stormActive = [.. session.State.Npcs.All
            .Where(n => DaggerfallCivilians.IsSourcePopulation(n) && n.Presence == DaggerfallNpcPresence.Active)];
        // Only guards remain active outdoors during a settled thunderstorm.
        Assert.All(stormActive, npc => Assert.Equal("guard", npc.Role));

        // 2. Storm-to-clear recovery: weather clears back to Sunny -> walkers recover with exact same DurableIds & 0 save bytes.
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        session.ResetScheduleWeatherBaseline();
        session.Population.ReconcileImmediate();

        DaggerfallNpc[] recovered = [.. session.State.Npcs.All
            .Where(n => DaggerfallCivilians.IsSourcePopulation(n) && n.Presence == DaggerfallNpcPresence.Active)];
        Assert.True(recovered.Length >= middayActive.Length);
        foreach (long id in middayWalkerIds)
            Assert.Single(session.State.Actors.All, a => a.DurableId == id);

        DaggerfallSavePayload clearSave = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.DoesNotContain(clearSave.ActorLedger, e => e.Location?.Kind != ActorLocationKind.OnOverlandBand);
        Assert.Empty(clearSave.ActorInventories);

        // 3. Same-profile expedition / recall arrival at night (02:00) via TryRelocatePlayer reconciles immediately!
        AdvanceToMinuteOfDay(session, 2 * 60);
        DaggerfallSiteAnchor arrivalAnchor = new("expedition-night", exterior.Project.PlayerPosition ?? default, 0f, 0f);
        Assert.True(session.Sites.TryRelocatePlayer(exterior.ProfileKey, arrivalAnchor));

        DaggerfallNpc[] nightActive = [.. session.State.Npcs.All
            .Where(n => DaggerfallCivilians.IsSourcePopulation(n) && n.Presence == DaggerfallNpcPresence.Active)];
        foreach (DaggerfallNpc npc in nightActive)
        {
            Assert.False(session.Population.IsRetiring(npc.DurableId));
            string sched = DaggerfallNpcSchedulePolicy.ResolveExteriorScheduleId(
                session.WorldSocialSeed, Charing, npc.StableKey, npc.Role == "guard", exterior.Population.Count);
            Assert.True(sched is DaggerfallNpcSchedulePolicy.CityGuardScheduleId or DaggerfallNpcSchedulePolicy.UnderworldNightScheduleId);
        }

        // Save/load across night arrival keeps zero save bytes for untouched townsfolk.
        DaggerfallSavePayload nightSave = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.DoesNotContain(nightSave.ActorLedger, e => e.Location?.Kind != ActorLocationKind.OnOverlandBand);
        Assert.Empty(nightSave.ActorInventories);
    }

    private static void AdvanceToMinuteOfDay(DaggerfallSession session, int targetMinuteOfDay)
    {
        DaggerfallCalendar cal = session.Calendar;
        int current = cal.Hour * 60 + cal.Minute;
        int deltaMinutes = targetMinuteOfDay - current;
        if (deltaMinutes <= 0)
            deltaMinutes += 24 * 60;
        session.AdvanceElapsedTime(deltaMinutes * 60L, idleFatigue: false);
    }
}
