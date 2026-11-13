using System.Numerics;
using Daggerfall.Import.Normalized;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// What a real session asks Engine Spatial to keep resident: the dungeon action models a site admits, the
/// exterior cell window around the player, and what each site transition and unload removes. The spatial
/// fake applies every delta under the Engine's residency rules, so a delta Engine would refuse fails here.
/// </summary>
public sealed class SessionCollisionResidencyTests
{
    [Fact]
    public void Dungeon_admission_and_transitions_keep_only_the_active_sites_action_models_resident_and_unload_removes_them()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile destination = ReadProfile(root, FullContent(root), definitions, "daggerfall.castle-necromoghan");
        DaggerfallDungeonActionModelDefinition[] sourceModels = CollisionModels(source);
        DaggerfallDungeonActionModelDefinition[] destinationModels = CollisionModels(destination);
        Assert.NotEmpty(sourceModels);
        Assert.NotEmpty(destinationModels);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, destination]));

        // Admission sends one delta after the site's content artifact: every collision-bearing model as
        // an asset with its own geometry slice, an instance for each one whose collider is enabled, and
        // nothing removed.
        CollisionResidencyRequest admission = Assert.Single(spatial.CollisionResidencyRequests);
        Assert.Equal(1, spatial.ReplaceCalls);
        HashSet<ulong> sourceAssets = AssetIds(admission);
        Assert.Equal(sourceModels.Length, sourceAssets.Count);
        Assert.Equal(sourceModels.Sum(model => model.CollisionVertices.Length), admission.Vertices.Length);
        Assert.Equal(sourceModels.Sum(model => model.CollisionTriangles.Length), admission.Triangles.Length);
        Assert.Equal(sourceModels.Count(model => model.DoorIdentity is not { } door || session.Doors.Read(door).CollisionEnabled),
            admission.Instances.Length);
        Assert.True(admission.RemovedAssets.IsEmpty);
        Assert.True(admission.RemovedInstances.IsEmpty);
        Assert.Equal(sourceAssets, spatial.ResidentCollisionAssets.ToHashSet());
        Assert.Equal(InstanceIds(admission), spatial.ResidentCollisionInstances.Keys.ToHashSet());

        // Entering the castle replaces the content artifact, which drops the source's colliders, then
        // admits the castle's models; the departing projection's removal comes last and names exactly
        // the source identities, which Engine treats as already absent.
        Assert.True(session.TryTransitionTo(destination.ProfileKey));
        Assert.Equal(2, spatial.ReplaceCalls);
        Assert.Equal(3, spatial.CollisionResidencyRequests.Count);
        CollisionResidencyRequest entered = spatial.CollisionResidencyRequests[1];
        HashSet<ulong> destinationAssets = AssetIds(entered);
        Assert.Equal(destinationModels.Length, destinationAssets.Count);
        Assert.Empty(destinationAssets.Intersect(sourceAssets));
        Assert.True(entered.RemovedAssets.IsEmpty);
        Assert.True(entered.RemovedInstances.IsEmpty);
        CollisionResidencyRequest departed = spatial.CollisionResidencyRequests[2];
        AssertRemovalOnly(departed, sourceAssets, sourceModels.Length);
        Assert.Equal(destinationAssets, spatial.ResidentCollisionAssets.ToHashSet());
        Assert.Equal(InstanceIds(entered), spatial.ResidentCollisionInstances.Keys.ToHashSet());
        Assert.Equal(destination.Site, session.Site.Active);

        // Returning re-admits the source's models under the same identities and removes the castle's.
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        Assert.Equal(5, spatial.CollisionResidencyRequests.Count);
        Assert.Equal(sourceAssets, AssetIds(spatial.CollisionResidencyRequests[3]));
        AssertRemovalOnly(spatial.CollisionResidencyRequests[4], destinationAssets, destinationModels.Length);
        Assert.Equal(sourceAssets, spatial.ResidentCollisionAssets.ToHashSet());

        // Unloading the session removes what the active site admitted before it releases the spatial
        // session, so nothing is left for the release to drop.
        session.Dispose();
        AssertRemovalOnly(spatial.CollisionResidencyRequests[^1], sourceAssets, sourceModels.Length);
        Assert.True(spatial.SessionReleased);
        Assert.Equal((0, 0), spatial.CollidersLeftAtRelease);
    }

    [Fact]
    public void An_exterior_session_admits_its_cell_window_moves_it_on_a_cell_crossing_and_clears_it_before_an_interior()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root, "worldrpg/imports/charing");
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior");
        DaggerfallSiteProfile interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0");
        Assert.Equal(DaggerfallWorldProfileKind.Exterior, exterior.ProfileKey.Kind);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, interior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        // The step answers with the pose it was given, so the crossing below is the player's own move.
        spatial.KeepPosition = true;
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([exterior, interior]));
        long rat = session.SpawnActor("rat", new ActorPose(new WorldPoint(9f, 0f, 9f), 0f));

        // Construction admits the seven-by-seven window centred on the player's cell, in the site's local frame:
        // the frame the player's own position is in, so the window and the player agree on where the player stands.
        DaggerfallExteriorCellResidencySave admittedWindow = session.Exterior.CaptureExteriorResidency()
            ?? throw new InvalidOperationException("The exterior session admitted no cell window.");
        DaggerfallExteriorCellId center = admittedWindow.Center;
        Assert.Equal(session.Exterior.ActiveExteriorCell(), admittedWindow.Origin);
        Assert.Equal(session.Exterior.CurrentExteriorCell(), center);
        DaggerfallExteriorCellId[] cells = Window(center);
        Assert.Equal(49, cells.Length);
        Assert.Equal(cells.ToHashSet(), session.Exterior.ResidentExteriorCells.ToHashSet());

        // A step that leaves the player where it stands leaves the window where it is. The ground is the
        // collider's, which the step stands up: the window brings no terrain batches of its own.
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(1, spatial.HeightfieldColliderCreates);
        DaggerfallExteriorCellResidencySave window = session.Exterior.CaptureExteriorResidency()!.Value;
        Assert.Equal(center, window.Center);

        // One cell east is a new column of seven cells to admit and the far west column to remove. The
        // movement also crosses the local origin's threshold, so the Engine rebases retained collision,
        // the collider with it: it is neither made anew nor refocused in any but its own frame.
        WorldPoint start = session.State.PlayerControl.Position ?? throw new InvalidOperationException("The exterior player has no position.");
        WorldPoint crossed = new(start.X + DaggerfallExteriorCellResidency.CellSize, start.Y, start.Z);
        float yaw = session.State.PlayerControl.YawRadians;
        double health = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        int originCommitsBeforeCrossing = engine.OriginCommits.Count;
        session.State.PlayerControl.MoveTo(crossed.ToVector());
        session.Update(new ProductUpdate(OuterUpdate(2), []));

        DaggerfallExteriorCellId east = new(center.X + 1, center.Y);
        Assert.Equal(Window(east).ToHashSet(), session.Exterior.ResidentExteriorCells.ToHashSet());
        DaggerfallExteriorCellResidencySave moved = session.Exterior.CaptureExteriorResidency()!.Value;
        DaggerfallExteriorWorldOrigin origin = window.WorldOrigin;
        // The focus is the player and anyone in the wilderness, in the collider's own frame: the step's
        // focuses are worked out before the same update commits the rebase, so they carry the prior frame.
        Assert.Equal(1, spatial.HeightfieldColliderCreates);
        Vector3 focused = crossed.ToVector() - origin.Compensation;
        Assert.Equal(new HeightfieldColliderFocus(focused.X, focused.Z), spatial.HeightfieldColliderFocuses[^1][0]);
        Assert.Contains(new HeightfieldColliderFocus(9, 9), spatial.HeightfieldColliderFocuses[^1]);
        Assert.Equal(east, moved.Center);
        Assert.Equal(window.Origin, moved.Origin);
        WorldOriginCommitReceipt rebase = Assert.Single(engine.OriginCommits.Skip(originCommitsBeforeCrossing));
        Assert.NotEqual(Vector3.Zero, rebase.LocalDelta);
        Assert.Equal(origin.Compensation + rebase.LocalDelta, moved.WorldOrigin.Compensation);
        Assert.Equal((rebase.OriginAfterCellX, rebase.OriginAfterCellY, rebase.OriginAfterCellZ),
            (moved.EngineOriginCellX, moved.EngineOriginCellY, moved.EngineOriginCellZ));
        Assert.Equal(east, session.Exterior.CurrentExteriorCell());

        // The crossing preserves world-space poses and vitals. Both player and actor adopt the
        // receipt's local delta, keeping their relative position and the collision window aligned.
        Assert.Equal(crossed.ToVector() + rebase.LocalDelta, session.State.PlayerControl.Position!.Value.ToVector());
        Vector3 activeFrame = session.Exterior.ExteriorProfileFrameTranslation(exterior.ProfileKey);
        Assert.Equal(crossed.ToVector() + rebase.LocalDelta - activeFrame,
            session.Exterior.LocalToProfile(session.State.PlayerControl.Position.Value.ToVector()));
        Assert.Equal(yaw, session.State.PlayerControl.YawRadians);
        Assert.Equal(health, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.True(session.State.Actors.TryGet(rat, out ActorState? ratState));
        // The rat was spawned before the idle update's vertical origin commit. Its absolute local pose
        // therefore carries the pre-crossing window compensation as well as the crossing receipt.
        Assert.Equal(new Vector3(9f, 0f, 9f) + origin.Compensation + rebase.LocalDelta, ratState.Position.ToVector());
        Assert.Equal(new Vector3(9f, 0f, 9f) + origin.Compensation - crossed.ToVector(),
            ratState.Position.ToVector() - session.State.PlayerControl.Position.Value.ToVector());
        Assert.Equal(exterior.Site, session.Site.Active);

        // Entering the interior lets go of the ground before the interior's content replaces the collision
        // set, so no exterior ground outlives the exterior.
        int replacements = spatial.ReplaceCalls;
        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        Assert.Equal(replacements + 1, spatial.ReplaceCalls);
        Assert.Equal(1, spatial.HeightfieldColliderReleases);
        Assert.Null(session.Exterior.CaptureExteriorResidency());
        Assert.Equal(interior.ProfileKey, session.Sites.ActiveProfile);
    }

    [Fact]
    public void A_staged_arrival_outside_waits_for_its_ground_and_fails_on_a_refused_tile()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root, "worldrpg/imports/charing");
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, DaggerfallTuning.Defaults));

        spatial.HeightfieldColliderReady = false;
        session.Exterior.DeferCollision(() => session.Exterior.UpdateExteriorResidency());

        // Nothing may stand on ground that is not there: the arrival stays pending until it is.
        Assert.True(session.Exterior.CollisionPending);
        Assert.False(session.Exterior.AwaitArrivalCollision().Done);
        Assert.True(session.Exterior.CollisionPending);

        spatial.HeightfieldColliderRefusedTiles = 1;
        Assert.Throws<DaggerfallArrivalCollisionRefusedException>(() => session.Exterior.AwaitArrivalCollision());
        spatial.HeightfieldColliderRefusedTiles = 0;

        spatial.HeightfieldColliderReady = true;
        Assert.True(session.Exterior.AwaitArrivalCollision().Done);
        Assert.False(session.Exterior.CollisionPending);
    }

    [Fact]
    public void Play_arrival_and_load_all_wait_for_the_ground_under_an_enemy_out_in_the_wilderness()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root, "worldrpg/imports/charing");
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, exterior, DaggerfallTuning.Defaults);
        DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallSession? restored = null;
        try
        {
            Assert.True(session.AwaitArrivalGround().Done);
            WorldPoint player = session.State.PlayerControl.Position!.Value;
            const float away = 300F;
            session.SpawnActor("rat", new ActorPose(new WorldPoint(player.X + away, player.Y, player.Z), 0F));
            // The ground stands about the player, and is still on its way out where the enemy is.
            bool arrived = false;
            spatial.HeightfieldColliderReadyAt = read => arrived || read.X < player.X + (away / 2F);

            // Play: held, nobody steps, and both places were asked after.
            int steps = spatial.StepCalls;
            spatial.HeightfieldColliderReads.Clear();
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            Assert.Equal(steps, spatial.StepCalls);
            Assert.Contains(spatial.HeightfieldColliderReads, read => read.X < player.X + 1F);
            Assert.Contains(spatial.HeightfieldColliderReads, read => read.X > player.X + (away / 2F));
            Assert.False(session.AwaitArrivalGround().Done);

            // A staged arrival: the walkers are asked after there too, and nothing stands until they have ground.
            session.Exterior.DropGroundCollider();
            session.Exterior.DeferCollision(() => session.Exterior.UpdateExteriorResidency());
            Assert.False(session.Exterior.AwaitArrivalCollision().Done);
            Assert.True(session.Exterior.CollisionPending);

            arrived = true;
            Assert.True(session.Exterior.AwaitArrivalCollision().Done);
            Assert.False(session.Exterior.CollisionPending);
            session.Update(new ProductUpdate(OuterUpdate(2), []));
            Assert.True(spatial.StepCalls > steps);

            // Load: the saved enemy's ground holds the load's own wait and the first update after it.
            RulesetSavePayload save = session.CaptureSave();
            session.Dispose();
            arrived = false;
            restored = DaggerfallSession.Restore(engine.Context, composition, save);
            Assert.False(restored.AwaitArrivalGround().Done);
            steps = spatial.StepCalls;
            restored.Update(new ProductUpdate(OuterUpdate(3), []));
            Assert.Equal(steps, spatial.StepCalls);
            arrived = true;
            Assert.True(restored.AwaitArrivalGround().Done);
            restored.Update(new ProductUpdate(OuterUpdate(4), []));
            Assert.True(spatial.StepCalls > steps);
        }
        finally
        {
            restored?.Dispose();
            if (restored is null) session.Dispose();
        }
    }

    [Fact]
    public void A_location_the_window_brings_in_on_the_road_is_prepared_and_drawn_off_the_update_and_its_floor_waits_for_it()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord[] exteriorRecords = [.. definitions.Locations.Records.Where(record => record.Exterior is not null)];
        DaggerfallSiteRecord activeRecord = exteriorRecords.First(record => exteriorRecords.Any(candidate =>
            candidate.Id != record.Id
            && (candidate.MapPixelX != record.MapPixelX || candidate.MapPixelY != record.MapPixelY)
            && Math.Abs(candidate.MapPixelX - record.MapPixelX) <= DaggerfallExteriorCellResidency.StreamingRadius
            && Math.Abs(candidate.MapPixelY - record.MapPixelY) <= DaggerfallExteriorCellResidency.StreamingRadius));
        DaggerfallSiteRecord residentRecord = exteriorRecords.First(record =>
            record.Id != activeRecord.Id
            && (record.MapPixelX != activeRecord.MapPixelX || record.MapPixelY != activeRecord.MapPixelY)
            && Math.Abs(record.MapPixelX - activeRecord.MapPixelX) <= DaggerfallExteriorCellResidency.StreamingRadius
            && Math.Abs(record.MapPixelY - activeRecord.MapPixelY) <= DaggerfallExteriorCellResidency.StreamingRadius);
        DaggerfallSiteProfile template = ReadInputs(root);
        DaggerfallSiteProfile source = EmptyExteriorAt(template, activeRecord.Id);
        DaggerfallSiteProfile resident = EmptyExteriorAt(template, residentRecord.Id);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, resident);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(definitions, source, DaggerfallTuning.Defaults) { Profiles = new([source, resident]) });
        spatial.HoldDeferredCollision = true;
        int requests = spatial.ContentResidencyRequests.Count;
        int waited = spatial.SynchronousWaitsOnDeferred;
        // The site the session starts at draws at once.
        Assert.NotEmpty(appearance.StaticMeshPaths);
        Assert.Empty(appearance.DeferredStaticMeshPaths);

        // The first admitted update moves the window with the player and brings the neighbour in.
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Contains(resident.ProfileKey, session.Exterior.ResidentExteriorProfiles);
        SpatialContentArtifactResidencyRequest placed = Assert.Single(spatial.ContentResidencyRequests.Skip(requests));
        Assert.True(placed.Deferred);
        Assert.Equal(waited, spatial.SynchronousWaitsOnDeferred);
        // Its meshes, and the textures its materials sample, are prepared on the renderer's workers.
        string[] geometry = [.. resident.Geometry.Meshes.Select(mesh => mesh.Path)];
        Assert.NotEmpty(geometry);
        Assert.All(geometry, path => Assert.Contains(appearance.DeferredStaticMeshPaths, deferred => deferred?.EndsWith(path, StringComparison.Ordinal) == true));
        Assert.Contains(appearance.OpenResourceContentRequests, request => request.Deferred);
        // The world runs on; only what the location places waits for its floor.
        Assert.True(session.Exterior.CollisionPending);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.True(session.Exterior.CollisionPending);

        spatial.HoldDeferredCollision = false;
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.False(session.Exterior.CollisionPending);
        Assert.Equal(waited, spatial.SynchronousWaitsOnDeferred);
    }

    [Fact]
    public void Resident_actor_delta_is_profile_framed_and_active_capture_excludes_resident_identities()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord[] exteriorRecords = [.. definitions.Locations.Records.Where(record => record.Exterior is not null)];
        DaggerfallSiteRecord activeRecord = exteriorRecords.First(record => exteriorRecords.Any(candidate =>
            candidate.Id != record.Id
            && (candidate.MapPixelX != record.MapPixelX || candidate.MapPixelY != record.MapPixelY)
            && Math.Abs(candidate.MapPixelX - record.MapPixelX) <= DaggerfallExteriorCellResidency.StreamingRadius
            && Math.Abs(candidate.MapPixelY - record.MapPixelY) <= DaggerfallExteriorCellResidency.StreamingRadius));
        DaggerfallSiteRecord residentRecord = exteriorRecords.First(record =>
            record.Id != activeRecord.Id
            && (record.MapPixelX != activeRecord.MapPixelX || record.MapPixelY != activeRecord.MapPixelY)
            && Math.Abs(record.MapPixelX - activeRecord.MapPixelX) <= DaggerfallExteriorCellResidency.StreamingRadius
            && Math.Abs(record.MapPixelY - activeRecord.MapPixelY) <= DaggerfallExteriorCellResidency.StreamingRadius);
        DaggerfallSiteProfile template = ReadInputs(root);
        DaggerfallSiteProfile source = EmptyExteriorAt(template, activeRecord.Id);
        DaggerfallSiteProfile resident = EmptyExteriorAt(template, residentRecord.Id);
        // The helper keeps the same source closure while the profile key supplies the geographic
        // identity. The third profile is interior so the resident closure is retired before its
        // delta is saved and admitted again.
        DaggerfallSiteProfile interior = EmptyProfileAt(template, activeRecord.Id,
            DaggerfallWorldProfileKind.Interior, "resident-interior");
        DaggerfallSiteProfiles profiles = new([source, resident, interior]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, resident);
        PopulateContent(content, interior);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(definitions, source, DaggerfallTuning.Defaults) { Profiles = profiles });

        // New-game initial terrain admission precedes the composition catalog, so the first
        // admitted update reconciles the catalog's neighboring exterior profiles.
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Contains(resident.ProfileKey, session.Exterior.ResidentExteriorProfiles);
        Assert.True(session.TryTransitionTo(resident.ProfileKey));
        WorldPoint authoredProfilePose = new(4F, 0F, 5F);
        Vector3 residentFrame = session.Exterior.ExteriorProfileFrameTranslation(resident.ProfileKey);
        long actorId = session.SpawnActor("rat",
            new ActorPose(WorldPoint.From(authoredProfilePose.ToVector() + residentFrame), .25F));
        WorldPoint profilePose = WorldPoint.From(
            session.State.Actors.Get(actorId).Position.ToVector() - residentFrame);

        Assert.True(session.TryTransitionTo(source.ProfileKey));
        // The source transition establishes a new local origin. Re-read the resident profile's
        // frame after that rebase; its profile-space pose remains unchanged while its local cell
        // translation changes with the active source origin.
        Vector3 residentFrameInSource = session.Exterior.ExteriorProfileFrameTranslation(resident.ProfileKey);
        Vector3 expectedResidentPose = profilePose.ToVector()
            + residentFrameInSource;
        Assert.Equal(expectedResidentPose.X, session.State.Actors.Get(actorId).Position.X, 3);
        Assert.Equal(expectedResidentPose.Y, session.State.Actors.Get(actorId).Position.Y, 3);
        Assert.Equal(expectedResidentPose.Z, session.State.Actors.Get(actorId).Position.Z, 3);

        // Leaving the exterior captures the resident closure and the active source in separate
        // profile deltas. Save validation is the durable identity guard: the resident actor may
        // occur exactly once, even though it was live while the active source was captured.
        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.DoesNotContain(saved.SiteDeltas, delta => delta.Profile.Require() == source.ProfileKey
            && delta.DynamicActors.Any(actor => actor.EntityId == actorId));
        DaggerfallSiteDeltaSave residentDelta = Assert.Single(saved.SiteDeltas,
            delta => delta.Profile.Require() == resident.ProfileKey);
        WorldPoint savedProfilePose = new(
            Assert.Single(residentDelta.DynamicActors).X,
            Assert.Single(residentDelta.DynamicActors).Y,
            Assert.Single(residentDelta.DynamicActors).Z);
        Assert.Equal(profilePose.X, savedProfilePose.X, 3);
        Assert.Equal(profilePose.Y, savedProfilePose.Y, 3);
        Assert.Equal(profilePose.Z, savedProfilePose.Z, 3);

        Assert.True(session.TryTransitionTo(source.ProfileKey));
        Assert.True(session.State.Actors.TryGet(actorId, out ActorState? restoredResident));
        Vector3 restoredResidentFrame = session.Exterior.ExteriorProfileFrameTranslation(resident.ProfileKey);
        Vector3 restoredExpectedPose = profilePose.ToVector() + restoredResidentFrame;
        Assert.Equal(restoredExpectedPose.X, restoredResident!.Position.X, 3);
        Assert.Equal(restoredExpectedPose.Y, restoredResident.Position.Y, 3);
        Assert.Equal(restoredExpectedPose.Z, restoredResident.Position.Z, 3);
    }

    // Exteriors stream by the id that names their location.
    private static DaggerfallSiteProfile EmptyExteriorAt(DaggerfallSiteProfile template, DaggerfallSiteId site) =>
        EmptyProfileAt(template, site, DaggerfallWorldProfileKind.Exterior, DaggerfallWorldProfileIds.Exterior(site).LogicalId);

    private static DaggerfallSiteProfile EmptyProfileAt(DaggerfallSiteProfile template, DaggerfallSiteId site,
        DaggerfallWorldProfileKind kind, string logicalId) => new(
            new ProjectFacts(new WorldPoint(1F, 1F, 1F), new Dictionary<long, AuthoredActor>()),
            template.Geometry, template.WorldAppearance, template.InitialLook,
            template.Materials, new Dictionary<long, NormalizedActorSprite>(), template.MobileSprites,
            template.Audio, template.ClassicPresentation, site, [], kind, logicalId, [],
            template.Anchors.Values.ToArray(), template.Lights, template.GroundContainerSprites, null, [], [], null,
            template.QuestMarkers, template.BillboardSprites, [],
            template.WaterVolumes, template.TerrainTextures, []);

    private static DaggerfallDungeonActionModelDefinition[] CollisionModels(DaggerfallSiteProfile site) =>
        [.. site.DungeonActionModels.Where(model => model.CollisionTriangles.Length != 0)];

    private static HashSet<ulong> AssetIds(CollisionResidencyRequest request) => [.. request.Assets.ToArray().Select(asset => asset.Id)];

    private static HashSet<ulong> InstanceIds(CollisionResidencyRequest request) => [.. request.Instances.ToArray().Select(instance => instance.Id)];

    private static void AssertRemovalOnly(CollisionResidencyRequest request, HashSet<ulong> assets, int instances)
    {
        Assert.True(request.Assets.IsEmpty);
        Assert.True(request.Instances.IsEmpty);
        Assert.Equal(assets, request.RemovedAssets.ToArray().ToHashSet());
        Assert.Equal(instances, request.RemovedInstances.ToArray().Distinct().Count());
    }

    private static DaggerfallExteriorCellId[] Window(DaggerfallExteriorCellId center) =>
    [
        .. from y in Enumerable.Range(center.Y - DaggerfallExteriorCellResidency.StreamingRadius, DaggerfallExteriorCellResidency.StreamingDimension)
           from x in Enumerable.Range(center.X - DaggerfallExteriorCellResidency.StreamingRadius, DaggerfallExteriorCellResidency.StreamingDimension)
           select new DaggerfallExteriorCellId(x, y),
    ];
}
