using System.Numerics;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Site appearance lifecycle: world visuals, ground containers, lights, texture admission and release on failure.</summary>
public sealed class SiteAppearanceLifecycleTests
{
    [Theory]
    [InlineData(.9f)]
    [InlineData(1.2f)]
    public void Close_enemy_and_its_corpse_are_visible_in_the_canonical_engine_snapshot(float distance)
    {
        string root = TestData.RepositoryRoot;
        var definitions = TestPayload.Definitions;
        var inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases); PopulateContent(content, inputs);
        AppearanceFake graphics = new(releases);
        var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        var engine = EngineContextFake.Create(content, spatial.Service, graphics);
        using var session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        var enemy = session.State.Actors.Get(inputs.Project.Actors.Values.First().EntityId);
        var position = new WorldRpg.Kit.Controls.WorldPoint(0, 0, distance);
        session.State.PlayerControl.MoveTo(Vector3.Zero);
        enemy.ApplyPose(new ActorPose(position, 0));
        session.PublishInitial();
        var live = Assert.Single(graphics.Snapshots.Last(), fact => fact.ObjectId == (ulong)enemy.DurableId);
        Assert.True(live.Visible); Assert.Equal(RenderLayer.Scene, live.Layer);
        Assert.Equal(position.ToVector(), live.Transform.Translation);
        enemy.Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).SetCurrent(0);
        session.PublishInitial();
        var corpse = Assert.Single(graphics.Snapshots.Last(), fact => fact.ObjectId == (ulong)enemy.DurableId);
        Assert.True(corpse.Visible); Assert.Equal(RenderLayer.Scene, corpse.Layer);
        Assert.Equal(position.ToVector(), corpse.Transform.Translation);
        Assert.NotSame(live.Appearance, corpse.Appearance);
        Assert.True(DaggerfallTuning.Defaults.Camera.NearPlane < distance);
    }

    [Fact]
    public void Selected_RDB_door_visuals_share_their_runtime_pose_and_keep_the_appearance_snapshot_unique()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        AppearanceFake graphics = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, graphics, PerceptionFake.Create().Service);

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.PublishInitial();

        AppearanceFact[] snapshot = Assert.Single(graphics.Snapshots);
        Assert.Equal(snapshot.Length, snapshot.Select(fact => fact.ObjectId).Distinct().Count());
        // The world mesh, each door, each dungeon action model, and the ranged-flight arrow.
        Assert.Equal(1 + inputs.Doors.Count + inputs.DungeonActionModels.Count + 1, graphics.StaticMeshPaths.Count);
        foreach (DaggerfallRdbDoorDefinition door in inputs.Doors)
            Assert.Contains(snapshot, fact => fact.Transform.Translation == door.Position);
    }

    [Fact]
    public void Every_authored_enemy_has_a_live_appearance_at_its_world_position()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        AppearanceFake appearance = new(releases);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        // The outer update owns final publication: simulation completes first, then one snapshot.
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d), []));

        AppearanceFact[] snapshot = appearance.Snapshots.Last();
        foreach (AuthoredActor actor in inputs.Project.Actors.Values)
        {
            Assert.False(session.State.Actors.Get(actor.EntityId).IsDefeated);
            AppearanceFact fact = Assert.Single(snapshot, value => value.ObjectId == checked((ulong)actor.EntityId));
            Assert.True(fact.Visible);
            Assert.Equal(RenderLayer.Scene, fact.Layer);
            Assert.Equal(new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One), fact.Transform);
        }
    }

    [Fact]
    public void Ground_container_appearance_tracks_drop_empty_transition_and_restore_projection()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        NormalizedBillboardSprite sprite = new("sprite/treasure.png", Hash, 39, 26,
            [new NormalizedAtlasFrame(0, 0, 0, 39, 26)], 0, new Vector2(.5F, .5F), new Vector2(.975F, .65F));
        DaggerfallWorldProfileKey profile = new DaggerfallWorldProfileKey(new DaggerfallSiteId(1, 2), DaggerfallWorldProfileKind.Dungeon, "hold").Validate();
        DaggerfallGroundContainer pile = new(profile, 100, new EntityId(2), new WorldPoint(4F, 1F, 8F));
        using ActorsState actors = EmptyActors();
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(groundContainerSprites: [sprite]));

        presentation.Publish(actors, new Dictionary<long, DaggerfallGroundContainer> { [pile.Id] = pile });
        AppearanceFact fact = Assert.Single(appearance.Snapshots.Last(), value => value.ObjectId == (ulong)pile.Id);
        // The pile rests on the floor at its position; its centre-pivoted billboard is lifted half its height to stand there.
        Assert.Equal(pile.Position.ToVector() + new Vector3(0F, .65F * .5F, 0F), fact.Transform.Translation);
        Assert.Equal(RenderLayer.Scene, fact.Layer);
        SpriteFromAtlasRequest request = appearance.SpriteRequests.Last();
        Assert.Equal(sprite.InitialFrameId, request.FrameId);
        Assert.Equal(sprite.Pivot, request.Pivot);
        Assert.Equal(sprite.Size, request.Size);
        Assert.Equal(DaggerfallFlatsTuning.Default.Items, request.Billboard);
        Assert.Equal(SpriteSizeMode.World, request.SizeMode);

        // A profile switch or a fully looted pile is represented by the owner's filtered empty
        // dictionary. The previous ordinary appearance is retired through the existing admitted
        // update lifecycle, and the same durable pile can be projected again after restore.
        presentation.Publish(actors, new Dictionary<long, DaggerfallGroundContainer>());
        Assert.DoesNotContain(appearance.Snapshots.Last(), value => value.ObjectId == (ulong)pile.Id);
        presentation.BeginAdmittedUpdate();
        Assert.Equal(1, appearance.DisposedAppearances);
        int requestsBeforeRestore = appearance.SpriteRequests.Count;
        presentation.Publish(actors, new Dictionary<long, DaggerfallGroundContainer> { [pile.Id] = pile });
        Assert.Equal(requestsBeforeRestore + 1, appearance.SpriteRequests.Count);
        Assert.Contains(appearance.Snapshots.Last(), value => value.ObjectId == (ulong)pile.Id);
    }

    /// <summary>
    /// A ground pile stands as one of the treasure piles, which one fixed by its identity so it keeps it across saves,
    /// until it holds one kind of item that has a refreshed look; it then stands as that item, and goes back to its pile
    /// when the look goes.
    /// </summary>
    [Fact]
    public void A_ground_pile_stands_as_the_treasure_pile_its_identity_picks_or_as_its_one_item()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("refreshed/item.png", Hash);
        content.Add("refreshed/item-dye.png", Hash);
        content.Add("refreshed/shared.png", Hash);
        content.Add("refreshed/shared-dye.png", Hash);
        AppearanceFake appearance = new(releases);
        NormalizedBillboardSprite small = new("sprite/treasure.png", Hash, 39, 26,
            [new NormalizedAtlasFrame(0, 0, 0, 39, 26)], 0, new Vector2(.5F, .5F), new Vector2(.975F, .65F));
        NormalizedBillboardSprite large = small with { Size = new Vector2(1.5F, 1F) };
        DaggerfallWorldProfileKey profile = new DaggerfallWorldProfileKey(new DaggerfallSiteId(1, 2), DaggerfallWorldProfileKind.Dungeon, "hold").Validate();
        DaggerfallGroundContainer even = new(profile, 100, new EntityId(2), new WorldPoint(4F, 1F, 8F));
        DaggerfallGroundContainer odd = new(profile, 101, new EntityId(3), new WorldPoint(5F, 1F, 8F));
        Dictionary<long, DaggerfallGroundContainer> piles = new() { [even.Id] = even, [odd.Id] = odd };
        using ActorsState actors = EmptyActors();
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(groundContainerSprites: [small, large]));

        presentation.Publish(actors, piles);
        Assert.Equal([small.Size, large.Size], appearance.SpriteRequests.TakeLast(2).Select(request => request.Size));
        int created = appearance.SpriteRequests.Count;
        presentation.Publish(actors, piles);
        Assert.Equal(created, appearance.SpriteRequests.Count);

        // The item stands on the floor (its pivot at its foot, unlifted), 40 classic pixels tall at the first pile's
        // .65 m for 26 pixels, as wide as its image's proportions, in its colour through its mask.
        DaggerfallGroundItemLook look = new(new(new("refreshed/item.png", Hash), 64, 128, (20, 40)),
            new(new("refreshed/item-dye.png", Hash), 64, 128, null), "#ff8000", 1, 40, Doll: false);
        presentation.Publish(actors, piles, groundLook: id => id == odd.Id ? look : null);
        SpriteFromAtlasRequest item = appearance.SpriteRequests.Last();
        Assert.Equal(created + 1, appearance.SpriteRequests.Count);
        Assert.Equal(.5F, item.Size.X, 4);
        Assert.Equal(1F, item.Size.Y, 4);
        Assert.Equal(new Vector2(.5F, 0F), item.Pivot);
        Assert.Equal(new Color(1F, (float)Math.Pow(128 / 255d, 2.2), 0F, 1F), item.Tint);
        Assert.NotEqual(default, item.Material.TintMask);
        Assert.Equal(odd.Position.ToVector(), Assert.Single(appearance.Snapshots.Last(), fact => fact.ObjectId == (ulong)odd.Id).Transform.Translation);

        // The same image untinted (bare steel) and then in a metal: the metal still draws through its mask, whichever
        // opened the shared image first.
        DaggerfallGroundItemLook metal = look with { Image = new(new("refreshed/shared.png", Hash), 64, 128, null),
            Mask = new(new("refreshed/shared-dye.png", Hash), 64, 128, null) };
        presentation.Publish(actors, piles, groundLook: id => id == odd.Id ? metal with { Mask = null, Color = null } : null);
        Assert.Equal(default, appearance.SpriteRequests.Last().Material.TintMask);
        presentation.Publish(actors, piles, groundLook: id => id == odd.Id ? metal : null);
        Assert.NotEqual(default, appearance.SpriteRequests.Last().Material.TintMask);

        // A worn item's image is sized in doll units, at a body's scale: 65 units are .65 m.
        presentation.Publish(actors, piles, groundLook: id => id == odd.Id ? look with { ClassicHeight = 65, Doll = true } : null);
        Assert.Equal(.65F, appearance.SpriteRequests.Last().Size.Y, 4);

        presentation.Publish(actors, piles);
        Assert.Equal(large.Size, appearance.SpriteRequests.Last().Size);
    }

    /// <summary>
    /// An assembled location draws each of its block models as its own static mesh: posed in the profile
    /// frame under the world transform, its mesh slots bound to the profile slots its textures have, each
    /// with its own snapshot identity, and every one released with the location resources.
    /// </summary>
    [Fact]
    public void An_assembled_location_draws_each_placed_mesh_with_its_pose_and_slot_bindings()
    {
        List<string> releases = [];
        ContentFake content = new("geometry/mesh-1.rstatmsh", Hash, releases);
        content.Add("geometry/mesh-2.rstatmsh", Hash);
        content.Add("texture/wall.png", Hash);
        content.Add("texture/floor.png", Hash);
        AppearanceFake appearance = new(releases);
        Transform first = new(new Vector3(1F, 0F, -2F), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2F), Vector3.One);
        Transform second = new(new Vector3(102.4F, 3F, 0F), Quaternion.Identity, Vector3.One);
        DaggerfallSiteGeometry geometry = new(1,
            [new DaggerfallSiteSpatialPart("block/0/0", "blocks/a.rspatial", Hash), new DaggerfallSiteSpatialPart("block/1/0", "blocks/b.rspatial", Hash, 128, 0)],
            [
                new DaggerfallSiteMesh("geometry/mesh-1.rstatmsh", Hash, first, [new(0, 7), new(1, 9)]),
                new DaggerfallSiteMesh("geometry/mesh-2.rstatmsh", Hash, second, [new(0, 9)]),
            ]);
        Vector3 rebased = new(10F, 0F, 20F);
        DaggerfallSiteProfile inputs = new(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            geometry,
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(rebased, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0),
            [new NormalizedMaterial(7, "texture/wall.png", Hash), new NormalizedMaterial(9, "texture/floor.png", Hash)],
            new Dictionary<long, NormalizedActorSprite>());
        Assert.Throws<InvalidOperationException>(() => inputs.SpatialArtifact);

        using ActorsState actors = EmptyActors();
        using (DaggerfallSiteAppearance presentation = new(content, appearance, inputs))
        {
            Assert.Equal(["geometry/mesh-1.rstatmsh", "geometry/mesh-2.rstatmsh"], appearance.StaticMeshPaths);
            Assert.Equal([0u, 1u, 0u], appearance.StaticMeshBindings.Select(binding => binding.MaterialSlot));
            presentation.Publish(actors);
            AppearanceFact[] snapshot = appearance.Snapshots.Last();
            AppearanceFact a = Assert.Single(snapshot, fact => fact.ObjectId == DaggerfallPresentationObjectIds.WorldMesh(DaggerfallPresentationObjectIds.ActiveLocationSlot, 0));
            Assert.Equal(first.Translation + rebased, a.Transform.Translation);
            Assert.True(Quaternion.Dot(first.Rotation, a.Transform.Rotation) > 0.9999F);
            AppearanceFact b = Assert.Single(snapshot, fact => fact.ObjectId == DaggerfallPresentationObjectIds.WorldMesh(DaggerfallPresentationObjectIds.ActiveLocationSlot, 1));
            Assert.Equal(second.Translation + rebased, b.Transform.Translation);
        }

        Assert.Equal(2, releases.Count(release => release == "appearance"));
    }

    /// <summary>
    /// Placements of one block model with the same slot bindings show one appearance, so the renderer draws them as
    /// instances of one mesh; a placement bound differently has its own, and each appearance is released once.
    /// </summary>
    [Fact]
    public void Placements_of_one_model_alike_share_its_appearance()
    {
        List<string> releases = [];
        ContentFake content = new("geometry/mesh-1.rstatmsh", Hash, releases);
        content.Add("texture/wall.png", Hash);
        content.Add("texture/floor.png", Hash);
        AppearanceFake appearance = new(releases);
        Transform Pose(float x) => new(new Vector3(x, 0F, 0F), Quaternion.Identity, Vector3.One);
        DaggerfallSiteGeometry geometry = new(1, [new DaggerfallSiteSpatialPart("block/0/0", "blocks/a.rspatial", Hash)],
            [
                new DaggerfallSiteMesh("geometry/mesh-1.rstatmsh", Hash, Pose(0F), [new(0, 7)]),
                new DaggerfallSiteMesh("geometry/mesh-1.rstatmsh", Hash, Pose(10F), [new(0, 7)]),
                new DaggerfallSiteMesh("geometry/mesh-1.rstatmsh", Hash, Pose(20F), [new(0, 9)]),
            ]);
        DaggerfallSiteProfile inputs = new(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            geometry,
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0),
            [new NormalizedMaterial(7, "texture/wall.png", Hash), new NormalizedMaterial(9, "texture/floor.png", Hash)],
            new Dictionary<long, NormalizedActorSprite>());

        using ActorsState actors = EmptyActors();
        using (DaggerfallSiteAppearance presentation = new(content, appearance, inputs))
        {
            Assert.Equal(2, appearance.StaticMeshPaths.Count);
            presentation.Publish(actors);
            AppearanceFact[] snapshot = appearance.Snapshots.Last();
            AppearanceFact Placed(int index) => Assert.Single(snapshot, fact =>
                fact.ObjectId == DaggerfallPresentationObjectIds.WorldMesh(DaggerfallPresentationObjectIds.ActiveLocationSlot, index));
            Assert.Same(Placed(0).Appearance, Placed(1).Appearance);
            Assert.NotSame(Placed(0).Appearance, Placed(2).Appearance);
            Assert.Equal(new Vector3(10F, 0F, 0F), Placed(1).Transform.Translation);
        }

        Assert.Equal(2, releases.Count(release => release == "appearance"));
    }

    [Fact]
    public void Appearance_uses_normalized_material_slots_and_atlases_then_releases_dependents_in_order()
    {
        List<string> releases = [];
        ContentFake content = new("mesh/hold.json", Hash, releases);
        content.Add("texture/wall.png", Hash);
        content.Add("sprite/rat.png", Hash);
        AppearanceFake appearance = new(releases);
        DaggerfallSiteProfile inputs = new(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            DaggerfallSiteGeometry.Closure(new SpatialContentArtifact("spatial/hold.json", Hash, 1),
            new ContentArtifact("mesh/hold.json", Hash)),
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0),
            [new NormalizedMaterial(3, "texture/wall.png", Hash)],
            new Dictionary<long, NormalizedActorSprite>
            {
                [11] = new("sprite/rat.png", Hash, 32, 32, [new NormalizedAtlasFrame(0, 0, 0, 16, 16)], 0, new Vector2(.5F, 0F), Vector2.One),
            });

        using (DaggerfallSiteAppearance presentation = new(content, appearance, inputs))
        {
            Assert.Equal([3u], appearance.StaticMeshBindings.Select(binding => binding.MaterialSlot));
            Assert.Single(appearance.AtlasRequests);
            Assert.Single(appearance.SpriteRequests);
            presentation.Dispose();
        }

        Assert.True(releases.IndexOf("appearance") < releases.IndexOf("atlas"));
        Assert.True(releases.IndexOf("atlas") < releases.IndexOf("material"));

        // Engine resources are owned by whoever opens them now, so the appearance releases each one it
        // opened (the material texture and the actor sprite texture) after the materials that name them.
        Assert.Equal(2, appearance.ReleasedResources);
        Assert.True(releases.IndexOf("material") < releases.IndexOf("resource"));
    }

    [Fact]
    public void Site_projection_owns_normalized_lights_and_clears_the_inside_background()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, appearance);

        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            Assert.Equal(inputs.Lights.Count + 1, appearance.LightRequests.Count);
            Assert.All(appearance.LightRequests, request => Assert.InRange(request.LogicalId, 1UL, (1UL << 53) - 1UL));
            Assert.Equal(appearance.LightRequests.Count, appearance.LightRequests.Select(request => request.LogicalId).Distinct().Count());
            LightRequest ambient = Assert.Single(appearance.LightRequests, request => request.Descriptor.Kind == LightKind.Ambient);
            Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.Dungeon, ambient.Descriptor.Intensity);
            Assert.Equal(new Color(0F, 0F, 0F, 1F), engine.BackgroundColors.Single());
            LightRequest first = appearance.LightRequests.First(request => request.Descriptor.Position == new Vector3(54.4F, 34.4F, -17.2F));
            Assert.Equal(LightKind.Point, first.Descriptor.Kind);
            Assert.Equal(Vector3.One, first.Descriptor.Color);
            Assert.Equal(7.5F, first.Descriptor.Range);
            Assert.Equal(1F, first.Descriptor.Intensity);
            Assert.Equal(LightShadowIntent.Disabled, first.Descriptor.ShadowIntent);
        }

        Assert.Equal(inputs.Lights.Count + 1, appearance.DisposedLights);

        DaggerfallSiteProfile exterior = new(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            DaggerfallSiteGeometry.Closure(new SpatialContentArtifact("spatial/exterior.json", Hash, 1), new ContentArtifact("mesh/exterior.json", Hash)),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: new DaggerfallSiteId(17, 4), profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: "worldrpg/test/exterior");
        using DaggerfallSiteLighting exteriorLighting = new(appearance, engine.Context.CameraView, exterior,
            DaggerfallTuning.Defaults.SiteLighting, DaggerfallCalendar.Start);
        Assert.Equal(2, engine.ClearedSkyBackgrounds);
        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.ExteriorNight * DaggerfallTuning.Defaults.SiteLighting.ExteriorScale,
            appearance.LightRequests.Last().Descriptor.Intensity);
        exteriorLighting.UpdateAmbient(DaggerfallCalendar.Start with { Hour = 12 });
        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.ExteriorNoon * DaggerfallTuning.Defaults.SiteLighting.ExteriorScale,
            appearance.LightUpdates.Single(update => update.Replacement.Descriptor.Kind == LightKind.Ambient).Replacement.Descriptor.Intensity);
        LightRequest sun = Assert.Single(appearance.LightRequests, request => request.Descriptor.Kind == LightKind.Directional);
        // The sun asks for its shadow at the tuned reach and detail; the Engine's Shadows video option, on by default, decides whether it draws.
        Assert.Equal(LightShadowIntent.Requested, sun.Descriptor.ShadowIntent);
        Assert.True(sun.Descriptor.HasRange);
        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.SunShadowRange, sun.Descriptor.Range);
        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.SunShadowResolution, sun.Descriptor.ShadowResolution);
        var noon = DaggerfallCalendar.Start with { Hour = 12 };
        float daylight = DaggerfallWeatherTuning.Classic.Daylight(noon, DaggerfallWeatherKind.Thunder);
        exteriorLighting.UpdateAmbient(noon, daylight);
        var ambientUpdate = appearance.LightUpdates.Last(update => update.Replacement.Descriptor.Kind == LightKind.Ambient);
        Assert.Equal((DaggerfallTuning.Defaults.SiteLighting.ExteriorNight
            + (DaggerfallTuning.Defaults.SiteLighting.ExteriorNoon - DaggerfallTuning.Defaults.SiteLighting.ExteriorNight) * daylight) * DaggerfallTuning.Defaults.SiteLighting.ExteriorScale,
            ambientUpdate.Replacement.Descriptor.Intensity);
        var sunUpdate = appearance.LightUpdates.Last(update => update.Replacement.Descriptor.Kind == LightKind.Directional);
        Assert.Equal(daylight * DaggerfallTuning.Defaults.SiteLighting.ExteriorSun, sunUpdate.Replacement.Descriptor.Intensity);
        // At noon the sun stands its tilt south of overhead, so its light travels down and north (z is south).
        float tilt = DaggerfallTuning.Defaults.SiteLighting.SunTiltDegrees * MathF.PI / 180f;
        Assert.Equal(0f, sunUpdate.Replacement.Descriptor.Direction.X, 4);
        Assert.Equal(-MathF.Cos(tilt), sunUpdate.Replacement.Descriptor.Direction.Y, 4);
        Assert.Equal(-MathF.Sin(tilt), sunUpdate.Replacement.Descriptor.Direction.Z, 4);
        // In the morning it is in the east, so its light travels west (x is east).
        exteriorLighting.UpdateAmbient(DaggerfallCalendar.Start with { Hour = 8 }, daylight);
        Assert.True(appearance.LightUpdates.Last(update => update.Replacement.Descriptor.Kind == LightKind.Directional).Replacement.Descriptor.Direction.X < -.5f);
        exteriorLighting.UpdateAmbient(noon, daylight);
        exteriorLighting.UpdateAmbient(noon, daylight, lightningFlash: 2);
        Assert.Equal(2f * DaggerfallTuning.Defaults.SiteLighting.ExteriorScale, appearance.LightUpdates.Last().Replacement.Descriptor.Intensity);
        exteriorLighting.UpdateAmbient(noon, daylight);
        Assert.Equal(ambientUpdate.Replacement.Descriptor.Intensity, appearance.LightUpdates.Last().Replacement.Descriptor.Intensity);
        exteriorLighting.Dispose();
        Assert.Equal(appearance.LightRequests.Count, appearance.DisposedLights);
    }

    [Fact]
    public void Appearance_failures_release_staged_handles_and_keep_the_previous_playback_live()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake constructionFailure = new(releases) { FailSpritePlaybackCreateAt = 1 };
        Assert.Throws<InvalidOperationException>(() => new DaggerfallSiteAppearance(content, constructionFailure, MediaInputs()));
        Assert.Equal(constructionFailure.CreatedAtlases, constructionFailure.DisposedAtlases);
        Assert.Equal(constructionFailure.CreatedAppearances, constructionFailure.DisposedAppearances);

        AppearanceFake replacementFailure = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, replacementFailure, MediaInputs());
        SpritePlaybackHandle original = Assert.Single(replacementFailure.CreatedPlaybacks).Handle;
        replacementFailure.FailSpritePlaybackControlAt = replacementFailure.ControlRequests.Count + 1;
        Assert.Throws<InvalidOperationException>(() => presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9)));

        presentation.Advance(OuterUpdate(1));
        Assert.Equal(original, Assert.Single(replacementFailure.AdvanceRequests).Playback.Handle);
        Assert.Equal(1, replacementFailure.DisposedPlaybacks);
    }

    [Fact]
    public void Presentation_failure_propagates_and_disposal_releases_staged_resources()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(), audio.Service);
        EnemyAttackStartedFact hit = new(11, 12, true, 17, 23);

        presentation.BeginAdmittedUpdate();
        presentation.React(hit);
        SpritePlayback staged = Visual(presentation).Playback!;
        appearance.FailPublishAt = appearance.PublishCalls + 1;
        using ActorsState actors = EmptyActors();
        Assert.Throws<InvalidOperationException>(() => presentation.Publish(actors));

        presentation.Dispose();

        Assert.Contains(staged.Handle, appearance.DisposedPlaybackHandles);
        Assert.NotEmpty(releases);
    }

    [Fact]
    public void Site_audio_retires_pending_one_shot_handles_before_releasing_clips()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        DaggerfallSiteProfile inputs = MediaInputs(audio:
        [
            new NormalizedAudioClip("sound.3", "audio/swing.wav", Hash),
            new NormalizedAudioClip("hit1", "audio/hit.wav", Hash),
            new NormalizedAudioClip("hit2", "audio/hit2.wav", Hash),
            new NormalizedAudioClip("hit3", "audio/hit3.wav", Hash),
            new NormalizedAudioClip("hit4", "audio/hit4.wav", Hash),
            new NormalizedAudioClip("hit5", "audio/hit5.wav", Hash),
        ]);
        using DaggerfallSiteAppearance presentation = new(content, appearance, inputs, audio.Service);

        presentation.BeginAdmittedUpdate();
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 17, 23) { Feedback = new(true, "sound.3") });
        Assert.Single(audio.EmittedSignals);
        presentation.Dispose();

        Assert.Equal(audio.EmittedSignals, audio.RetiredSignals);
    }

    [Fact]
    public void Classic_textures_are_admitted_during_construction_and_retired_playback_releases_on_the_next_admission()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        NormalizedClassicPresentation weapon = ClassicWeapon();
        NormalizedClassicPresentation classic = new(weapon.Weapons, ClassicEffects().Effects)
        {
            CompatibleItemVisuals = weapon.CompatibleItemVisuals,
            Viewmodel = weapon.Viewmodel,
        };
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: classic));
        int constructionResourceRequests = appearance.OpenedResourcePaths.Count;
        appearance.RejectLateResourceOpen = true;
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        SpritePlayback actorPlayback = Visual(presentation).Playback!;
        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackStartedFact(2, 3));
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 2, 3));
        presentation.React(new AttackWhiffedFact(DaggerfallActorIdentity.PlayerEntityId, 12, false, 2, 3));
        presentation.React(new AttackWhiffedFact(DaggerfallActorIdentity.PlayerEntityId, 12, false, 2, 3));
        using ActorsState actors = ActorsAt(new WorldPoint(2F, 0F, 3F));
        presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1d, 3, false, 2, 3), actors);
        presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1d, 4, false, 2, 3), actors);

        presentation.CompleteAdmittedUpdate();
        presentation.BeginAdmittedUpdate();

        Assert.Equal(constructionResourceRequests, appearance.OpenedResourcePaths.Count);
        Assert.Contains(actorPlayback.Handle, appearance.DisposedPlaybackHandles);
        Assert.Empty(appearance.OpenedResourcePaths.Skip(constructionResourceRequests));
    }

    [Fact]
    public void Failed_outer_update_reraises_and_a_resumed_update_continues_the_same_appearance()
    {
        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        // Baseline success on a fresh weapon: no swing staged yet.
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        int playbacksBefore = appearance.CreatedPlaybacks.Count;

        // The next admitted update stages a weapon strike, then its publish throws. The Engine faults
        // the lifecycle and a resume continues this same session, so the escape tears nothing down.
        appearance.FailPublishAt = appearance.PublishCalls + 1;
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => session.Update(new ProductUpdate(OuterUpdate(2), [Ui("{\"action\":\"attack\"}")])));
        Assert.Equal("Injected presentation publish failure.", failure.Message);

        Assert.True(appearance.CreatedPlaybacks.Count > playbacksBefore, "The failed update should have staged new appearance playback before its publish threw.");
        SpritePlayback[] staged = [.. appearance.CreatedPlaybacks.Skip(playbacksBefore)];
        Assert.All(staged, playback => Assert.DoesNotContain(playback.Handle, appearance.DisposedPlaybackHandles));

        // Resume: the next admitted update runs on the same appearance and publishes normally.
        int publishesBefore = appearance.PublishCalls;
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.True(appearance.PublishCalls > publishesBefore, "The resumed update should publish on the live appearance.");
        Assert.All(staged, playback => Assert.DoesNotContain(playback.Handle, appearance.DisposedPlaybackHandles));
    }

    [Fact]
    public void Retired_playback_is_lagged_to_the_next_admitted_update()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs());
        SpritePlayback original = Visual(presentation).Playback!;
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 1, 2));

        Assert.Equal(0, appearance.DisposedPlaybacks);
        presentation.BeginAdmittedUpdate();
        Assert.Equal(1, appearance.DisposedPlaybacks);
        Assert.Equal(original.Handle, appearance.DisposedPlaybackHandles[0]);
        presentation.CompleteAdmittedUpdate();
    }

    /// <summary>
    /// Leaving a location suspends it and disposes it with no admitted update between. An actor that changed state in the
    /// last update, finished or still running, left its old playback queued, where it still holds the actor's sprite.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_location_left_just_after_an_actor_changed_state_releases_the_old_playback_first(bool updateCompleted, bool suspended)
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        DaggerfallSiteAppearance presentation = new(MediaContent(releases), appearance, MediaInputs());
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 1, 2));
        if (updateCompleted) presentation.CompleteAdmittedUpdate();
        if (suspended)
        {
            presentation.RetireAllActors();
            presentation.SuspendLocationResources();
        }

        presentation.Dispose();

        AssertEverySpriteReleased(appearance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_location_left_just_after_an_actor_died_releases_its_playback_before_its_sprite(bool updateCompleted)
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        DaggerfallSiteAppearance presentation = new(MediaContent(releases), appearance, MediaInputs());
        presentation.React(new ActorDiedFact(11, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 10, 2, 1, 2));
        if (updateCompleted) presentation.CompleteAdmittedUpdate();
        presentation.RetireAllActors();
        presentation.SuspendLocationResources();

        presentation.Dispose();

        AssertEverySpriteReleased(appearance);
    }

    [Fact]
    public void A_location_left_just_after_the_weapon_changed_action_releases_the_old_playback_first()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        presentation.React(new PlayerAttackRestFact(1, 2));

        presentation.Dispose();

        AssertEverySpriteReleased(appearance);
    }

    private static void AssertEverySpriteReleased(AppearanceFake appearance)
    {
        Assert.Equal(appearance.CreatedPlaybacks.Count, appearance.DisposedPlaybacks);
        Assert.Equal(appearance.CreatedAppearances, appearance.DisposedAppearances);
        Assert.Equal(appearance.CreatedAtlases, appearance.DisposedAtlases);
    }

    private static DaggerfallSiteProfile SkyExterior() => new(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
        DaggerfallSiteGeometry.Closure(new SpatialContentArtifact("spatial/exterior.json", Hash, 1), new ContentArtifact("mesh/exterior.json", Hash)),
        new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
        site: new DaggerfallSiteId(17, 4), profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: "worldrpg/test/exterior");

    /// <summary>A location unloading behind a player walking away leaves the sky lit, and the sky goes on following the day.</summary>
    [Fact]
    public void The_sky_outlasts_its_location_unloading_and_keeps_following_the_day()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(Hash, releases).Service, appearance);
        DaggerfallCalendar noon = DaggerfallCalendar.Start with { Hour = 12 };
        using DaggerfallSiteLighting lighting = new(appearance, engine.Context.CameraView, SkyExterior(), DaggerfallTuning.Defaults.SiteLighting, noon);
        int created = appearance.LightRequests.Count;

        lighting.Suspend(keepSky: true);
        Assert.Equal(0, appearance.DisposedLights);
        lighting.UpdateAmbient(noon, .5f);
        Assert.Equal(.5f * DaggerfallTuning.Defaults.SiteLighting.ExteriorSun, appearance.LightUpdates.Last(update => update.Replacement.Descriptor.Kind == LightKind.Directional).Replacement.Descriptor.Intensity);

        int cleared = engine.ClearedSkyBackgrounds;
        lighting.Resume();
        Assert.Equal(created, appearance.LightRequests.Count);
        Assert.Equal(cleared, engine.ClearedSkyBackgrounds);

        lighting.Suspend(keepSky: true);
        lighting.Suspend();
        Assert.Equal(created, appearance.DisposedLights);
        lighting.RestoreSky();
        Assert.Equal(created * 2, appearance.LightRequests.Count);
    }

    /// <summary>A resident location beside the active one lights only itself: a second sun would light the same ground twice.</summary>
    [Fact]
    public void A_resident_location_brings_no_sky_of_its_own()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(Hash, releases).Service, appearance);
        int cleared = engine.ClearedSkyBackgrounds;
        using DaggerfallSiteLighting lighting = new(appearance, engine.Context.CameraView, SkyExterior(), DaggerfallTuning.Defaults.SiteLighting,
            DaggerfallCalendar.Start, carriesSky: false);

        lighting.Suspend();
        lighting.Resume();

        Assert.DoesNotContain(appearance.LightRequests, request => request.Descriptor.Kind is LightKind.Directional or LightKind.Ambient);
        Assert.Equal(cleared, engine.ClearedSkyBackgrounds);
    }

    /// <summary>The sky's ambient cools by night and is white by noon, so a lamp's warmth stands out; indoors it stays white.</summary>
    [Fact]
    public void The_night_ambient_is_cool_and_noon_is_white()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(Hash, releases).Service, appearance);
        using DaggerfallSiteLighting lighting = new(appearance, engine.Context.CameraView, SkyExterior(), DaggerfallTuning.Defaults.SiteLighting, DaggerfallCalendar.Start);

        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.NightColor, appearance.LightRequests.Single(request => request.Descriptor.Kind == LightKind.Ambient).Descriptor.Color);

        lighting.UpdateAmbient(DaggerfallCalendar.Start with { Hour = 12 });

        Assert.Equal(Vector3.One, appearance.LightUpdates.Last(update => update.Replacement.Descriptor.Kind == LightKind.Ambient).Replacement.Descriptor.Color);
    }

    /// <summary>The sun rises warm and whitens as it climbs, so dawn and dusk warm what they light.</summary>
    [Fact]
    public void The_low_sun_is_warm_and_the_high_sun_white()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(new ContentFake(releases), SpatialFake.Create(Hash, releases).Service, appearance);
        DaggerfallCalendar noon = DaggerfallCalendar.Start with { Hour = 12 };
        using DaggerfallSiteLighting lighting = new(appearance, engine.Context.CameraView, SkyExterior(), DaggerfallTuning.Defaults.SiteLighting, noon);

        lighting.UpdateAmbient(noon, 1);
        Assert.Equal(Vector3.One, appearance.LightUpdates.Last(update => update.Replacement.Descriptor.Kind == LightKind.Directional).Replacement.Descriptor.Color);

        lighting.UpdateAmbient(noon with { Hour = 6, Minute = 10 }, .3f);
        Vector3 low = appearance.LightUpdates.Last(update => update.Replacement.Descriptor.Kind == LightKind.Directional).Replacement.Descriptor.Color;
        Assert.True(low.Z < low.X);
    }
}
