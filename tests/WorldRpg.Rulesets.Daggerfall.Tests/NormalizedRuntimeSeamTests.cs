using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NormalizedRuntimeSeamTests
{
    private static readonly ContentSha256 Hash = new(1, 2, 3, 4);

    [Fact]
    public void Spatial_system_admits_one_content_artifact_reads_it_back_and_releases_session_before_reference()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning))
        {
            Assert.Equal(1, spatial.ReplaceCalls);
            Assert.Equal(1, spatial.ReadCalls);
            Assert.Equal((ulong)7, spatial.LastRequest!.Value.NavigationGridId);
            Assert.Equal((ulong)1, spatial.LastRequest.Value.Content.Handle.Value);
        }

        Assert.Equal(["session", "content"], releases);
    }

    [Fact]
    public void Rejected_controller_config_does_not_create_a_session_or_admit_content()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        spatial.RejectConfigValidation = true;

        Assert.Throws<InvalidOperationException>(() => new SpatialMovementSystem(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2)));

        Assert.Equal(1, spatial.ConfigValidationCalls);
        Assert.Equal(0, spatial.CreateSessionCalls);
        Assert.Equal(0, content.ResolveCalls);
    }

    [Fact]
    public void Spatial_system_layers_only_ruleset_controller_overrides_and_persists_the_engine_receipt()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2, new CharacterControllerTuning(
            StandingHeight: 1.8f,
            Radius: .25f,
            ForwardSpeed: 3.5f,
            BackwardSpeed: 3.5f,
            StrafeSpeed: 3.5f,
            RecoveryMaximumDistance: 1f,
            MaximumStepHeight: .75f));
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), .25f, 0f);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning))
        {
            system.Step(player, new ProductUpdateState(1f / 60f) { PlanarIntent = new Vector2(1f, 0f) });
            system.Step(player, new ProductUpdateState(1f / 60f) { PlanarIntent = new Vector2(0f, 1f) });
        }

        CharacterStepRequest first = spatial.StepRequests[0];
        CharacterControllerConfig config = first.Config;
        Assert.Equal(1.8f, config.Shape.StandingHeight);
        Assert.Equal(.25f, config.Shape.Radius);
        Assert.Equal(3.5f, config.Ground.ForwardSpeed);
        Assert.Equal(3.5f, config.Ground.BackwardSpeed);
        Assert.Equal(3.5f, config.Ground.StrafeSpeed);
        Assert.Equal(1f, config.Recovery.MaximumDistance);
        Assert.Equal(.75f, config.Surface.MaximumStepHeight);
        Assert.Equal(spatial.RepresentativeValidConfig.Shape.CrouchedHeight, config.Shape.CrouchedHeight);
        Assert.Equal(spatial.RepresentativeValidConfig.Ground.Acceleration, config.Ground.Acceleration);
        Assert.Equal(spatial.RepresentativeValidConfig.Recovery.MaximumSpeed, config.Recovery.MaximumSpeed);
        Assert.Equal(spatial.RepresentativeValidConfig.Surface.FloorSnapDistance, config.Surface.FloorSnapDistance);
        Assert.Equal(1, spatial.ConfigValidationCalls);
        Assert.Equal(2, spatial.CommandValidationCalls);
        Assert.Equal([1UL, 2UL], spatial.StepRequests.Select(request => request.Command.Sequence));
        Assert.Equal(.25f, first.Command.HeadingYawRadians);
        Assert.Equal(new Vector2(1f, 0f), first.Command.PlanarIntent);
        Assert.Equal(new WorldPoint(3f, 2f, 3f), player.Position);
        Assert.True(player.Motion.Grounded);
        Assert.True(player.Ground.Present);
    }

    [Fact]
    public void Restored_spatial_continuation_can_be_captured_again_before_its_next_step()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake source = SpatialFake.Create(Hash, releases);
        SpatialFake resumed = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2);
        PlayerControlState player = new(new WorldPoint(0, 0, 0), 0, 0);
        using SpatialMovementSystem first = new(source.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning);
        first.Step(player, new ProductUpdateState(.125f));
        CharacterContinuationCheckpoint checkpoint = first.CaptureContinuation();

        using SpatialMovementSystem second = new(resumed.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning);
        CharacterContinuationRestoreReceipt receipt = second.RestoreContinuation(checkpoint);

        Assert.Equal(checkpoint.SourceGeneration, receipt.SourceGeneration);
        Assert.True(second.HasContinuation);
        Assert.Equal(checkpoint, second.CaptureContinuation());
    }

    [Fact]
    public void Zero_controller_overrides_are_layered_then_left_for_engine_validation()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2, new CharacterControllerTuning(
            ForwardSpeed: 0f,
            BackwardSpeed: 0f,
            StrafeSpeed: 0f,
            RecoveryMaximumDistance: 0f,
            MaximumStepHeight: 0f));
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning))
            system.Step(player, new ProductUpdateState(1f / 60f));

        CharacterControllerConfig config = Assert.Single(spatial.StepRequests).Config;
        Assert.Equal(0f, config.Ground.ForwardSpeed);
        Assert.Equal(0f, config.Ground.BackwardSpeed);
        Assert.Equal(0f, config.Ground.StrafeSpeed);
        Assert.Equal(0f, config.Recovery.MaximumDistance);
        Assert.Equal(0f, config.Surface.MaximumStepHeight);
        Assert.Equal(1, spatial.ConfigValidationCalls);
    }

    [Fact]
    public void Prepared_spatial_steps_are_owner_bound_stale_checked_and_single_use()
    {
        List<string> releases = [];
        ContentFake firstContent = new("spatial/hold.json", Hash, releases);
        ContentFake secondContent = new("spatial/hold.json", Hash, releases);
        SpatialFake firstSpatial = SpatialFake.Create(Hash, releases);
        SpatialFake secondSpatial = SpatialFake.Create(Hash, releases);
        PlayerInputSystem input = new(DaggerfallTuning.Defaults.PlayerControl, LookRecorder.Create().Service, DaggerfallInput.Controls, DaggerfallInput.Bindings);
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);
        ProductUpdateState update = new(1f / 60f);

        using SpatialMovementSystem first = new(firstSpatial.Service, firstContent, new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2));
        using SpatialMovementSystem second = new(secondSpatial.Service, secondContent, new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2));
        PreparedSpatialStep foreign = Assert.IsType<PreparedSpatialStep>(first.Prepare(player, update, input.Prepare(player, update), CharacterStepEnvironment.Empty));

        Assert.Throws<InvalidOperationException>(() => second.Propose(foreign));
        Assert.Equal(0, firstSpatial.StepCalls);
        Assert.Equal(0, secondSpatial.StepCalls);

        player.MoveTo(new Vector3(2f, 2f, 3f));
        Assert.Throws<InvalidOperationException>(() => first.Propose(foreign));
        Assert.Equal(0, firstSpatial.StepCalls);

        PreparedSpatialStep accepted = Assert.IsType<PreparedSpatialStep>(first.Prepare(player, update, input.Prepare(player, update), CharacterStepEnvironment.Empty));
        first.Propose(accepted);
        Assert.Equal(1, firstSpatial.StepCalls);
        Assert.Throws<InvalidOperationException>(() => first.Propose(accepted));
        Assert.Equal(1, firstSpatial.StepCalls);
    }

    [Fact]
    public void Daggerfall_tuning_exposes_its_controller_values_in_loaded_payloads()
    {
        string root = RepositoryRoot();
        DaggerfallTuning tuning = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json")));
        CharacterControllerTuning controller = Assert.IsType<CharacterControllerTuning>(tuning.Spatial.CharacterController);

        Assert.Equal(3.5f, controller.ForwardSpeed);
        Assert.Equal(3.5f, controller.BackwardSpeed);
        Assert.Equal(3.5f, controller.StrafeSpeed);
        Assert.Equal(1.8f, controller.StandingHeight);
        Assert.Equal(.25f, controller.Radius);
        Assert.Equal(1f, controller.RecoveryMaximumDistance);
        Assert.Equal(.75f, controller.MaximumStepHeight);
        Assert.Equal(2.25d, tuning.MeleeTargeting.MaximumDistance);
        Assert.Equal(.5d, tuning.MeleeTargeting.MinimumFacingCosine);
    }

    [Fact]
    public void Realtime_substeps_reuse_postlook_held_movement_with_one_sequence_each()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        float expectedYaw = inputs.InitialLook.YawRadians + .25f;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputEvent[] input =
        [
            Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW),
            Input(InputEventKind.PointerDelta, x: .25f, y: -.5f),
        ];

        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 3, 1, 1, 1, 60, 3, 0, 1d / 60d), input);
        }

        Assert.Equal([1UL, 2UL, 3UL], spatial.StepRequests.Select(request => request.Command.Sequence));
        Assert.All(spatial.StepRequests, request =>
        {
            Assert.Equal(new Vector2(0f, 1f), request.Command.PlanarIntent);
            Assert.Equal(expectedYaw, request.Command.HeadingYawRadians);
            Assert.Equal(1f / 60f, request.Command.StepSeconds);
        });
    }

    [Fact]
    public void Direct_axis_is_first_substep_only_while_held_input_continues()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 3, 1, 1, 1, 60, 3, 0, 1d / 60d), [Input(InputEventKind.DirectAxis, x: .5f, y: .75f, intent: "move")]);
        }

        Assert.Equal(new Vector2(.5f, .75f), spatial.StepRequests[0].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[1].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[2].Command.PlanarIntent);
    }

    [Fact]
    public void Direct_digital_movement_is_first_substep_only()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 3, 1, 1, 1, 60, 3, 0, 1d / 60d), [Input(InputEventKind.DirectDigital, x: 1f, intent: "move")]);
        }

        Assert.Equal(new Vector2(0f, 1f), spatial.StepRequests[0].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[1].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[2].Command.PlanarIntent);
    }

    [Fact]
    public void Rejected_command_leaves_the_entire_staged_slice_uncommitted()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        WorldPoint? positionBefore = session.State.PlayerControl.Position;
        CharacterMotion motionBefore = session.State.PlayerControl.Motion;
        CharacterGround groundBefore = session.State.PlayerControl.Ground;
        float yawBefore = session.State.PlayerControl.YawRadians;
        float pitchBefore = session.State.PlayerControl.PitchRadians;
        spatial.RejectCommandValidation = true;

        Assert.Throws<InvalidOperationException>(() => session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d),
        [
            Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW),
            Input(InputEventKind.PointerDelta, x: .25f, y: -.5f),
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack"),
        ]));

        Assert.Equal(1, spatial.CommandValidationCalls);
        Assert.Equal(0, spatial.StepCalls);
        Assert.Equal(positionBefore, session.State.PlayerControl.Position);
        Assert.Equal(motionBefore, session.State.PlayerControl.Motion);
        Assert.Equal(groundBefore, session.State.PlayerControl.Ground);
        Assert.Equal(yawBefore, session.State.PlayerControl.YawRadians);
        Assert.Equal(pitchBefore, session.State.PlayerControl.PitchRadians);
        spatial.RejectCommandValidation = false;
        session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 2, 1, 1, 2, 60, 1, 0, 1d / 60d), ReadOnlySpan<ProductInputEvent>.Empty);
        Assert.Equal(Vector2.Zero, spatial.StepRequests.Single().Command.PlanarIntent);
        Assert.Equal(1UL, spatial.StepRequests.Single().Command.Sequence);
    }

    [Fact]
    public void Call_local_support_and_obstacles_are_forwarded_without_a_product_registry()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f) { Motion = default(CharacterMotion) with { LastCommandSequence = 41 } };
        Transform transform = new(Vector3.One, Quaternion.Identity, Vector3.One);
        CharacterSupport support = new(true, CharacterSupportLifecycle.Active, 99, transform);
        CharacterObstacle obstacle = new(100, transform, new Vector3(-1f), Vector3.One, true, Vector3.Zero, Vector3.Zero);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2)))
            system.Step(player, new ProductUpdateState(1f / 60f), new CharacterStepEnvironment(support, new[] { obstacle }));

        CharacterStepRequest request = Assert.Single(spatial.StepRequests);
        Assert.Equal(support, request.Support);
        Assert.Equal([obstacle], request.Obstacles.ToArray());
        Assert.Equal(42UL, request.Command.Sequence);
    }

    [Fact]
    public void Appearance_uses_normalized_material_slots_and_atlases_then_releases_dependents_in_order()
    {
        List<string> releases = [];
        ContentFake content = new("mesh/hold.json", Hash, releases);
        content.Add("texture/wall.png", Hash);
        content.Add("sprite/rat.png", Hash);
        AppearanceFake appearance = new(releases);
        PrivateersHoldInputs inputs = new(
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

        using (PrivateersHoldAppearance presentation = new(content, appearance, inputs))
        {
            Assert.Equal([3u], appearance.StaticMeshBindings.Select(binding => binding.MaterialSlot));
            Assert.Single(appearance.AtlasRequests);
            Assert.Single(appearance.SpriteRequests);
            presentation.Dispose();
        }

        Assert.True(releases.IndexOf("appearance") < releases.IndexOf("atlas"));
        Assert.True(releases.IndexOf("atlas") < releases.IndexOf("material"));
    }

    [Fact]
    public void Normalized_actor_states_preserve_all_directional_sectors_and_select_an_explicit_sector()
    {
        PrivateersHoldInputs inputs = ReadInputs(RepositoryRoot());
        NormalizedSpriteState state = inputs.ActorSprites.Values
            .SelectMany(sprite => sprite.States.Values)
            .First(value => value.Orientations.Count == 8);

        Assert.Equal(Enumerable.Range(0, 8), state.Orientations.Keys.OrderBy(key => key));
        foreach ((int sector, IReadOnlyList<uint> frames) in state.Orientations)
        {
            Assert.NotEmpty(frames);
            Assert.Equal(frames, state.SelectOrientation(sector));
        }

        NormalizedSpriteState sparse = new("idle", [10], 8F, true)
        {
            Orientations = new Dictionary<int, IReadOnlyList<uint>>
            {
                [0] = [10],
                [4] = [40],
            },
        };
        Assert.Equal([40u], sparse.SelectOrientation(4));
        Assert.Throws<InvalidOperationException>(() => sparse.SelectOrientation(1));
    }

    [Fact]
    public void Authored_actor_presentation_resolves_rest_state_and_effective_playback_without_mobile_specific_runtime_policy()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        string scenario = File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        PrivateersHoldInputs ratInputs = PrivateersHoldContent.Read(ImportContent(root), Encoding.UTF8.GetBytes(scenario), definitions);
        NormalizedActorSprite rat = SpriteFor(ratInputs, "rat");
        Assert.Equal("ratIdle", rat.PreferredRestState);

        PrivateersHoldInputs impInputs = PrivateersHoldContent.Read(ImportContent(root), Encoding.UTF8.GetBytes(scenario.Replace("\"actor\": \"rat\"", "\"actor\": \"imp\"", StringComparison.Ordinal)), definitions);
        Assert.Equal("move", SpriteFor(impInputs, "imp").PreferredRestState);
        Assert.Equal(10F, SpriteFor(impInputs, "imp").States["move"].EffectiveFramesPerSecond);
        Assert.DoesNotContain("idle", SpriteFor(impInputs, "imp").States.Keys);

        PrivateersHoldInputs batInputs = PrivateersHoldContent.Read(ImportContent(root), Encoding.UTF8.GetBytes(scenario.Replace("\"actor\": \"rat\"", "\"actor\": \"giant-bat\"", StringComparison.Ordinal)), definitions);
        Assert.Equal("move", SpriteFor(batInputs, "giant-bat").PreferredRestState);
        Assert.Equal(10F, SpriteFor(batInputs, "giant-bat").States["move"].EffectiveFramesPerSecond);
        Assert.DoesNotContain("idle", SpriteFor(batInputs, "giant-bat").States.Keys);

        NormalizedActorSprite ordinary = SpriteFor(ratInputs, "skeletal-warrior");
        Assert.Equal("idle", ordinary.PreferredRestState);
        Assert.All(ordinary.States.Values, state => Assert.Equal(state.FramesPerSecond, state.EffectiveFramesPerSecond));
    }

    [Fact]
    public void Normalized_media_parses_an_imported_preferred_rest_state_and_rejects_unknown_presentation_states()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] scenario = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        ProductContent withImportedPreference = MutateDungeonMedia(root, media => media["actors"]!.AsArray()
            .Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "idle");
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(withImportedPreference, scenario, definitions);
        Assert.Equal("idle", SpriteFor(inputs, "skeletal-warrior").PreferredRestState);

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(
            MutateDungeonMedia(root, media => media["actors"]!.AsArray().Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "missingState"),
            scenario,
            definitions));

        DaggerfallDefinitions unknownAuthoredState = DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")).Replace("\"preferredRestState\": \"ratIdle\"", "\"preferredRestState\": \"missingState\"", StringComparison.Ordinal)));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(ImportContent(root), scenario, unknownAuthoredState));
    }

    [Fact]
    public void Directional_sprite_sectors_follow_actor_heading_with_classic_octant_boundaries()
    {
        Assert.Equal(0, PrivateersHoldAppearance.RelativeSector(0f, 0f, -1f));
        Assert.Equal(4, PrivateersHoldAppearance.RelativeSector(0f, 0f, 1f));
        Assert.Equal(6, PrivateersHoldAppearance.RelativeSector(0f, 1f, 0f));
        Assert.Equal(2, PrivateersHoldAppearance.RelativeSector(0f, -1f, 0f));
        Assert.Equal(0, PrivateersHoldAppearance.RelativeSector(MathF.PI / 2f, 1f, 0f));
        Assert.Equal(0, PrivateersHoldAppearance.RelativeSector(0f, 0f, 0f));

        float twentyTwo = 22f * MathF.PI / 180f;
        float twentyThree = 23f * MathF.PI / 180f;
        float halfSector = MathF.PI / 8f;
        Assert.Equal(0, PrivateersHoldAppearance.RelativeSector(0f, MathF.Sin(twentyTwo), -MathF.Cos(twentyTwo)));
        Assert.Equal(7, PrivateersHoldAppearance.RelativeSector(0f, MathF.Sin(halfSector), -MathF.Cos(halfSector)));
        Assert.Equal(1, PrivateersHoldAppearance.RelativeSector(0f, -MathF.Sin(halfSector), -MathF.Cos(halfSector)));
        Assert.Equal(7, PrivateersHoldAppearance.RelativeSector(0f, MathF.Sin(twentyThree), -MathF.Cos(twentyThree)));
        Assert.Equal(1, PrivateersHoldAppearance.RelativeSector(0f, -MathF.Sin(twentyThree), -MathF.Cos(twentyThree)));
    }

    [Fact]
    public void Enemy_idle_transition_returns_to_the_authored_preferred_rest_state()
    {
        List<string> releases = [];
        using PrivateersHoldAppearance presentation = new(MediaContent(releases), new AppearanceFake(releases), MediaInputs(preferredRestState: "ratIdle"));

        presentation.React(new EnemyBehaviorTransitionFact(11, EnemyBehaviorState.Idle, EnemyBehaviorState.Chase, 1, 1));
        Assert.Equal("move", Visual(presentation).State);
        presentation.React(new EnemyBehaviorTransitionFact(11, EnemyBehaviorState.Chase, EnemyBehaviorState.Idle, 1, 2));

        Assert.Equal("ratIdle", Visual(presentation).State);
    }

    [Fact]
    public void Direction_change_maps_the_current_idle_and_attack_frame_without_recreating_playback()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(directional: true));
        using ActorsState actors = new(
            new PlayerActorState(new ActorMechanicsState(new EntityId(99), [], []), "health"),
            [new ActorState(11, new ActorMechanicsState(new EntityId(11), [], []), new ActorPose(new WorldPoint(0f, 0f, 0f), 0f), "health")]);

        presentation.UpdateDirections(actors, new WorldPoint(0f, 0f, -1f));
        Assert.Equal(0u, appearance.SetFrameRequests.Last().FrameId);
        presentation.React(new AttackHitFact(11, 12, 1, 0, false, 1, 1));
        int playbackCount = appearance.PlaybackRequests.Count;
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Playing, 0d, 0, 1, false),
            true));
        presentation.Advance(OuterUpdate(1));
        presentation.UpdateDirections(actors, new WorldPoint(1f, 0f, 0f));

        Assert.Equal(playbackCount, appearance.PlaybackRequests.Count);
        Assert.Equal(3u, appearance.SetFrameRequests.Last().FrameId);
    }

    [Fact]
    public void Attack_alternate_selection_is_keyed_by_stable_event_identity_and_respects_authored_weights()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        KeyedRandomFake random = KeyedRandomFake.Create(40);
        PrivateersHoldInputs favoredAlternate = MediaInputs(primaryChance: 60);

        using (PrivateersHoldAppearance first = new(content, appearance, favoredAlternate, random: random.Service))
        {
            first.React(new AttackHitFact(11, 12, 1, 0, false, 7, 9));
            SpritePlaybackCreateRequest selected = appearance.PlaybackRequests.Last();
            Assert.Equal([3u], selected.Frames.Span.ToArray().Select(frame => frame.FrameId));

            int beforeDuplicate = appearance.PlaybackRequests.Count;
            first.React(new AttackHitFact(11, 12, 1, 0, false, 7, 9));
            Assert.Equal(beforeDuplicate, appearance.PlaybackRequests.Count);
        }

        AppearanceFake secondAppearance = new(releases);
        using (PrivateersHoldAppearance second = new(content, secondAppearance, MediaInputs(primaryChance: 20), random: KeyedRandomFake.Create(40).Service))
        {
            second.React(new AttackHitFact(11, 12, 1, 0, false, 7, 9));
            Assert.Equal([2u], secondAppearance.PlaybackRequests.Last().Frames.Span.ToArray().Select(frame => frame.FrameId));
        }

        Assert.Equal("daggerfall.media.attack-alternate.v1", random.Requests[0].Scope);
        Assert.Equal("daggerfall.media.hit-cue.v1", random.Requests[1].Scope);
    }

    [Fact]
    public void Marker_crossings_are_consumed_once_without_emitting_a_second_combat_presentation()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 1, false),
            true));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 2, false),
            true));

        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(), audio.Service);
        presentation.React(new AttackHitFact(11, 12, 1, 0, false, 3, 4));
        int playbacksBefore = appearance.PlaybackRequests.Count;
        int audioBefore = audio.Emits.Count;

        presentation.Advance(OuterUpdate(1));
        presentation.Advance(OuterUpdate(2));

        Assert.Equal(playbacksBefore, appearance.PlaybackRequests.Count);
        // The Daggerfall marker may produce a presentation sound, but a duplicate
        // Engine crossing must not replay it or cause any gameplay mutation.
        Assert.Equal(audioBefore + 1, audio.Emits.Count);
        FieldInfo actorsField = typeof(PrivateersHoldAppearance).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        System.Collections.IDictionary visuals = (System.Collections.IDictionary)actorsField.GetValue(presentation)!;
        object visual = visuals[11L]!;
        FieldInfo crossingField = visual.GetType().GetField("<LastMarkerCrossing>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ulong consumed = (ulong)crossingField.GetValue(visual)!;
        Assert.Equal((ulong)1, consumed);
    }

    [Fact]
    public void Duplicate_attack_delivery_does_not_restart_playback_or_duplicate_tuned_audio()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        DaggerfallPresentationAudioTuning tuning = new(.25F, 1.5F, .75F, 12F);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(), audio.Service, tuning);
        AttackHitFact hit = new(11, 12, 1, 0, false, 7, 9);

        presentation.React(hit);
        int playbackCount = appearance.PlaybackRequests.Count;
        presentation.React(hit);

        Assert.Equal(playbackCount, appearance.PlaybackRequests.Count);
        AudioEmitRequest emitted = Assert.Single(audio.Emits);
        Assert.Equal(.25F, emitted.Descriptor.Volume);
        Assert.Equal(1.5F, emitted.Descriptor.Pitch);
        Assert.Equal(.75F, emitted.Descriptor.SpatialBlend);
        Assert.Equal(12F, emitted.Descriptor.Attenuation);
        Assert.True(float.IsFinite(emitted.Descriptor.Attenuation));
        Assert.True(emitted.Descriptor.Attenuation > 0F);
    }

    [Fact]
    public void Completed_one_shot_returns_to_rest_on_the_next_distinct_outer_update_even_when_engine_does_not_advance()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs());
        presentation.React(new AttackHitFact(11, 12, 1, 0, false, 7, 9));
        int beforeCompletion = appearance.PlaybackRequests.Count;
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Completed, 0D, 0, 1, true),
            true));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Completed, 0D, 0, 2, true),
            false));

        presentation.Advance(OuterUpdate(1));
        Assert.Equal(beforeCompletion, appearance.PlaybackRequests.Count);

        presentation.Advance(OuterUpdate(2));
        Assert.Equal(beforeCompletion + 1, appearance.PlaybackRequests.Count);
        Assert.Equal([0u], appearance.PlaybackRequests.Last().Frames.Span.ToArray().Select(frame => frame.FrameId));
        int advancesAfterRest = appearance.AdvanceRequests.Count;

        presentation.Advance(OuterUpdate(2));
        Assert.Equal(advancesAfterRest, appearance.AdvanceRequests.Count);
        Assert.Equal(beforeCompletion + 1, appearance.PlaybackRequests.Count);
    }

    [Fact]
    public void Appearance_failures_release_staged_handles_and_keep_the_previous_playback_live()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake constructionFailure = new(releases) { FailSpritePlaybackCreateAt = 1 };
        Assert.Throws<InvalidOperationException>(() => new PrivateersHoldAppearance(content, constructionFailure, MediaInputs()));
        Assert.Equal(constructionFailure.CreatedAtlases, constructionFailure.DisposedAtlases);
        Assert.Equal(constructionFailure.CreatedAppearances, constructionFailure.DisposedAppearances);

        AppearanceFake replacementFailure = new(releases);
        using PrivateersHoldAppearance presentation = new(content, replacementFailure, MediaInputs());
        SpritePlaybackHandle original = Assert.Single(replacementFailure.CreatedPlaybacks).Handle;
        replacementFailure.FailSpritePlaybackControlAt = replacementFailure.ControlRequests.Count + 1;
        Assert.Throws<InvalidOperationException>(() => presentation.React(new AttackHitFact(11, 12, 1, 0, false, 7, 9)));

        presentation.Advance(OuterUpdate(1));
        Assert.Equal(original, Assert.Single(replacementFailure.AdvanceRequests).Playback.Handle);
        Assert.Equal(1, replacementFailure.DisposedPlaybacks);
    }

    [Fact]
    public void Trailing_attack_marker_is_rejected_before_engine_playback_creation()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        PrivateersHoldInputs inputs = MediaInputs(primaryFrames: [0, -1]);
        using PrivateersHoldAppearance presentation = new(content, appearance, inputs);
        int before = appearance.PlaybackRequests.Count;

        Assert.Throws<InvalidOperationException>(() => presentation.React(new AttackHitFact(11, 12, 1, 0, false, 7, 9)));
        Assert.Equal(before, appearance.PlaybackRequests.Count);
    }

    [Fact]
    public void Actor_atlas_frames_do_not_override_normalized_world_geometry()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(MediaContent(releases), appearance, MediaInputs());

        Assert.All(appearance.AtlasRequests.Single().Frames.Span.ToArray(), frame => Assert.False(frame.HasSize));
        Assert.Equal(Vector2.One, appearance.SpriteRequests.Single().Size);
    }

    [Fact]
    public void Presentation_checkpoint_restores_prior_playback_and_event_delivery_after_snapshot_failure()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(), audio.Service);
        SpritePlayback original = Visual(presentation).Playback!;
        AttackHitFact hit = new(11, 12, 1, 0, false, 17, 23);

        presentation.BeginAdmittedUpdate();
        PrivateersHoldAppearance.PresentationCheckpoint checkpoint = presentation.Checkpoint();
        presentation.React(hit);
        SpritePlayback staged = Visual(presentation).Playback!;
        appearance.FailPublishAt = appearance.PublishCalls + 1;
        using ActorsState actors = EmptyActors();
        Assert.Throws<InvalidOperationException>(() => presentation.Publish(actors));

        presentation.Restore(checkpoint);
        Assert.Same(original, Visual(presentation).Playback);
        presentation.React(hit);
        Assert.NotSame(original, Visual(presentation).Playback);
        Assert.NotSame(staged, Visual(presentation).Playback);
        Assert.Equal(2, audio.Emits.Count);
        Assert.Equal(audio.Emits[0].SignalId, audio.Emits[1].SignalId);
    }

    [Fact]
    public void Presentation_rollback_detaches_a_published_new_effect_before_disposing_its_appearance()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases) { RejectDisposeOfRetainedAppearance = true };
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicEffects()));
        using ActorsState actors = ActorsAt(new WorldPoint(2F, 0F, 3F));
        presentation.Publish(actors);
        presentation.BeginAdmittedUpdate();
        PrivateersHoldAppearance.PresentationCheckpoint checkpoint = presentation.Checkpoint();
        presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1, false, 2, 3), actors);
        Appearance effectAppearance = Effect(presentation).Appearance;
        appearance.FailPublishAt = appearance.PublishCalls + 1;

        Assert.Throws<InvalidOperationException>(() => presentation.Publish(actors));
        presentation.Restore(checkpoint);

        Assert.DoesNotContain(appearance.RetainedAppearances, retained => ReferenceEquals(retained, effectAppearance));
        Assert.Equal(1, appearance.DisposedAppearances);
    }

    [Fact]
    public void Classic_textures_are_admitted_during_construction_and_rollback_releases_all_noncheckpoint_intermediates()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        NormalizedClassicPresentation weapon = ClassicWeapon();
        NormalizedClassicPresentation classic = new(weapon.Weapon, ClassicEffects().Effects)
        {
            CompatibleItemVisuals = weapon.CompatibleItemVisuals,
            Viewmodel = weapon.Viewmodel,
        };
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: classic));
        int constructionResourceRequests = appearance.OpenResourceRequests.Count;
        appearance.RejectLateResourceOpen = true;
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        SpritePlayback actorPlayback = Visual(presentation).Playback!;
        SpritePlayback viewmodelPlayback = Viewmodel(presentation).Playback!;
        presentation.BeginAdmittedUpdate();
        PrivateersHoldAppearance.PresentationCheckpoint checkpoint = presentation.Checkpoint();
        presentation.React(new AttackMissedFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1, false, 2, 3));
        presentation.React(new AttackMissedFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 2, false, 2, 3));
        using ActorsState actors = ActorsAt(new WorldPoint(2F, 0F, 3F));
        presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 3, false, 2, 3), actors);
        presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 4, false, 2, 3), actors);

        presentation.Restore(checkpoint);

        Assert.Equal(constructionResourceRequests, appearance.OpenResourceRequests.Count);
        SpritePlaybackHandle[] discarded = appearance.CreatedPlaybacks
            .Where(playback => !ReferenceEquals(playback, actorPlayback) && !ReferenceEquals(playback, viewmodelPlayback))
            .Select(playback => playback.Handle)
            .OrderBy(handle => handle.Value)
            .ToArray();
        Assert.Equal(discarded, appearance.DisposedPlaybackHandles.OrderBy(handle => handle.Value).ToArray());
        Assert.Empty(appearance.OpenResourceRequests.Skip(constructionResourceRequests));
    }

    [Fact]
    public void Retired_playback_is_lagged_to_next_update_and_engine_rollback_keeps_it_retryable()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs());
        SpritePlayback original = Visual(presentation).Playback!;
        presentation.React(new AttackHitFact(11, 12, 1, 0, false, 1, 2));

        Assert.Equal(0, appearance.DisposedPlaybacks);
        presentation.BeginAdmittedUpdate();
        Assert.Equal(1, appearance.DisposedPlaybacks);
        appearance.RollbackPendingPlaybackReleases();

        presentation.BeginAdmittedUpdate();
        Assert.Equal(2, appearance.DisposedPlaybacks);
        Assert.Equal(original.Handle, appearance.DisposedPlaybackHandles[0]);
        Assert.Equal(original.Handle, appearance.DisposedPlaybackHandles[1]);
        appearance.CommitPendingPlaybackReleases();
        presentation.CompleteAdmittedUpdate();
    }

    [Fact]
    public void Non_advanced_receipts_cannot_consume_markers_or_complete_a_one_shot()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
        presentation.React(new AttackHitFact(11, 12, 1, 0, false, 2, 3));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Completed, 0D, 0, 1, true),
            false));

        presentation.Advance(OuterUpdate(1));

        PrivateersHoldAppearance.ActorVisual visual = Visual(presentation);
        Assert.Equal((ulong)0, visual.LastMarkerCrossing);
        Assert.False(visual.CompletedOuterUpdate);
        Assert.Empty(audio.Emits);
    }

    [Fact]
    public void Missed_attack_markers_never_emit_hit_variants()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
        presentation.React(new AttackMissedFact(11, 12, 1, 1, false, 2, 3));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 1, false),
            true));

        presentation.Advance(OuterUpdate(1));

        Assert.DoesNotContain(audio.Emits, emitted => emitted.SignalId.Contains("hit", StringComparison.Ordinal));
    }

    [Fact]
    public void Hit_variant_is_keyed_by_event_identity_and_can_select_beyond_the_first_classic_hit_clip()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AttackHitFact hit = new(11, 12, 1, 0, false, 8, 13);
        AudioRecorder firstAudio = AudioRecorder.Create();
        AppearanceFake firstAppearance = new(releases);
        using (PrivateersHoldAppearance first = new(content, firstAppearance, MediaInputs(includeAlternate: false), firstAudio.Service, random: KeyedRandomFake.Create(5).Service))
        {
            first.React(hit);
            AudioEmitRequest emitted = Assert.Single(firstAudio.Emits);
            Assert.Equal((ulong)6, emitted.Descriptor.Clip.Value);
        }

        AudioRecorder secondAudio = AudioRecorder.Create();
        using (PrivateersHoldAppearance second = new(content, new AppearanceFake(releases), MediaInputs(includeAlternate: false), secondAudio.Service, random: KeyedRandomFake.Create(5).Service))
        {
            second.React(hit);
        }

        Assert.Equal(firstAudio.Emits.Single().SignalId, secondAudio.Emits.Single().SignalId);
        Assert.Equal(firstAudio.Emits.Single().Descriptor.Clip, secondAudio.Emits.Single().Descriptor.Clip);
    }

    [Fact]
    public void Hit_variant_selection_uses_the_authored_contiguous_catalog_cardinality()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AudioRecorder audio = AudioRecorder.Create();
        IReadOnlyList<NormalizedAudioClip> authoredAudio =
        [
            new NormalizedAudioClip("swing", "audio/swing.wav", Hash),
            new NormalizedAudioClip("hit1", "audio/hit.wav", Hash),
            new NormalizedAudioClip("hit2", "audio/hit2.wav", Hash),
        ];

        using PrivateersHoldAppearance presentation = new(content, new AppearanceFake(releases), MediaInputs(includeAlternate: false, audio: authoredAudio), audio.Service, random: KeyedRandomFake.Create(2).Service);
        presentation.React(new AttackHitFact(11, 12, 1, 0, false, 8, 13));

        Assert.Equal((ulong)3, Assert.Single(audio.Emits).Descriptor.Clip.Value);
    }

    [Fact]
    public void Classic_blood_effect_is_keyed_to_the_player_hit_and_retires_after_its_final_frame()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicEffects()));
        WorldPoint targetPosition = new(7F, 2F, -3F);
        using ActorsState actors = ActorsAt(new WorldPoint(1F, 1F, 1F));
        presentation.Publish(actors);
        actors.All[12].ApplyPose(new ActorPose(targetPosition, 0F));
        AttackHitFact hit = new(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 0, false, 5, 8);

        presentation.React(hit, actors);
        presentation.React(hit, actors);

        Assert.Equal(2, appearance.PlaybackRequests.Count);
        Assert.Equal(1, EffectCount(presentation));
        Assert.Equal(targetPosition, Effect(presentation).Position);
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 1, true), true));
        presentation.Advance(OuterUpdate(1));
        Assert.Equal(1, EffectCount(presentation));
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 2, true), true));
        presentation.Advance(OuterUpdate(2));
        Assert.Equal(0, EffectCount(presentation));
    }

    [Fact]
    public void Compatible_right_hand_creates_a_viewmodel_and_uses_one_shot_strike_playback()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));

        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"));
        Assert.Single(appearance.PlaybackRequests);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        Assert.Equal(2, appearance.PlaybackRequests.Count);
        Assert.All(appearance.AtlasRequests.Last().Frames.Span.ToArray(), frame => Assert.False(frame.HasSize));
        Assert.Equal(Vector2.One, appearance.SpriteRequests.Last().Size);
        presentation.Publish(EmptyActors());
        Assert.Contains(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);

        AttackMissedFact miss = new(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1, false, 3, 4);
        presentation.React(miss);
        presentation.React(miss);
        Assert.Equal(3, appearance.PlaybackRequests.Count);
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 1, true), true));
        presentation.Advance(OuterUpdate(1));
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 2, true), true));
        presentation.Advance(OuterUpdate(2));
        Assert.Equal(4, appearance.PlaybackRequests.Count);

        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"));
        presentation.Publish(EmptyActors());
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
    }

    [Fact]
    public void Classic_effect_atlas_frames_do_not_override_varied_authored_display_geometry()
    {
        Vector2[] expectedSizes = [new(.25F, .5F), new(.75F, .3F), new(.4F, .9F)];
        for (int ordinal = 0; ordinal < expectedSizes.Length; ordinal++)
        {
            List<string> releases = [];
            ContentFake content = MediaContent(releases);
            AppearanceFake appearance = new(releases);
            NormalizedClassicPresentation classic = ClassicEffects(expectedSizes);
            using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: classic), random: KeyedRandomFake.Create(ordinal).Service);
            using ActorsState actors = ActorsAt(new WorldPoint(2F, 0F, 3F));

            presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, ordinal, false, 2, 3), actors);

            Assert.All(appearance.AtlasRequests.Last().Frames.Span.ToArray(), frame => Assert.False(frame.HasSize));
            Assert.Equal(expectedSizes[ordinal], appearance.SpriteRequests.Last().Size);
        }
    }

    [Fact]
    public void Viewmodel_publishes_its_authored_bounded_local_transform_and_restores_it_exactly()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        using ActorsState actors = EmptyActors();

        presentation.Publish(actors);
        AppearanceFact first = Assert.Single(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
        Assert.Equal(new Vector3(.2F, -.2F, -.7F), first.Transform.Translation);
        Assert.Equal(Quaternion.Identity, first.Transform.Rotation);
        Assert.All([first.Transform.Translation.X, first.Transform.Translation.Y, first.Transform.Translation.Z], coordinate => Assert.InRange(coordinate, -16F, 16F));
        PrivateersHoldAppearance.PresentationCheckpoint checkpoint = presentation.Checkpoint();
        presentation.Publish(actors);
        Assert.Equal(first.Transform, Assert.Single(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel).Transform);

        presentation.Restore(checkpoint);
        Assert.Equal(first.Transform, Viewmodel(presentation).Transform);
    }

    [Fact]
    public void Normalized_media_rejects_invalid_sector_loop_and_per_sector_attack_index()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateDungeonMedia(root, media => FirstActorState(media, "idle")["frames"]!.AsArray()[0]!["orientation"] = 8), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateDungeonMedia(root, media => FirstActorState(media, "idle")["playback"]!["loops"] = "true"), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateDungeonMedia(root, media =>
        {
            JsonArray frames = FirstActorState(media, "primaryAttack")["frames"]!.AsArray();
            JsonNode frame = frames.Last(value => value!["orientation"]!.GetValue<int>() == 7)!;
            frames.Remove(frame);
        }), payload, definitions));
    }

    [Fact]
    public void Normalized_classic_audio_requires_a_contiguous_canonical_hit_cue_family()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media =>
        {
            JsonArray audio = media["audio"]!.AsArray();
            audio.Single(value => value!["clip"]!.GetValue<string>() == "hit2")!.AsObject()["clip"] = "hit3";
        }), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media =>
        {
            JsonArray audio = media["audio"]!.AsArray();
            audio.Remove(audio.Single(value => value!["clip"]!.GetValue<string>() == "hit2"));
        }), payload, definitions));
    }

    [Fact]
    public void Normalized_classic_sidecar_rejects_noncanonical_schema_source_records_ranges_frames_effects_and_descriptors()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["schemaVersion"] = 2), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["weaponActions"]!.AsArray()[0]!["sourceRecordOrdinal"] = 6), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["weaponActions"]!.AsArray()[1]!["frameStart"] = 0), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["weaponActions"]!.AsArray()[0]!["frameCount"] = int.MaxValue), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["frames"]!.AsArray()[0]!["frameIndex"] = 4), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media =>
        {
            JsonObject frame = WeaponResource(media)["frames"]!.AsArray()[0]!.AsObject();
            frame["x"] = 1;
            frame["width"] = int.MaxValue;
        }), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["effects"]!.AsArray()[0]!["sourceRecordOrdinal"] = 3), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["effects"]!.AsArray()[0]!["timing"]!["framesPerSecond"] = 12), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => EffectResource(media, "effect.blood.0")["frames"]!.AsArray()[0]!["frameIndex"] = 1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["byteLength"] = 1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["mimeType"] = ""), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["sourceWidth"] = -1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["relativePath"] = "../weapon.png"), payload, definitions));
    }

    [Fact]
    public void Host_admits_one_realtime_update_and_releases_the_normalized_session_owners()
    {
        string root = RepositoryRoot();
        List<string> releases = [];
        ContentFake content = new(releases);
        PrivateersHoldInputs inputs = ReadInputs(root);
        content.Add(inputs.SpatialArtifact.Path, inputs.SpatialArtifact.Sha256);
        content.Add(inputs.StaticMesh.Path, inputs.StaticMesh.Sha256);
        foreach (NormalizedMaterial material in inputs.Materials) content.Add(material.TexturePath, material.TextureSha256);
        foreach (NormalizedActorSprite sprite in inputs.ActorSprites.Values)
        {
            content.Add(sprite.TexturePath, sprite.TextureSha256);
            if (sprite.Corpse is { } corpse) content.Add(corpse.TexturePath, corpse.TextureSha256);
        }
        foreach (NormalizedAudioClip clip in inputs.Audio) content.Add(clip.Path, clip.Sha256);
        foreach (NormalizedClassicEffect effect in inputs.ClassicPresentation.Effects) content.Add(effect.TexturePath, effect.TextureSha256);
        if (inputs.ClassicPresentation.Weapon is { } weapon) content.Add(weapon.TexturePath, weapon.TextureSha256);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);

        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input)))
        {
            product.Start();
            ProductUpdateFacts facts = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d);
            Assert.Equal(ProductUpdateResult.None, product.Update(new ProductUpdate(facts, ReadOnlySpan<ProductInputEvent>.Empty)));
            Assert.Equal(1, spatial.StepCalls);
            product.Shutdown();
        }

        Assert.Equal(1, engine.UiOpenCalls);
        Assert.True(releases.IndexOf("session") < releases.LastIndexOf("content"));
    }

    [Fact]
    public void Typed_input_and_explicit_combat_remain_product_semantic_above_the_normalized_engine_seams()
    {
        LookRecorder look = LookRecorder.Create();
        InputActionId attack = new("test.attack");
        PlayerControlBindings controls = new(["move"u8.ToArray()], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD, new DirectionalMovementBindings("forward"u8.ToArray(), "backward"u8.ToArray(), "left"u8.ToArray(), "right"u8.ToArray()));
        PlayerInputSystem input = new(DaggerfallTuning.Defaults.PlayerControl, look.Service, controls, [new InputActionBinding(attack, "attack"u8.ToArray())]);
        PlayerControlState player = new(new WorldPoint(0, 0, 0), 0, 0);
        ProductUpdateState update = new(.125F);
        update.Add(Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW));
        update.Add(Input(InputEventKind.PointerDelta, x: .25F, y: -.5F));
        update.Add(Input(InputEventKind.DirectDigital, x: 1F, phase: InputPhase.DirectUi, intent: "attack"));
        input.Apply(player, update);
        Assert.Equal(new Vector2(0, 1), update.PlanarIntent);
        Assert.True(update.IsRequested(attack));
        Assert.Equal(new Vector2(.25F, -.5F), look.LastDelta);

        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        long healthBefore = session.State.Actors.All[2000].Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw;
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        Assert.True(session.State.Actors.All[2000].Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw < healthBefore);
    }

    [Fact]
    public void Daggerfall_save_before_first_step_uses_an_explicit_no_checkpoint_branch()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);

        DaggerfallSavePayload saved = DaggerfallSavePayload.Decode(session.CaptureSave());

        Assert.Null(saved.Continuation);
        Assert.NotEmpty(saved.Inventory.UniqueItems);
        Assert.NotEmpty(saved.Inventory.Stacks);
        Assert.Equal(session.State.Actors.All.Count, saved.Actors.Length);
    }

    [Fact]
    public void Daggerfall_save_after_a_step_serializes_the_engine_continuation_checkpoint()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.Update(new ProductUpdateState(.125f));

        DaggerfallSavePayload saved = DaggerfallSavePayload.Decode(session.CaptureSave());

        Assert.NotNull(saved.Continuation);
        Assert.Equal(1UL, saved.Continuation!.Checkpoint.SourceGeneration);
    }

    [Fact]
    public void Restored_daggerfall_session_reuses_the_checkpoint_for_an_immediate_resave_without_initial_loadout_duplication()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        RulesetSavePayload payload;
        using (DaggerfallSession original = new(source.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            original.Update(new ProductUpdateState(.125f));
            payload = original.CaptureSave();
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = new(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Decode(payload));

        DaggerfallSavePayload immediate = DaggerfallSavePayload.Decode(resumed.CaptureSave());

        Assert.Equal(DaggerfallSavePayload.Decode(payload).Continuation, immediate.Continuation);
        DaggerfallInventorySave originalInventory = DaggerfallSavePayload.Decode(payload).Inventory;
        Assert.Equal(originalInventory.Stacks, immediate.Inventory.Stacks);
        Assert.Equal(originalInventory.UniqueItems, immediate.Inventory.UniqueItems);
        Assert.Equal(originalInventory.Equipment, immediate.Inventory.Equipment);
        Assert.Equal(0, resumedSpatial.StepCalls);
    }

    [Fact]
    public void Restored_combat_cooldown_is_relative_to_the_resumed_host_timeline_not_the_saved_generation()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        RulesetSavePayload payload;
        using (DaggerfallSession original = new(source.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            original.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 77, 400, .125));
            payload = original.CaptureSave();
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = new(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Decode(payload));
        long before = resumed.State.Actors.All[2000].Mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw;

        resumed.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 2, 1, .125));

        Assert.Equal(before, resumed.State.Actors.All[2000].Mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw);
    }

    [Fact]
    public void Restore_payload_rejects_unknown_inventory_and_non_defeated_corpse_before_session_construction()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSavePayload saved;
        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
            saved = DaggerfallSavePayload.Decode(session.CaptureSave());

        DaggerfallSavePayload badInventory = saved with
        {
            Inventory = saved.Inventory with { Stacks = [new DaggerfallStackSave("not-an-item", 1)] },
        };
        DaggerfallSavePayload badCorpse = saved with
        {
            Corpses = [new DaggerfallCorpseSave(2000, 1, false, true, [], [])],
        };
        DaggerfallSavePayload badPitch = saved with { Player = saved.Player with { PitchRadians = 2f } };
        DaggerfallSavePayload badHealth = saved with { Player = saved.Player with { Health = long.MaxValue } };
        DaggerfallSavePayload badProgression = saved with { Level = 2 };
        DaggerfallSavePayload collidingUnique = saved with
        {
            Inventory = saved.Inventory with
            {
                UniqueItems = [new DaggerfallUniqueSave(saved.Inventory.UniqueItems[0].ItemId, 1)],
                Equipment = saved.Inventory.Equipment.Select(value => value with { ItemEntityId = 1 }).ToArray(),
            },
        };
        DaggerfallSavePayload missingStableReservation = saved with { ReservedUniqueItemEntityIds = saved.ReservedUniqueItemEntityIds.Where(value => value != 1).ToArray() };

        Assert.Throws<ArgumentException>(() => badInventory.ValidateForRestore(definitions, inputs, DaggerfallTuning.Defaults, RandomMinimum.Create()));
        Assert.Throws<ArgumentException>(() => badCorpse.ValidateForRestore(definitions, inputs, DaggerfallTuning.Defaults, RandomMinimum.Create()));
        Assert.Throws<ArgumentException>(() => badPitch.ValidateForRestore(definitions, inputs, DaggerfallTuning.Defaults, RandomMinimum.Create()));
        Assert.Throws<ArgumentException>(() => badHealth.ValidateForRestore(definitions, inputs, DaggerfallTuning.Defaults, RandomMinimum.Create()));
        Assert.Throws<ArgumentException>(() => badProgression.ValidateForRestore(definitions, inputs, DaggerfallTuning.Defaults, RandomMinimum.Create()));
        Assert.Throws<ArgumentException>(() => collidingUnique.ValidateForRestore(definitions, inputs, DaggerfallTuning.Defaults, RandomMinimum.Create()));
        Assert.Throws<ArgumentException>(() => missingStableReservation.ValidateForRestore(definitions, inputs, DaggerfallTuning.Defaults, RandomMinimum.Create()));
    }

    [Fact]
    public void Ordinary_attack_uses_the_engine_visibility_receipt_then_the_shared_explicit_melee_policy()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        long healthBefore = session.State.Actors.All[2000].Mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw;
        session.Update(AttackUpdate());

        DaggerfallMeleeTargetingEvidence evidence = Assert.IsType<DaggerfallMeleeTargetingEvidence>(session.LastMeleeTargeting);
        Assert.Equal(2000, evidence.SelectedTargetId);
        Assert.Equal(perception.Requests.Last(), evidence.Request);
        Assert.True(perception.Requests.Count > 1);
        Assert.Equal((ulong)1, evidence.Request.Observers.Span[0].Entity);
        Assert.Equal(2.25d, evidence.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal(.5d, evidence.Request.Observers.Span[0].MinimumFacingCosine);
        Assert.Equal(1, evidence.Receipt.Pairs.Length);
        Assert.True(session.State.Actors.All[2000].Mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw < healthBefore);
    }

    [Fact]
    public void Corpse_loot_uses_engine_visibility_and_transfers_to_the_player_only_after_explicit_interaction()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.ToDictionary(
            item => new InventoryItemId(item.Id.Value),
            item => new ItemDefinition(ItemDefinitionId.Parse(item.Id.Value), item.IsFungible ? ItemKind.Fungible : ItemKind.Unique, item.MaximumQuantity));
        InventoryWorld world = new();
        EntityId playerOwner = new(1);
        world.RegisterInventory(new InventoryState(playerOwner));
        MechanicsInventoryContainerCoordinator containers = new(world, items);
        using SpatialMovementSystem movement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        using ActorsState actors = new(
            new PlayerActorState(new ActorMechanicsState(playerOwner, [], []), "health"),
            [new ActorState(2000, DefeatedMechanics(2000), new WorldPoint(0f, 0f, 1f), "health")]);
        DaggerfallCorpseLootModule loot = new(
            perception.Service, movement, containers, playerOwner, actors,
            new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new DaggerfallActorId("thief")) },
            definitions, RandomMinimum.Create(), new DaggerfallUniqueItemAllocator(1_000), new ProgressionState(), DaggerfallTuning.Defaults.LootInteraction);
        // Corpse policy is independent of player XP credit.
        ActorDiedFact death = new(2000, 77, 3, 2, 3);
        loot.Create(death);
        Assert.True(loot.Corpses[2000].IsInteractable);
        Assert.Empty(containers.Read(playerOwner).Stacks);

        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Occluded, 0d));
        Assert.Null(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
        Assert.True(loot.Corpses[2000].IsInteractable);

        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        PendingCorpseLoot pending = Assert.IsType<PendingCorpseLoot>(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
        Assert.False(pending.IsEmpty);
        FactBuffer<IProductFact> facts = new();
        Assert.Equal(CorpseLootCommitResult.Committed, loot.TryCommitLoot(pending, facts));
        Assert.False(loot.Corpses[2000].IsInteractable);
        Assert.NotEmpty(containers.Read(playerOwner).Stacks);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.NotEmpty(delivered.OfType<LootAwardedFact>());
        Assert.Single(delivered.OfType<CorpseLootedFact>());
        Assert.Equal(2.25d, loot.LastEvidence?.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal(.5d, loot.LastEvidence?.Request.Observers.Span[0].MinimumFacingCosine);
        Assert.Null(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
    }

    [Fact]
    public void Empty_corpse_is_explicitly_searchable_once_without_an_engine_inventory_owner()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.ToDictionary(
            item => new InventoryItemId(item.Id.Value),
            item => new ItemDefinition(ItemDefinitionId.Parse(item.Id.Value), item.IsFungible ? ItemKind.Fungible : ItemKind.Unique, item.MaximumQuantity));
        InventoryWorld world = new();
        EntityId playerOwner = new(1);
        world.RegisterInventory(new InventoryState(playerOwner));
        MechanicsInventoryContainerCoordinator containers = new(world, items);
        using SpatialMovementSystem movement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        using ActorsState actors = new(
            new PlayerActorState(new ActorMechanicsState(playerOwner, [], []), "health"),
            [new ActorState(2000, DefeatedMechanics(2000), new WorldPoint(0f, 0f, 1f), "health")]);
        DaggerfallCorpseLootModule loot = new(
            perception.Service, movement, containers, playerOwner, actors,
            new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new DaggerfallActorId("rat")) },
            definitions, RandomMinimum.Create(), new DaggerfallUniqueItemAllocator(1_000), new ProgressionState(), DaggerfallTuning.Defaults.LootInteraction);
        loot.Create(new ActorDiedFact(2000, 77, 3, 2, 3));
        Assert.True(loot.Corpses[2000].IsInteractable);
        Assert.False(loot.Corpses[2000].IsRegistered);
        Assert.Equal([playerOwner], world.InventoryOwners);

        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d));
        PendingCorpseLoot pending = Assert.IsType<PendingCorpseLoot>(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
        Assert.True(pending.IsEmpty);
        FactBuffer<IProductFact> facts = new();
        Assert.Equal(CorpseLootCommitResult.Committed, loot.TryCommitLoot(pending, facts));
        Assert.False(loot.Corpses[2000].IsInteractable);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Single(delivered.OfType<CorpseSearchedEmptyFact>());
        Assert.Null(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
    }

    [Fact]
    public void Enemy_behavior_uses_engine_visibility_then_shared_combat_and_transitions_without_replaying_damage()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        long healthBefore = session.State.Actors.Player.Mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw;
        session.Update(new ProductUpdateState(.125f));
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);
        long healthAfterAttack = session.State.Actors.Player.Mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw;
        Assert.True(healthAfterAttack < healthBefore);

        session.Update(new ProductUpdateState(.125f));
        Assert.Equal(healthAfterAttack, session.State.Actors.Player.Mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw);

        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 0d, PerceptionPairKind.FacingRejected, 0d));
        session.Update(new ProductUpdateState(.125f));
        Assert.Equal(EnemyBehaviorState.Idle, session.LastEnemyBehavior[2000].State);
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Occluded, 0d));
        session.Update(new ProductUpdateState(.125f));
        Assert.Equal(EnemyBehaviorState.Idle, session.LastEnemyBehavior[2000].State);

        ActorState rat = session.State.Actors.All[2000];
        rat.Mechanics.SetTrack(TrackId.Parse("health"), new ExactValue(-999), ExactTrackSetPolicy.ClampToBounds);
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        session.Update(new ProductUpdateState(.125f));
        Assert.Equal(EnemyBehaviorState.Dead, session.LastEnemyBehavior[2000].State);
    }

    [Fact]
    public void Daggerfall_target_selection_accepts_engine_inclusive_boundaries_and_rejects_other_engine_classifications()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        session.Update(AttackUpdate());
        Assert.Equal(2000, session.LastMeleeTargeting?.SelectedTargetId);

        foreach (PerceptionPairKind rejected in new[] { PerceptionPairKind.FacingRejected, PerceptionPairKind.Occluded })
        {
            perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, rejected, 1d));
            session.Update(AttackUpdate());
            Assert.Null(session.LastMeleeTargeting?.SelectedTargetId);
        }

        perception.Receipt = new PerceptionReadoutLeaseReceipt(ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty, 1, 1, 1, 1, 0, 0, 0);
        session.Update(AttackUpdate());
        Assert.Null(session.LastMeleeTargeting?.SelectedTargetId);
        Assert.Equal(1U, session.LastMeleeTargeting?.Receipt.DistanceRejects);
    }

    [Fact]
    public void Daggerfall_target_selection_excludes_defeated_and_stale_product_actors_and_uses_a_stable_tie_break()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);

        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            perception.Receipt = Receipt(
                new PerceptionPair(1, 2007, 1d, .8d, PerceptionPairKind.Visible, 1d),
                new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d),
                new PerceptionPair(1, 2008, .5d, .8d, PerceptionPairKind.Visible, 1d));
            session.Update(AttackUpdate());
            Assert.Equal(2008, session.LastMeleeTargeting?.SelectedTargetId);

            perception.Receipt = Receipt(
                new PerceptionPair(1, 2007, 1d, .8d, PerceptionPairKind.Visible, 1d),
                new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d));
            session.Update(AttackUpdate());
            Assert.Equal(2000, session.LastMeleeTargeting?.SelectedTargetId);

            session.State.Actors.All[2008].Mechanics.SetTrack(TrackId.Parse("health"), new ExactValue(0), ExactTrackSetPolicy.ClampToBounds);
            perception.Receipt = Receipt(new PerceptionPair(1, 2008, .5d, .8d, PerceptionPairKind.Visible, 1d));
            session.Update(AttackUpdate());
            Assert.Null(session.LastMeleeTargeting?.SelectedTargetId);
        }

        using SpatialMovementSystem movement = new(spatial.Service, content, new SpatialContentArtifact(inputs.SpatialArtifact.Path, inputs.SpatialArtifact.Sha256, inputs.SpatialArtifact.NavigationGridId), DaggerfallTuning.Defaults.Spatial);
        ActorMechanicsState playerMechanics = new(new EntityId(1), [], []);
        ActorMechanicsState staleMechanics = new(new EntityId(2001), [], []);
        using ActorsState actors = new(new PlayerActorState(playerMechanics, "health"), [new ActorState(2000, staleMechanics, new WorldPoint(0f, 0f, 1f), "health")]);
        DaggerfallMeleeTargetingModule targeting = new(
            perception.Service,
            movement,
            actors,
            new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new DaggerfallActorId("skeletal-warrior")) },
            DaggerfallTuning.Defaults.MeleeTargeting);
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d));
        long? stale = targeting.Select(
            new PlayerControlState(new WorldPoint(0f, 0f, 0f), 0f, 0f),
            new LookReceipt(default, default, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
            2.25d);
        Assert.Null(stale);
        Assert.Empty(perception.Requests.Last().Targets.Span.ToArray());
    }

    private static ProductUpdateState AttackUpdate()
    {
        ProductUpdateState update = new(.125f);
        update.Add(Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack"));
        return update;
    }

    private static LookReceipt ForwardLook() => new(default, default, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);

    private static ActorMechanicsState DefeatedMechanics(ulong entityId)
    {
        ExactTrackDefinition health = new(TrackId.Parse("health"), ExactValue.Zero, new ExactTrackMaximum.Fixed(new ExactValue(100)));
        return new ActorMechanicsState(new EntityId(entityId), [], [new ExactTrack(health, ExactValue.Zero)]);
    }

    private static PerceptionReadoutLeaseReceipt Receipt(params PerceptionPair[] pairs) => new(pairs, ReadOnlyMemory<PerceptionAggregate>.Empty, 1, checked((uint)pairs.Length), checked((ulong)pairs.Length), 0, 0, 0, 0);

    private static PrivateersHoldInputs ReadInputs(string root)
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        return PrivateersHoldContent.Read(ImportContent(root), File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
    }

    private static NormalizedActorSprite SpriteFor(PrivateersHoldInputs inputs, string actorId) => inputs.ActorSprites.First(pair => inputs.Project.Actors[pair.Key].ActorId.Value == actorId).Value;

    private static ProductContent ImportContent(string root) => ContentAt(root, "worldrpg/imports/privateers-hold");

    private static ProductContent FullContent(string root) => ContentAt(root, "worldrpg");

    private static ProductContent ContentAt(string root, string relativeDirectory)
    {
        string contentRoot = Path.Combine(root, "content");
        string selected = Path.Combine(contentRoot, relativeDirectory);
        return new ProductContent(Directory.GetFiles(selected, "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray());
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }

    private static void PopulateContent(ContentFake content, PrivateersHoldInputs inputs)
    {
        content.Add(inputs.SpatialArtifact.Path, inputs.SpatialArtifact.Sha256);
        content.Add(inputs.StaticMesh.Path, inputs.StaticMesh.Sha256);
        foreach (NormalizedMaterial material in inputs.Materials) content.Add(material.TexturePath, material.TextureSha256);
        foreach (NormalizedActorSprite sprite in inputs.ActorSprites.Values)
        {
            content.Add(sprite.TexturePath, sprite.TextureSha256);
            if (sprite.Corpse is { } corpse) content.Add(corpse.TexturePath, corpse.TextureSha256);
        }
        foreach (NormalizedAudioClip clip in inputs.Audio) content.Add(clip.Path, clip.Sha256);
        foreach (NormalizedClassicEffect effect in inputs.ClassicPresentation.Effects) content.Add(effect.TexturePath, effect.TextureSha256);
        if (inputs.ClassicPresentation.Weapon is { } weapon) content.Add(weapon.TexturePath, weapon.TextureSha256);
    }

    private static ContentFake MediaContent(List<string> releases)
    {
        ContentFake content = new(releases);
        content.Add("mesh/hold.json", Hash);
        content.Add("sprite/enemy.png", Hash);
        content.Add("audio/swing.wav", Hash);
        content.Add("audio/hit.wav", Hash);
        content.Add("audio/hit2.wav", Hash);
        content.Add("audio/hit3.wav", Hash);
        content.Add("audio/hit4.wav", Hash);
        content.Add("audio/hit5.wav", Hash);
        content.Add("effect/blood0.png", Hash);
        content.Add("effect/blood1.png", Hash);
        content.Add("effect/blood2.png", Hash);
        content.Add("effect/sparkle.png", Hash);
        return content;
    }

    private static PrivateersHoldInputs MediaInputs(int primaryChance = 50, IReadOnlyList<int>? primaryFrames = null, bool includeAlternate = true, bool directional = false, IReadOnlyList<NormalizedAudioClip>? audio = null, string? preferredRestState = null, NormalizedClassicPresentation? classic = null)
    {
        NormalizedSpriteState idle = new("idle", [0], 10F, true)
        {
            Orientations = directional
                ? Enumerable.Range(0, 8).ToDictionary(sector => sector, sector => (IReadOnlyList<uint>)(sector == 6 ? [1] : [0]))
                : new Dictionary<int, IReadOnlyList<uint>>(),
        };
        NormalizedSpriteState move = new("move", [0], 10F, true);
        NormalizedSpriteState hurt = new("hurt", [1], 10F, false);
        NormalizedSpriteState attack = new("primaryAttack", [2, 3], 10F, false)
        {
            Orientations = directional
                ? Enumerable.Range(0, 8).ToDictionary(sector => sector, sector => (IReadOnlyList<uint>)(sector == 6 ? [3, 2] : [2, 3]))
                : new Dictionary<int, IReadOnlyList<uint>>(),
        };
        Dictionary<string, NormalizedSpriteState> states = new()
        {
            [idle.Name] = idle,
            [move.Name] = move,
            [hurt.Name] = hurt,
            [attack.Name] = attack,
        };
        if (preferredRestState is not null && !states.ContainsKey(preferredRestState)) states.Add(preferredRestState, new(preferredRestState, [0], 10F, true));
        NormalizedActorSprite sprite = new("sprite/enemy.png", Hash, 32, 32,
            [new NormalizedAtlasFrame(0, 0, 0, 8, 8), new NormalizedAtlasFrame(1, 8, 0, 8, 8), new NormalizedAtlasFrame(2, 16, 0, 8, 8), new NormalizedAtlasFrame(3, 24, 0, 8, 8)],
            0, new Vector2(.5F, 0F), Vector2.One)
        {
            States = states,
            PreferredRestState = preferredRestState,
            AttackSequences = includeAlternate
                ? [new NormalizedAttackSequence(100 - primaryChance, primaryFrames ?? [0]), new NormalizedAttackSequence(primaryChance, [1])]
                : [new NormalizedAttackSequence(100, primaryFrames ?? [0])],
        };
        return new PrivateersHoldInputs(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            new SpatialContentArtifact("spatial/hold.json", Hash, 1),
            new ContentArtifact("mesh/hold.json", Hash),
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0),
            [],
            new Dictionary<long, NormalizedActorSprite> { [11] = sprite },
            audio ??
            [
                new NormalizedAudioClip("swing", "audio/swing.wav", Hash),
                new NormalizedAudioClip("hit1", "audio/hit.wav", Hash),
                new NormalizedAudioClip("hit2", "audio/hit2.wav", Hash),
                new NormalizedAudioClip("hit3", "audio/hit3.wav", Hash),
                new NormalizedAudioClip("hit4", "audio/hit4.wav", Hash),
                new NormalizedAudioClip("hit5", "audio/hit5.wav", Hash),
            ],
            classic);
    }

    private static NormalizedClassicPresentation ClassicEffects(IReadOnlyList<Vector2>? bloodDisplaySizes = null)
    {
        IReadOnlyList<NormalizedAtlasFrame> frames = [new NormalizedAtlasFrame(0, 0, 0, 8, 8)];
        Vector2 Size(int sourceRecordOrdinal) => sourceRecordOrdinal < 3 && bloodDisplaySizes is { Count: 3 } ? bloodDisplaySizes[sourceRecordOrdinal] : Vector2.One;
        NormalizedClassicEffect Effect(string name, int sourceRecordOrdinal, string path) => new(name, sourceRecordOrdinal, path, Hash, 8, 8, frames, new Vector2(.5F, .5F), Size(sourceRecordOrdinal), [0], 10F, false);
        return new NormalizedClassicPresentation(null, [Effect("blood0", 0, "effect/blood0.png"), Effect("blood1", 1, "effect/blood1.png"), Effect("blood2", 2, "effect/blood2.png"), Effect("magicSparkle", 3, "effect/sparkle.png")]);
    }

    private static NormalizedClassicPresentation ClassicWeapon()
    {
        IReadOnlyList<NormalizedAtlasFrame> frames = [new NormalizedAtlasFrame(0, 0, 0, 8, 8)];
        string[] names = ["idle", "strikeDown", "strikeDownLeft", "strikeLeft", "strikeRight", "strikeDownRight", "strikeUp"];
        IReadOnlyDictionary<string, NormalizedClassicWeaponAction> actions = names.Select((name, sourceRecordOrdinal) => new NormalizedClassicWeaponAction(name, sourceRecordOrdinal, 0, 1, "right", name == "idle" ? .1F : .4F, 10F, name == "idle", 0, 0)).ToDictionary(action => action.Name);
        return new NormalizedClassicPresentation(new NormalizedClassicWeapon("weapon.dagger.steel", "weapon/dagger.png", Hash, 8, 8, frames, new Vector2(.5F, .5F), Vector2.One, [0], actions), [])
        {
            CompatibleItemVisuals = new Dictionary<string, string> { ["iron-dagger"] = "weapon.dagger.steel" },
            Viewmodel = new ClassicViewmodelStyle(new WorldPoint(.2F, -.2F, -.7F), new Vector2(.5F, .5F), Vector2.One, 0),
        };
    }

    private static PrivateersHoldAppearance.ActorVisual Visual(PrivateersHoldAppearance presentation)
    {
        FieldInfo field = typeof(PrivateersHoldAppearance).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((Dictionary<long, PrivateersHoldAppearance.ActorVisual>)field.GetValue(presentation)!)[11];
    }

    private static ActorsState EmptyActors() => new(new PlayerActorState(new ActorMechanicsState(new EntityId(99), [], []), "health"), []);
    private static WorldRpg.Kit.Inventory.EquipmentRead RightHand(string itemId) => new([new WorldRpg.Kit.Inventory.EquipmentAssignment(new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand"), new WorldRpg.Kit.Inventory.UniqueInventoryItem(1, new WorldRpg.Kit.Inventory.InventoryItemId(itemId)))], 1, 1);
    private static ActorsState ActorsAt(WorldPoint point) => new(new PlayerActorState(new ActorMechanicsState(new EntityId(99), [], []), "health"), [new ActorState(12, new ActorMechanicsState(new EntityId(12), [], []), point, "health")]);
    private static int EffectCount(PrivateersHoldAppearance presentation) => ((List<PrivateersHoldAppearance.EffectVisual>)typeof(PrivateersHoldAppearance).GetField("effects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation)!).Count;
    private static PrivateersHoldAppearance.EffectVisual Effect(PrivateersHoldAppearance presentation) => Assert.Single((List<PrivateersHoldAppearance.EffectVisual>)typeof(PrivateersHoldAppearance).GetField("effects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation)!);
    private static PrivateersHoldAppearance.ViewmodelVisual Viewmodel(PrivateersHoldAppearance presentation) => Assert.IsType<PrivateersHoldAppearance.ViewmodelVisual>(typeof(PrivateersHoldAppearance).GetField("viewmodel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation));

    private static ProductUpdateFacts OuterUpdate(ulong simulationStep) => new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, simulationStep, simulationStep, 60, 1, 0, 1d / 60d);

    private static JsonObject FirstActorState(JsonObject media, string name) => media["actors"]!.AsArray()
        .Select(value => value!.AsObject())
        .SelectMany(actor => actor["states"]!.AsArray())
        .Select(value => value!.AsObject())
        .First(state => state["state"]!.GetValue<string>() == name);

    private static JsonObject WeaponResource(JsonObject media) => media["media"]!["resources"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(resource => resource["kind"]!.GetValue<string>() == "weaponSprite");

    private static JsonObject EffectResource(JsonObject media, string id) => media["media"]!["resources"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(resource => resource["id"]!.GetValue<string>() == id);

    private static ProductContent MutateDungeonMedia(string repositoryRoot, Action<JsonObject> mutate)
    {
        string contentRoot = Path.Combine(repositoryRoot, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports/privateers-hold"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray();
        int mediaIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("media/dungeon/manifest.json", StringComparison.Ordinal));
        int importsIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("import-manifest.json", StringComparison.Ordinal));
        Assert.True(mediaIndex >= 0 && importsIndex >= 0);

        JsonObject media = JsonNode.Parse(files[mediaIndex].Bytes.Span)!.AsObject();
        mutate(media);
        byte[] mediaBytes = Encoding.UTF8.GetBytes(media.ToJsonString());
        files[mediaIndex] = new ProductContentFile(files[mediaIndex].Path, mediaBytes);

        JsonObject imports = JsonNode.Parse(files[importsIndex].Bytes.Span)!.AsObject();
        JsonObject artifact = imports["artifacts"]!.AsArray().Select(value => value!.AsObject())
            .Single(value => value["relativePath"]!.GetValue<string>() == "media/dungeon/manifest.json");
        artifact["contentHash"] = Convert.ToHexString(SHA256.HashData(mediaBytes));
        files[importsIndex] = new ProductContentFile(files[importsIndex].Path, Encoding.UTF8.GetBytes(imports.ToJsonString()));
        return new ProductContent(files);
    }

    private static ProductContent MutateClassicMedia(string repositoryRoot, Action<JsonObject> mutate)
    {
        string contentRoot = Path.Combine(repositoryRoot, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports/privateers-hold"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray();
        int mediaIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("media/classic/manifest.json", StringComparison.Ordinal));
        int importsIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("import-manifest.json", StringComparison.Ordinal));
        Assert.True(mediaIndex >= 0 && importsIndex >= 0);

        JsonObject media = JsonNode.Parse(files[mediaIndex].Bytes.Span)!.AsObject();
        mutate(media);
        byte[] mediaBytes = Encoding.UTF8.GetBytes(media.ToJsonString());
        files[mediaIndex] = new ProductContentFile(files[mediaIndex].Path, mediaBytes);

        JsonObject imports = JsonNode.Parse(files[importsIndex].Bytes.Span)!.AsObject();
        JsonObject artifact = imports["artifacts"]!.AsArray().Select(value => value!.AsObject())
            .Single(value => value["relativePath"]!.GetValue<string>() == "media/classic/manifest.json");
        artifact["contentHash"] = Convert.ToHexString(SHA256.HashData(mediaBytes));
        files[importsIndex] = new ProductContentFile(files[importsIndex].Path, Encoding.UTF8.GetBytes(imports.ToJsonString()));
        return new ProductContent(files);
    }

    private static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl keyboard = KeyboardControl.None, float x = 0F, float y = 0F, InputPhase phase = InputPhase.None, string intent = "") => new(kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, keyboard, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None, InputValueKind.None, phase, InputProvenance.None, default, default, default, x, y, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, Encoding.UTF8.GetBytes(intent), ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    private sealed class ContentFake : IContentService
    {
        private readonly List<string> releases;
        private readonly Dictionary<string, ContentSha256> values = new(StringComparer.Ordinal);
        private readonly Dictionary<ulong, KeyValuePair<string, ContentSha256>> references = [];
        private ulong nextHandle = 1;

        internal ContentFake(string path, ContentSha256 hash, List<string> releaseLog) : this(releaseLog) => Add(path, hash);
        internal ContentFake(List<string> releaseLog) => releases = releaseLog;

        internal void Add(string contentPath, ContentSha256 contentHash) => values[contentPath] = contentHash;

        public ContentReference OpenReference(ContentOpenRequest request) => ResolveReference(new ContentResolveRequest(request.Path, values[request.Path]));

        public ContentReference ResolveReference(ContentResolveRequest request)
        {
            ResolveCalls++;
            if (!values.TryGetValue(request.Path, out ContentSha256 known) || known != request.Sha256) throw new InvalidOperationException("Unexpected content reference.");
            ulong value = nextHandle++;
            references.Add(value, new KeyValuePair<string, ContentSha256>(request.Path, known));
            return new ContentReference(new ContentReferenceHandle(value), () => releases.Add("content"));
        }

        public ReadOnlyMemory<ContentReferenceInfo> ReadReferenceInfo(ContentReference reference)
        {
            KeyValuePair<string, ContentSha256> item = references[reference.Handle.Value];
            return new[] { new ContentReferenceInfo(item.Key, item.Value, 1) };
        }

        public ReadOnlyMemory<byte> ReadBytes(ContentReadBytesRequest request) => ReadOnlyMemory<byte>.Empty;
        internal int ResolveCalls { get; private set; }
    }

    private class SpatialFake : DispatchProxy
    {
        private ContentSha256 hash = Hash;
        private List<string> releases = null!;
        internal ISpatialService Service { get; private set; } = null!;
        internal int ReplaceCalls { get; private set; }
        internal int ReadCalls { get; private set; }
        internal int CreateSessionCalls { get; private set; }
        internal int StepCalls { get; private set; }
        internal int ConfigValidationCalls { get; private set; }
        internal int CommandValidationCalls { get; private set; }
        internal bool RejectConfigValidation { get; set; }
        internal bool RejectCommandValidation { get; set; }
        internal SpatialContentArtifactReplaceRequest? LastRequest { get; private set; }
        internal List<CharacterStepRequest> StepRequests { get; } = [];
        // Representative fixture only: Engine owns the actual default and validity contract.
        internal CharacterControllerConfig RepresentativeValidConfig { get; } = default(CharacterControllerConfig) with
        {
            Shape = new CharacterShapeConfig(2.2f, 1.3f, .45f, .03f, .02f),
            Ground = new CharacterGroundConfig(6f, 5f, 4f, 31f, 42f, 7f, 3f, 2f),
            Air = new CharacterAirConfig(4f, 10f, 1f, 4f, 1f, 0f),
            Vertical = new CharacterVerticalConfig(18f, 48f, 46f, 6f, .4f),
            Jump = new CharacterJumpConfig(.2f, .15f, 0f, false),
            Surface = new CharacterSurfaceConfig(.9f, .02f, 16f, 9f, .35f, .04f, .2f, 8f, .2f),
            Recovery = new CharacterRecoveryConfig(.7f, 18f, .002f, .003f),
            Platform = new CharacterPlatformConfig(true, true, true, .8f, 0f, .03f),
            ExternalMotion = new CharacterExternalMotionConfig(1f, 0f, 40f, 70f, 1f, 400f),
            Solver = new CharacterSolverConfig(4, 7, 3, 24, 1, 8f, 48),
        };

        internal static SpatialFake Create(ContentSha256 contentHash, List<string> releaseLog)
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialFake>();
            SpatialFake fake = (SpatialFake)(object)service;
            fake.Service = service;
            fake.hash = contentHash;
            fake.releases = releaseLog;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.CreateSession) => CreateSession(),
            nameof(ISpatialService.DefaultCharacterControllerConfig) => RepresentativeValidConfig,
            nameof(ISpatialService.ValidateCharacterControllerConfig) => ValidateConfig((CharacterControllerConfig)arguments![0]!),
            nameof(ISpatialService.ValidateCharacterControllerCommand) => ValidateCommand((CharacterControllerValidationRequest)arguments![0]!),
            nameof(ISpatialService.ReplaceContentArtifact) => Replace((SpatialContentArtifactReplaceRequest)arguments![0]!),
            nameof(ISpatialService.ReadContentArtifact) => Read(),
            nameof(ISpatialService.ProposeCharacterStep) => Step((CharacterStepRequest)arguments![0]!),
            nameof(ISpatialService.CaptureCharacterContinuation) => Capture((CharacterContinuationCaptureRequest)arguments![0]!),
            nameof(ISpatialService.RestoreCharacterContinuation) => Restore((CharacterContinuationRestoreRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private SpatialContentArtifactReplaceReceipt Replace(SpatialContentArtifactReplaceRequest request)
        {
            ReplaceCalls++;
            LastRequest = request;
            return new(request.Content.Handle.Value, hash, 1, 2, 3, 4, 5, 6, 7, 8);
        }

        private SpatialSession CreateSession()
        {
            CreateSessionCalls++;
            return new SpatialSession(new SpatialSessionHandle(1), () => releases.Add("session"));
        }

        private object? ValidateConfig(CharacterControllerConfig config)
        {
            ConfigValidationCalls++;
            if (RejectConfigValidation) throw new InvalidOperationException("Rejected controller configuration.");
            return null;
        }

        private object? ValidateCommand(CharacterControllerValidationRequest request)
        {
            CommandValidationCalls++;
            if (RejectCommandValidation) throw new InvalidOperationException("Rejected controller command.");
            return null;
        }

        private SpatialContentArtifactReadout Read()
        {
            ReadCalls++;
            SpatialContentArtifactReplaceRequest request = LastRequest ?? throw new InvalidOperationException("Read before replace.");
            return new(true, request.Content.Handle.Value, hash, 2, 3, 4, 5, 6, 7, 8);
        }

        private CharacterStepReceipt Step(CharacterStepRequest request)
        {
            StepCalls++;
            StepRequests.Add(request);
            return default(CharacterStepReceipt) with
            {
                Generation = checked((ulong)StepCalls),
                Transform = new Transform(request.Position + new Vector3(1f, 0f, 0f), Quaternion.Identity, Vector3.One),
                Motion = request.Motion with { Grounded = true, LastCommandSequence = request.Command.Sequence },
                Ground = default(CharacterGround) with { Present = true },
            };
        }

        private CharacterContinuationCheckpoint Capture(CharacterContinuationCaptureRequest request)
        {
            if (request.ExpectedGeneration != checked((ulong)StepCalls)) throw new InvalidOperationException("Stale checkpoint generation.");
            CharacterMotion motion = StepRequests.Last().Motion with { LastCommandSequence = StepRequests.Last().Command.Sequence };
            return new CharacterContinuationCheckpoint(1, request.ExpectedGeneration, 1, 1, 1, RepresentativeValidConfig, motion);
        }

        private static CharacterContinuationRestoreReceipt Restore(CharacterContinuationRestoreRequest request) =>
            new(request.Checkpoint.SourceGeneration, request.Checkpoint.Motion);
    }

    private class LookRecorder : DispatchProxy
    {
        internal ILookService Service { get; private set; } = null!;
        internal Vector2 LastDelta { get; private set; }
        internal static LookRecorder Create()
        {
            ILookService service = DispatchProxy.Create<ILookService, LookRecorder>();
            LookRecorder proxy = (LookRecorder)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == nameof(ILookService.Diagnose)) return LookDiagnostic.Accepted;
            if (method?.Name == nameof(ILookService.Integrate))
            {
                LookRequest request = (LookRequest)arguments![0]!;
                LastDelta = request.Delta;
                LookState after = new(request.State.YawRadians + request.Delta.X, request.State.PitchRadians + request.Delta.Y);
                return new LookReceipt(request.State, after, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
            }
            return default(LookReceipt);
        }
    }

    private class RandomMinimum : DispatchProxy
    {
        internal IRandomService Service { get; private set; } = null!;

        internal static IRandomService Create()
        {
            IRandomService service = DispatchProxy.Create<IRandomService, RandomMinimum>();
            ((RandomMinimum)(object)service).Service = service;
            return service;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

    private class PerceptionFake : DispatchProxy
    {
        internal IPerceptionService Service { get; private set; } = null!;
        internal List<PerceptionQueryRequest> Requests { get; } = [];
        internal PerceptionReadoutLeaseReceipt Receipt { get; set; }

        internal static PerceptionFake Create()
        {
            IPerceptionService service = DispatchProxy.Create<IPerceptionService, PerceptionFake>();
            PerceptionFake proxy = (PerceptionFake)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IPerceptionService.QueryVisibility)) throw new NotSupportedException(method?.Name);
            PerceptionQueryRequest request = (PerceptionQueryRequest)arguments![0]!;
            Requests.Add(request);
            return Receipt;
        }
    }

    private class EngineContextFake : DispatchProxy
    {
        internal IEngineContext Context { get; private set; } = null!;
        internal int UiOpenCalls { get; private set; }
        private IContentService content = null!;
        private ISpatialService spatial = null!;
        private IAppearanceService appearance = null!;
        private ILookService look = null!;
        private IPerceptionService perception = null!;
        private ICameraViewService camera = null!;
        private IAudioService audio = null!;
        private IRandomService random = null!;
        private IUiService ui = null!;

        internal static EngineContextFake Create(IContentService content, ISpatialService spatial, IAppearanceService appearance, IPerceptionService? perception = null)
        {
            IEngineContext context = DispatchProxy.Create<IEngineContext, EngineContextFake>();
            EngineContextFake fake = (EngineContextFake)(object)context;
            fake.Context = context;
            fake.content = content;
            fake.spatial = spatial;
            fake.appearance = appearance;
            fake.look = ServiceProxy<ILookService, LookServiceFake>.Create();
            fake.perception = perception ?? PerceptionFake.Create().Service;
            fake.camera = ServiceProxy<ICameraViewService, CameraServiceFake>.Create();
            fake.audio = ServiceProxy<IAudioService, AudioServiceFake>.Create();
            fake.random = ServiceProxy<IRandomService, RandomServiceFake>.Create();
            fake.ui = UiServiceFake.Create(fake);
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            "get_Content" => content,
            "get_Spatial" => spatial,
            "get_Appearance" => appearance,
            "get_Look" => look,
            "get_Perception" => perception,
            "get_CameraView" => camera,
            "get_Audio" => audio,
            "get_Random" => random,
            "get_Ui" => ui,
            _ => throw new NotSupportedException(method?.Name),
        };

        private class LookServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(ILookService.Integrate) => Integrate((LookRequest)arguments![0]!),
                nameof(ILookService.Reset) => default(LookReceipt),
                nameof(ILookService.Rebase) => default(LookReceipt),
                nameof(ILookService.Diagnose) => default(LookDiagnostic),
                _ => throw new NotSupportedException(method?.Name),
            };
            private static LookReceipt Integrate(LookRequest request) => new(request.State, new LookState(request.State.YawRadians + request.Delta.X, request.State.PitchRadians + request.Delta.Y), Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
        }

        private class CameraServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(ICameraViewService.CreateCamera) => new Camera(new CameraHandle(1), () => { }),
                nameof(ICameraViewService.UpdateCamera) or nameof(ICameraViewService.SetActiveCamera) or nameof(ICameraViewService.ClearActiveCamera) or nameof(ICameraViewService.SetSkyBackground) or nameof(ICameraViewService.ClearSkyBackground) => null,
                nameof(ICameraViewService.ReplaceCamera) => new Camera(new CameraHandle(1), () => { }),
                _ => throw new NotSupportedException(method?.Name),
            };
        }

        private class RandomServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
                ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
                : throw new NotSupportedException(method?.Name);
        }

        private class AudioServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(IAudioService.OpenClip) => new AudioClipHandle(1),
                nameof(IAudioService.Emit) => new AudioSignalHandle(1),
                _ => throw new NotSupportedException(method?.Name),
            };
        }

        private class UiServiceFake : DispatchProxy
        {
            private EngineContextFake owner = null!;
            internal static IUiService Create(EngineContextFake parent)
            {
                IUiService service = DispatchProxy.Create<IUiService, UiServiceFake>();
                ((UiServiceFake)(object)service).owner = parent;
                return service;
            }
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(IUiService.OpenStream) => Open(),
                nameof(IUiService.PublishProjection) => null,
                _ => throw new NotSupportedException(method?.Name),
            };
            private UiStream Open() { owner.UiOpenCalls++; return new UiStream(new UiStreamHandle(1), () => { }); }
        }
    }

    private class ServiceProxy<TService, TProxy> : DispatchProxy where TService : class where TProxy : DispatchProxy
    {
        internal static TService Create()
        {
            return DispatchProxy.Create<TService, TProxy>();
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => throw new NotSupportedException(method?.Name);
    }

    private class KeyedRandomFake : DispatchProxy
    {
        internal IRandomService Service { get; private set; } = null!;
        internal List<KeyedRngRequest> Requests { get; } = [];
        private long value;

        internal static KeyedRandomFake Create(long returnedValue)
        {
            IRandomService service = DispatchProxy.Create<IRandomService, KeyedRandomFake>();
            KeyedRandomFake fake = (KeyedRandomFake)(object)service;
            fake.Service = service;
            fake.value = returnedValue;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            Requests.Add(request);
            return new KeyedRngReceipt(Math.Clamp(value, request.Minimum, request.Maximum));
        }
    }

    private class AudioRecorder : DispatchProxy
    {
        internal IAudioService Service { get; private set; } = null!;
        internal List<AudioEmitRequest> Emits { get; } = [];
        private ulong nextHandle = 1;

        internal static AudioRecorder Create()
        {
            IAudioService service = DispatchProxy.Create<IAudioService, AudioRecorder>();
            AudioRecorder recorder = (AudioRecorder)(object)service;
            recorder.Service = service;
            return recorder;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IAudioService.OpenClip) => new AudioClipHandle(nextHandle++),
            nameof(IAudioService.Emit) => Emit((AudioEmitRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private AudioSignalHandle Emit(AudioEmitRequest request)
        {
            Emits.Add(request);
            return new AudioSignalHandle(nextHandle++);
        }
    }

    private sealed class AppearanceFake(List<string> releases) : IAppearanceService
    {
        internal List<RenderResourceRequest> OpenResourceRequests { get; } = [];
        internal List<MeshMaterialBinding> StaticMeshBindings { get; } = [];
        internal List<SpriteAtlasCreateRequest> AtlasRequests { get; } = [];
        internal List<SpriteFromAtlasRequest> SpriteRequests { get; } = [];
        internal List<SpritePlaybackCreateRequest> PlaybackRequests { get; } = [];
        internal List<SpritePlaybackControlRequest> ControlRequests { get; } = [];
        internal List<SpritePlaybackAdvanceRequest> AdvanceRequests { get; } = [];
        internal List<AppearanceFact[]> Snapshots { get; } = [];
        internal List<SpriteFrameUpdateRequest> SetFrameRequests { get; } = [];
        internal List<SpritePlayback> CreatedPlaybacks { get; } = [];
        internal Queue<SpritePlaybackAdvanceLeaseReceipt> AdvanceReceipts { get; } = [];
        internal int CreatedAtlases { get; private set; }
        internal int DisposedAtlases { get; private set; }
        internal int CreatedAppearances { get; private set; }
        internal int DisposedAppearances { get; private set; }
        internal int DisposedPlaybacks { get; private set; }
        internal List<SpritePlaybackHandle> DisposedPlaybackHandles { get; } = [];
        internal int FailSpritePlaybackCreateAt { get; set; }
        internal int FailSpritePlaybackControlAt { get; set; }
        internal int FailPublishAt { get; set; }
        internal bool RejectLateResourceOpen { get; set; }
        internal bool RejectDisposeOfRetainedAppearance { get; set; }
        internal int PublishCalls { get; private set; }
        internal ulong LastCrossingSequence { get; private set; }
        internal IReadOnlyCollection<Appearance> RetainedAppearances => retainedAppearances;
        private readonly List<Action> pendingPlaybackCommits = [];
        private readonly List<Action> pendingPlaybackRollbacks = [];
        private readonly HashSet<Appearance> retainedAppearances = new(ReferenceEqualityComparer.Instance);
        private ulong nextHandle = 1;

        public RenderResourceInfo OpenResource(RenderResourceRequest request)
        {
            if (RejectLateResourceOpen) throw new InvalidOperationException("Render resource selection is sealed after product creation.");
            OpenResourceRequests.Add(request);
            return new(new RenderResourceHandle(checked((ulong)OpenResourceRequests.Count)), default, 0);
        }
        public Material CreateMaterial(MaterialRequest request) => new(new MaterialHandle(1), () => releases.Add("material"));
        public void UpdateMaterial(MaterialUpdateRequest request) { }
        public Material ReplaceMaterial(MaterialUpdateRequest request) => CreateMaterial(request.Replacement);
        public Appearance CreatePrimitive(PrimitiveAppearanceRequest request) => CreateAppearance();
        public Appearance ReplacePrimitive(PrimitiveAppearanceReplaceRequest request) => CreateAppearance();
        public Appearance CreateStaticMesh(StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMesh(Appearance appearance, StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMeshFromContent(Appearance appearance, StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public void UpdateStaticMeshMaterials(StaticMeshMaterialUpdateRequest request) => StaticMeshBindings.AddRange(request.Bindings.ToArray());
        public Appearance CreateSprite(SpriteAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest request) => CreateAppearance();
        public SpriteAtlas CreateSpriteAtlas(SpriteAtlasCreateRequest request)
        {
            AtlasRequests.Add(request);
            CreatedAtlases++;
            return new(new SpriteAtlasHandle(nextHandle++), () => { DisposedAtlases++; releases.Add("atlas"); });
        }
        public Appearance CreateSpriteFromAtlas(SpriteFromAtlasRequest request) { SpriteRequests.Add(request); return CreateAppearance(); }
        public Appearance ReplaceSpriteFromAtlas(SpriteFromAtlasReplaceRequest request) => CreateAppearance();
        public void SetSpriteFrame(SpriteFrameUpdateRequest request) => SetFrameRequests.Add(request);
        public SpriteReadout ReadSprite(Appearance appearance) => default;
        public SpritePlayback CreateSpritePlayback(SpritePlaybackCreateRequest request)
        {
            PlaybackRequests.Add(request);
            if (FailSpritePlaybackCreateAt == PlaybackRequests.Count) throw new InvalidOperationException("Injected sprite playback create failure.");
            SpritePlaybackHandle handle = new(nextHandle++);
            SpritePlayback playback = new(handle, () =>
            {
                DisposedPlaybacks++;
                DisposedPlaybackHandles.Add(handle);
                releases.Add("playback");
            }, static () => false, (commit, rollback) =>
            {
                pendingPlaybackCommits.Add(commit);
                pendingPlaybackRollbacks.Add(rollback);
            });
            CreatedPlaybacks.Add(playback);
            return playback;
        }
        public SpritePlaybackReadout ControlSpritePlayback(SpritePlaybackControlRequest request)
        {
            ControlRequests.Add(request);
            if (FailSpritePlaybackControlAt == ControlRequests.Count) throw new InvalidOperationException("Injected sprite playback control failure.");
            return default;
        }
        public SpritePlaybackAdvanceLeaseReceipt AdvanceSpritePlayback(SpritePlaybackAdvanceRequest request)
        {
            AdvanceRequests.Add(request);
            SpritePlaybackAdvanceLeaseReceipt receipt = AdvanceReceipts.Count == 0 ? default : AdvanceReceipts.Dequeue();
            foreach (SpritePlaybackMarkerCrossing crossing in receipt.Crossings.Span) LastCrossingSequence = Math.Max(LastCrossingSequence, crossing.CrossingSequence);
            return receipt;
        }
        public SpritePlaybackSample SampleSpritePlayback(SpritePlaybackSampleRequest request) => default;
        public SpritePlaybackReadout ReadSpritePlayback(SpritePlayback playback) => default;
        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values)
        {
            PublishCalls++;
            Snapshots.Add(values.ToArray());
            retainedAppearances.Clear();
            foreach (AppearanceFact value in values) retainedAppearances.Add(value.Appearance);
            if (FailPublishAt == PublishCalls) throw new InvalidOperationException("Injected presentation publish failure.");
        }
        public Light CreateLight(LightRequest request) => new(new LightHandle(1), () => { });
        public void UpdateLight(LightUpdateRequest request) { }
        public Light ReplaceLight(LightUpdateRequest request) => NewLight(request.Replacement);
        public LightReadout ReadLight(Light light) => default;
        public PresentationReadout ReadPresentation() => default;

        private Appearance CreateAppearance()
        {
            CreatedAppearances++;
            Appearance value = null!;
            value = new(new AppearanceHandle(nextHandle++), () =>
            {
                if (RejectDisposeOfRetainedAppearance && retainedAppearances.Contains(value)) throw new InvalidOperationException("CSHARP_APPEARANCE_IN_USE");
                DisposedAppearances++;
                releases.Add("appearance");
            });
            return value;
        }
        internal void CommitPendingPlaybackReleases()
        {
            foreach (Action commit in pendingPlaybackCommits) commit();
            pendingPlaybackCommits.Clear();
            pendingPlaybackRollbacks.Clear();
        }
        internal void RollbackPendingPlaybackReleases()
        {
            foreach (Action rollback in pendingPlaybackRollbacks) rollback();
            pendingPlaybackCommits.Clear();
            pendingPlaybackRollbacks.Clear();
        }
        private static Light NewLight(LightRequest request) => new(new LightHandle(1), () => { });
    }
}
