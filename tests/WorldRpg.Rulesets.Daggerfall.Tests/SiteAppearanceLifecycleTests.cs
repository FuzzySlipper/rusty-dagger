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
        Assert.Equal(1 + inputs.Doors.Count + inputs.DungeonActionModels.Count + 1, graphics.StaticMeshContentRequests.Count);
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
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d), []));

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
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(groundContainerSprite: sprite));

        presentation.Publish(actors, new Dictionary<long, DaggerfallGroundContainer> { [pile.Id] = pile });
        AppearanceFact fact = Assert.Single(appearance.Snapshots.Last(), value => value.ObjectId == (ulong)pile.Id);
        Assert.Equal(pile.Position.ToVector(), fact.Transform.Translation);
        Assert.Equal(RenderLayer.Scene, fact.Layer);
        SpriteFromAtlasRequest request = appearance.SpriteRequests.Last();
        Assert.Equal(sprite.InitialFrameId, request.FrameId);
        Assert.Equal(sprite.Pivot, request.Pivot);
        Assert.Equal(sprite.Size, request.Size);
        Assert.Equal(BillboardMode.Cylindrical, request.Billboard);
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
            new SpatialContentArtifact("spatial/hold.json", Hash, 1),
            new ContentArtifact("mesh/hold.json", Hash),
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
            new SpatialContentArtifact("spatial/exterior.json", Hash, 1), new ContentArtifact("mesh/exterior.json", Hash),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: new DaggerfallSiteId(17, 4), profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: "worldrpg/test/exterior");
        using DaggerfallSiteLighting exteriorLighting = new(appearance, engine.Context.CameraView, exterior,
            DaggerfallTuning.Defaults.SiteLighting, DaggerfallCalendar.Start);
        Assert.Equal(2, engine.ClearedSkyBackgrounds);
        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.ExteriorNight,
            appearance.LightRequests.Last().Descriptor.Intensity);
        exteriorLighting.UpdateAmbient(DaggerfallCalendar.Start with { Hour = 12 });
        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.ExteriorNoon,
            appearance.LightUpdates.Single(update => update.Replacement.Descriptor.Kind == LightKind.Ambient).Replacement.Descriptor.Intensity);
        Assert.Single(appearance.LightRequests, request => request.Descriptor.Kind == LightKind.Directional);
        var noon = DaggerfallCalendar.Start with { Hour = 12 };
        float daylight = DaggerfallWeatherTuning.Classic.Daylight(noon, DaggerfallWeatherKind.Thunder);
        exteriorLighting.UpdateAmbient(noon, daylight);
        var ambientUpdate = appearance.LightUpdates.Last(update => update.Replacement.Descriptor.Kind == LightKind.Ambient);
        Assert.Equal(DaggerfallTuning.Defaults.SiteLighting.ExteriorNight
            + (DaggerfallTuning.Defaults.SiteLighting.ExteriorNoon - DaggerfallTuning.Defaults.SiteLighting.ExteriorNight) * daylight,
            ambientUpdate.Replacement.Descriptor.Intensity);
        var sunUpdate = appearance.LightUpdates.Last(update => update.Replacement.Descriptor.Kind == LightKind.Directional);
        Assert.Equal(daylight, sunUpdate.Replacement.Descriptor.Intensity);
        Assert.Equal(-1f, sunUpdate.Replacement.Descriptor.Direction.Y, 4);
        exteriorLighting.UpdateAmbient(noon, daylight, lightningFlash: 2);
        Assert.Equal(2f, appearance.LightUpdates.Last().Replacement.Descriptor.Intensity);
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
        DaggerfallSiteProfile inputs = MediaInputs(audio: [new NormalizedAudioClip("sound.3", "audio/swing.wav", Hash)]);
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
        int constructionResourceRequests = appearance.OpenResourceRequests.Count;
        appearance.RejectLateResourceOpen = true;
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        SpritePlayback actorPlayback = Visual(presentation).Playback!;
        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackStartedFact(2, 3));
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 2, 3));
        presentation.React(new AttackMissedFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1, false, 2, 3));
        presentation.React(new AttackMissedFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 2, false, 2, 3));
        using ActorsState actors = ActorsAt(new WorldPoint(2F, 0F, 3F));
        presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1d, 3, false, 2, 3), actors);
        presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1d, 4, false, 2, 3), actors);

        presentation.CompleteAdmittedUpdate();
        presentation.BeginAdmittedUpdate();

        Assert.Equal(constructionResourceRequests, appearance.OpenResourceRequests.Count);
        Assert.Contains(actorPlayback.Handle, appearance.DisposedPlaybackHandles);
        Assert.Empty(appearance.OpenResourceRequests.Skip(constructionResourceRequests));
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
}
