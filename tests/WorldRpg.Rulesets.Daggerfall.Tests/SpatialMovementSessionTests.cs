using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The spatial seam: content admission, controller configuration, character steps, falls, levitation and update catch-up.</summary>
public sealed class SpatialMovementSessionTests
{
    [Fact]
    public void Grounded_spawns_use_engine_floor_hits_while_flying_markers_keep_their_height()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.FloorHit = request => new SpatialHit { Present = true, Point = request.Origin - Vector3.UnitY, Normal = Vector3.UnitY };
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        foreach (AuthoredActor source in inputs.Project.Actors.Values)
        {
            WorldPoint actual = session.State.Actors.Get(source.EntityId).Position;
            bool grounded = definitions.Actors[source.ActorId].GroundOnSpawn;
            Assert.Equal(source.Position.Y + (grounded ? DaggerfallTuning.Defaults.EnemyBehavior.SpawnGroundProbeLift - 1f : 0f), actual.Y, precision: 4);
            Assert.Equal(source.Position.X, actual.X);
            Assert.Equal(source.Position.Z, actual.Z);
        }
        Assert.Equal(inputs.Project.Actors.Values.Count(actor => definitions.Actors[actor.ActorId].GroundOnSpawn), spatial.FloorProbes.Count);
        Assert.All(spatial.FloorProbes, request => Assert.Equal(-Vector3.UnitY, request.Direction));
    }

    [Fact]
    public void Spatial_system_admits_one_content_artifact_and_releases_session_before_reference()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning))
        {
            Assert.Equal(1, spatial.ReplaceCalls);
            Assert.Equal(0, spatial.ReadCalls);
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
        Assert.Equal(0, spatial.CommandValidationCalls);
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
    public void Player_continuation_is_retained_when_a_later_direct_character_step_updates_spatial()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2);
        PlayerControlState player = new(new WorldPoint(0, 0, 0), 0, 0);

        using SpatialMovementSystem system = new(spatial.Service, content,
            new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning);
        system.Step(player, new ProductUpdateState(.125f));
        CharacterContinuationCheckpoint checkpoint = system.CaptureContinuation();

        // Population's direct Engine character proposal changes Spatial's latest receipt. The
        // player's save checkpoint must remain the one captured at the player's own boundary.
        spatial.Service.ProposeCharacterStep(spatial.StepRequests.Single());

        Assert.Equal(checkpoint, system.CaptureContinuation());
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
    public void Spatial_step_submits_current_control_state_and_applies_the_engine_receipt()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);
        ProductUpdateState update = new(1f / 60f);
        update.PlanarIntent = new Vector2(.25f, .5f);

        using SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2));
        system.Step(player, update);

        CharacterStepRequest request = Assert.Single(spatial.StepRequests);
        Assert.Equal(update.PlanarIntent, request.Command.PlanarIntent);
        Assert.Equal(new WorldPoint(2f, 2f, 3f), player.Position);
        Assert.Equal(1UL, player.Motion.LastCommandSequence);
    }

    [Fact]
    public void Spatial_step_forwards_one_product_selected_swim_request_with_the_call_local_water_volume()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);
        CharacterWaterVolume volume = new(77, new Vector3(-2f, -1f, -2f), new Vector3(2f, 4f, 2f));
        CharacterMovementRequest movement = new(
            CharacterMovementMode.Swimming,
            VerticalIntent: 1f,
            Speed: 2f,
            Acceleration: 12f,
            Drag: 4f,
            Minimum: volume.Minimum,
            Maximum: volume.Maximum,
            GravityScale: 0f,
            Buoyancy: 1f,
            ClimbReach: 0f);

        using SpatialMovementSystem system = new(spatial.Service, content,
            new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2));
        system.Step(player, new ProductUpdateState(1f / 60f),
            new CharacterStepEnvironment(default, Array.Empty<CharacterObstacle>(), Array.Empty<CharacterMeshInstance>(), new[] { volume }),
            new CharacterStepControls(PlanarIntent: Vector2.UnitY, Movement: movement));

        CharacterStepRequest request = Assert.Single(spatial.StepRequests);
        Assert.Equal(movement, request.Command.Movement);
    }

    [Fact]
    public void Spatial_trigger_registration_is_idempotent_and_deactivation_is_engine_admitted()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);

        using SpatialMovementSystem system = new(spatial.Service, content,
            new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2));

        Assert.True(system.RegisterTrigger(91, "profile", "water"));
        Assert.False(system.RegisterTrigger(91, "profile", "water"));
        system.ReleaseTrigger(91, tick: 4);
        Assert.Empty(spatial.TriggerLifecycleRequests);
        system.ReleaseTrigger(91, tick: 5);
        Assert.Single(spatial.TriggerLifecycleRequests);
        Assert.False(spatial.TriggerLifecycleRequests[0].Active);
        Assert.False(system.RegisterTrigger(91, "profile", "water"));
        system.ActivateTrigger(91, tick: 6);

        Assert.Single(spatial.TriggerRegistrations);
        Assert.Equal(91UL, spatial.TriggerRegistrations[0].Trigger);
        Assert.Equal(2, spatial.TriggerLifecycleRequests.Count);
        Assert.Equal(91UL, spatial.TriggerLifecycleRequests[0].Trigger);
        Assert.False(spatial.TriggerLifecycleRequests[0].Active);
        Assert.Equal(5UL, spatial.TriggerLifecycleRequests[0].Tick);
        Assert.True(spatial.TriggerLifecycleRequests[1].Active);
        Assert.Equal(6UL, spatial.TriggerLifecycleRequests[1].Tick);
    }

    [Fact]
    public void Over_capacity_player_still_receives_an_engine_step_but_cannot_propose_planar_movement()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        InventoryStackId coins = InventoryStackId.Parse("test.encumbrance.movement");
        session.State.Inventory.Grant(new(new InventoryItemId("gold-piece"), coins, checked((ulong)(session.State.Encumbrance.Read().MaximumClassicUnits + 1))));
        session.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Player, session.State.Inventory.Read().Stacks.Single(stack => stack.Id == coins), definitions.RequireItem(new DaggerfallItemId("gold-piece")));
        Assert.False(session.State.Encumbrance.Read().CanMove);

        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));

        Assert.Single(spatial.StepRequests);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[0].Command.PlanarIntent);
    }

    [Fact]
    public void Engine_reported_landing_applies_one_lethal_fall_and_disables_later_player_movement()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        Track health = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        health.SetCurrent(3.75d, clamp: true);
        session.State.PlayerControl.Restore(new WorldPoint(0f, 2f, 0f),
            default(CharacterMotion) with { Grounded = false, PeakY = 9f });

        session.Update(new ProductUpdate(OuterUpdate(1), []));
        session.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));

        Assert.Equal(0d, health.Current);
        Assert.Equal(2, spatial.StepCalls);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[1].Command.PlanarIntent);
    }

    [Fact]
    public void Admitted_wall_checks_reach_skill_progression_and_save_with_live_movement_caller()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.FloorHit = request => request.Direction.Y == 0f
            ? default(SpatialHit) with { Present = true, Normal = new Vector3(0f, 0f, 1f) }
            : default;
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: RandomMinimum.Create());
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        for (ulong step = 2; step <= 49; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));

        Assert.Equal(1, session.State.Progression.SkillUses["climbing"]);
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.True(saved.Climbing.Attached);
        Assert.Equal(1, saved.SkillUses.Counters.Single(counter => counter.Skill == "climbing").Uses);
        Assert.Contains(spatial.StepRequests, request => request.Config.Vertical.Gravity == 0f && request.Motion.ControlledVelocity.Y > 0f);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));
        Assert.True(DaggerfallSavePayload.Read(restored.CaptureSave()).Climbing.Attached);
        Assert.Equal(1, restored.State.Progression.SkillUses["climbing"]);
    }

    [Fact]
    public void Live_effect_grant_drives_levitation_then_expiry_restores_gravity()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallEffectCatalog effects = new([
            new DaggerfallEffectDefinition("levitation-test", "levitation-test", DaggerfallEffectStacking.Stack, 1, 1,
                MovementProtection: new DaggerfallMovementProtection(false, GrantsLevitation: true)),
        ]);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults) { Effects = effects });
        using JsonDocument state = JsonDocument.Parse("{}");
        _ = session.State.Effects.Start(new DaggerfallEffectRequest("levitation-instance", "levitation-test", "test-source",
            null, DaggerfallActorIdentity.PlayerEntityId, "classic", null, null, 1, 5, state.RootElement));
        double staminaBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current;

        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.Space)]));
        Assert.True(session.State.Effects.GrantsLevitation(DaggerfallActorIdentity.PlayerEntityId));
        Assert.Equal(0f, spatial.StepRequests[^1].Config.Vertical.Gravity);
        Assert.Equal(4f, spatial.StepRequests[^1].Motion.ControlledVelocity.Y);
        Assert.Equal(staminaBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);

        session.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.Space)]));
        Assert.Equal(0f, spatial.StepRequests[^1].Motion.ControlledVelocity.Y);
        Assert.Equal(0f, spatial.StepRequests[^1].Config.Vertical.Gravity);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity) { Effects = effects }, session.CaptureSave());
        Assert.True(restored.State.Effects.GrantsLevitation(DaggerfallActorIdentity.PlayerEntityId));
        restored.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.Equal(0f, resumedSpatial.StepRequests[^1].Config.Vertical.Gravity);

        Assert.True(session.State.Effects.Cancel(EffectInstanceId.Parse("levitation-instance")));
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.False(session.State.Effects.GrantsLevitation(DaggerfallActorIdentity.PlayerEntityId));
        Assert.True(spatial.StepRequests[^1].Config.Vertical.Gravity > 0f);
        Assert.Equal(0f, spatial.StepRequests[^1].Motion.ControlledVelocity.Y);
    }

    [Fact]
    public void Realtime_substeps_reuse_postlook_held_movement_with_one_sequence_each()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        // The authored start pose is π. With yaw wrapping enabled, the
        // admitted positive pointer delta crosses into [-π, π), rather than
        // leaving the product with an unbounded yaw accumulator.
        float expectedYaw = -MathF.PI + (.25f * DaggerfallTuning.Defaults.PlayerControl.LookSensitivity);
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

        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
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
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
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
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 3, 1, 1, 1, 60, 3, 0, 1d / 60d), [Input(InputEventKind.DirectDigital, x: 1f, intent: "move")]);
        }

        Assert.Equal(new Vector2(0f, 1f), spatial.StepRequests[0].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[1].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[2].Command.PlanarIntent);
    }

    [Fact]
    public void Native_step_failure_keeps_applied_input_without_claiming_rollback()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        WorldPoint? positionBefore = session.State.PlayerControl.Position;
        CharacterMotion motionBefore = session.State.PlayerControl.Motion;
        CharacterGround groundBefore = session.State.PlayerControl.Ground;
        float yawBefore = session.State.PlayerControl.YawRadians;
        float pitchBefore = session.State.PlayerControl.PitchRadians;
        spatial.RejectProposedStep = true;

        Assert.Throws<InvalidOperationException>(() => session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d),
        [
            Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW),
            Input(InputEventKind.PointerDelta, x: .25f, y: -.5f),
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack"),
        ]));

        Assert.Equal(0, spatial.CommandValidationCalls);
        Assert.Equal(0, spatial.StepCalls);
        Assert.Equal(positionBefore, session.State.PlayerControl.Position);
        Assert.Equal(motionBefore, session.State.PlayerControl.Motion);
        Assert.Equal(groundBefore, session.State.PlayerControl.Ground);
        Assert.NotEqual(yawBefore, session.State.PlayerControl.YawRadians);
        Assert.NotEqual(pitchBefore, session.State.PlayerControl.PitchRadians);
        // The escape faults the Engine lifecycle; nothing here retries the refused step.
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
    public void Admitted_authored_entity_ids_cover_construction_and_allocator_inputs_and_reject_duplicates()
    {
        // One helper now serves actor construction validation and allocator reservations. The
        // deleted payload copy silently dropped duplicate loadout ids from the reservation set
        // while construction threw; this pins the unified strict behavior.
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));

        HashSet<ulong> admitted = DaggerActorFactory.AdmittedAuthoredEntityIds(inputs, player.Loadout);
        Assert.Contains((ulong)DaggerfallActorIdentity.PlayerEntityId, admitted);
        Assert.Equal(
            1 + inputs.Project.Actors.Values.Count() + player.Loadout.Count(entry => entry.UniqueEntityId is not null),
            admitted.Count);

        DaggerfallLoadoutEntry duplicated = player.Loadout.First(entry => entry.UniqueEntityId is not null);
        InvalidOperationException rejected = Assert.Throws<InvalidOperationException>(() =>
            DaggerActorFactory.AdmittedAuthoredEntityIds(inputs, [.. player.Loadout, duplicated]));
        Assert.Contains("collides", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_step_outer_update_steps_each_catch_up_step_but_publishes_once()
    {
        // Structural evidence for publish-once: three admitted steps advance the world three
        // times, but graphics/UI publication happens exactly once, after animation impacts.
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, PerceptionFake.Create().Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        static ProductUpdateFacts ThreeSteps(ulong step) =>
            new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, step, step, 60, 3, 0, 1d / 60d);

        int stepsBefore = spatial.StepCalls;
        int publishesBefore = appearance.PublishCalls;
        session.Update(new ProductUpdate(ThreeSteps(1),
            [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Assert.Equal(stepsBefore + 3, spatial.StepCalls);
        Assert.Equal(publishesBefore + 1, appearance.PublishCalls);
        Assert.NotEqual(Vector2.Zero, spatial.StepRequests[^1].Command.PlanarIntent);

        // A held world still publishes its single presentation per outer update, with no steps.
        // (The mode transition above publishes on its own; only the update's publication counts.)
        session.ApplyProductMode(ProductMode.Modal);
        int modalPublishesBefore = appearance.PublishCalls;
        session.Update(new ProductUpdate(ThreeSteps(4), []));
        Assert.Equal(stepsBefore + 3, spatial.StepCalls);
        Assert.Equal(modalPublishesBefore + 1, appearance.PublishCalls);

        // An update with no admitted steps publishes nothing and steps nothing.
        ProductUpdateFacts noSteps = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 7, 7, 60, 0, 0, 1d / 60d);
        session.Update(new ProductUpdate(noSteps, []));
        Assert.Equal(stepsBefore + 3, spatial.StepCalls);
        Assert.Equal(modalPublishesBefore + 1, appearance.PublishCalls);
    }
}
