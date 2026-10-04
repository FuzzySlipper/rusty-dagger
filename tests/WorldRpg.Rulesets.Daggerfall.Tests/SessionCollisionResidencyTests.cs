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
        DaggerfallSiteProfile destination = ReadProfile(root, FullContent(root), definitions, "daggerfall.castle-necromoghan.json");
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
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        DaggerfallSiteProfile interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
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

        // Construction admits the seven-by-seven window centred on the player's cell as one delta: an
        // asset and an instance per cell, placed in the site's local frame - the frame the player's own
        // position is in, so the window and the player agree on which cell the player stands in.
        DaggerfallExteriorCellResidencySave window = session.Sites.CaptureExteriorResidency()
            ?? throw new InvalidOperationException("The exterior session admitted no cell window.");
        DaggerfallExteriorCellId center = window.Center;
        Assert.Equal(session.Sites.ActiveExteriorCell(), window.Origin);
        Assert.Equal(session.Sites.CurrentExteriorCell(), center);
        CollisionResidencyRequest admission = Assert.Single(spatial.CollisionResidencyRequests, IsExterior);
        DaggerfallExteriorCellId[] cells = Window(center);
        Assert.Equal(49, cells.Length);
        Assert.Equal(cells.Select(DaggerfallExteriorCellResidency.AssetId).ToHashSet(), AssetIds(admission));
        Assert.Equal(cells.Select(DaggerfallExteriorCellResidency.InstanceId).ToHashSet(), InstanceIds(admission));
        Assert.All(admission.Instances.ToArray(), instance => Assert.Equal(instance.Asset,
            DaggerfallExteriorCellResidency.AssetId(cells.Single(cell => DaggerfallExteriorCellResidency.InstanceId(cell) == instance.Id))));
        Assert.True(admission.RemovedAssets.IsEmpty);
        Assert.True(admission.RemovedInstances.IsEmpty);
        Assert.Equal(cells.Select(DaggerfallExteriorCellResidency.AssetId).ToHashSet(), ExteriorAssets(spatial));

        // A step that leaves the player where it stands leaves the window where it is.
        int admissions = spatial.CollisionResidencyRequests.Count;
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(admissions, spatial.CollisionResidencyRequests.Count);
        Assert.Equal(center, session.Sites.CaptureExteriorResidency()!.Value.Center);

        // One cell east is a new column of seven cells to admit and the far west column to remove; the
        // cells both windows share are not rebuilt. The movement also crosses the local origin's
        // threshold, so Engine rebases retained collision after admitting the new column.
        WorldPoint start = session.State.PlayerControl.Position ?? throw new InvalidOperationException("The exterior player has no position.");
        WorldPoint crossed = new(start.X + DaggerfallExteriorCellResidency.CellSize, start.Y, start.Z);
        float yaw = session.State.PlayerControl.YawRadians;
        double health = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        int before = spatial.CollisionResidencyRequests.Count;
        session.State.PlayerControl.MoveTo(crossed.ToVector());
        session.Update(new ProductUpdate(OuterUpdate(2), []));

        DaggerfallExteriorCellId east = new(center.X + 1, center.Y);
        CollisionResidencyRequest crossing = Assert.Single(spatial.CollisionResidencyRequests.Skip(before));
        DaggerfallExteriorCellId[] entering = [.. Window(east).Except(cells)];
        DaggerfallExteriorCellId[] leaving = [.. cells.Except(Window(east))];
        Assert.Equal(7, entering.Length);
        Assert.All(entering, cell => Assert.Equal(center.X + DaggerfallExteriorCellResidency.StreamingRadius + 1, cell.X));
        Assert.All(leaving, cell => Assert.Equal(center.X - DaggerfallExteriorCellResidency.StreamingRadius, cell.X));
        Assert.Equal(entering.Select(DaggerfallExteriorCellResidency.AssetId).ToHashSet(), AssetIds(crossing));
        Assert.Equal(entering.Select(DaggerfallExteriorCellResidency.InstanceId).ToHashSet(), InstanceIds(crossing));
        Assert.Equal(leaving.Select(DaggerfallExteriorCellResidency.AssetId).ToHashSet(), crossing.RemovedAssets.ToArray().ToHashSet());
        Assert.Equal(leaving.Select(DaggerfallExteriorCellResidency.InstanceId).ToHashSet(), crossing.RemovedInstances.ToArray().ToHashSet());
        DaggerfallExteriorWorldOrigin origin = new(window.Origin.X, window.Origin.Y, new Vector3(window.CompensationX, window.CompensationY, window.CompensationZ));
        Assert.All(crossing.Instances.ToArray(), instance => Assert.Equal(
            origin.LocalTranslation(entering.Single(cell => DaggerfallExteriorCellResidency.InstanceId(cell) == instance.Id)),
            instance.Transform.Translation));
        Assert.Equal(Window(east).Select(DaggerfallExteriorCellResidency.AssetId).ToHashSet(), ExteriorAssets(spatial));
        DaggerfallExteriorCellResidencySave moved = session.Sites.CaptureExteriorResidency()!.Value;
        Assert.Equal(east, moved.Center);
        Assert.Equal(window.Origin, moved.Origin);
        WorldOriginCommitReceipt rebase = Assert.Single(engine.OriginCommits);
        Assert.NotEqual(Vector3.Zero, rebase.LocalDelta);
        Assert.Equal(origin.Compensation + rebase.LocalDelta,
            new Vector3(moved.CompensationX, moved.CompensationY, moved.CompensationZ));
        Assert.Equal(east, session.Sites.CurrentExteriorCell());

        // The crossing preserves world-space poses and vitals. Both player and actor adopt the
        // receipt's local delta, keeping their relative position and the collision window aligned.
        Assert.Equal(crossed.ToVector() + rebase.LocalDelta, session.State.PlayerControl.Position!.Value.ToVector());
        Assert.Equal(crossed.ToVector() - origin.Compensation,
            session.Sites.LocalToProfile(session.State.PlayerControl.Position.Value.ToVector()));
        Assert.Equal(yaw, session.State.PlayerControl.YawRadians);
        Assert.Equal(health, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.True(session.State.Actors.TryGet(rat, out ActorState? ratState));
        Assert.Equal(new Vector3(9f, 0f, 9f) + rebase.LocalDelta, ratState.Position.ToVector());
        Assert.Equal(new Vector3(9f, 0f, 9f) - crossed.ToVector(),
            ratState.Position.ToVector() - session.State.PlayerControl.Position.Value.ToVector());
        Assert.Equal(exterior.Site, session.Site.Active);

        // Entering the interior removes the whole window before the interior's content replaces the
        // collision set, so no exterior cell outlives the exterior.
        before = spatial.CollisionResidencyRequests.Count;
        int replacements = spatial.ReplaceCalls;
        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        CollisionResidencyRequest cleared = spatial.CollisionResidencyRequests[before];
        Assert.True(cleared.Assets.IsEmpty);
        Assert.True(cleared.Instances.IsEmpty);
        Assert.Equal(Window(east).Select(DaggerfallExteriorCellResidency.AssetId).ToHashSet(), cleared.RemovedAssets.ToArray().ToHashSet());
        Assert.Equal(Window(east).Select(DaggerfallExteriorCellResidency.InstanceId).ToHashSet(), cleared.RemovedInstances.ToArray().ToHashSet());
        Assert.Equal(replacements + 1, spatial.ReplaceCalls);
        Assert.Empty(ExteriorAssets(spatial));
        Assert.DoesNotContain(spatial.ResidentCollisionInstances.Keys, IsExteriorInstance);
        Assert.Null(session.Sites.CaptureExteriorResidency());
        Assert.Equal(interior.ProfileKey, session.Sites.ActiveProfile);
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
        DaggerfallSiteProfile source = EmptyExteriorAt(template, activeRecord.Id, "resident-source");
        DaggerfallSiteProfile resident = EmptyExteriorAt(template, residentRecord.Id, "resident-neighbor");
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
        Assert.Contains(resident.ProfileKey, session.Sites.ResidentExteriorProfiles);
        Assert.True(session.TryTransitionTo(resident.ProfileKey));
        WorldPoint profilePose = new(4F, 0F, 5F);
        long actorId = session.SpawnActor("rat", new ActorPose(profilePose, .25F));
        profilePose = session.State.Actors.Get(actorId).Position;

        Assert.True(session.TryTransitionTo(source.ProfileKey));
        DaggerfallExteriorCellResidencySave originSave = session.Sites.CaptureExteriorResidency()!.Value;
        DaggerfallExteriorWorldOrigin origin = new(originSave.Origin.X, originSave.Origin.Y,
            new Vector3(originSave.CompensationX, originSave.CompensationY, originSave.CompensationZ));
        DaggerfallExteriorCellId residentCell = new(residentRecord.MapPixelX, residentRecord.MapPixelY);
        Vector3 expectedResidentPose = profilePose.ToVector() + origin.LocalTranslation(residentCell);
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
        Assert.Equal(expectedResidentPose.X, restoredResident!.Position.X, 3);
        Assert.Equal(expectedResidentPose.Y, restoredResident.Position.Y, 3);
        Assert.Equal(expectedResidentPose.Z, restoredResident.Position.Z, 3);

        static DaggerfallSiteProfile EmptyExteriorAt(DaggerfallSiteProfile template, DaggerfallSiteId site, string logicalId) =>
            EmptyProfileAt(template, site, DaggerfallWorldProfileKind.Exterior, logicalId);

        static DaggerfallSiteProfile EmptyProfileAt(DaggerfallSiteProfile template, DaggerfallSiteId site,
            DaggerfallWorldProfileKind kind, string logicalId) => new(
                new ProjectFacts(new WorldPoint(1F, 1F, 1F), new Dictionary<long, AuthoredActor>()),
                template.SpatialArtifact, template.StaticMesh, template.WorldAppearance, template.InitialLook,
                template.Materials, new Dictionary<long, NormalizedActorSprite>(), template.MobileSprites,
                template.Audio, template.ClassicPresentation, site, [], kind, logicalId, [],
                template.Anchors.Values.ToArray(), template.Lights, template.GroundContainerSprite, null, [], [], null,
                template.Music, template.AudioBundle, template.QuestMarkers, template.BillboardSprites, [],
                template.WaterVolumes, template.TerrainTextures, []);
    }

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

    private static bool IsExterior(CollisionResidencyRequest request) =>
        request.Assets.ToArray().Any(asset => asset.Id == DaggerfallExteriorCellResidency.AssetId(Cell(asset.Id)));

    private static HashSet<ulong> ExteriorAssets(SpatialFake spatial) =>
        [.. spatial.ResidentCollisionAssets.Where(asset => asset == DaggerfallExteriorCellResidency.AssetId(Cell(asset)))];

    private static bool IsExteriorInstance(ulong instance) => instance == DaggerfallExteriorCellResidency.InstanceId(Cell(instance));

    /// <summary>The cell an exterior identity would pack, for recognizing exterior identities among others.</summary>
    private static DaggerfallExteriorCellId Cell(ulong identity) => new((int)((identity >> 24) & 0xFF_FFFF), (int)(identity & 0xFF_FFFF));
}
