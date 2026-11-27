using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused tests for #11139:
/// - Visible near-player schedule transitions (walkers walk to their #11104 shelter door, open it, cross inside,
///   and emerge from their home/shelter door in the morning / when weather clears).
/// - Residential home night routines (lights out, sleeping in real bed spots with zero save bytes for untouched
///   residents, stealth-aware waking response with lights on + bark + trespassing report in non-owned houses,
///   and calm behavior in player-owned houses).
/// </summary>
public sealed class VisibleScheduleTransitionsAndNightRoutinesTests
{
    [Fact]
    public void Near_player_walker_retires_to_assigned_shelter_door_opens_it_crosses_and_door_closes_behind()
    {
        DaggerfallSiteRecord town = TestPayload.Definitions.Locations.Records
            .Single(s => s.Id == Charing && s.Exterior is not null);
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));

        // Pick a real exterior door belonging to a building in Charing.
        DaggerfallRdbDoorDefinition targetDoor = exterior.Doors
            .First(d => d.ExteriorBuilding is not null && d.Kind == DaggerfallDoorKind.Normal);
        DaggerfallSiteBuildingId assignedBuilding = targetDoor.ExteriorBuilding!.Value;

        // Place one walker 6m in front of that door and the player 12m away watching.
        Vector3 outward = Vector3.Transform(Vector3.UnitZ, targetDoor.Rotation);
        Vector3 flatNormal = new(outward.X, 0f, outward.Z);
        flatNormal = flatNormal.LengthSquared() > 1e-4f ? Vector3.Normalize(flatNormal) : Vector3.UnitZ;
        WorldPoint doorThreshold = WorldPoint.From(targetDoor.Position + new Vector3(0f, targetDoor.BoundsMin.Y, 0f) + (flatNormal * 0.75f));
        WorldPoint walkerStart = new(doorThreshold.X + (flatNormal.X * 6f), doorThreshold.Y, doorThreshold.Z + (flatNormal.Z * 6f));
        WorldPoint playerPos = new(doorThreshold.X + (flatNormal.X * 14f), doorThreshold.Y, doorThreshold.Z + (flatNormal.Z * 14f));

        DaggerfallPopulationPlacement[] placements =
        [
            new("population/visible-retire/001", walkerStart, 210, 4, 0, 0, 2001),
        ];

        using CivilianStage stage = new(town, placements);
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
            doors: [targetDoor],
            profileKind: DaggerfallWorldProfileKind.Exterior,
            logicalProfileId: stage.Profile.ProfileKey.LogicalId,
            billboardSprites: stage.Profile.BillboardSprites,
            population: placements);

        // Lock the door to confirm resident entry swings it open and closes it without clearing its lock!
        DaggerfallRdbDoorDefinition lockedDoor = targetDoor with { StartingLockValue = 12 };
        using DaggerfallDoorRuntime doors = new(stage.Actors.Entities, RandomMinimum.Create(), [lockedDoor], profileWithDoor.ProfileKey.LogicalId);
        Assert.True(doors.Read(lockedDoor.Id).IsLocked);
        Assert.Equal(DaggerfallDoorMotion.Closed, doors.Read(lockedDoor.Id).Motion);

        List<DaggerfallDoorOperationKind> doorSounds = [];
        doors.SoundRequested += (_, kind) => doorSounds.Add(kind);

        PlayerControlState player = new(playerPos, yawRadians: 0f, pitchRadians: 0f);
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
            MoveTowardStagedGoal,
            _ => { },
            isPlacementScheduledOutdoors: (_, _, _, _) => outdoorsAllowed,
            resolveShelterBuilding: (_, _) => assignedBuilding,
            openResidentDoor: (pos, bldg) => doors.TryOpenForResident(pos, bldg, autoCloseSeconds: 1.0f));

        // 1. Midday: walker is active outdoors.
        population.ReconcileImmediate();
        long walkerId = stage.Npcs.All.Single(n => n.StableKey == "population/visible-retire/001").DurableId;
        Assert.Equal(DaggerfallNpcPresence.Active, stage.Npcs.Require(walkerId).Presence);

        // 2. Schedule flips indoors on live tick -> walker stages toward the exact #11104 assigned building door.
        outdoorsAllowed = false;
        population.Reconcile(immediate: false);
        Assert.True(population.IsRetiring(walkerId));
        ActorState actor = stage.Actors.Get(walkerId);
        Assert.NotNull(actor.Wander.Route);
        Assert.True(actor.Wander.Route![0].HorizontalDistanceTo(doorThreshold) < 0.05f);

        // 3. Step walker to the door:
        //    - First reaches approach threshold outside the door -> opens door, enters CrossingDoor phase,
        //      restages route to an inside-door point (signedOutward < 0), and stays ACTIVE (no outside pop-out!).
        //    - Then physically crosses the door plane (signedOutward <= -DoorCrossingThresholdMeters) before hiding.
        //    - Door swings shut behind the walker without clearing the lock or persisting open on mid-swing save.
        WorldPoint doorPlane = WorldPoint.From(targetDoor.Position + new Vector3(0f, targetDoor.BoundsMin.Y, 0f));
        float SignedOutward(WorldPoint p) =>
            Vector3.Dot(new Vector3(p.X - doorPlane.X, 0f, p.Z - doorPlane.Z), flatNormal);

        bool sawDoorOpening = false;
        bool sawCrossingPhaseWhileActive = false;
        bool checkedMidSwingSave = false;
        float lastActiveSignedOutward = float.PositiveInfinity;
        for (ulong step = 1; step <= 24; step++)
        {
            population.Step(step, 0.5f);
            doors.Advance(0.5);
            if (doors.Read(lockedDoor.Id).Motion is DaggerfallDoorMotion.Opening or DaggerfallDoorMotion.Open)
            {
                sawDoorOpening = true;
                if (!checkedMidSwingSave)
                {
                    checkedMidSwingSave = true;
                    DaggerfallDoorSave midSwingSave = Assert.Single(doors.Capture(), s => s.Id == lockedDoor.Id);
                    Assert.Equal(DaggerfallDoorMotion.Closed, midSwingSave.Motion);
                    Assert.Equal(0f, midSwingSave.Progress);
                    Assert.True(midSwingSave.IsLocked);
                }
            }
            if (stage.Npcs.Require(walkerId).Presence == DaggerfallNpcPresence.Active)
            {
                lastActiveSignedOutward = SignedOutward(actor.Position);
                if (population.IsCrossingDoor(walkerId))
                {
                    sawCrossingPhaseWhileActive = true;
                    Assert.NotNull(actor.Wander.Route);
                    Assert.True(SignedOutward(actor.Wander.Route![0]) < -0.5f,
                        $"Expected crossing route target inside doorway (signedOutward < -0.5), got {SignedOutward(actor.Wander.Route![0]):F2}.");
                }
            }
        }

        Assert.True(sawDoorOpening, "Expected the assigned building's exterior door to visibly open as the walker arrived.");
        Assert.True(sawCrossingPhaseWhileActive, "Expected retiring walker to remain Active while crossing through the open doorway.");
        Assert.True(SignedOutward(actor.Position) <= -DaggerfallSourcePopulation.DoorCrossingThresholdMeters,
            $"Expected retiring walker to physically cross past the door plane before hiding; final signedOutward={SignedOutward(actor.Position):F2}, lastActive={lastActiveSignedOutward:F2}.");
        Assert.Contains(DaggerfallDoorOperationKind.Open, doorSounds);
        Assert.Contains(DaggerfallDoorOperationKind.Close, doorSounds);
        Assert.False(population.IsRetiring(walkerId));
        Assert.False(population.IsCrossingDoor(walkerId));
        Assert.Equal(DaggerfallNpcPresence.Hidden, stage.Npcs.Require(walkerId).Presence);
        Assert.Equal(DaggerfallDoorMotion.Closed, doors.Read(lockedDoor.Id).Motion);
        Assert.True(doors.Read(lockedDoor.Id).IsLocked, "Resident using their own door must not permanently unlock it for the player.");
    }

    [Fact]
    public void Morning_live_transition_spawns_walker_at_home_door_opens_door_and_steps_out_onto_street()
    {
        DaggerfallSiteRecord town = TestPayload.Definitions.Locations.Records
            .Single(s => s.Id == Charing && s.Exterior is not null);
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));

        DaggerfallRdbDoorDefinition homeDoor = exterior.Doors
            .First(d => d.ExteriorBuilding is not null && d.Kind == DaggerfallDoorKind.Normal);
        DaggerfallSiteBuildingId homeBuilding = homeDoor.ExteriorBuilding!.Value;

        Vector3 outward = Vector3.Transform(Vector3.UnitZ, homeDoor.Rotation);
        Vector3 flatNormal = new(outward.X, 0f, outward.Z);
        flatNormal = flatNormal.LengthSquared() > 1e-4f ? Vector3.Normalize(flatNormal) : Vector3.UnitZ;
        WorldPoint doorThreshold = WorldPoint.From(homeDoor.Position + new Vector3(0f, homeDoor.BoundsMin.Y, 0f) + (flatNormal * 0.75f));
        WorldPoint streetPlacement = new(doorThreshold.X + (flatNormal.X * 14f), doorThreshold.Y, doorThreshold.Z + (flatNormal.Z * 14f));
        WorldPoint playerPos = new(doorThreshold.X + (flatNormal.X * 10f), doorThreshold.Y, doorThreshold.Z + (flatNormal.Z * 10f));

        DaggerfallPopulationPlacement[] placements =
        [
            new("population/morning-emerge/001", streetPlacement, 210, 4, 0, 0, 2002),
        ];

        using CivilianStage stage = new(town, placements);
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
            doors: [homeDoor],
            profileKind: DaggerfallWorldProfileKind.Exterior,
            logicalProfileId: stage.Profile.ProfileKey.LogicalId,
            billboardSprites: stage.Profile.BillboardSprites,
            population: placements);

        using DaggerfallDoorRuntime doors = new(stage.Actors.Entities, RandomMinimum.Create(), [homeDoor], profileWithDoor.ProfileKey.LogicalId);
        PlayerControlState player = new(playerPos, yawRadians: 0f, pitchRadians: 0f);
        bool outdoorsAllowed = false;

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
            MoveTowardStagedGoal,
            _ => { },
            isPlacementScheduledOutdoors: (_, _, _, _) => outdoorsAllowed,
            resolveHomeBuilding: (_, _, _) => homeBuilding,
            openResidentDoor: (pos, bldg) => doors.TryOpenForResident(pos, bldg, autoCloseSeconds: 1.0f));

        // 1. Night: walker is sheltered indoors (not active outdoors).
        population.ReconcileImmediate();
        Assert.DoesNotContain(stage.Npcs.All, n => n.StableKey == "population/morning-emerge/001" && n.Presence == DaggerfallNpcPresence.Active);

        // 2. Morning breaks on a live tick near the player: walker emerges right at the home doorway and opens the door!
        outdoorsAllowed = true;
        population.Reconcile(immediate: false);
        long walkerId = stage.Npcs.All.Single(n => n.StableKey == "population/morning-emerge/001").DurableId;
        Assert.Equal(DaggerfallNpcPresence.Active, stage.Npcs.Require(walkerId).Presence);
        Assert.True(population.IsEmerging(walkerId));

        ActorState actor = stage.Actors.Get(walkerId);
        Assert.True(actor.Position.HorizontalDistanceTo(doorThreshold) < 0.25f,
            $"Expected emerging walker to spawn at doorway threshold {doorThreshold}, got {actor.Position}.");
        Assert.Equal(DaggerfallDoorMotion.Opening, doors.Read(homeDoor.Id).Motion);
        Assert.NotNull(actor.Wander.Route);

        // 3. Stepping moves the walker out into the street, clears IsEmerging, and closes the door behind them.
        for (ulong step = 1; step <= 12; step++)
        {
            population.Step(step, 0.5f);
            doors.Advance(0.5);
        }

        Assert.False(population.IsEmerging(walkerId));
        Assert.True(actor.Position.HorizontalDistanceTo(doorThreshold) >= 1.2f,
            "Expected emerging walker to step out of the doorway onto the street.");
        Assert.Equal(DaggerfallDoorMotion.Closed, doors.Read(homeDoor.Id).Motion);
    }

    [Fact]
    public void Residential_night_routine_dims_lights_puts_residents_in_beds_with_zero_save_bytes_and_wakes_on_intruder()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord charing = definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));

        // Find a real residential building in Charing that receives sheltered residents at night (23:00).
        DaggerfallTownShelterAssignment nightPlan = DaggerfallScheduleShelterResolver.ResolveTown(
            0UL,
            definitions,
            charing,
            [.. charing.Exterior!.Buildings.Values],
            exterior.Population,
            minuteOfDay: 23 * 60,
            weather: DaggerfallWeatherKind.Sunny,
            weatherElapsed: DaggerfallWeatherShelterElapsed.Settled);

        DaggerfallSiteBuildingSource residenceBuilding = charing.Exterior.Buildings.Values
            .OrderBy(b => b.Id.BlockY).ThenBy(b => b.Id.BlockX).ThenBy(b => b.Id.Index)
            .First(b => DaggerfallScheduleShelterResolver.IsResidenceBuildingType(b.Source.BuildingType)
                && nightPlan.VisitorsFor(b.Id).Count > 0);

        DaggerfallSiteProfile residence = Assembled(DaggerfallWorldProfileIds.Interior(Charing, residenceBuilding.Id));
        DaggerfallSiteProfiles profiles = new([exterior, residence]);

        IReadOnlyList<DaggerfallBedSpot> bedSpots = DaggerfallScheduleShelterResolver.ResolveInteriorBedSpots(residence, 4);
        Assert.NotEmpty(bedSpots);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, residence);
        SpatialFake spatial = SpatialFake.Create(residence.Geometry.Spatial[0].Sha256, releases);
        AppearanceFake appearanceFake = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearanceFake);
        DaggerfallSessionComposition composition = new(definitions, residence, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);

        // 1. Advance to 23:00 (residential sleep hour): lights out, residents asleep in bed spots, ZERO save bytes!
        AdvanceToMinuteOfDay(session, 23 * 60);

        Assert.True(session.HomeNightRoutine.LightsOut);
        Assert.True(session.Sites.Projection.Lighting.IsInteriorLightsOut);
        Assert.False(session.HomeNightRoutine.IntruderDetected);

        ActorState[] sleepingResidents =
        [
            .. session.State.Actors.All.Where(a =>
                a.DurableId != DaggerfallActorIdentity.PlayerEntityId
                && a.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov)
                && ov.IsSleeping),
        ];
        Assert.NotEmpty(sleepingResidents);
        ActorState primarySleeper = sleepingResidents[0];
        Assert.True(session.HomeNightRoutine.BedAssignments.TryGetValue(primarySleeper.DurableId, out DaggerfallBedSpot assignedSpot));
        Assert.True(primarySleeper.Position.HorizontalDistanceTo(assignedSpot.Position) < 0.05f);

        // Verify visible sleeping presentation via DaggerfallSiteAppearance.AppendActorFacts:
        // classic/fallback billboard resident switches to a BillboardMode.None appearance so the supine transform
        // is honored by the renderer (not overridden upright by CylindricalView), with front facing up toward the viewer.
        ulong primaryVisualId = DaggerfallPresentationObjectIds.Gameplay(primarySleeper.DurableId, "Actor");
        List<AppearanceFact> sleepingFacts = [];
        session.Sites.Projection.Appearance.AppendActorFacts(sleepingFacts, session.State.Actors, DaggerfallPresentationObjectIds.ActiveLocationSlot);
        AppearanceFact sleepFact = Assert.Single(sleepingFacts, f => f.ObjectId == primaryVisualId);
        Assert.True(appearanceFake.SpriteRequestByAppearance.TryGetValue(sleepFact.Appearance, out SpriteFromAtlasRequest sleepSpriteRequest),
            "Expected sleeping classic/fallback resident to publish a sprite appearance.");
        Assert.Equal(BillboardMode.None, sleepSpriteRequest.Billboard);
        Vector3 sleepingUp = Vector3.Transform(Vector3.UnitY, sleepFact.Transform.Rotation);
        Vector3 sleepingFront = Vector3.Transform(Vector3.UnitZ, sleepFact.Transform.Rotation);
        Assert.True(MathF.Abs(Vector3.Dot(sleepingUp, Vector3.UnitY)) < 0.05f,
            $"Expected sleeping resident to render supine (horizontal), got up vector {sleepingUp}.");
        Assert.True(Vector3.Dot(sleepingFront, Vector3.UnitY) > 0.99f,
            $"Expected sleeping sprite front face to point upward on the bed, got front vector {sleepingFront}.");
        float expectedLift = assignedSpot.IsRealBedMesh
            ? DaggerfallSiteAppearance.SleepingBedHeightMeters
            : DaggerfallSiteAppearance.SleepingFloorHeightMeters;
        Assert.Equal(primarySleeper.Position.Y + expectedLift, sleepFact.Transform.Translation.Y, precision: 3);
        Assert.Equal(Vector3.One, sleepFact.Transform.Scale);

        // Verify rigged sleeper transform also lies supine at bed height with full scale.
        Transform riggedSleepTransform = DaggerfallSiteAppearance.ComposeActorTransform(
            primarySleeper, isRigged: true, isCorpseMesh: false, corpseMeshScale: 1f, spriteHeight: 1.7f, sleeping: true, sleepingOnBed: assignedSpot.IsRealBedMesh);
        Assert.True(MathF.Abs(Vector3.Dot(Vector3.Transform(Vector3.UnitY, riggedSleepTransform.Rotation), Vector3.UnitY)) < 0.05f);
        Assert.Equal(Vector3.One, riggedSleepTransform.Scale);

        // Untouched sleeping residents must write ZERO save bytes!
        DaggerfallSavePayload nightSave = DaggerfallSavePayload.Read(session.CaptureSave());
        foreach (ActorState sleeper in sleepingResidents)
        {
            Assert.DoesNotContain(nightSave.ActorLedger, e => e.DurableId == sleeper.DurableId);
            Assert.DoesNotContain(nightSave.ActorInventories, i => i.EntityId == sleeper.DurableId);
        }

        // 2. Sneaking crouched well outside sneak detection range stays undetected.
        WorldPoint farCrouchPos = new(primarySleeper.Position.X + 6.0f, primarySleeper.Position.Y, primarySleeper.Position.Z);
        session.State.PlayerControl.Restore(farCrouchPos, session.State.PlayerControl.Motion with { Stance = CharacterStance.Crouched, ControlledVelocity = Vector3.Zero });
        session.HomeNightRoutine.Step(residence, session.Sites.Projection.Lighting, session.Sites.Projection.Appearance, session.Calendar.Sky, 0.1f);
        Assert.False(session.HomeNightRoutine.IntruderDetected);
        Assert.True(session.HomeNightRoutine.LightsOut);

        // 3. Standing right beside the sleeping resident wakes the household, turns lights ON, barks, and reports Trespassing!
        int crimesBefore = session.State.Crime.Incidents.Count;
        WorldPoint caughtPos = new(primarySleeper.Position.X + 1.5f, primarySleeper.Position.Y, primarySleeper.Position.Z);
        session.State.PlayerControl.Restore(caughtPos, session.State.PlayerControl.Motion with { Stance = CharacterStance.Standing, ControlledVelocity = Vector3.Zero });
        session.HomeNightRoutine.Step(residence, session.Sites.Projection.Lighting, session.Sites.Projection.Appearance, session.Calendar.Sky, 0.1f);

        Assert.True(session.HomeNightRoutine.IntruderDetected);
        Assert.True(session.HomeNightRoutine.IntruderReported);
        Assert.False(session.HomeNightRoutine.LightsOut);
        Assert.False(session.Sites.Projection.Lighting.IsInteriorLightsOut);
        Assert.Contains(primarySleeper.DurableId, session.HomeNightRoutine.AwakenedActorIds);
        Assert.False(primarySleeper.Actor.Get<ActorOverlayComponent>().IsSleeping);
        Assert.True(primarySleeper.Actor.Get<ActorOverlayComponent>().AwakenedByIntruder);
        Assert.Contains(session.Presentation.Messages, m => m.Contains("Get out of my house", StringComparison.OrdinalIgnoreCase));

        // Verify waking immediately restores the ordinary cylindrical billboard appearance and upright transform!
        List<AppearanceFact> awakeFacts = [];
        session.Sites.Projection.Appearance.AppendActorFacts(awakeFacts, session.State.Actors, DaggerfallPresentationObjectIds.ActiveLocationSlot);
        AppearanceFact awakeFact = Assert.Single(awakeFacts, f => f.ObjectId == primaryVisualId);
        Assert.NotSame(sleepFact.Appearance, awakeFact.Appearance);
        Assert.True(appearanceFake.SpriteRequestByAppearance.TryGetValue(awakeFact.Appearance, out SpriteFromAtlasRequest awakeSpriteRequest));
        Assert.Equal(DaggerfallSpritesTuning.Default.Actors, awakeSpriteRequest.Billboard);
        Vector3 awakeUp = Vector3.Transform(Vector3.UnitY, awakeFact.Transform.Rotation);
        Assert.True(Vector3.Dot(awakeUp, Vector3.UnitY) > 0.99f,
            $"Expected awakened resident to restore upright orientation, got up vector {awakeUp}.");
        Assert.Equal(primarySleeper.Position.Y, awakeFact.Transform.Translation.Y, precision: 3);
        Assert.Equal(Vector3.One, awakeFact.Transform.Scale);

        Assert.True(session.State.Crime.Incidents.Count > crimesBefore);
        DaggerfallCrimeIncidentSave incident = session.State.Crime.Incidents[^1];
        Assert.Equal(DaggerfallCrimeKind.Trespassing, incident.Crime);
        Assert.Equal(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, incident.Witnesses.Query);
        Assert.Contains(primarySleeper.DurableId, incident.Witnesses.WitnessActorIds);

        // 4. Advance to morning (08:00): night routine clears cleanly and lights stay on.
        AdvanceToMinuteOfDay(session, 8 * 60);
        Assert.False(session.HomeNightRoutine.LightsOut);
        Assert.False(session.HomeNightRoutine.IntruderDetected);
        Assert.False(session.Sites.Projection.Lighting.IsInteriorLightsOut);
    }

    [Fact]
    public void Owned_residence_does_not_trigger_intruder_alarm_or_trespassing_at_night()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord charing = definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));

        DaggerfallTownShelterAssignment nightPlan = DaggerfallScheduleShelterResolver.ResolveTown(
            0UL,
            definitions,
            charing,
            [.. charing.Exterior!.Buildings.Values],
            exterior.Population,
            minuteOfDay: 23 * 60,
            weather: DaggerfallWeatherKind.Sunny,
            weatherElapsed: DaggerfallWeatherShelterElapsed.Settled);

        DaggerfallSiteBuildingSource residenceBuilding = charing.Exterior.Buildings.Values
            .OrderBy(b => b.Id.BlockY).ThenBy(b => b.Id.BlockX).ThenBy(b => b.Id.Index)
            .First(b => DaggerfallScheduleShelterResolver.IsResidenceBuildingType(b.Source.BuildingType)
                && nightPlan.VisitorsFor(b.Id).Count > 0);

        DaggerfallSiteProfile residence = Assembled(DaggerfallWorldProfileIds.Interior(Charing, residenceBuilding.Id));
        DaggerfallSiteProfiles profiles = new([exterior, residence]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, residence);
        SpatialFake spatial = SpatialFake.Create(residence.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, residence, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        AdvanceToMinuteOfDay(session, 23 * 60);

        // Construct an isolated DaggerfallHomeNightRoutine configured as player-owned for this residence.
        List<string> outcomes = [];
        DaggerfallHomeNightRoutine ownedRoutine = new(
            session.State.Actors,
            session.State.Npcs,
            session.State.PlayerControl,
            session.Crimes,
            ownsInterior: _ => true,
            isPlayerConcealed: () => false,
            hasPlayerLight: () => false,
            reportOutcome: outcomes.Add);

        ownedRoutine.Reconcile(residence, session.Sites.Projection.Lighting, session.Sites.Projection.Appearance, session.Calendar.Sky, immediate: true);
        ActorState sleeper = session.State.Actors.All.First(a =>
            a.DurableId != DaggerfallActorIdentity.PlayerEntityId
            && a.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov)
            && ov.IsSleeping);

        int crimesBefore = session.State.Crime.Incidents.Count;
        session.State.PlayerControl.Restore(
            new WorldPoint(sleeper.Position.X + 1.0f, sleeper.Position.Y, sleeper.Position.Z),
            session.State.PlayerControl.Motion with { Stance = CharacterStance.Standing, ControlledVelocity = Vector3.Zero });

        ownedRoutine.Step(residence, session.Sites.Projection.Lighting, session.Sites.Projection.Appearance, session.Calendar.Sky, 0.1f);
        Assert.False(ownedRoutine.IntruderDetected);
        Assert.False(ownedRoutine.IntruderReported);
        Assert.Equal(crimesBefore, session.State.Crime.Incidents.Count);
        Assert.Empty(outcomes);
    }

    [Fact]
    public void Talking_to_sleeping_resident_in_non_owned_house_wakes_household_restores_lights_and_greets_as_intruder()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord charing = definitions.Locations.Records.Single(r => r.Id == Charing);
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));

        DaggerfallTownShelterAssignment nightPlan = DaggerfallScheduleShelterResolver.ResolveTown(
            0UL,
            definitions,
            charing,
            [.. charing.Exterior!.Buildings.Values],
            exterior.Population,
            minuteOfDay: 23 * 60,
            weather: DaggerfallWeatherKind.Sunny,
            weatherElapsed: DaggerfallWeatherShelterElapsed.Settled);

        DaggerfallSiteBuildingSource residenceBuilding = charing.Exterior.Buildings.Values
            .OrderBy(b => b.Id.BlockY).ThenBy(b => b.Id.BlockX).ThenBy(b => b.Id.Index)
            .First(b => DaggerfallScheduleShelterResolver.IsResidenceBuildingType(b.Source.BuildingType)
                && nightPlan.VisitorsFor(b.Id).Count > 0);

        DaggerfallSiteProfile residence = Assembled(DaggerfallWorldProfileIds.Interior(Charing, residenceBuilding.Id));
        DaggerfallSiteProfiles profiles = new([exterior, residence]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, residence);
        SpatialFake spatial = SpatialFake.Create(residence.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, residence, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        AdvanceToMinuteOfDay(session, 23 * 60);

        Assert.True(session.HomeNightRoutine.LightsOut);
        ActorState sleeper = session.State.Actors.All.First(a =>
            a.DurableId != DaggerfallActorIdentity.PlayerEntityId
            && a.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov)
            && ov.IsSleeping);

        // Talk directly to the sleeping resident -> wakes household, turns lights on, reports Trespassing, and greets alarmed.
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(),
            candidate => checked((long)candidate.Identity.Value) == sleeper.DurableId);
        DaggerfallActivationOutcome outcome = session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
        Assert.True(outcome.Applied);
        Assert.True(session.HomeNightRoutine.IntruderDetected);
        Assert.False(session.HomeNightRoutine.LightsOut);
        Assert.False(sleeper.Actor.Get<ActorOverlayComponent>().IsSleeping);
        Assert.True(sleeper.Actor.Get<ActorOverlayComponent>().AwakenedByIntruder);
        Assert.NotNull(session.ActivationPanel.View.Dialogue);
        Assert.Contains("What are you doing in my home", session.ActivationPanel.View.Dialogue!.Greeting, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Live_session_exterior_retirement_uses_real_civilian_movement_to_approach_doorway_without_popping_out()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfiles profiles = new([exterior]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.Geometry.Spatial[0].Sha256, releases);
        spatial.NavigationResult = request =>
        {
            Vector3 delta = request.Target - request.From;
            float dist = delta.Length();
            float step = MathF.Min(dist, request.MaxStepUnits);
            Vector3 next = dist > 1e-4f ? request.From + ((delta / dist) * step) : request.Target;
            PlanarNavCell[] navPath = [new(0, 0, 0), new(1, 0, 0)];
            return new(navPath, navPath.Select(_ => new NavigationPathEdge(NavigationEdgeKind.Walk)).ToArray(),
                NavigationPathOutcome.Reached, next, navPath[1], NavigationEdgeKind.Walk, step, 1, 2, 1, 1, 1, false, default, default);
        };
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, exterior, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        AdvanceToMinuteOfDay(session, 12 * 60);

        // Pick a live non-guard civilian walker in Charing and place the player nearby (within 45m visible transition range).
        DaggerfallNpc walkerNpc = session.State.Npcs.All.First(n =>
            DaggerfallCivilians.IsSourcePopulation(n)
            && n.Presence == DaggerfallNpcPresence.Active
            && n.Role != "guard"
            && session.State.Actors.TryGet(n.DurableId, out _));
        ActorState walker = session.State.Actors.Get(walkerNpc.DurableId);
        WorldPoint startPos = walker.Position;
        session.State.PlayerControl.Restore(
            new WorldPoint(startPos.X + 8f, startPos.Y, startPos.Z + 8f),
            session.State.PlayerControl.Motion);

        // Trigger settled storm on a live outdoor tick: walker must enter visible retirement with a staged doorway route.
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Thunder);
        session.ResetScheduleWeatherBaseline();
        session.Population.Reconcile(immediate: false);

        Assert.True(session.Population.IsRetiring(walker.DurableId));
        Assert.Equal(DaggerfallNpcPresence.Active, session.State.Npcs.Require(walker.DurableId).Presence);
        Assert.NotNull(walker.Wander.Route);
        WorldPoint stagedGoal = walker.Wander.Route![0];
        float initialGoalDist = startPos.HorizontalDistanceTo(stagedGoal);

        // Advance through real session Population.Step (using real DaggerfallEnemyBehaviorModule.UpdateCivilian + ActorWanderCoordinator)
        // and real Doors.Advance: confirm real movement advances toward the doorway, opens a door, crosses past the door plane, and hides!
        bool sawMovedCloser = false;
        bool sawCrossing = false;
        WorldPoint doorPlaneLocal = default;
        Vector3 outwardNormal = Vector3.Zero;
        bool capturedDoorGeometry = false;
        for (ulong step = 1; step <= 360; step++)
        {
            if (session.Population.TryGetRetiringDoorGeometry(walker.DurableId, out WorldPoint plane, out Vector3 normal))
            {
                doorPlaneLocal = plane;
                outwardNormal = normal;
                capturedDoorGeometry = true;
            }
            session.Population.Step(step, 0.1f);
            session.Sites.Projection.Doors.Advance(0.1);
            if (session.State.Npcs.Require(walker.DurableId).Presence == DaggerfallNpcPresence.Active)
            {
                if (walker.Position.HorizontalDistanceTo(stagedGoal) < initialGoalDist - 0.5f)
                    sawMovedCloser = true;
                if (session.Population.IsCrossingDoor(walker.DurableId))
                    sawCrossing = true;
            }
            else
            {
                break;
            }
        }

        Assert.True(sawMovedCloser, "Expected real civilian movement to advance toward staged doorway goal.");
        Assert.True(sawCrossing, "Expected real civilian retirement to enter the through-door crossing phase before hiding.");
        Assert.True(capturedDoorGeometry && outwardNormal.LengthSquared() > 1e-4f, "Expected retiring walker to target a doorway with outward normal.");
        Assert.Equal(DaggerfallNpcPresence.Hidden, session.State.Npcs.Require(walker.DurableId).Presence);
        float signedOutwardAtHide = Vector3.Dot(
            new Vector3(walker.Position.X - doorPlaneLocal.X, 0f, walker.Position.Z - doorPlaneLocal.Z),
            outwardNormal);
        Assert.True(signedOutwardAtHide <= -DaggerfallSourcePopulation.DoorCrossingThresholdMeters,
            $"Expected walker to have crossed past the doorway plane at hiding (signedOutward <= {-DaggerfallSourcePopulation.DoorCrossingThresholdMeters}), got {signedOutwardAtHide:F3}.");
    }

    [Fact]
    public void Blocked_doorway_crossing_keeps_visible_walker_active_instead_of_popping_out()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfiles profiles = new([exterior]);

        bool blockNavigation = false;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.Geometry.Spatial[0].Sha256, releases);
        spatial.NavigationResult = request =>
        {
            PlanarNavCell[] navPath = [new(0, 0, 0), new(1, 0, 0)];
            if (blockNavigation)
            {
                return new(navPath, ReadOnlyMemory<NavigationPathEdge>.Empty, NavigationPathOutcome.NoPath, request.From, navPath[0], NavigationEdgeKind.Walk, 0f, 1, 1, 0, 0, 0, false, default, default);
            }
            Vector3 delta = request.Target - request.From;
            float dist = delta.Length();
            float step = MathF.Min(dist, request.MaxStepUnits);
            Vector3 next = dist > 1e-4f ? request.From + ((delta / dist) * step) : request.Target;
            return new(navPath, navPath.Select(_ => new NavigationPathEdge(NavigationEdgeKind.Walk)).ToArray(),
                NavigationPathOutcome.Reached, next, navPath[1], NavigationEdgeKind.Walk, step, 1, 2, 1, 1, 1, false, default, default);
        };
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, exterior, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        AdvanceToMinuteOfDay(session, 12 * 60);

        DaggerfallNpc walkerNpc = session.State.Npcs.All.First(n =>
            DaggerfallCivilians.IsSourcePopulation(n)
            && n.Presence == DaggerfallNpcPresence.Active
            && n.Role != "guard"
            && session.State.Actors.TryGet(n.DurableId, out _));
        ActorState walker = session.State.Actors.Get(walkerNpc.DurableId);
        session.State.PlayerControl.Restore(
            new WorldPoint(walker.Position.X + 6f, walker.Position.Y, walker.Position.Z + 6f),
            session.State.PlayerControl.Motion);

        session.LocalWeather.State.Force(DaggerfallWeatherKind.Thunder);
        session.ResetScheduleWeatherBaseline();
        session.Population.Reconcile(immediate: false);
        Assert.True(session.Population.IsRetiring(walker.DurableId));

        // Walk until the walker reaches the door and begins crossing, then keep the player nearby and block navigation!
        ulong step = 0;
        while (step < 360 && !session.Population.IsCrossingDoor(walker.DurableId))
        {
            step++;
            session.Population.Step(step, 0.1f);
            session.Sites.Projection.Doors.Advance(0.1);
            // Keep player within 10m so the walker remains plainly visible throughout.
            session.State.PlayerControl.Restore(
                new WorldPoint(walker.Position.X + 6f, walker.Position.Y, walker.Position.Z + 6f),
                session.State.PlayerControl.Motion);
        }
        Assert.True(session.Population.IsCrossingDoor(walker.DurableId));
        Assert.Equal(DaggerfallNpcPresence.Active, session.State.Npcs.Require(walker.DurableId).Presence);

        // Block navigation for 10 full seconds (well past the old 4s crossing timeout): visible walker MUST NOT disappear!
        blockNavigation = true;
        for (int i = 0; i < 100; i++)
        {
            step++;
            session.Population.Step(step, 0.1f);
            session.Sites.Projection.Doors.Advance(0.1);
            Assert.Equal(DaggerfallNpcPresence.Active, session.State.Npcs.Require(walker.DurableId).Presence);
            Assert.True(session.Population.IsRetiring(walker.DurableId));
        }

        // Unblock navigation: walker completes the crossing (or alternate doorway crossing) and hides only past the door plane.
        blockNavigation = false;
        WorldPoint finalDoorPlane = default;
        Vector3 finalOutward = Vector3.Zero;
        for (int i = 0; i < 360 && session.State.Npcs.Require(walker.DurableId).Presence == DaggerfallNpcPresence.Active; i++)
        {
            if (session.Population.TryGetRetiringDoorGeometry(walker.DurableId, out WorldPoint plane, out Vector3 normal))
            {
                finalDoorPlane = plane;
                finalOutward = normal;
            }
            step++;
            session.Population.Step(step, 0.1f);
            session.Sites.Projection.Doors.Advance(0.1);
            session.State.PlayerControl.Restore(
                new WorldPoint(walker.Position.X + 6f, walker.Position.Y, walker.Position.Z + 6f),
                session.State.PlayerControl.Motion);
        }

        Assert.Equal(DaggerfallNpcPresence.Hidden, session.State.Npcs.Require(walker.DurableId).Presence);
        float signedOutward = Vector3.Dot(
            new Vector3(walker.Position.X - finalDoorPlane.X, 0f, walker.Position.Z - finalDoorPlane.Z),
            finalOutward);
        Assert.True(signedOutward <= -DaggerfallSourcePopulation.DoorCrossingThresholdMeters,
            $"Expected unblocked walker to cross past the door plane before hiding, got signedOutward={signedOutward:F3}.");
    }

    private static void MoveTowardStagedGoal(ActorState actor, WorldRpg.Kit.Ai.WanderPolicy _, ulong __, float dt)
    {
        if (actor.Wander.Route is not { Count: > 0 } route)
            return;
        WorldPoint target = route[0];
        Vector3 delta = new(target.X - actor.Position.X, 0f, target.Z - actor.Position.Z);
        float len = delta.Length();
        if (len > 1e-3f)
        {
            float step = MathF.Min(len, 3f * dt);
            Vector3 next = actor.Position.ToVector() + (delta / len) * step;
            actor.ApplyPose(new ActorPose(WorldPoint.From(next), ActorHeading.Yaw(delta)));
        }
    }

    private static void AdvanceToMinuteOfDay(DaggerfallSession session, int targetMinuteOfDay)
    {
        DaggerfallCalendar cal = session.Calendar;
        int current = cal.Hour * DaggerfallCalendar.MinutesPerHour + cal.Minute;
        int deltaMinutes = targetMinuteOfDay - current;
        if (deltaMinutes <= 0)
            deltaMinutes += 24 * DaggerfallCalendar.MinutesPerHour;
        session.AdvanceElapsedTime(checked(deltaMinutes * 60L));
    }
}
