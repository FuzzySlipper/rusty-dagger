using System.Numerics;
using System.Text.Json;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class ExteriorOriginSessionTests
{
    [Fact]
    public void Native_collision_retains_its_relative_hit_after_the_session_origin_changes()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile exterior = ReadProfile(root, FullContent(root), definitions, "daggerfall.charing-exterior.json");
        Dictionary<string, ReadOnlyMemory<byte>> files = PublishedUiArt(root)
            .ToDictionary(entry => entry.Path, entry => (ReadOnlyMemory<byte>)entry.Bytes);
        files[exterior.SpatialArtifact.Path] = File.ReadAllBytes(Path.Combine(root, "content", exterior.SpatialArtifact.Path));
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        host.Call(engine =>
        {
            ISpatialService spatial = DispatchProxy.Create<ISpatialService, RecordingSpatial>();
            RecordingSpatial recorded = (RecordingSpatial)(object)spatial;
            recorded.Inner = engine.Spatial;
            List<string> releases = [];
            EngineContextFake context = EngineContextFake.Create(engine.Content, spatial, new AppearanceFake(releases),
                worldOrigin: engine.WorldOrigin);
            using DaggerfallSession session = DaggerfallSession.StartNew(context.Context,
                new(definitions, exterior, DaggerfallTuning.Defaults));
            Vector3 ray = exterior.Portals[0].Position.ToVector() + Vector3.UnitY * 20f;
            SpatialHit Hit(Vector3 point) => spatial.CastRay(new(recorded.Session!, point, -Vector3.UnitY, 100f,
                default, ReadOnlyMemory<SpatialEntityCollider>.Empty, ReadOnlyMemory<ulong>.Empty,
                ReadOnlyMemory<SpatialEntityCollider>.Empty));
            SpatialHit before = Hit(ray);
            Assert.True(before.Present);
            var artifact = EngineBinaryContent.ReadSpatial(files[exterior.SpatialArtifact.Path].Span);
            var cell = artifact.Cells.First(cell => cell.Walkable);
            Vector3 routePoint = new((float)((cell.Column + .5) * artifact.Config[0]),
                (float)cell.SupportHeight, (float)((cell.Row + .5) * artifact.Config[0]));
            NavigationStepResult beforeRoute = spatial.EvaluateNavigationStep(new(recorded.Session!,
                routePoint, routePoint, 1f, 1000));
            Assert.Equal(NavigationPathOutcome.Reached, beforeRoute.Outcome);
            session.State.PlayerControl.MoveTo(new Vector3(1000f, 1f, 5f));
            long actor = session.SpawnActor("rat", new ActorPose(new WorldPoint(1002f, 1f, 5f), 0f));
            session.Sites.RebaseExteriorIfNeeded();
            Vector3 delta = session.Sites.LocalCompensation;
            Assert.NotEqual(Vector3.Zero, delta);
            SpatialHit after = Hit(ray + delta);
            Assert.True(after.Present);
            Assert.Equal(before.Distance, after.Distance, 3);
            Assert.Equal(before.Point.X + delta.X, after.Point.X, 3);
            Assert.Equal(before.Point.Z + delta.Z, after.Point.Z, 3);
            NavigationStepResult afterRoute = spatial.EvaluateNavigationStep(new(recorded.Session!,
                routePoint + delta, routePoint + delta, 1f, 1000));
            Assert.Equal(beforeRoute.Outcome, afterRoute.Outcome);
            Assert.Equal(new Vector3(2f, 0f, 0f), session.State.Actors.Get(actor).Position.ToVector()
                - session.State.PlayerControl.Position!.Value.ToVector());
            WorldOriginReadout native = engine.WorldOrigin.Read(new(recorded.Session!));
            Assert.Equal(-delta.X, native.CellX);
            Assert.Equal(-delta.Z, native.CellZ);
        });
    }

    private class RecordingSpatial : DispatchProxy
    {
        internal ISpatialService Inner { get; set; } = null!;
        internal SpatialSession? Session { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            object? result = method!.Invoke(Inner, arguments);
            if (method.Name == nameof(ISpatialService.CreateSession)) Session = (SpatialSession)result!;
            return result;
        }
    }

    [Fact]
    public void Admitted_movement_rebases_actors_portals_ground_and_presentation_and_restores_once()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        DaggerfallSiteProfile interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
        DaggerfallSiteProfiles profiles = new([exterior, interior]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, interior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(admitted,
            new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        RulesetSavePayload save;
        long actorId;
        WorldPoint actorPosition = new(1002, 1, 5);
        WorldPoint playerPosition = new(1000, 1, 5);
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(definitions, exterior, DaggerfallTuning.Defaults, identity) { Profiles = profiles }))
        {
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            actorId = session.SpawnActor("rat", new ActorPose(actorPosition, .3f));
            session.State.PlayerControl.MoveTo(playerPosition.ToVector());
            session.State.PlayerControl.Motion = session.State.PlayerControl.Motion with
            {
                SupportPreviousTranslation = new(1000, 0, 5), TetherAnchorPoint = new(1001, 3, 6),
            };
            DaggerfallInventoryPresentation inventory = new(new(session.State.Inventory, session.State.Equipment, definitions),
                definitions, new Dictionary<string, string>());
            InventoryPresentation rows = inventory.Read();
            InventoryItemPresentation gold = rows.Items.First(row => row.Definition == "gold-piece");
            session.PublishInitial();
            session.Update(new ProductUpdate(OuterUpdate(2), [Ui(JsonSerializer.Serialize(new
            {
                action = "inventory-drop", revision = engine.PublishedNested("inventory", "revision"), item = gold.Key, amount = 1,
            }))]));
            Assert.Single(DaggerfallSavePayload.Read(session.CaptureSave()).GroundContainers);
            session.Update(new ProductUpdate(OuterUpdate(3), []));
            WorldOriginCommitReceipt commit = Assert.Single(engine.OriginCommits);
            Vector3 delta = commit.LocalDelta;
            Assert.NotEqual(Vector3.Zero, delta);
            Assert.Equal(playerPosition.ToVector() + delta, session.State.PlayerControl.Position!.Value.ToVector());
            Assert.Equal(actorPosition.ToVector() + delta, session.State.Actors.Get(actorId).Position.ToVector());
            Assert.Equal(exterior.Portals[0].Position.ToVector() + delta,
                session.Sites.Projection.Portals.All.First().Portal.Position.ToVector());
            Assert.Equal(exterior.WorldAppearance.Transform.Translation + delta,
                Assert.Single(appearance.Snapshots.Last(), fact => fact.ObjectId == 1).Transform.Translation);
            Assert.Equal(new Vector3(1001, 3, 6) + delta, session.State.PlayerControl.Motion.TetherAnchorPoint);
            save = session.CaptureSave();
            DaggerfallSavePayload captured = DaggerfallSavePayload.Read(save);
            Assert.Equal(delta.X, captured.ExteriorResidency!.Value.CompensationX);
            Assert.Equal(playerPosition.X + delta.X, captured.GroundContainers[0].X);
        }
        List<string> restoredReleases = [];
        ContentFake restoredContent = new(restoredReleases);
        PopulateContent(restoredContent, exterior);
        PopulateContent(restoredContent, interior);
        SpatialFake restoredSpatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, restoredReleases);
        EngineContextFake restoredEngine = EngineContextFake.Create(restoredContent, restoredSpatial.Service,
            new AppearanceFake(restoredReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context,
            new(definitions, exterior, DaggerfallTuning.Defaults, identity) { Profiles = profiles }, save);
        Vector3 compensation = restored.Sites.LocalCompensation;
        Assert.Equal(playerPosition.ToVector() + compensation, restored.State.PlayerControl.Position!.Value.ToVector());
        Assert.Equal(actorPosition.ToVector() + compensation, restored.State.Actors.Get(actorId).Position.ToVector());
        Assert.Equal(exterior.Portals[0].Position.ToVector() + compensation,
            restored.Sites.Projection.Portals.All.First().Portal.Position.ToVector());
        Assert.True(restored.TryTransitionTo(interior.ProfileKey));
        DaggerfallSavePayload inside = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Equal(playerPosition.X, inside.Site.ReturnPose!.X);
        Assert.Equal(actorPosition.X, Assert.Single(inside.SiteDeltas).DynamicActors.Single(actor => actor.EntityId == actorId).X);
        Assert.Equal(playerPosition.X, inside.GroundContainers[0].X);
        Assert.True(restored.TryTransitionTo(exterior.ProfileKey));
        Assert.Equal(playerPosition, restored.State.PlayerControl.Position);
        Assert.Equal(actorPosition, restored.State.Actors.Get(actorId).Position);
        Assert.Equal(Vector3.Zero, restored.Sites.LocalCompensation);
    }

    [Fact]
    public void Fractional_saved_origin_is_rejected_before_native_commit()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile exterior = ReadProfile(root, FullContent(root), definitions, "daggerfall.charing-exterior.json");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, DaggerfallTuning.Defaults));
        DaggerfallExteriorCellResidencySave saved = session.Sites.CaptureExteriorResidency()!.Value;
        Assert.Throws<InvalidOperationException>(() => session.Sites.RestoreExteriorResidency(saved with { CompensationX = .5f }));
        Assert.Empty(engine.OriginCommits);
    }
}
