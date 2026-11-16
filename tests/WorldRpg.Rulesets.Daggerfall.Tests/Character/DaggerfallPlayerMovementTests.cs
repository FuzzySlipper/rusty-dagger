using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The player's movement in one step over the Character policies, the input and the spatial fake, with no session.</summary>
public sealed class DaggerfallPlayerMovementTests
{
    [Fact]
    public void A_hard_landing_is_one_within_the_fall_grace_on_dry_ground_and_not_slowfalling()
    {
        DaggerfallPlayerStep step = new() { Landing = new DaggerfallLanding(4f) };
        Assert.True(step.HardLanding);
        Assert.False((step with { Landing = new DaggerfallLanding(1f) }).HardLanding);
        Assert.False((step with { Landing = null }).HardLanding);
        Assert.False((step with { Swimming = DaggerfallSwimmingStep.None with { Swimming = true } }).HardLanding);
        Assert.False((step with { SlowfallDescending = true }).HardLanding);
    }

    [Fact]
    public void A_load_too_heavy_or_death_stops_the_feet_and_a_movement_restriction_forbids_moving()
    {
        using Stage stage = new();
        ProductUpdateState Walking()
        {
            stage.Input.Observe(stage.Control, [], 0f, new Vector2(0f, 1f));
            return new ProductUpdateState(.05f);
        }

        ProductUpdateState free = Walking();
        DaggerfallPlayerStep step = stage.Movement.ApplyControls(free, canCarry: true);
        Assert.Equal(new Vector2(0f, 1f), free.PlanarIntent);
        Assert.True(step.CanMove && step.Alive);

        ProductUpdateState burdened = Walking();
        step = stage.Movement.ApplyControls(burdened, canCarry: false);
        Assert.Equal(Vector2.Zero, burdened.PlanarIntent);
        Assert.False(step.CanMove);

        stage.Effects.Restrictions = new ActorControlRestrictions { Movement = true };
        step = stage.Movement.ApplyControls(Walking(), canCarry: true);
        Assert.False(step.CanMove);
        stage.Effects.Restrictions = default;

        stage.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).SetCurrent(0, clamp: true);
        ProductUpdateState dead = Walking();
        step = stage.Movement.ApplyControls(dead, canCarry: true);
        Assert.Equal(Vector2.Zero, dead.PlanarIntent);
        Assert.False(step.Alive || step.CanMove);
    }

    [Fact]
    public void Levitation_drives_the_vertical_and_stops_running_jumping_and_crouching()
    {
        using Stage stage = new();
        stage.Effects.Levitation = true;
        DaggerfallPlayerStep step = Begun(stage, new ProductUpdateState(.05f));
        step.Locomotion = step.Locomotion with
        {
            Running = true, JumpRequested = true, UpHeld = true,
            Controls = step.Locomotion.Controls with { CrouchRequested = true },
        };

        stage.Movement.ShapeLocomotion(ref step, new ProductUpdateState(.05f), 1, 1, stage.Water);

        Assert.True(step.Levitation.IsLevitating);
        Assert.False(step.Locomotion.Running || step.Locomotion.JumpRequested || step.Locomotion.Controls.CrouchRequested);
        Assert.Equal(step.Levitation.VerticalVelocity, step.Locomotion.Controls.VerticalVelocity);
        Assert.Equal(DaggerfallTuning.Defaults.Locomotion.LevitationVerticalSpeed, step.Locomotion.Controls.VerticalVelocity);
    }

    [Fact]
    public void Water_the_player_swims_in_replaces_the_controls_with_a_stroke_unless_walking_on_it()
    {
        using Stage stage = new();
        stage.Water.Volume = new CharacterWaterVolume(7, new Vector3(-10f, -5f, -10f), new Vector3(10f, 1f, 10f));
        DaggerfallPlayerStep step = Begun(stage, new ProductUpdateState(.05f));
        step.Locomotion = step.Locomotion with { Running = true, JumpRequested = true };

        stage.Movement.ShapeLocomotion(ref step, new ProductUpdateState(.05f), 1, 1, stage.Water);

        Assert.Equal(stage.Water.Volume, step.ActiveWater);
        Assert.False(step.Locomotion.Running || step.Locomotion.JumpRequested);
        Assert.Null(step.Locomotion.Controls.VerticalVelocity);
        Assert.False(step.Locomotion.Controls.JumpPressed || step.Locomotion.Controls.JumpHeld || step.Locomotion.Controls.CrouchRequested);

        stage.Effects.WaterWalking = true;
        step = Begun(stage, new ProductUpdateState(.05f));
        step.Locomotion = step.Locomotion with { Running = true };
        stage.Movement.ShapeLocomotion(ref step, new ProductUpdateState(.05f), 1, 2, stage.Water);
        Assert.True(step.Locomotion.Running);
    }

    [Theory]
    [InlineData(.05f, true)]
    [InlineData(-.05f, true)]
    [InlineData(.07f, false)]
    public void Only_a_wall_close_to_vertical_ahead_of_the_player_is_climbed(float normalY, bool climbed)
    {
        using Stage stage = new();
        stage.Spatial.FloorHit = _ => new SpatialHit { Present = true, Kind = SpatialHitKind.StaticMesh,
            Normal = Vector3.Normalize(new Vector3(0f, normalY, 1f)), Distance = .2f };
        ProductUpdateState update = new(DaggerfallTuning.Defaults.Climbing.StartCheckSeconds);
        DaggerfallPlayerStep step = Begun(stage, update);
        step.Locomotion = step.Locomotion with { ForwardIntent = 1f };

        stage.Movement.ShapeLocomotion(ref step, update, 1, 1, stage.Water);

        Assert.Equal(climbed, step.Climb.Climbing);
        Assert.Equal(climbed, step.Locomotion.Climbing);
    }

    /// <summary>
    /// Paralysis stops the feet but leaves the crouch toggle alone: a paralysed crouched player stays crouched, as the
    /// donor's PlayerHeightChanger never reads paralysis, rather than standing and staying stood once it ends.
    /// </summary>
    [Fact]
    public void A_paralysed_crouched_player_stays_crouched()
    {
        using Stage stage = new();
        stage.Effects.Restrictions = new ActorControlRestrictions { Movement = true };
        DaggerfallPlayerStep step = Begun(stage, new ProductUpdateState(.05f));
        step.Locomotion = step.Locomotion with { Controls = step.Locomotion.Controls with { CrouchRequested = true, PlanarIntent = Vector2.UnitY } };

        stage.Movement.ConstrainLocomotion(ref step);

        Assert.True(step.Locomotion.Controls.CrouchRequested);
        Assert.Equal(Vector2.Zero, step.Locomotion.Controls.PlanarIntent);
    }

    /// <summary>A climb stands a crouched player, as the donor's PlayerHeightChanger forces a climber standing; the crouch toggle stays otherwise.</summary>
    [Theory]
    [InlineData(.05f, false)]
    [InlineData(.07f, true)]
    public void A_climb_stands_a_crouched_player(float normalY, bool crouchKept)
    {
        using Stage stage = new();
        stage.Spatial.FloorHit = _ => new SpatialHit { Present = true, Kind = SpatialHitKind.StaticMesh,
            Normal = Vector3.Normalize(new Vector3(0f, normalY, 1f)), Distance = .2f };
        ProductUpdateState update = new(DaggerfallTuning.Defaults.Climbing.StartCheckSeconds);
        DaggerfallPlayerStep step = Begun(stage, update);
        step.Locomotion = step.Locomotion with
        {
            ForwardIntent = 1f,
            Controls = step.Locomotion.Controls with { CrouchRequested = true },
        };

        stage.Movement.ShapeLocomotion(ref step, update, 1, 1, stage.Water);

        Assert.Equal(!crouchKept, step.Climb.Climbing);
        Assert.Equal(crouchKept, step.Locomotion.Controls.CrouchRequested);
    }

    [Fact]
    public void Slowfall_holds_a_falling_player_to_its_descent_but_not_one_rising_or_held_up()
    {
        using Stage stage = new();
        stage.Effects.Slowfall = true;
        DaggerfallPlayerStep step = Begun(stage, new ProductUpdateState(.05f));
        step.MotionBefore = step.MotionBefore with { Grounded = false, ControlledVelocity = new Vector3(0f, -2f, 0f) };

        stage.Movement.ConstrainLocomotion(ref step);
        Assert.True(step.SlowfallDescending);
        Assert.Equal(-DaggerfallTuning.Defaults.Locomotion.SlowfallDescentSpeed, step.Locomotion.Controls.VerticalVelocity);

        step = Begun(stage, new ProductUpdateState(.05f));
        step.MotionBefore = step.MotionBefore with { Grounded = false, ControlledVelocity = new Vector3(0f, 2f, 0f) };
        stage.Movement.ConstrainLocomotion(ref step);
        Assert.False(step.SlowfallDescending);

        step = Begun(stage, new ProductUpdateState(.05f));
        step.MotionBefore = step.MotionBefore with { Grounded = false, ControlledVelocity = new Vector3(0f, -2f, 0f) };
        step.ActiveWater = new CharacterWaterVolume(7, Vector3.Zero, Vector3.One);
        stage.Movement.ConstrainLocomotion(ref step);
        Assert.False(step.SlowfallDescending);
    }

    /// <summary>
    /// The Engine refuses every step of a body it cannot free, and with no clear spot in reach the player is held: they are
    /// told once as the hold begins, not again while it lasts, nor once a step goes through; a later hold is told again.
    /// </summary>
    [Fact]
    public void A_player_held_in_collision_is_told_once_and_a_freed_one_no_more()
    {
        using Stage stage = new();
        stage.Spatial.OverlapHit = _ => new SpatialHit { Present = true };
        string[] Notices() => [.. stage.Presentation.Messages.Where(message => message == "You are stuck.")];
        CharacterStepReceipt? Moved()
        {
            ProductUpdateState update = new(.05f);
            DaggerfallPlayerStep step = Begun(stage, update);
            stage.Movement.Move(ref step, update);
            return step.Movement;
        }

        stage.Spatial.StepResult = (_, _) => throw new EngineCallException("Spatial", "ProposeCharacterStep", 1,
            new[] { new EngineDiagnostic("unresolved-character-controller-penetration", "Wedged.", "test") });
        for (int held = 0; held < 3; held++) Assert.Null(Moved());
        Assert.Single(Notices());

        stage.Spatial.StepResult = null;
        for (int freed = 0; freed < 2; freed++) Assert.NotNull(Moved());
        Assert.Single(Notices());

        stage.Spatial.StepResult = (_, _) => throw new EngineCallException("Spatial", "ProposeCharacterStep", 1,
            new[] { new EngineDiagnostic("unresolved-character-controller-penetration", "Wedged.", "test") });
        Assert.Null(Moved());
        Assert.Equal(2, Notices().Length);
    }

    [Fact]
    public void One_trigger_request_carries_the_water_then_the_dungeon_actions_then_the_player()
    {
        using Stage stage = new();
        CharacterWaterVolume[] water = [new(7, new Vector3(-1f, -1f, -1f), Vector3.One)];
        CharacterStepEnvironment environment = new(default, ReadOnlyMemory<CharacterObstacle>.Empty, ReadOnlyMemory<CharacterMeshInstance>.Empty, water);
        SpatialEntityCollider action = SpatialMovementSystem.ProjectWaterCollider(new CharacterWaterVolume(9, new Vector3(4f, 0f, 4f), new Vector3(5f, 1f, 5f)));
        List<SpatialTriggerReconcileRequest> requests = [];
        stage.Spatial.TriggerResult = request => { requests.Add(request); return default; };

        stage.Movement.ReconcileTriggers(environment, 3, [action]);

        SpatialTriggerReconcileRequest sent = Assert.Single(requests);
        ulong[] entities = [.. sent.Entities.ToArray().Select(collider => collider.Entity)];
        Assert.Equal([7UL, 9UL, stage.Actors.Player.Actor.Entity.Value], entities);
    }

    private static DaggerfallPlayerStep Begun(Stage stage, ProductUpdateState update)
    {
        DaggerfallPlayerStep step = stage.Movement.ApplyControls(update, canCarry: true);
        stage.Movement.BeginLocomotion(ref step, update);
        return step;
    }

    private sealed class Effects : IDaggerfallMovementEffects
    {
        internal ActorControlRestrictions Restrictions { get; set; }
        internal bool Slowfall, Levitation, WaterWalking;
        public ActorControlRestrictions ControlsFor(long targetId) => Restrictions;
        public bool GrantsSlowfall(long targetId) => Slowfall;
        public bool EnhancesJumping(long targetId) => false;
        public bool GrantsWaterWalking(long targetId) => WaterWalking;
        public bool EnhancesClimbing(long targetId) => false;
        public bool GrantsLevitation(long targetId) => Levitation;
        public bool GrantsWaterBreathing(long targetId) => false;
    }

    private sealed class Surface : IDaggerfallWaterSurface
    {
        internal CharacterWaterVolume? Volume { get; set; }
        public CharacterWaterVolume? WaterAt(CharacterWaterVolume? volume, Vector3 position) => Volume;
    }

    private sealed class OnFoot : IDaggerfallRide
    {
        public bool IsOnFoot => true;
        public bool OnShip => false;
        public double MovementSpeed(double liveSpeed, double classicToEngineRatio) => liveSpeed;
    }

    private sealed class Stage : IDisposable
    {
        private readonly SpatialMovementSystem _spatial;

        internal Stage()
        {
            DaggerfallTuning tuning = DaggerfallTuning.Defaults;
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
            StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
                DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerfallActorFactory.PlayerCapabilities);
            DaggerfallCharacterState character = new(definitions, stats, player);
            List<string> releases = [];
            Spatial = SpatialFake.Create(new ContentSha256(1, 2, 3, 4), releases);
            _spatial = new SpatialMovementSystem(Spatial.Service, new ContentFake(releases), null, tuning.Spatial);
            Input = new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput);
            Movement = new DaggerfallPlayerMovement(Input, _spatial, new DaggerfallLocomotionPolicy(tuning.Locomotion, new DaggerfallControlSettings()),
                new DaggerfallClimbingPolicy(tuning.Climbing), new DaggerfallLevitationPolicy(), new DaggerfallSwimmingPolicy(tuning.Swimming), Control,
                Actors, character, new DaggerfallSkillUseReactions(Actors.Player.Progression, stats, definitions, () => character.Career),
                Effects, new OnFoot(), RandomMinimum.Create(), tuning.Locomotion, tuning.Time.GameSecondsPerRealSecond, Presentation);
        }

        internal ActorsState Actors { get; } = new();
        internal PlayerControlState Control { get; } = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        internal SpatialFake Spatial { get; }
        internal PlayerInputSystem Input { get; }
        internal Effects Effects { get; } = new();
        internal Surface Water { get; } = new();
        internal DaggerfallPlayerMovement Movement { get; }
        internal PresentationState Presentation { get; } = new("", 5d, 4);

        public void Dispose()
        {
            _spatial.Dispose();
            Actors.Dispose();
        }
    }
}
