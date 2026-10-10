using System.Numerics;
using System.Text.Json;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Testing;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
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
            Vector3 exteriorFrame = session.Sites.ExteriorProfileFrameTranslation(exterior.ProfileKey);
            // Charing's sampled terrain height is deliberately not representable by the old
            // quarter-level placement field.  The location artifact and every source-owned pose
            // must therefore agree on this exact continuous frame.
            float quarterQuantized = MathF.Round(exteriorFrame.Y / .25F) * .25F;
            Assert.True(MathF.Abs(exteriorFrame.Y - quarterQuantized) > .001F,
                $"Charing's sampled location frame was quantized to {quarterQuantized} instead of retaining {exteriorFrame.Y}.");
            WorldPoint sourcePlayer = exterior.Project.PlayerPosition
                ?? throw new InvalidOperationException("Charing exterior has no authored player position.");
            WorldPoint livePlayer = session.State.PlayerControl.Position
                ?? throw new InvalidOperationException("Native Charing session has no live player position.");
            Assert.Equal(sourcePlayer.X + exteriorFrame.X, livePlayer.X, 3);
            // The authored start names the ground, and a new game stands the player's capsule centre on it.
            Assert.InRange(livePlayer.Y - (sourcePlayer.Y + exteriorFrame.Y), .85F, 1F);
            Assert.Equal(sourcePlayer.Z + exteriorFrame.Z, livePlayer.Z, 3);

            Assert.NotEmpty(exterior.Doors);
            DaggerfallRdbDoorDefinition sourceDoor = exterior.Doors[0];
            DaggerfallDoorView liveDoor = session.Sites.Projection.Doors.Read(sourceDoor.Id);
            Assert.Equal(sourceDoor.Position.X + exteriorFrame.X, liveDoor.Pose.Translation.X, 3);
            Assert.Equal(sourceDoor.Position.Y + exteriorFrame.Y, liveDoor.Pose.Translation.Y, 3);
            Assert.Equal(sourceDoor.Position.Z + exteriorFrame.Z, liveDoor.Pose.Translation.Z, 3);

            // Probe an admitted neighboring map pixel without a location artifact.  This keeps
            // the ray on the real generated terrain mesh, while the city ray below remains the
            // native artifact collision check.
            DaggerfallExteriorCellResidencySave admitted = session.Sites.CaptureExteriorResidency()
                ?? throw new InvalidOperationException("Native Charing session did not admit an exterior terrain window.");
            HashSet<DaggerfallExteriorCellId> locationCells = definitions.Locations.Records
                .Where(record => record.Exterior is not null)
                .Select(record => new DaggerfallExteriorCellId(record.Exterior!.MapPixelX, record.Exterior.MapPixelY))
                .ToHashSet();
            DaggerfallExteriorCellId terrainCell = default;
            bool foundTerrainCell = false;
            for (int row = -DaggerfallExteriorCellResidency.StreamingRadius;
                 row <= DaggerfallExteriorCellResidency.StreamingRadius && !foundTerrainCell; row++)
            {
                for (int column = -DaggerfallExteriorCellResidency.StreamingRadius;
                     column <= DaggerfallExteriorCellResidency.StreamingRadius; column++)
                {
                    DaggerfallExteriorCellId candidate = new(admitted.Center.X + column,
                        admitted.Center.Y + row);
                    if ((uint)candidate.X >= (uint)definitions.Terrain.Width
                        || (uint)candidate.Y >= (uint)definitions.Terrain.Height
                        || locationCells.Contains(candidate)) continue;
                    terrainCell = candidate;
                    foundTerrainCell = true;
                    break;
                }
            }
            Assert.True(foundTerrainCell, "Charing's admitted terrain window had no non-city collision cell to probe.");
            DaggerfallTerrainSurface terrain = DaggerfallTerrainSurfaceBuilder.Build(
                definitions.Terrain, terrainCell.X, terrainCell.Y);
            DaggerfallExteriorWorldOrigin terrainOrigin = admitted.WorldOrigin;
            int quadsPerSide = DaggerfallTerrainSurfaceBuilder.SampleDimension - 1;
            int centerQuad = quadsPerSide / 2;
            int centerTriangle = ((centerQuad * quadsPerSide) + centerQuad) * 2;
            Triangle terrainTriangle = terrain.Triangles[centerTriangle];
            Vector3 triangleA = terrain.Vertices[checked((int)terrainTriangle.A)];
            Vector3 triangleB = terrain.Vertices[checked((int)terrainTriangle.B)];
            Vector3 triangleC = terrain.Vertices[checked((int)terrainTriangle.C)];
            // A shared grid vertex can be rejected by native triangle edge rules. Probe a
            // strictly interior point of the admitted triangle while retaining the exact mesh
            // plane, rather than approximating it with a bilinear sample.
            Vector3 terrainPoint = terrainOrigin.LocalTranslation(terrainCell)
                + (triangleA * .2F) + (triangleB * .3F) + (triangleC * .5F);
            SpatialHit terrainHit = Hit(terrainPoint + Vector3.UnitY * 20F, 50F);
            Assert.True(terrainHit.Present);
            Assert.Equal(terrainPoint.X, terrainHit.Point.X, 2);
            Assert.Equal(terrainPoint.Y, terrainHit.Point.Y, 2);
            Assert.Equal(terrainPoint.Z, terrainHit.Point.Z, 2);

            Vector3 ray = exterior.Portals[0].Position.ToVector() + exteriorFrame + Vector3.UnitY * 20f;
            SpatialHit Hit(Vector3 point, float distance = 100F) => spatial.CastRay(new(recorded.Session!, point, -Vector3.UnitY, distance,
                default, ReadOnlyMemory<SpatialEntityCollider>.Empty, ReadOnlyMemory<ulong>.Empty,
                ReadOnlyMemory<SpatialEntityCollider>.Empty));
            SpatialHit before = Hit(ray);
            Assert.True(before.Present);
            var artifact = EngineBinaryContent.ReadSpatial(files[exterior.SpatialArtifact.Path].Span);
            var cell = artifact.Cells.First(cell => cell.Walkable);
            Vector3 routePoint = exteriorFrame + new Vector3((float)((cell.Column + .5) * artifact.Config[0]),
                (float)cell.SupportHeight, (float)((cell.Row + .5) * artifact.Config[0]));
            NavigationStepResult beforeRoute = spatial.EvaluateNavigationStep(new(recorded.Session!,
                routePoint, routePoint, 1f, 1000));
            Assert.Equal(NavigationPathOutcome.Reached, beforeRoute.Outcome);
            SpatialHit cityFloor = Hit(routePoint + Vector3.UnitY * .5F, 1F);
            Assert.True(cityFloor.Present);
            Assert.Equal(routePoint.X, cityFloor.Point.X, 2);
            Assert.Equal(routePoint.Y, cityFloor.Point.Y, 2);
            Assert.Equal(routePoint.Z, cityFloor.Point.Z, 2);

            // Advance only the calendar: this admits the real Charing source population without
            // running a movement step, so its first actor pose remains an exact source-frame fact.
            session.AdvanceElapsedTime(8 * 60 * 60);
            DaggerfallNpc[] civilians = [.. session.State.Npcs.All.Where(npc =>
                npc.StableKey.StartsWith("population/", StringComparison.Ordinal))];
            Assert.NotEmpty(civilians);
            DaggerfallNpc civilian = civilians[0];
            DaggerfallPopulationPlacement sourcePopulation = Assert.Single(exterior.Population,
                placement => placement.Id == civilian.StableKey);
            Assert.True(session.State.Actors.TryGet(civilian.DurableId, out ActorState? civilianActor));
            Assert.Equal(sourcePopulation.Position.X + exteriorFrame.X, civilianActor!.Position.X, 3);
            Assert.Equal(sourcePopulation.Position.Y + exteriorFrame.Y, civilianActor.Position.Y, 3);
            Assert.Equal(sourcePopulation.Position.Z + exteriorFrame.Z, civilianActor.Position.Z, 3);

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
            new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        RulesetSavePayload save;
        long actorId;
        long npcId;
        WorldPoint npcPosition = new(1003, 1, 5);
        WorldPoint actorPosition = new(1002, 1, 5);
        WorldPoint playerPosition = new(1000, 1, 5);
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(definitions, exterior, DaggerfallTuning.Defaults, identity) { Profiles = profiles }))
        {
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            actorId = session.SpawnActor("rat", new ActorPose(actorPosition, .3f));
            var address = exterior.BillboardSprites.Keys.First(value => value != (216, 0));
            npcId = session.State.Npcs.RegisterStable(DaggerfallNpcKind.Questor, "origin-npc",
                new(exterior.Site!.Value.Region, definitions.Locations.Records.Single(site => site.Id == exterior.Site).Name, "origin"),
                new("Breton", "Female", address.Archive, address.Record, 1, 0), "quest person", ["talk"]);
            session.State.Npcs.Place(npcId, exterior.ProfileKey, npcPosition);
            session.ReconcileNpcProjection();
            EntityId npcBody = session.State.Actors.Entities.Resolve(ActorsState.Identity(npcId));
            WorldPoint npcBeforeRebase = DaggerfallNpcBody.Position(session.State.Actors.Store, npcBody);
            session.State.Actors.Store.Set(session.State.Actors.Get(actorId).Actor.Entity, EngineComponentTypes.CharacterMotion,
                session.State.Actors.Store.Get(session.State.Actors.Get(actorId).Actor.Entity, EngineComponentTypes.CharacterMotion)
                    with { SupportPreviousTranslation = actorPosition.ToVector() });
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
            AppearanceFact worldBeforeHorizontalRebaseFact = Assert.Single(appearance.Snapshots.Last(),
                fact => fact.ObjectId == DaggerfallPresentationObjectIds.WorldMesh(DaggerfallPresentationObjectIds.ActiveLocationSlot, 0));
            Vector3 worldBeforeHorizontalRebase = new(
                worldBeforeHorizontalRebaseFact.Transform.Translation.X,
                worldBeforeHorizontalRebaseFact.Transform.Translation.Y,
                worldBeforeHorizontalRebaseFact.Transform.Translation.Z);
            session.Update(new ProductUpdate(OuterUpdate(2), [Ui(JsonSerializer.Serialize(new
            {
                action = "inventory-drop", revision = engine.PublishedNested("inventory", "revision"), item = gold.Key, amount = 1,
            }))]));
            Assert.Single(DaggerfallSavePayload.Read(session.CaptureSave()).GroundContainers);
            session.Update(new ProductUpdate(OuterUpdate(3), []));
            // The admitted Charing frame itself crosses the vertical origin threshold during the
            // first update; the explicit move above then contributes the horizontal commit.
            Assert.Equal(2, engine.OriginCommits.Count);
            WorldOriginCommitReceipt commit = engine.OriginCommits[^1];
            Vector3 delta = commit.LocalDelta;
            Assert.NotEqual(Vector3.Zero, delta);
            Assert.Equal(playerPosition.ToVector() + delta, session.State.PlayerControl.Position!.Value.ToVector());
            Assert.Equal(actorPosition.ToVector() + delta, session.State.Actors.Get(actorId).Position.ToVector());
            // The Engine EntityOriginRebaser moved the projected quest person's Transform and the
            // actor's stored character motion in the same commit; no global position outlives it.
            Assert.Equal(npcBeforeRebase.ToVector() + delta,
                DaggerfallNpcBody.Position(session.State.Actors.Store, npcBody).ToVector());
            Assert.Equal(actorPosition.ToVector() + delta, session.State.Actors.Store.Get(
                session.State.Actors.Get(actorId).Actor.Entity, EngineComponentTypes.CharacterMotion).SupportPreviousTranslation);
            Assert.Empty(session.State.Actors.Store.Query(DaggerfallSiteLifecycle.GlobalPositions));
            Vector3 activePortalProfile = session.Sites.LocalToProfile(
                session.Sites.Projection.Portals.All.First().Portal.Position.ToVector());
            Assert.Equal(exterior.Portals[0].Position.X, activePortalProfile.X, 3);
            Assert.Equal(exterior.Portals[0].Position.Y, activePortalProfile.Y, 3);
            Assert.Equal(exterior.Portals[0].Position.Z, activePortalProfile.Z, 3);
            Vector3 worldAfterHorizontalRebase = Assert.Single(appearance.Snapshots.Last(),
                fact => fact.ObjectId == DaggerfallPresentationObjectIds.WorldMesh(DaggerfallPresentationObjectIds.ActiveLocationSlot, 0)).Transform.Translation;
            Assert.Equal(worldBeforeHorizontalRebase.X + delta.X, worldAfterHorizontalRebase.X, 3);
            Assert.Equal(worldBeforeHorizontalRebase.Y + delta.Y, worldAfterHorizontalRebase.Y, 3);
            Assert.Equal(worldBeforeHorizontalRebase.Z + delta.Z, worldAfterHorizontalRebase.Z, 3);
            Assert.Equal(new Vector3(1001, 3, 6) + delta, session.State.PlayerControl.Motion.TetherAnchorPoint);
            save = session.CaptureSave();
            DaggerfallSavePayload captured = DaggerfallSavePayload.Read(save);
            // The save carries the Engine origin cell read back from WorldOrigin, not a product copy.
            Assert.Equal(-(long)delta.X, captured.ExteriorResidency!.Value.EngineOriginCellX);
            Assert.Equal(engine.OriginCommits[^1].OriginAfterCellX, captured.ExteriorResidency!.Value.EngineOriginCellX);
            Assert.Equal(playerPosition.ToVector() + delta,
                new Vector3(captured.Player.X, captured.Player.Y, captured.Player.Z));
            DaggerfallDynamicActorSave capturedActor = Assert.Single(captured.DynamicActors,
                actor => actor.EntityId == actorId);
            Assert.Equal(actorPosition.ToVector() + delta,
                new Vector3(capturedActor.X, capturedActor.Y, capturedActor.Z));
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
        DaggerfallSavePayload savedPayload = DaggerfallSavePayload.Read(save);
        Assert.Equal(new Vector3(savedPayload.Player.X, savedPayload.Player.Y, savedPayload.Player.Z),
            restored.State.PlayerControl.Position!.Value.ToVector());
        DaggerfallDynamicActorSave savedActor = Assert.Single(savedPayload.DynamicActors,
            actor => actor.EntityId == actorId);
        Assert.Equal(new Vector3(savedActor.X, savedActor.Y, savedActor.Z),
            restored.State.Actors.Get(actorId).Position.ToVector());
        var npcEntity = restored.State.Actors.Entities.Resolve(ActorsState.Identity(npcId));
        // Quest NPC registry positions are profile coordinates.  Re-entry projects them through
        // both the saved origin compensation and Charing's admitted continuous terrain frame;
        // resident player/actor poses already carry that frame in their saved local coordinates.
        WorldPoint expectedNpcLocal = restored.Sites.ProfileToLocal(npcPosition);
        Vector3 restoredNpcLocal = DaggerfallNpcBody.Position(restored.State.Actors.Store, npcEntity).ToVector();
        Assert.Equal(expectedNpcLocal.ToVector(), restoredNpcLocal);
        Assert.Equal(restoredNpcLocal,
            restored.Dialogue.NpcTargets().Single(target => target.Identity.Value == (ulong)npcId).Position.ToVector());
        Vector3 restoredNpcProfile = restored.Sites.LocalToProfile(restoredNpcLocal);
        Assert.Equal(npcPosition.X, restoredNpcProfile.X, 3);
        Assert.Equal(npcPosition.Y, restoredNpcProfile.Y, 3);
        Assert.Equal(npcPosition.Z, restoredNpcProfile.Z, 3);
        Vector3 restoredPortalProfile = restored.Sites.LocalToProfile(
            restored.Sites.Projection.Portals.All.First().Portal.Position.ToVector());
        Assert.Equal(exterior.Portals[0].Position.X, restoredPortalProfile.X, 3);
        Assert.Equal(exterior.Portals[0].Position.Y, restoredPortalProfile.Y, 3);
        Assert.Equal(exterior.Portals[0].Position.Z, restoredPortalProfile.Z, 3);
        // The explicit test poses are local after exterior admission.  Leaving the exterior
        // normalizes the Engine origin before the source return pose and detached actor delta are
        // captured: the player carries the inverse saved compensation into the return pose, while
        // the actor delta removes the admitted terrain frame and stores the canonical profile pose.
        Vector3 restoredPlayerLocal = restored.State.PlayerControl.Position!.Value.ToVector();
        Vector3 expectedPlayerProfile = restored.Sites.LocalToProfile(restoredPlayerLocal);
        Vector3 expectedActorProfile = restored.Sites.LocalToProfile(
            restored.State.Actors.Get(actorId).Position.ToVector());
        Vector3 expectedReturnPlayer = restoredPlayerLocal - restored.Sites.LocalCompensation;
        Assert.True(restored.TryTransitionTo(interior.ProfileKey));
        DaggerfallSavePayload inside = DaggerfallSavePayload.Read(restored.CaptureSave());
        DaggerfallSiteReturnPoseSave returnPose = inside.Site.ReturnPose!;
        Assert.Equal(expectedReturnPlayer.X, returnPose.X, 3);
        Assert.Equal(expectedReturnPlayer.Y, returnPose.Y, 3);
        Assert.Equal(expectedReturnPlayer.Z, returnPose.Z, 3);
        DaggerfallDynamicActorSave insideActor = Assert.Single(inside.SiteDeltas).DynamicActors.Single(
            actor => actor.EntityId == actorId);
        Assert.Equal(expectedActorProfile.X, insideActor.X, 3);
        Assert.Equal(expectedActorProfile.Y, insideActor.Y, 3);
        Assert.Equal(expectedActorProfile.Z, insideActor.Z, 3);
        // The interior also admits its own property storage; the dropped pile is the exterior one.
        DaggerfallGroundContainerSave insideGround = Assert.Single(inside.GroundContainers,
            ground => ground.Profile.Require() == exterior.ProfileKey);
        Assert.Equal(expectedReturnPlayer.X, insideGround.X, 3);
        Assert.Equal(expectedReturnPlayer.Y, insideGround.Y, 3);
        Assert.Equal(expectedReturnPlayer.Z, insideGround.Z, 3);
        DurableIdentityReference actorIdentity = new(DurableIdentityKind.Actor, checked((ulong)actorId));
        Assert.Equal(DurableEntityResolution.Unloaded,
            restored.State.Actors.Entities.Classify(actorIdentity, restored.State.Npcs.Identities!));

        // Saving inside the destination must retain the boundary site's issued identity without
        // a native entity. A fresh session distinguishes it from an ID the session never issued.
        List<string> insideReleases = [];
        ContentFake insideContent = new(insideReleases);
        PopulateContent(insideContent, exterior);
        PopulateContent(insideContent, interior);
        SpatialFake insideSpatial = SpatialFake.Create(interior.SpatialArtifact.Sha256, insideReleases);
        EngineContextFake insideEngine = EngineContextFake.Create(insideContent, insideSpatial.Service,
            new AppearanceFake(insideReleases));
        using DaggerfallSession fromInside = DaggerfallSession.Restore(insideEngine.Context,
            new(definitions, exterior, DaggerfallTuning.Defaults, identity) { Profiles = profiles }, restored.CaptureSave());
        Assert.Equal(DurableEntityResolution.Unloaded,
            fromInside.State.Actors.Entities.Classify(actorIdentity, fromInside.State.Npcs.Identities!));
        Assert.Equal(DurableEntityResolution.NeverIssued,
            fromInside.State.Actors.Entities.Classify(new(DurableIdentityKind.Actor, ulong.MaxValue), fromInside.State.Npcs.Identities!));
        Assert.True(fromInside.TryTransitionTo(exterior.ProfileKey));
        Assert.Equal(DurableEntityResolution.Materialized,
            fromInside.State.Actors.Entities.Classify(actorIdentity, fromInside.State.Npcs.Identities!));
        WorldPoint savedActorProfile = new(insideActor.X, insideActor.Y, insideActor.Z);
        WorldPoint fromInsideActorPosition = fromInside.Sites.ProfileToLocal(savedActorProfile);
        Assert.Equal(fromInsideActorPosition.X, fromInside.State.Actors.Get(actorId).Position.X, 3);
        Assert.Equal(fromInsideActorPosition.Y, fromInside.State.Actors.Get(actorId).Position.Y, 3);
        Assert.Equal(fromInsideActorPosition.Z, fromInside.State.Actors.Get(actorId).Position.Z, 3);
        Assert.Single(fromInside.DynamicActors, actor => actor.Key == actorId);
        Assert.Equal(returnPose.X, fromInside.State.PlayerControl.Position!.Value.X, 3);
        Assert.Equal(returnPose.Y, fromInside.State.PlayerControl.Position!.Value.Y, 3);
        Assert.Equal(returnPose.Z, fromInside.State.PlayerControl.Position!.Value.Z, 3);
        Assert.True(restored.TryTransitionTo(exterior.ProfileKey));
        WorldPoint restoredActorPosition = restored.Sites.ProfileToLocal(savedActorProfile);
        Assert.Equal(restoredActorPosition.X, restored.State.Actors.Get(actorId).Position.X, 3);
        Assert.Equal(restoredActorPosition.Y, restored.State.Actors.Get(actorId).Position.Y, 3);
        Assert.Equal(returnPose.X, restored.State.PlayerControl.Position!.Value.X, 3);
        Assert.Equal(returnPose.Y, restored.State.PlayerControl.Position!.Value.Y, 3);
        Assert.Equal(returnPose.Z, restored.State.PlayerControl.Position!.Value.Z, 3);
        Vector3 restoredPlayerProfile = restored.Sites.LocalToProfile(
            restored.State.PlayerControl.Position!.Value.ToVector());
        Assert.Equal(expectedPlayerProfile.X, restoredPlayerProfile.X, 3);
        Assert.Equal(expectedPlayerProfile.Y, restoredPlayerProfile.Y, 3);
        Assert.Equal(expectedPlayerProfile.Z, restoredPlayerProfile.Z, 3);
        Assert.Equal(Vector3.Zero, restored.Sites.LocalCompensation);
    }

    [Fact]
    public void Session_rebase_keeps_pending_encounter_local_through_save_restore()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        var composition = new DaggerfallSessionComposition(definitions, exterior, DaggerfallTuning.Defaults);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.State.PlayerControl.MoveTo(new Vector3(1000, 1, 5));
        session.State.PlayerControl.YawRadians = .3f;
        var encounter = session.QueueEncounter(new(DaggerfallEncounterContext.WildernessDay, 1, Climate: 224));
        Assert.NotNull(encounter.ActorDefinition);
        session.Sites.RebaseExteriorIfNeeded();
        Vector3 delta = session.Sites.LocalCompensation;
        RulesetSavePayload saved = session.CaptureSave();
        var payload = DaggerfallSavePayload.Read(saved);
        Assert.Equal(encounter.Pose.Position.ToVector() + delta, Assert.Single(payload.Encounters.Resolved).Pose.Position.ToVector());
        Assert.Equal(.3f, Assert.Single(payload.Encounters.Resolved).Pose.HeadingYawRadians);
        Assert.Null(Assert.Single(payload.Encounters.Resolved).SpawnedActorId);
        List<string> restoredReleases = [];
        ContentFake restoredContent = new(restoredReleases);
        PopulateContent(restoredContent, exterior);
        SpatialFake restoredSpatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, restoredReleases);
        restoredSpatial.KeepPosition = true;
        EngineContextFake restoredEngine = EngineContextFake.Create(restoredContent, restoredSpatial.Service,
            new AppearanceFake(restoredReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, composition, saved);
        restored.Update(new ProductUpdate(OuterUpdate(1), []));
        var materialized = Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).Encounters.Resolved);
        Assert.NotNull(materialized.SpawnedActorId);
        // Actor materialization grounds the selected mobile through Spatial. Its horizontal
        // position must still use the saved local frame exactly once.
        WorldPoint live = restored.State.Actors.Get(materialized.SpawnedActorId!.Value).Position;
        Assert.Equal(materialized.Pose.Position.X, live.X);
        Assert.Equal(materialized.Pose.Position.Z, live.Z);
        Assert.Equal(encounter.Pose.Position.ToVector() + delta, materialized.Pose.Position.ToVector());
    }

    [Fact]
    public void A_rebasing_step_leaves_the_water_trigger_rows_in_the_new_frame_for_actor_navigation()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new DaggerfallSessionComposition(definitions, exterior, DaggerfallTuning.Defaults));
        session.State.PlayerControl.MoveTo(new Vector3(1000, 1, 5));

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        // The step moved the origin under the player, so every water volume now sits in a new frame.
        Assert.NotEqual(Vector3.Zero, session.Sites.LocalCompensation);
        CharacterWaterVolume[] water = session.Sites.CharacterEnvironment(session.State.PlayerControl.Motion).WaterVolumes.ToArray();
        Assert.NotEmpty(water);
        // Actor navigation asks the Engine's trigger rows whether an actor is in water; those rows are
        // the volumes as they now stand, not as they stood before the origin moved.
        foreach (CharacterWaterVolume volume in water)
        {
            SpatialEntityCollider row = spatial.TriggerRows[volume.Trigger];
            Assert.Equal(volume.Minimum, row.Min);
            Assert.Equal(volume.Maximum, row.Max);
        }
    }

    [Fact]
    public void An_ordinary_step_keeps_the_water_trigger_rows_beside_the_action_triggers_for_actor_navigation()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new DaggerfallSessionComposition(definitions, exterior, DaggerfallTuning.Defaults));
        Assert.True(session.State.DungeonActions.ContainsKey(exterior.ProfileKey));
        session.State.PlayerControl.MoveTo(new Vector3(5, 1, 5));
        int reconcilesBefore = spatial.TriggerReconcileCalls;

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        // A step that moves the player and leaves the origin alone still reconciles after movement, with
        // the action graph admitted. Engine keeps only the rows of the last reconcile, so the water
        // volumes must be in it: actor navigation asks those rows whether an actor stands in water.
        Assert.Equal(Vector3.Zero, session.Sites.LocalCompensation);
        Assert.True(spatial.TriggerReconcileCalls - reconcilesBefore >= 2);
        CharacterWaterVolume[] water = session.Sites.CharacterEnvironment(session.State.PlayerControl.Motion).WaterVolumes.ToArray();
        Assert.NotEmpty(water);
        foreach (CharacterWaterVolume volume in water)
        {
            SpatialEntityCollider row = spatial.TriggerRows[volume.Trigger];
            Assert.Equal(volume.Minimum, row.Min);
            Assert.Equal(volume.Maximum, row.Max);
            Assert.Contains(volume.Trigger, spatial.TriggersAt((volume.Minimum + volume.Maximum) / 2f));
        }
    }

    [Fact]
    public void A_transition_after_walking_past_the_local_envelope_resets_the_origin_and_keeps_a_far_actor_through_save()
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
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        RulesetSavePayload save;
        long far;
        Vector3 farProfile;
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, DaggerfallTuning.Defaults)))
        {
            session.AdmitSiteProfiles(profiles);
            // Walk 20 km east; the in-play rebase keeps the player and a nearby rat close to the origin.
            session.State.PlayerControl.MoveTo(new Vector3(20000f, 1f, 5f));
            far = session.SpawnActor("rat", new ActorPose(new WorldPoint(20002f, 1f, 5f), 0f));
            Assert.True(session.Sites.RebaseExteriorIfNeeded());
            farProfile = session.Sites.LocalToProfile(session.State.Actors.Get(far).Position.ToVector());

            // Returning the origin to cell zero leaves the rat beyond the local envelope. The reset
            // excludes it from the Engine batch instead of refusing, and still carries it into the
            // profile frame its site capture reads.
            Assert.True(session.TryTransitionTo(interior.ProfileKey));
            Assert.Equal(interior.ProfileKey, session.Sites.ActiveProfile);
            WorldOriginCommitReceipt reset = engine.OriginCommits[^1];
            Assert.Equal((0L, 0L, 0L), (reset.OriginAfterCellX, reset.OriginAfterCellY, reset.OriginAfterCellZ));
            Assert.True(reset.ExcludedEntityCount >= 1);
            save = session.CaptureSave();
        }

        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(save);
        var delta = Assert.Single(captured.SiteDeltas, value => value.Profile.Require() == exterior.ProfileKey);
        var rat = Assert.Single(delta.DynamicActors, actor => actor.EntityId == far);
        Assert.Equal(farProfile.X, rat.X, 2);
        Assert.Equal(farProfile.Z, rat.Z, 2);

        List<string> resumedReleases = [];
        ContentFake resumedContent = new(resumedReleases);
        PopulateContent(resumedContent, exterior);
        PopulateContent(resumedContent, interior);
        SpatialFake resumedSpatial = SpatialFake.Create(interior.SpatialArtifact.Sha256, resumedReleases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(resumedReleases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(admitted, new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context,
            new(definitions, exterior, DaggerfallTuning.Defaults, identity) { Profiles = profiles }, save);
        Assert.True(restored.TryTransitionTo(exterior.ProfileKey));
        DaggerfallSavePayload returned = DaggerfallSavePayload.Read(restored.CaptureSave());
        // Twenty kilometres from Charing its location closure stays unloaded, so the rat waits detached in
        // the site delta, at the profile position the reset carried it to, until the player comes back.
        Assert.False(restored.State.Actors.TryGet(far, out _));
        var retained = Assert.Single(returned.SiteDeltas.SelectMany(value => value.DynamicActors), actor => actor.EntityId == far);
        Assert.Equal(farProfile.X, retained.X, 2);
        Assert.Equal(farProfile.Z, retained.Z, 2);
    }

    [Fact]
    public void Same_profile_relocation_updates_the_exterior_window_and_rebases_before_returning()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile exterior = ReadProfile(root, FullContent(root), definitions, "daggerfall.charing-exterior.json");
        List<string> releases = [];
        ContentFake content = new(releases); PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, DaggerfallTuning.Defaults));
        var before = session.Sites.CaptureExteriorResidency()!.Value;
        WorldPoint destination = new(4 * DaggerfallExteriorCellResidency.CellSize, 1, 1);
        Assert.True(session.Sites.TryRelocatePlayer(exterior.ProfileKey, new("recall", destination, .7f, .1f)));
        Assert.Single(engine.OriginCommits);
        var after = session.Sites.CaptureExteriorResidency()!.Value;
        Assert.Equal(new DaggerfallExteriorCellId(before.Origin.X + 4, before.Origin.Y), after.Center);
        Assert.Equal(after.Center, session.Sites.CurrentExteriorCell());
        Assert.Equal(destination.ToVector(), session.Sites.LocalToProfile(session.State.PlayerControl.Position!.Value.ToVector()));
        var saved = DaggerfallSavePayload.Read(session.CaptureSave());
        _ = saved.ResolveRestore(definitions, exterior);
        var malformed = saved with { ExteriorResidency = after with { Center = before.Center } };
        Assert.Throws<ArgumentException>(() => malformed.ResolveRestore(definitions, exterior));
    }

    [Theory]
    [InlineData(501f)]
    [InlineData(-501f)]
    public void Vertical_origin_commit_preserves_world_height_and_does_not_move_the_horizontal_cell(float height)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile exterior = ReadProfile(root, FullContent(root), definitions, "daggerfall.charing-exterior.json");
        List<string> releases = [];
        ContentFake content = new(releases); PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, DaggerfallTuning.Defaults));
        var before = session.Sites.CaptureExteriorResidency()!.Value;
        WorldPoint start = session.State.PlayerControl.Position
            ?? throw new InvalidOperationException("The exterior player has no position.");
        // Rebase thresholds are evaluated in the current local frame. The authored profile is
        // elevated by Charing's sampled terrain frame, so offset the probe back into that frame
        // while keeping the asserted durable profile pose explicit.
        float frameHeight = session.Sites.ExteriorProfileFrameTranslation(exterior.ProfileKey).Y;
        WorldPoint desiredProfilePosition = new(start.X, height - frameHeight, start.Z);
        WorldPoint desiredLocalPosition = session.Sites.ProfileToLocal(desiredProfilePosition);
        session.State.PlayerControl.MoveTo(desiredLocalPosition.ToVector());
        session.Sites.RebaseExteriorIfNeeded();
        Assert.Single(engine.OriginCommits);
        Assert.Equal(before.Center, session.Sites.CurrentExteriorCell());
        Vector3 actualProfilePosition = session.Sites.LocalToProfile(session.State.PlayerControl.Position!.Value.ToVector());
        Assert.Equal(desiredProfilePosition.X, actualProfilePosition.X, 3);
        Assert.Equal(desiredProfilePosition.Y, actualProfilePosition.Y, 3);
        Assert.Equal(desiredProfilePosition.Z, actualProfilePosition.Z, 3);
        // Engine origin cells are integral; the sampled location frame is fractional, so the
        // rebase retains the local height residual while preserving the requested profile height.
        Assert.Equal(desiredLocalPosition.Y - MathF.Floor(desiredLocalPosition.Y),
            session.State.PlayerControl.Position.Value.Y, 3);
        Assert.Equal(-MathF.Floor(desiredLocalPosition.Y), session.Sites.LocalCompensation.Y);
    }

    [Fact]
    public void Teleport_recall_does_not_convert_a_committed_origin_failure_into_a_recoverable_refusal()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        DaggerfallSiteProfile interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, interior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(definitions, exterior, DaggerfallTuning.Defaults) { Profiles = new([exterior, interior]) });

        long nextSequence = 1;
        void RequestTeleport()
        {
            DaggerfallSpellEffectDefinition setting = new("teleport", 43, -1, 10, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1);
            DaggerfallSpellDefinition spell = new("test.teleport", 1, false, "Mysticism", 4, 0, 0, 0, [setting]);
            var magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
            magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(10000);
            DaggerfallCasting casting = new(definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } },
                session.State.Effects, _ => session.State.Actors.Player.Actor, session.MagicProfile,
                _ => true, _ => { }, _ => { }, RandomMaximum.Create(), 1,
                nextSequence: nextSequence, playerKnowsSpell: _ => true, casterLevel: _ => 1);
            Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
            casting.Deliver(casting.Release(1, true).Bundle!, [1]);
            nextSequence = casting.NextSequence;
        }

        Assert.True(session.TryTransitionTo(interior.ProfileKey));
        RequestTeleport();
        session.ChooseTeleport(session.TeleportView!.Revision, "anchor");
        Assert.True(session.TryTransitionTo(exterior.ProfileKey));
        session.State.PlayerControl.MoveTo(new Vector3(1000, 1, 5));
        session.Sites.RebaseExteriorIfNeeded();
        RequestTeleport();
        engine.FailNextCameraUpdate();
        DaggerfallOriginCommitException failure = Assert.Throws<DaggerfallOriginCommitException>(
            () => session.ChooseTeleport(session.TeleportView!.Revision, "recall"));
        Assert.Contains("inconsistent world coordinates", failure.Message);
        Assert.Equal(exterior.ProfileKey, session.Sites.ActiveProfile);
        Assert.Equal(2, engine.OriginCommits.Count);
    }

    [Fact]
    public void Post_commit_camera_failure_is_explicit_and_cannot_be_reported_as_a_recoverable_transition()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent admitted = FullContent(root);
        DaggerfallSiteProfile exterior = ReadProfile(root, admitted, definitions, "daggerfall.charing-exterior.json");
        DaggerfallSiteProfile interior = ReadProfile(root, admitted, definitions, "daggerfall.charing-interior-1-1-0.json");
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, interior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context,
            new(definitions, exterior, DaggerfallTuning.Defaults) { Profiles = new([exterior, interior]) });
        session.State.PlayerControl.MoveTo(new Vector3(1000, 1, 5));
        session.Sites.RebaseExteriorIfNeeded();
        Assert.Single(engine.OriginCommits);
        engine.FailNextCameraUpdate();
        DaggerfallOriginCommitException failure = Assert.Throws<DaggerfallOriginCommitException>(() => session.TryTransitionTo(interior.ProfileKey));
        Assert.Contains("origin was committed", failure.Message);
        Assert.Contains("inconsistent world coordinates", failure.Message);
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal(2, engine.OriginCommits.Count);
        Assert.Equal(Vector3.Zero, session.Sites.LocalCompensation);
        Assert.Equal(exterior.ProfileKey, session.Sites.ActiveProfile);
        Assert.Equal(new WorldPoint(1000, 1, 5), session.State.PlayerControl.Position);
    }
}
