using System.Numerics;
using System.Reflection;
using System.Text;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSessionTests
{
    [Fact]
    public void Normalized_fixture_produces_typed_definitions_and_explicit_placements()
    {
        DaggerfallDefinitions definitions = Definitions();
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(SpatialContent(), Encoding.UTF8.GetBytes(ScenarioPayload), definitions);

        Assert.Equal(85, definitions.RequireActor(new DaggerfallActorId("player")).PlayerInitialVitals.HealthMaximum);
        Assert.Equal((9, 16), (definitions.RequireActor(new DaggerfallActorId("rat")).Health.Minimum, definitions.RequireActor(new DaggerfallActorId("rat")).Health.Maximum));
        Assert.Equal((ulong)1, definitions.Items[new DaggerfallItemId("iron-longsword")].MaximumQuantity);
        Assert.Empty(inputs.Project.Actors);
        Assert.Equal(4, inputs.Loadout.Count);
        Assert.Equal((ulong)1001, inputs.Loadout[0].UniqueEntityId);
        Assert.Equal("right-hand", inputs.Loadout[0].EquipSlot?.Value);
    }

    [Fact]
    public void Content_reader_reports_missing_artifacts_and_typed_broken_references()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallContentException absent = Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(new ProductContent(System.Array.Empty<ProductContentFile>()), Encoding.UTF8.GetBytes(ScenarioPayload), definitions));
        Assert.Contains(absent.Diagnostics, diagnostic => diagnostic.Contains("asset 'projects/privateers-hold.navgrid.json'", StringComparison.Ordinal));

        string broken = ScenarioPayload.Replace("\"iron-longsword\"", "\"not-an-item\"", StringComparison.Ordinal);
        DaggerfallContentException reference = Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(SpatialContent(), Encoding.UTF8.GetBytes(broken), definitions));
        Assert.Contains(reference.Diagnostics, diagnostic => diagnostic.Contains("missing item 'not-an-item'", StringComparison.Ordinal));
    }

    [Fact]
    public void Equipment_metadata_and_starting_slot_references_are_validated_before_session_construction()
    {
        DaggerfallContentException unsupportedClassification = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(BasePayload.Replace("\"weapon\"],\"requiredSlots\":1", "\"not-a-slot-classification\"],\"requiredSlots\":1", StringComparison.Ordinal))));
        Assert.Contains(unsupportedClassification.Diagnostics, diagnostic => diagnostic.Contains("no compatible equipment slot", StringComparison.Ordinal));

        DaggerfallContentException missingSlot = Assert.Throws<DaggerfallContentException>(() =>
            PrivateersHoldContent.Read(SpatialContent(), Encoding.UTF8.GetBytes(ScenarioPayload.Replace("\"right-hand\"", "\"missing-hand\"", StringComparison.Ordinal)), Definitions()));
        Assert.Contains(missingSlot.Diagnostics, diagnostic => diagnostic.Contains("missing equipment slot 'missing-hand'", StringComparison.Ordinal));
    }

    [Fact]
    public void Engine_bound_inventory_metadata_and_loadout_identity_collisions_are_rejected_before_binding()
    {
        DaggerfallContentException uniqueQuantity = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(BasePayload.Replace("\"maximumQuantity\":1,\"equipment\"", "\"maximumQuantity\":2,\"equipment\"", StringComparison.Ordinal))));
        Assert.Contains(uniqueQuantity.Diagnostics, diagnostic => diagnostic.Contains("maximumQuantity must be exactly 1", StringComparison.Ordinal));

        DaggerfallContentException duplicateGold = Assert.Throws<DaggerfallContentException>(() =>
            PrivateersHoldContent.Read(SpatialContent(), Encoding.UTF8.GetBytes(ScenarioPayload.Replace("{\"item\":\"gold-piece\",\"quantity\":25}", "{\"item\":\"gold-piece\",\"quantity\":25},{\"item\":\"gold-piece\",\"quantity\":1}", StringComparison.Ordinal)), Definitions()));
        Assert.Contains(duplicateGold.Diagnostics, diagnostic => diagnostic.Contains("repeats fungible item 'gold-piece'", StringComparison.Ordinal));

        PrivateersHoldInputs collision = new(new ProjectFacts(new WorldPoint(0, 0, 0), new Dictionary<long, AuthoredActor> { [1001] = new(1001, new DaggerfallActorId("rat"), new WorldPoint(0, 0, 0), null) }), [], new CollisionMesh([], []), null, new PlayerInitialLook(0, 0), [new ScenarioLoadoutEntry(new DaggerfallItemId("iron-longsword"), 1, 1001, new DaggerfallEquipmentSlotId("right-hand"))], []);
        Assert.Throws<InvalidOperationException>(() => new DaggerfallSession(new RecordingEngine().Context, Definitions(), collision, DaggerfallTuning.Defaults));
    }

    [Fact]
    public void Player_starts_with_managed_equipment_state_even_without_assignments()
    {
        RecordingEngine engine = new();
        PrivateersHoldInputs emptyLoadout = new(new ProjectFacts(new WorldPoint(0, 0, 0), new Dictionary<long, AuthoredActor>()), [], new CollisionMesh([], []), null);
        using DaggerfallSession session = new(engine.Context, Definitions(), emptyLoadout, DaggerfallTuning.Defaults);

        Assert.Empty(session.State.Equipment.Read().Assignments);
    }

    [Fact]
    public void Base_reader_bounds_engine_catalog_sizes_and_fungible_loot_quantities()
    {
        string items = string.Join(',', Enumerable.Range(0, 257).Select(index => $"{{\"id\":\"item-{index}\",\"kind\":\"fungible\",\"maximumQuantity\":1}}"));
        string slots = string.Join(',', Enumerable.Range(0, 65).Select(index => $"{{\"id\":\"slot-{index}\",\"allowedClassifications\":[\"class-{index}\"]}}"));
        string oversized = "{\"schemaVersion\":1,\"ruleset\":\"daggerfall\",\"actors\":[],\"items\":[" + items + "],\"equipmentSlots\":[" + slots + "],\"hudResources\":[]}";
        DaggerfallContentException limits = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(oversized)));
        Assert.Contains(limits.Diagnostics, diagnostic => diagnostic.Contains("256 items", StringComparison.Ordinal));
        Assert.Contains(limits.Diagnostics, diagnostic => diagnostic.Contains("64 equipment slots", StringComparison.Ordinal));

        DaggerfallContentException loot = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(BasePayload.Replace("\"gold-piece\",\"kind\":\"fungible\",\"maximumQuantity\":10000", "\"gold-piece\",\"kind\":\"fungible\",\"maximumQuantity\":5", StringComparison.Ordinal))));
        Assert.Contains(loot.Diagnostics, diagnostic => diagnostic.Contains("exceeds fungible item 'gold-piece'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}", "{\"payload\":{\"source\":{\"positions\":[],\"indices\":[]}}}")]
    [InlineData("{\"cells\":[[0,0]]}", "{\"payload\":{\"source\":{\"positions\":[],\"indices\":[]}}}")]
    [InlineData("{\"cells\":[]}", "{\"payload\":{\"source\":{\"positions\":[0,0],\"indices\":[]}}}")]
    [InlineData("{\"cells\":[]}", "{\"payload\":{\"source\":{\"positions\":[0,0,0],\"indices\":[0,1,0]}}}")]
    public void Malformed_spatial_artifacts_produce_bounded_content_diagnostics(string navigation, string collision)
    {
        DaggerfallContentException exception = Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(SpatialContent(navigation, collision), Encoding.UTF8.GetBytes(ScenarioPayload), Definitions()));
        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Contains("spatial artifact", StringComparison.Ordinal));
        Assert.True(exception.Diagnostics.Count <= 17);
    }

    [Fact]
    public void Pack_assets_must_exist_exactly_once_and_spatial_facts_expose_read_only_memory()
    {
        string sprite = "{\"id\":\"missing\",\"texture\":\"textures/missing.png\",\"uvMin\":[0,0],\"uvMax\":[1,1],\"pivot\":[0,0],\"size\":[1,1],\"billboard\":\"none\",\"tint\":[1,1,1,1],\"sizeMode\":\"world\",\"renderOrder\":0,\"depth\":\"default\",\"visible\":true,\"layer\":\"scene\"}";
        string missingSprite = ScenarioPayload.Replace("\"appearances\":[]", "\"appearances\":[" + sprite + "]", StringComparison.Ordinal);
        DaggerfallContentException missing = Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(SpatialContent(), Encoding.UTF8.GetBytes(missingSprite), Definitions()));
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Contains("textures/missing.png", StringComparison.Ordinal));

        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(SpatialContent(), Encoding.UTF8.GetBytes(ScenarioPayload), Definitions());
        Assert.IsType<ReadOnlyMemory<PlanarNavCell>>(inputs.Navigation);
        Assert.IsType<ReadOnlyMemory<Vector3>>(inputs.Collision.Vertices);
    }

    [Fact]
    public void Scenario_look_is_not_a_tuning_owner_and_spatial_inputs_copy_caller_arrays()
    {
        Assert.Null(typeof(DaggerfallTuning).GetProperty("InitialPlayerLook", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        DaggerfallTuning tuning = DaggerfallTuning.Read(Encoding.UTF8.GetBytes("""{"playerControl":{"lookSensitivity":0.0035,"pitchMinimumRadians":-1.5,"pitchMaximumRadians":1.5,"maximumLookDeltaRadians":0.35,"invertHorizontal":false,"invertVertical":false,"wrapYaw":true},"combat":{"playerMeleeStaminaCost":5,"playerMeleeCooldownSeconds":0.75},"spatial":{"collisionVoxelSize":0.5,"collisionChunkSize":32,"navigationCellSize":0.5,"navigationChunkSize":32,"navigationMaximumStepCells":2},"camera":{"eyeHeight":0.75,"fieldOfViewYDegrees":65,"nearPlane":0.1,"farPlane":100}}"""));
        Assert.Equal(.5, tuning.Spatial.NavigationCellSize);

        PlanarNavCell[] navigation = [new(1, 2, 3)];
        Vector3[] vertices = [new(1, 2, 3)];
        CollisionMesh collision = new(vertices, []);
        PrivateersHoldInputs inputs = new(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()), navigation, collision, null);
        navigation[0] = new(9, 9, 9);
        vertices[0] = Vector3.Zero;
        Assert.Equal(1, inputs.Navigation.Span[0].X);
        Assert.Equal(1f, inputs.Collision.Vertices.Span[0].X);
    }

    [Fact]
    public void Base_reader_rejects_incompatible_and_duplicate_definitions_with_bounded_diagnostics()
    {
        DaggerfallContentException incompatible = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(BasePayload.Replace("\"daggerfall\"", "\"other-ruleset\"", StringComparison.Ordinal))));
        Assert.Contains(incompatible.Diagnostics, diagnostic => diagnostic.Contains("ruleset", StringComparison.Ordinal));

        string duplicateActors = "[" + string.Join(',', Enumerable.Repeat("{\"id\":\"rat\",\"stats\":{},\"health\":{},\"armor\":0,\"rewards\":{}}", 20)) + "]";
        string malformed = "{\"schemaVersion\":1,\"ruleset\":\"daggerfall\",\"actors\":" + duplicateActors + ",\"items\":[],\"hudResources\":[]}";
        DaggerfallContentException bounded = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(malformed)));
        Assert.Equal(17, bounded.Diagnostics.Count);
        Assert.Equal("Daggerfall content diagnostics were truncated.", bounded.Diagnostics[^1]);
    }

    [Fact]
    public void Daggerfall_rows_drive_the_hud_and_unresolved_attack_has_no_target_side_effect()
    {
        RecordingEngine engine = new();
        using DaggerfallSession composition = new(engine.Context, Definitions(), SkeletalInputs(), DaggerfallTuning.Defaults);

        composition.PublishInitial();
        Assert.Equal(MathF.PI, composition.State.PlayerControl.YawRadians);
        Assert.Equal(0f, composition.State.PlayerControl.PitchRadians);
        Assert.Equal(50, composition.State.Actors.Player.Mechanics.ReadStat(Rusty.Engine.Mechanics.StatId.Parse("strength")).Base.Raw);
        Assert.Equal(85, composition.State.Actors.Player.Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw);
        Assert.Equal(90, composition.State.Actors.Player.Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("stamina")).Current.Raw);
        Assert.Equal(["gold-piece"], composition.State.Inventory.Read().Stacks.Select(stack => stack.Definition.Value));
        Assert.Equal(2, composition.State.Equipment.Read().Assignments.Count);
        EquipmentRead equipped = composition.State.Equipment.Read();
        Assert.True(equipped.TryGet(new EquipmentSlotId("right-hand"), out UniqueInventoryItem weapon));
        Assert.Equal("iron-longsword", weapon.Definition.Value);
        Assert.Null(typeof(DaggerfallSession).GetField("_rightHand", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Equal(["health", "stamina", "magicka"], ResourceIds(engine.Ui.Projections[^1].Value));
        Assert.Equal((85d, 85d), Resource(engine.Ui.Projections[^1].Value, "health"));
        Assert.Equal((90d, 90d), Resource(engine.Ui.Projections[^1].Value, "stamina"));

        ProductUpdateState attack = new(1f);
        attack.Request(DaggerfallInput.Attack);
        composition.Update(attack);

        Assert.Equal((90d, 90d), Resource(engine.Ui.Projections[^1].Value, "stamina"));
        Assert.Equal("No target in melee reach", composition.Presentation.LastOutcome);
    }

    [Fact]
    public void Unresolved_actors_do_not_attack_the_player()
    {
        RecordingEngine engine = new();
        using DaggerfallSession composition = new(engine.Context, Definitions(), SkeletalInputs(), DaggerfallTuning.Defaults);

        for (int update = 0; update < 6; update++) composition.Update(new ProductUpdateState(2f));

        Assert.Equal((85d, 85d), Resource(engine.Ui.Projections[^1].Value, "health"));
    }

    [Fact]
    public void Explicit_player_melee_reads_equipped_longsword_spends_stamina_and_applies_damage_once()
    {
        RecordingEngine engine = new();
        using DaggerfallSession session = new(engine.Context, Definitions(), SkeletalInputs(), DaggerfallTuning.Defaults);

        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 7, 10, .125));

        Assert.Equal(85, session.State.Actors.Player.Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("stamina")).Current.Raw);
        Assert.Equal(1, session.State.Actors.All[2000].Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw);
        Assert.Equal("Hit skeletal-warrior for 16 damage", session.Presentation.LastOutcome);

        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 7, 11, .125));
        Assert.Equal(85, session.State.Actors.Player.Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("stamina")).Current.Raw);
        Assert.Equal(1, session.State.Actors.All[2000].Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw);
        Assert.Equal("Cooldown", session.Presentation.LastOutcome);
    }

    [Fact]
    public void Explicit_enemy_melee_can_target_player_without_restoring_ai_and_duplicate_death_awards_once()
    {
        RecordingEngine engine = new();
        using DaggerfallSession session = new(engine.Context, Definitions(), SkeletalInputs(), DaggerfallTuning.Defaults);

        session.ResolveExplicitMelee(new ExplicitMeleeRequest(2000, 1, 3, 1, .125));
        Assert.Equal(70, session.State.Actors.Player.Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw);

        WorldRpg.Kit.Actors.ActorMechanicsState skeleton = session.State.Actors.All[2000].Mechanics;
        skeleton.SetTrack(
            Rusty.Engine.Mechanics.TrackId.Parse("health"),
            new Rusty.Engine.Mechanics.ExactValue(1),
            Rusty.Engine.Mechanics.ExactTrackSetPolicy.ClampToBounds);
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 3, 20, .125));
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 3, 30, .125));

        Assert.Equal(450, session.State.Progression.Experience);
        Assert.Equal(35UL, Assert.Single(session.State.Inventory.Read().Stacks, stack => stack.Definition.Value == "gold-piece").Quantity);
    }

    [Theory]
    [InlineData(60, 30, 80, 50, 90, 50, 0, 47)]
    [InlineData(60, 30, 50, 80, 50, 90, 0, 33)]
    [InlineData(10, 0, 0, 100, 0, 100, 1000, 3)]
    [InlineData(100, 100, 100, 0, 100, 0, 0, 97)]
    public void Hit_chance_uses_signed_truncating_attacker_target_differentials_and_clamps(int skill, int armor, int attackerLuck, int targetLuck, int attackerAgility, int targetAgility, int targetDodge, int expected)
    {
        Assert.Equal(expected, CombatModule.CalculateHitChance(skill, armor, attackerLuck, targetLuck, attackerAgility, targetAgility, targetDodge));
    }

    [Fact]
    public void Explicit_melee_miss_spends_stamina_but_insufficient_stamina_rejects_before_mutation()
    {
        RecordingEngine miss = new();
        miss.Random.Draw = request => checked((ulong)request.Maximum);
        using (DaggerfallSession missSession = new(miss.Context, Definitions(), SkeletalInputs(), DaggerfallTuning.Defaults))
        {
            missSession.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
            Assert.Equal(85, missSession.State.Actors.Player.Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("stamina")).Current.Raw);
            Assert.Equal(66, missSession.State.Actors.All[2000].Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw);
        }

        RecordingEngine exhausted = new();
        using DaggerfallSession session = new(exhausted.Context, Definitions(), SkeletalInputs(), DaggerfallTuning.Defaults);
        WorldRpg.Kit.Actors.ActorMechanicsState player = session.State.Actors.Player.Mechanics;
        player.SetTrack(
            Rusty.Engine.Mechanics.TrackId.Parse("stamina"),
            new Rusty.Engine.Mechanics.ExactValue(4),
            Rusty.Engine.Mechanics.ExactTrackSetPolicy.ClampToBounds);
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        Assert.Equal(4, player.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("stamina")).Current.Raw);
        Assert.Equal("Too exhausted to attack", session.Presentation.LastOutcome);
    }

    [Fact]
    public void Rat_explicit_attack_uses_exact_ids_and_initial_monster_health_is_keyed_within_authored_range()
    {
        RecordingEngine engine = new();
        using DaggerfallSession session = new(engine.Context, Definitions(), ActorInputs(3000, "rat"), DaggerfallTuning.Defaults);

        WorldRpg.Kit.Actors.ActorMechanicsState rat = session.State.Actors.All[3000].Mechanics;
        Assert.Equal(9, rat.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw);
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(3000, 1, 8, 1, .125));
        Assert.Contains(engine.Random.Requests, request => request.Key.Contains("spawn:actor:3000:rat:health", StringComparison.Ordinal));
        Assert.Contains(engine.Random.Requests, request => request.Scope == "dagger.combat.ai.v1" && request.Key.Contains("attacker:3000:target:1:salt:2", StringComparison.Ordinal));
    }

    [Fact]
    public void Actor_lifecycle_reads_the_current_engine_damage_track_after_set_and_restore()
    {
        RecordingEngine engine = new();
        using DaggerfallSession composition = new(engine.Context, Definitions(), SkeletalInputs(), DaggerfallTuning.Defaults);
        WorldRpg.Kit.Actors.ActorMechanicsState player = composition.State.Actors.Player.Mechanics;

        player.SetTrack(
            Rusty.Engine.Mechanics.TrackId.Parse("health"),
            Rusty.Engine.Mechanics.ExactValue.Zero,
            Rusty.Engine.Mechanics.ExactTrackSetPolicy.ClampToBounds);
        Assert.True(composition.State.Actors.Player.IsDefeated);

        player.RestoreTrack(Rusty.Engine.Mechanics.TrackId.Parse("health"), new Rusty.Engine.Mechanics.ExactValue(1));
        Assert.False(composition.State.Actors.Player.IsDefeated);
    }

    [Fact]
    public void Input_interpretation_uses_typed_edges_controls_and_semantic_intents()
    {
        RecordingEngine engine = new();
        InputActionId testAttack = new("test.attack");
        PlayerInputSystem input = new(DaggerfallTuning.Defaults.PlayerControl, engine.Look.Service, DaggerfallInput.Controls, [new InputActionBinding(testAttack, "attack"u8.ToArray())]);
        PlayerControlState player = new(new WorldPoint(0, 0, 0), yawRadians: 0f, pitchRadians: 0f);

        ProductUpdateState held = new(1f);
        held.Add(Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1f, phase: InputPhase.Pressed, intent: "move.forward"));
        held.Add(Input(InputEventKind.MappedDigital, InputEdge.Held, x: 1f, phase: InputPhase.Held, intent: "move.left"));
        held.Add(Input(InputEventKind.PointerDelta, x: .25f, y: -.5f));
        held.Add(Input(InputEventKind.DirectDigital, InputEdge.Held, x: 1f, phase: InputPhase.Held, intent: "attack"));
        held.Add(Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1f, phase: InputPhase.Pressed, intent: "attack"));
        input.Apply(player, held);

        Assert.Equal(new Vector2(-1f, 1f), held.PlanarIntent);
        Assert.True(held.IsRequested(testAttack));
        Assert.Equal(1, engine.Look.IntegrationCount);
        Assert.Equal(.25f, player.YawRadians);
        Assert.Equal(-.5f, player.PitchRadians);
        Assert.NotNull(engine.Look.LastRequest);
        LookConfig configuration = engine.Look.LastRequest.Value.Config;
        Assert.False(configuration.InvertHorizontal);
        Assert.False(configuration.InvertVertical);
        Assert.True(configuration.WrapYaw);

        ProductUpdateState released = new(1f);
        released.Add(Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.KeyW));
        released.Add(Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.KeyW));
        released.Add(Input(InputEventKind.Clear));
        released.Add(Input(InputEventKind.DirectAxis, x: .75f, y: -.25f, phase: InputPhase.DirectUi, intent: "movement"));
        input.Apply(player, released);

        Assert.Equal(new Vector2(.75f, -.25f), released.PlanarIntent);
        Assert.False(released.IsRequested(testAttack));
    }

    [Fact]
    public void Session_publishes_authoritative_player_pose_through_the_engine_camera()
    {
        RecordingEngine engine = new();
        engine.Spatial.Propose = request => default(CharacterStepReceipt) with
        {
            Transform = new Transform(new Vector3(4f, 2f, 3f), Quaternion.Identity, Vector3.One),
            Motion = request.Motion,
        };
        using (DaggerfallSession session = new(engine.Context, Definitions(), SkeletalInputs(), DaggerfallTuning.Defaults))
        {
            Assert.Single(engine.Camera.Descriptors);
            Assert.Equal(1, engine.Camera.ActiveSelections);

            session.State.PlayerControl.YawRadians = .5f;
            session.State.PlayerControl.PitchRadians = -.25f;
            session.Update(new ProductUpdateState(.125f));

            Assert.Equal(new WorldPoint(4f, 2f, 3f), session.State.PlayerControl.Position);
            CameraDescriptor descriptor = engine.Camera.Descriptors[^1];
            Assert.Equal(new Vector3(4f, 2.75f, 3f), descriptor.Pose.Position);
            Assert.Equal(.5d * 180d / Math.PI, descriptor.Pose.YawDegrees, 5);
            Assert.Equal(-.25d * 180d / Math.PI, descriptor.Pose.PitchDegrees, 5);
            Assert.Equal(CameraProjectionKind.Perspective, descriptor.Projection.Kind);
        }

        Assert.Equal(1, engine.Camera.ActiveClears);
        Assert.Equal(1, engine.Camera.Disposals);
    }

    [Fact]
    public void Realtime_batch_uses_engine_fixed_delta_once_per_admitted_step_and_ignores_invalid_turns()
    {
        RecordingEngine engine = new();
        using WorldRpgProduct game = new(new ProductCreateContext(engine.Context, ProductContentWithPlayer(), EmptyInputConfiguration()));
        game.Start();

        ProductUpdateResult request = game.Update(new ProductUpdate(
            Facts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, admittedSteps: 3, fixedDeltaSeconds: .125),
            new ProductInputEvent[] { Input(InputEventKind.MappedAxis, x: .5f, y: -.25f, phase: InputPhase.Axis, intent: "movement") }));
        Assert.Equal(ProductUpdateResult.None, request);
        Assert.Equal(3, engine.Spatial.StepCalls);
        Assert.All(engine.Spatial.Commands, command => Assert.Equal(.125f, command.StepSeconds));
        Assert.All(engine.Spatial.Commands, command => Assert.Equal(new Vector2(.5f, -.25f), command.PlanarIntent));

        game.Update(new ProductUpdate(Facts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, admittedSteps: 0, fixedDeltaSeconds: .125), ReadOnlySpan<ProductInputEvent>.Empty));
        game.Update(new ProductUpdate(Facts(ProductUpdateMode.Realtime, ProductLifecycleState.Paused, admittedSteps: 2, fixedDeltaSeconds: .125), ReadOnlySpan<ProductInputEvent>.Empty));
        game.Update(new ProductUpdate(Facts(ProductUpdateMode.Demand, ProductLifecycleState.Running, admittedSteps: 2, fixedDeltaSeconds: 0), ReadOnlySpan<ProductInputEvent>.Empty));
        game.Update(new ProductUpdate(Facts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, admittedSteps: 2, fixedDeltaSeconds: double.NaN), ReadOnlySpan<ProductInputEvent>.Empty));

        Assert.Equal(3, engine.Spatial.StepCalls);
    }

    [Fact]
    public void Host_gates_updates_and_disposes_the_selected_session_once()
    {
        RecordingEngine engine = new();
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, ProductContentWithPlayer(), EmptyInputConfiguration()));
        ProductUpdate update = new(Facts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, admittedSteps: 1, fixedDeltaSeconds: .125), ReadOnlySpan<ProductInputEvent>.Empty);

        Assert.Single(engine.Ui.Projections);
        Assert.Equal(ProductUpdateResult.None, product.Update(update));
        Assert.Equal(0, engine.Spatial.StepCalls);

        product.Start();
        Assert.Equal(2, engine.Ui.Projections.Count);
        product.Pause();
        Assert.Equal(ProductUpdateResult.None, product.Update(update));
        Assert.Equal(0, engine.Spatial.StepCalls);

        product.Resume();
        Assert.Equal(ProductUpdateResult.None, product.Update(update));
        Assert.Equal(1, engine.Spatial.StepCalls);

        product.Shutdown();
        Assert.Equal(ProductUpdateResult.None, product.Update(update));
        Assert.Equal(1, engine.Spatial.StepCalls);
        Assert.Equal(1, engine.Spatial.SessionDisposals);
    }

    [Fact]
    public void Host_projects_the_exact_resolved_composition_identity_in_pack_order()
    {
        ProductContent content = ProductContentWithPlayer();
        ResolvedCompositionIdentity expected = GameCompositionResolver.Resolve(content, new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        RecordingEngine engine = new();

        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, content, EmptyInputConfiguration()));

        UiValue value = Assert.Single(engine.Ui.Projections).Value;
        uint identity = Field(value, value.Root, "composition");
        Assert.Equal(expected.Bundle.Value, Text(value, value.Nodes.Span[(int)Field(value, identity, "bundle")]));
        Assert.Equal(expected.Ruleset.Value, Text(value, value.Nodes.Span[(int)Field(value, identity, "ruleset")]));
        Assert.Equal(expected.ContentPacks.Select(pack => pack.Value), Array(value, Field(value, identity, "contentPacks")).Select(node => Text(value, value.Nodes.Span[(int)node])));
        Assert.Equal(expected.Tuning.Value, Text(value, value.Nodes.Span[(int)Field(value, identity, "tuning")]));
        Assert.Equal(expected.Fingerprint, Text(value, value.Nodes.Span[(int)Field(value, identity, "fingerprint")]));
        Assert.Equal(expected.ContentFingerprint, Text(value, value.Nodes.Span[(int)Field(value, identity, "contentFingerprint")]));
        Assert.Equal(expected.TuningFingerprint, Text(value, value.Nodes.Span[(int)Field(value, identity, "tuningFingerprint")]));
    }

    [Fact]
    public void Normal_semantic_attack_projects_no_target_in_melee_reach_without_combat_side_effects()
    {
        RecordingEngine engine = new();
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, ProductContentWithPlayer(), EmptyInputConfiguration()));
        product.Start();

        ProductInputEvent attack = Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack");
        product.Update(new ProductUpdate(Facts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, admittedSteps: 1, fixedDeltaSeconds: .125), new[] { attack }));

        UiValue value = engine.Ui.Projections[^1].Value;
        Assert.Equal("No target in melee reach", Text(value, value.Nodes.Span[(int)Field(value, value.Root, "lastOutcome")]));
    }

    [Fact]
    public void Host_releases_the_created_session_when_create_time_projection_fails()
    {
        RecordingEngine engine = new();
        engine.Ui.FailOnPublishCall = 1;

        Assert.Throws<InvalidOperationException>(() => new WorldRpgProduct(new ProductCreateContext(engine.Context, ProductContentWithPlayer(), EmptyInputConfiguration())));

        Assert.Equal(1, engine.Spatial.SessionDisposals);
        Assert.Equal(1, engine.Ui.StreamDisposals);
    }

    [Fact]
    public void Appearance_owners_clear_retained_facts_and_release_partial_construction_once()
    {
        RecordingEngine engine = new();
        AuthoredSprite sprite = new("textures/test.png", default, default, default, Vector2.One, 0);
        PrivateersHoldInputs inputs = new(new ProjectFacts(null, new Dictionary<long, AuthoredActor> { [9] = new(9, new DaggerfallActorId("rat"), new WorldPoint(0, 0, 0), sprite) }), [], new CollisionMesh([], []), "mesh.json");
        using (PrivateersHoldAppearance appearance = new(engine.Appearance, inputs))
        {
            appearance.Dispose();
            appearance.Dispose();
        }
        Assert.Equal(2, engine.Appearance.AppearanceDisposals);
        Assert.Empty(engine.Appearance.Snapshots[^1]);

        RecordingEngine failing = new();
        failing.Appearance.FailOnCreateCall = 2;
        Assert.Throws<InvalidOperationException>(() => new PrivateersHoldAppearance(failing.Appearance, inputs));
        Assert.Equal(1, failing.Appearance.AppearanceDisposals);
    }

    private static PrivateersHoldInputs SkeletalInputs()
    {
        return ActorInputs(2000, "skeletal-warrior");
    }

    private static PrivateersHoldInputs ActorInputs(long entityId, string actorId)
    {
        AuthoredActor actor = new(entityId, new DaggerfallActorId(actorId), new WorldPoint(0, 0, 0), null);
        return new PrivateersHoldInputs(new ProjectFacts(new WorldPoint(0, 0, 0), new Dictionary<long, AuthoredActor> { [entityId] = actor }), [], new CollisionMesh([], []), null, new PlayerInitialLook(MathF.PI, 0f), [new ScenarioLoadoutEntry(new DaggerfallItemId("iron-longsword"), 1, 1001, new DaggerfallEquipmentSlotId("right-hand")), new ScenarioLoadoutEntry(new DaggerfallItemId("iron-dagger"), 1, 1002, null), new ScenarioLoadoutEntry(new DaggerfallItemId("iron-cuirass"), 1, 1003, new DaggerfallEquipmentSlotId("torso")), new ScenarioLoadoutEntry(new DaggerfallItemId("gold-piece"), 25, null, null)], []);
    }

    private static DaggerfallDefinitions Definitions() => DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(BasePayload));

    private const string BasePayload = """
    {"schemaVersion":1,"ruleset":"daggerfall","actors":[
      {"id":"player","stats":{"strength":50,"intelligence":50,"willpower":50,"agility":50,"endurance":40,"personality":50,"speed":50,"luck":50,"reflexes":2,"longBlade":60,"handToHand":40,"dodging":0},"health":{"minimum":85,"maximum":85},"armor":0,"rewards":{"experience":0}},
      {"id":"rat","mobileId":0,"stats":{"strength":40,"intelligence":10,"willpower":70,"agility":80,"endurance":55,"personality":50,"speed":45,"luck":50,"reflexes":0,"longBlade":0,"handToHand":35,"dodging":0},"health":{"minimum":9,"maximum":16},"armor":30,"attack":{"skill":"hand-to-hand","minimumDamage":1,"maximumDamage":4,"cooldownSeconds":1.5},"rewards":{"experience":50}},
      {"id":"skeletal-warrior","mobileId":15,"stats":{"strength":50,"intelligence":65,"willpower":40,"agility":80,"endurance":55,"personality":50,"speed":70,"luck":50,"reflexes":0,"longBlade":75,"handToHand":75,"dodging":75},"health":{"minimum":17,"maximum":66},"armor":10,"attack":{"skill":"long-blade","minimumDamage":5,"maximumDamage":15,"cooldownSeconds":2.0},"rewards":{"experience":450,"loot":{"table":"H","item":"gold-piece","minimumQuantity":2,"maximumQuantity":10}}}],
      "items":[{"id":"iron-longsword","kind":"unique","maximumQuantity":1,"weapon":{"minimumDamage":2,"maximumDamage":16,"skill":"long-blade","handedness":"either","value":15,"weight":18},"equipment":{"classifications":["weapon"],"requiredSlots":1}},{"id":"iron-dagger","kind":"unique","maximumQuantity":1},{"id":"iron-cuirass","kind":"unique","maximumQuantity":1,"equipment":{"classifications":["torso-armor"],"requiredSlots":1}},{"id":"gold-piece","kind":"fungible","maximumQuantity":10000}],
      "equipmentSlots":[{"id":"right-hand","allowedClassifications":["weapon"]},{"id":"torso","allowedClassifications":["torso-armor"]}],
      "hudResources":[{"id":"health","label":"Health","track":"health"},{"id":"stamina","label":"Stamina","track":"stamina"},{"id":"magicka","label":"Magicka","track":"magicka"}]}
    """;

    private const string ScenarioPayload = """
    {"schemaVersion":1,"ruleset":"daggerfall","startingState":{"position":[0,0,0],"look":{"yawRadians":3.1415927,"pitchRadians":0},"loadout":[{"item":"iron-longsword","entityId":1001,"equipSlot":"right-hand"},{"item":"iron-dagger","entityId":1002},{"item":"iron-cuirass","entityId":1003,"equipSlot":"torso"},{"item":"gold-piece","quantity":25}]},"appearances":[],"placements":[],"encounters":[],"world":{"staticMesh":"imported/privateers-hold.static-mesh.json","navigation":"projects/privateers-hold.navgrid.json","collision":"imported/privateers-hold.static-mesh.json","appearance":{"tint":[0.72,0.7,0.65,1],"position":[0,0,0],"rotation":[0,0,0,1],"scale":[1,1,1],"visible":true,"layer":"scene"}}}
    """;

    private static ProductContent SpatialContent(string navigation = "{\"cells\":[]}", string collision = "{\"payload\":{\"source\":{\"positions\":[],\"indices\":[]}}}") => new(new ProductContentFile[]
    {
        new(Encoding.UTF8.GetBytes("projects/privateers-hold.navgrid.json"), Encoding.UTF8.GetBytes(navigation)),
        new(Encoding.UTF8.GetBytes("imported/privateers-hold.static-mesh.json"), Encoding.UTF8.GetBytes(collision)),
    });

    private static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl keyboard = KeyboardControl.None, float x = 0f, float y = 0f, InputPhase phase = InputPhase.None, string intent = "") => new(
        kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, keyboard, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None, InputValueKind.None, phase, InputProvenance.None, default, default, default, x, y, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, Encoding.UTF8.GetBytes(intent), ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    private static ProductUpdateFacts Facts(ProductUpdateMode mode, ProductLifecycleState lifecycle, uint admittedSteps, double fixedDeltaSeconds) => new(mode, lifecycle, 1, 1, 1, 1, mode == ProductUpdateMode.Realtime ? 60u : 0u, admittedSteps, 0, fixedDeltaSeconds);

    private static ProductContent ProductContentWithPlayer() => new(new ProductContentFile[] {
        new ProductContentFile(Encoding.UTF8.GetBytes("worldrpg/bundles/daggerfall.privateers-hold.bundle.json"), Encoding.UTF8.GetBytes("""{"kind":"worldrpg.game-bundle","schemaVersion":1,"id":"daggerfall.privateers-hold","version":1,"ruleset":"daggerfall","contentPacks":[{"id":"daggerfall.base","version":1},{"id":"daggerfall.privateers-hold","version":1}],"tuning":{"id":"daggerfall.defaults","version":1}}""")),
        new ProductContentFile(Encoding.UTF8.GetBytes("worldrpg/content-packs/daggerfall.base.pack.json"), Encoding.UTF8.GetBytes("""{"kind":"worldrpg.content-pack","schemaVersion":1,"id":"daggerfall.base","version":1,"ruleset":"daggerfall","dependencies":[],"payload":"worldrpg/payloads/daggerfall.base.json"}""")),
        new ProductContentFile(Encoding.UTF8.GetBytes("worldrpg/payloads/daggerfall.base.json"), Encoding.UTF8.GetBytes(BasePayload)),
        new ProductContentFile(Encoding.UTF8.GetBytes("worldrpg/content-packs/daggerfall.privateers-hold.pack.json"), Encoding.UTF8.GetBytes("""{"kind":"worldrpg.content-pack","schemaVersion":1,"id":"daggerfall.privateers-hold","version":1,"ruleset":"daggerfall","dependencies":[{"id":"daggerfall.base","version":1}],"payload":"worldrpg/payloads/daggerfall.privateers-hold.json"}""")),
        new ProductContentFile(Encoding.UTF8.GetBytes("worldrpg/payloads/daggerfall.privateers-hold.json"), Encoding.UTF8.GetBytes(ScenarioPayload)),
        new ProductContentFile(Encoding.UTF8.GetBytes("worldrpg/tuning/daggerfall.defaults.tuning.json"), Encoding.UTF8.GetBytes("""{"kind":"worldrpg.tuning-profile","schemaVersion":1,"id":"daggerfall.defaults","version":1,"ruleset":"daggerfall","payload":"worldrpg/tuning-payloads/daggerfall.defaults.json"}""")),
        new ProductContentFile(Encoding.UTF8.GetBytes("worldrpg/tuning-payloads/daggerfall.defaults.json"), Encoding.UTF8.GetBytes("""{"playerControl":{"lookSensitivity":0.0035,"pitchMinimumRadians":-1.5533,"pitchMaximumRadians":1.5533,"maximumLookDeltaRadians":0.35,"invertHorizontal":false,"invertVertical":false,"wrapYaw":true},"combat":{"playerMeleeStaminaCost":5,"playerMeleeCooldownSeconds":0.75},"spatial":{"collisionVoxelSize":0.5,"collisionChunkSize":32,"navigationCellSize":0.5,"navigationChunkSize":32,"navigationMaximumStepCells":2},"camera":{"eyeHeight":0.75,"fieldOfViewYDegrees":65,"nearPlane":0.1,"farPlane":100}}""")),
        new ProductContentFile(Encoding.UTF8.GetBytes("projects/privateers-hold.navgrid.json"), Encoding.UTF8.GetBytes("""{"cells":[]}""")),
        new ProductContentFile(Encoding.UTF8.GetBytes("imported/privateers-hold.static-mesh.json"), Encoding.UTF8.GetBytes("""{"payload":{"source":{"positions":[],"indices":[]}}}"""))
    });

    private static ProductInputConfiguration EmptyInputConfiguration() => new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);

    private static IReadOnlyList<string> ResourceIds(UiValue value) => Array(value, Field(value, value.Root, "resources")).Select(node => Text(value, value.Nodes.Span[(int)Field(value, node, "id")])).ToArray();
    private static (double Current, double Maximum) Resource(UiValue value, string id)
    {
        uint row = Array(value, Field(value, value.Root, "resources")).Single(node => Text(value, value.Nodes.Span[(int)Field(value, node, "id")]) == id);
        return (value.Nodes.Span[(int)Field(value, row, "current")].NumberValue, value.Nodes.Span[(int)Field(value, row, "maximum")].NumberValue);
    }
    private static uint Field(UiValue value, uint objectNode, string key) => Edges(value, objectNode).Single(edge => Key(value, value.Nodes.Span[(int)edge]) == key);
    private static IReadOnlyList<uint> Array(UiValue value, uint arrayNode) => Edges(value, arrayNode).ToArray();
    private static IEnumerable<uint> Edges(UiValue value, uint node) { StructuredValueNode current = value.Nodes.Span[(int)node]; for (uint index = 0; index < current.ChildCount; index++) yield return value.Edges.Span[checked((int)(current.FirstEdge + index))]; }
    private static string Text(UiValue value, StructuredValueNode node) => Encoding.UTF8.GetString(value.Utf8.Span.Slice(checked((int)node.TextOffset), checked((int)node.TextLen)));
    private static string Key(UiValue value, StructuredValueNode node) => Encoding.UTF8.GetString(value.Utf8.Span.Slice(checked((int)node.KeyOffset), checked((int)node.KeyLen)));

    private sealed class RecordingEngine
    {
        internal RecordingEngine() { Context = DispatchProxy.Create<IEngineContext, EngineContextProxy>(); ((EngineContextProxy)(object)Context).Owner = this; }
        internal IEngineContext Context { get; }
        internal RecordingLook Look { get; } = RecordingLook.Create();
        internal RecordingSpatial Spatial { get; } = RecordingSpatial.Create();
        internal RecordingAppearance Appearance { get; } = new();
        internal RecordingCamera Camera { get; } = RecordingCamera.Create();
        internal RecordingRandom Random { get; } = RecordingRandom.Create();
        internal RecordingUi Ui { get; } = RecordingUi.Create();
    }

    private class EngineContextProxy : DispatchProxy
    {
        internal RecordingEngine Owner { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch { "get_Look" => Owner.Look.Service, "get_Spatial" => Owner.Spatial.Service, "get_Appearance" => Owner.Appearance.Service, "get_CameraView" => Owner.Camera.Service, "get_Random" => Owner.Random.Service, "get_Ui" => Owner.Ui.Service, _ => throw new NotSupportedException(method?.Name) };
    }

    private class RecordingLook : DispatchProxy
    {
        internal ILookService Service { get; private set; } = null!;
        internal int IntegrationCount { get; private set; }
        internal LookRequest? LastRequest { get; private set; }
        internal static RecordingLook Create()
        {
            ILookService service = DispatchProxy.Create<ILookService, RecordingLook>();
            RecordingLook proxy = (RecordingLook)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(ILookService.Integrate))
            {
                LookRequest request = (LookRequest)args![0]!;
                IntegrationCount++;
                LastRequest = request;
                LookState after = new(request.State.YawRadians + request.Delta.X, request.State.PitchRadians + request.Delta.Y);
                return new LookReceipt(request.State, after, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
            }
            return method?.ReturnType.IsValueType == true ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
    private class RecordingSpatial : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal int SessionDisposals { get; private set; }
        internal int StepCalls { get; private set; }
        internal List<CharacterControllerCommand> Commands { get; } = [];
        internal Func<CharacterStepRequest, CharacterStepReceipt>? Propose { get; set; }
        internal static RecordingSpatial Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, RecordingSpatial>();
            RecordingSpatial proxy = (RecordingSpatial)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(ISpatialService.CreateSession)) return new SpatialSession(new SpatialSessionHandle(1), () => SessionDisposals++);
            if (method?.Name == nameof(ISpatialService.DefaultCharacterControllerConfig)) return default(CharacterControllerConfig);
            if (method?.Name == nameof(ISpatialService.ProposeCharacterStep))
            {
                CharacterStepRequest request = (CharacterStepRequest)args![0]!;
                StepCalls++;
                Commands.Add(request.Command);
                return Propose?.Invoke(request) ?? default(CharacterStepReceipt) with
                {
                    Transform = new Transform(request.Position, Quaternion.Identity, Vector3.One),
                    Motion = request.Motion,
                };
            }
            return method?.ReturnType.IsValueType == true ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
    private sealed class RecordingAppearance : IAppearanceService
    {
        internal IAppearanceService Service => this;
        internal int AppearanceDisposals { get; private set; }
        internal List<AppearanceFact[]> Snapshots { get; } = [];
        internal int? FailOnCreateCall { get; set; }
        private int CreateCalls { get; set; }
        public RenderResourceInfo OpenResource(RenderResourceRequest request) => new(new RenderResourceHandle(1), default, 0);
        public Material CreateMaterial(MaterialRequest request) => new(new MaterialHandle(1), () => { });
        public void UpdateMaterial(MaterialUpdateRequest request) { }
        public Material ReplaceMaterial(MaterialUpdateRequest request) => new(new MaterialHandle(1), () => { });
        public Appearance CreatePrimitive(PrimitiveAppearanceRequest request) => CreateAppearance();
        public Appearance ReplacePrimitive(PrimitiveAppearanceReplaceRequest request) => CreateAppearance();
        public Appearance CreateStaticMesh(StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMesh(Appearance appearance, StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMeshFromContent(Appearance appearance, StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public void UpdateStaticMeshMaterials(StaticMeshMaterialUpdateRequest request) { }
        public Appearance CreateSprite(SpriteAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest request) => CreateAppearance();
        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values) => Snapshots.Add(values.ToArray());
        public Light CreateLight(LightRequest request) => new(new LightHandle(1), () => { });
        public void UpdateLight(LightUpdateRequest request) { }
        public Light ReplaceLight(LightUpdateRequest request) => new(new LightHandle(1), () => { });
        public LightReadout ReadLight(Light light) => default;
        public PresentationReadout ReadPresentation() => default;
        private Appearance CreateAppearance()
        {
            if (++CreateCalls == FailOnCreateCall) throw new InvalidOperationException("Injected appearance creation failure.");
            return new(new AppearanceHandle(1), () => AppearanceDisposals++);
        }
    }
    private class RecordingCamera : DispatchProxy
    {
        internal ICameraViewService Service { get; private set; } = null!;
        internal List<CameraDescriptor> Descriptors { get; } = [];
        internal int ActiveSelections { get; private set; }
        internal int ActiveClears { get; private set; }
        internal int Disposals { get; private set; }
        internal static RecordingCamera Create()
        {
            ICameraViewService service = DispatchProxy.Create<ICameraViewService, RecordingCamera>();
            RecordingCamera proxy = (RecordingCamera)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(ICameraViewService.CreateCamera) => CreateCamera((CameraDescriptor)args![0]!),
            nameof(ICameraViewService.UpdateCamera) => UpdateCamera((CameraUpdateRequest)args![0]!),
            nameof(ICameraViewService.SetActiveCamera) => Select(),
            nameof(ICameraViewService.ClearActiveCamera) => Clear(),
            _ => throw new NotSupportedException(method?.Name)
        };
        private Camera CreateCamera(CameraDescriptor descriptor)
        {
            Descriptors.Add(descriptor);
            return new Camera(new CameraHandle(1), () => Disposals++);
        }
        private object? UpdateCamera(CameraUpdateRequest request) { Descriptors.Add(request.Descriptor); return null; }
        private object? Select() { ActiveSelections++; return null; }
        private object? Clear() { ActiveClears++; return null; }
    }
    private class RecordingRandom : DispatchProxy
    {
        internal IRandomService Service { get; private set; } = null!;
        internal List<KeyedRngRequest> Requests { get; } = [];
        internal Func<KeyedRngRequest, ulong>? Draw { get; set; }
        internal static RecordingRandom Create()
        {
            IRandomService service = DispatchProxy.Create<IRandomService, RecordingRandom>();
            RecordingRandom proxy = (RecordingRandom)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)args![0]!;
            Requests.Add(request);
            ulong value = Draw?.Invoke(request) ?? checked((ulong)(request.Key.StartsWith("spawn:", StringComparison.Ordinal) || request.Maximum == 100 ? request.Minimum : request.Maximum));
            return new KeyedRngReceipt(checked((long)value));
        }
    }
    private class RecordingUi : DispatchProxy
    {
        internal IUiService Service { get; private set; } = null!;
        internal List<UiProjection> Projections { get; } = [];
        internal int StreamDisposals { get; private set; }
        internal int? FailOnPublishCall { get; set; }
        private int PublishCalls { get; set; }
        internal static RecordingUi Create()
        {
            IUiService service = DispatchProxy.Create<IUiService, RecordingUi>();
            RecordingUi proxy = (RecordingUi)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IUiService.OpenStream) => new UiStream(new UiStreamHandle(1), () => StreamDisposals++),
            nameof(IUiService.PublishProjection) => Publish((UiProjection)args![0]!),
            _ => throw new NotSupportedException(method?.Name)
        };
        private object? Publish(UiProjection projection)
        {
            if (++PublishCalls == FailOnPublishCall) throw new InvalidOperationException("Injected UI projection failure.");
            Projections.Add(projection);
            return null;
        }
    }
}
