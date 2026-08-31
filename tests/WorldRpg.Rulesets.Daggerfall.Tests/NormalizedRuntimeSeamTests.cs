using System.Numerics;
using System.Reflection;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
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
    public void Host_admits_one_realtime_update_and_releases_the_normalized_session_owners()
    {
        string root = RepositoryRoot();
        List<string> releases = [];
        ContentFake content = new(releases);
        PrivateersHoldInputs inputs = ReadInputs(root);
        content.Add(inputs.SpatialArtifact.Path, inputs.SpatialArtifact.Sha256);
        content.Add(inputs.StaticMesh.Path, inputs.StaticMesh.Sha256);
        foreach (NormalizedMaterial material in inputs.Materials) content.Add(material.TexturePath, material.TextureSha256);
        foreach (NormalizedActorSprite sprite in inputs.ActorSprites.Values) content.Add(sprite.TexturePath, sprite.TextureSha256);
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
        Assert.Equal(perception.Requests.Single(), evidence.Request);
        Assert.Equal((ulong)1, evidence.Request.Observers.Span[0].Entity);
        Assert.Equal(2.25d, evidence.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal(.5d, evidence.Request.Observers.Span[0].MinimumFacingCosine);
        Assert.Equal(1, evidence.Receipt.Pairs.Length);
        Assert.True(session.State.Actors.All[2000].Mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw < healthBefore);
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

    private static PerceptionReadoutLeaseReceipt Receipt(params PerceptionPair[] pairs) => new(pairs, ReadOnlyMemory<PerceptionAggregate>.Empty, 1, checked((uint)pairs.Length), checked((ulong)pairs.Length), 0, 0, 0, 0);

    private static PrivateersHoldInputs ReadInputs(string root)
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        return PrivateersHoldContent.Read(ImportContent(root), File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
    }

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
        foreach (NormalizedActorSprite sprite in inputs.ActorSprites.Values) content.Add(sprite.TexturePath, sprite.TextureSha256);
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
                Transform = new Transform(request.Position + new Vector3(1f, 0f, 0f), Quaternion.Identity, Vector3.One),
                Motion = request.Motion with { Grounded = true, LastCommandSequence = request.Command.Sequence },
                Ground = default(CharacterGround) with { Present = true },
            };
        }
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

    private sealed class AppearanceFake(List<string> releases) : IAppearanceService
    {
        internal List<MeshMaterialBinding> StaticMeshBindings { get; } = [];
        internal List<SpriteAtlasCreateRequest> AtlasRequests { get; } = [];
        internal List<SpriteFromAtlasRequest> SpriteRequests { get; } = [];

        public RenderResourceInfo OpenResource(RenderResourceRequest request) => new(new RenderResourceHandle(1), default, 0);
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
        public SpriteAtlas CreateSpriteAtlas(SpriteAtlasCreateRequest request) { AtlasRequests.Add(request); return new(new SpriteAtlasHandle(1), () => releases.Add("atlas")); }
        public Appearance CreateSpriteFromAtlas(SpriteFromAtlasRequest request) { SpriteRequests.Add(request); return CreateAppearance(); }
        public Appearance ReplaceSpriteFromAtlas(SpriteFromAtlasReplaceRequest request) => CreateAppearance();
        public void SetSpriteFrame(SpriteFrameUpdateRequest request) { }
        public SpriteReadout ReadSprite(Appearance appearance) => default;
        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values) { }
        public Light CreateLight(LightRequest request) => new(new LightHandle(1), () => { });
        public void UpdateLight(LightUpdateRequest request) { }
        public Light ReplaceLight(LightUpdateRequest request) => NewLight(request.Replacement);
        public LightReadout ReadLight(Light light) => default;
        public PresentationReadout ReadPresentation() => default;

        private Appearance CreateAppearance() => new(new AppearanceHandle(1), () => releases.Add("appearance"));
        private static Light NewLight(LightRequest request) => new(new LightHandle(1), () => { });
    }
}
