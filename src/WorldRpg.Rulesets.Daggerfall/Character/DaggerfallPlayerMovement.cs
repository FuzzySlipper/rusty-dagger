using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What the player's movement asks of the effects on them: the control restrictions and the movement magic. Magic's
/// effect lifecycle answers, when the step asks.
/// </summary>
internal interface IDaggerfallMovementEffects
{
    ActorControlRestrictions ControlsFor(long targetId);
    bool GrantsSlowfall(long targetId);
    bool EnhancesJumping(long targetId);
    bool GrantsWaterWalking(long targetId);
    bool EnhancesClimbing(long targetId);
    bool GrantsLevitation(long targetId);
    bool GrantsWaterBreathing(long targetId);
}

/// <summary>Where the water someone stands in really is: none past a river's banks. The exterior answers.</summary>
internal interface IDaggerfallWaterSurface
{
    CharacterWaterVolume? WaterAt(CharacterWaterVolume? volume, Vector3 position);
}

/// <summary>What one step's player movement carries from one call of the step's list to the next.</summary>
internal struct DaggerfallPlayerStep
{
    internal ActorControlRestrictions Restrictions;
    internal bool Alive;
    internal bool CanMove;
    internal bool Slowfall;
    internal DaggerfallLocomotionStep Locomotion;
    internal CharacterMotion MotionBefore;
    internal WorldPoint? PositionBefore;
    internal CharacterStepEnvironment Environment;
    internal CharacterWaterVolume? ActiveWater;
    internal bool WaterWalking;
    internal DaggerfallClimbStep Climb;
    internal DaggerfallLevitationStep Levitation;
    internal bool SlowfallDescending;
    internal bool ReleasedVerticalDrive;
    internal CharacterStepReceipt? Movement;
    internal bool WasSwimming;
    internal DaggerfallSwimmingStep Swimming;
    internal DaggerfallLanding? Landing;

    /// <summary>Whether the step ended in a hard landing the player hears: within the fall grace, out of water and not slowfalling.</summary>
    internal readonly bool HardLanding => Landing is { } landed && DaggerfallFormulaPolicy.IsHardLanding(landed.Distance)
        && !Swimming.Swimming && !SlowfallDescending;
}

/// <summary>
/// The player's movement in one simulation step: the observed input under the control restrictions, locomotion begun,
/// shaped by water, a wall to climb and levitation, constrained by the restrictions and slowfall, moved by the Engine,
/// then swimming, the landing and climbing settled against what the Engine accepted; and the step's one trigger
/// request's water and player part. The step's list orders these calls among the other domains' lines.
/// </summary>
internal sealed class DaggerfallPlayerMovement
{
    /// <summary>A wall the climb probes find is climbable only this close to vertical.</summary>
    internal const float ClimbWallNormalLimit = .06f;

    private static readonly TrackId Health = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly StatId Endurance = StatId.Parse(DaggerfallMechanicsIds.Endurance.Value);
    private readonly PlayerInputSystem _input;
    private readonly SpatialMovementSystem _spatial;
    private readonly DaggerfallLocomotionPolicy _locomotion;
    private readonly DaggerfallClimbingPolicy _climbing;
    private readonly DaggerfallLevitationPolicy _levitation;
    private readonly DaggerfallSwimmingPolicy _swimming;
    private readonly PlayerControlState _control;
    private readonly ActorsState _actors;
    private readonly DaggerfallCharacterState _character;
    private readonly IDaggerfallMovementEffects _effects;
    private readonly IDaggerfallRide _ride;
    private readonly IRandomService _random;
    private readonly DaggerfallLocomotionTuning _tuning;
    private readonly double _gameSecondsPerRealSecond;
    private readonly Action<DaggerfallSkillUse> _recordSkillUse;
    // The water's trigger colliders, kept while the environment keeps its water.
    private (ReadOnlyMemory<CharacterWaterVolume> Water, SpatialEntityCollider[] Colliders) _waterColliders = (ReadOnlyMemory<CharacterWaterVolume>.Empty, []);

    /// <param name="gameSecondsPerRealSecond">How much game time a real second of play passes (Time's rate).</param>
    internal DaggerfallPlayerMovement(PlayerInputSystem input, SpatialMovementSystem spatial, DaggerfallLocomotionPolicy locomotion,
        DaggerfallClimbingPolicy climbing, DaggerfallLevitationPolicy levitation, DaggerfallSwimmingPolicy swimming, PlayerControlState control,
        ActorsState actors, DaggerfallCharacterState character, DaggerfallSkillUseReactions skillUses, IDaggerfallMovementEffects effects,
        IDaggerfallRide ride, IRandomService random, DaggerfallLocomotionTuning tuning, double gameSecondsPerRealSecond)
    {
        _input = input;
        _spatial = spatial;
        _locomotion = locomotion;
        _climbing = climbing;
        _levitation = levitation;
        _swimming = swimming;
        _control = control;
        _actors = actors;
        _character = character;
        _effects = effects;
        _ride = ride;
        _random = random;
        _tuning = tuning;
        _gameSecondsPerRealSecond = gameSecondsPerRealSecond;
        _recordSkillUse = use => skillUses.Record(use);
    }

    /// <summary>The step's observed input under the player's control restrictions; a load too heavy to move or death stops the feet.</summary>
    /// <param name="canCarry">Whether the player can move under what they carry (Items' encumbrance).</param>
    internal DaggerfallPlayerStep ApplyControls(ProductUpdateState update, bool canCarry)
    {
        var restrictions = _effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId);
        _input.ApplyObserved(update, restrictions);
        // The Engine still receives an ordinary character step (grounding and gravity remain its
        // responsibility), but classic over-capacity removes planar intent before that proposal.
        bool alive = _actors.Player.Stats.GetTrack(Health).Current > 0d;
        if (!canCarry || !alive) update.PlanarIntent = Vector2.Zero;
        bool canMove = canCarry && alive && !restrictions.Movement;
        return new DaggerfallPlayerStep { Restrictions = restrictions, Alive = alive, CanMove = canMove };
    }

    /// <summary>The step's locomotion from its controls, and where the player stood before it.</summary>
    internal void BeginLocomotion(ref DaggerfallPlayerStep player, ProductUpdateState update)
    {
        player.Slowfall = _effects.GrantsSlowfall(DaggerfallActorIdentity.PlayerEntityId);
        player.Locomotion = _locomotion.BeginStep([], update.DeltaSeconds,
            _actors.Player.Stats, player.CanMove, _ride,
            _effects.EnhancesJumping(DaggerfallActorIdentity.PlayerEntityId), player.Slowfall);
        player.MotionBefore = _control.Motion;
        player.PositionBefore = _control.Position;
    }

    /// <summary>
    /// The water the player is in, a wall to climb and levitation, each reshaping the step's locomotion: a climb or
    /// levitation drives the vertical, levitation stops running, jumping and crouching, and water the player swims in
    /// replaces the controls with a swimming stroke.
    /// </summary>
    /// <param name="water">Where the water really is (the exterior's surface).</param>
    internal void ShapeLocomotion(ref DaggerfallPlayerStep player, ProductUpdateState update, ulong generation, ulong simulationStep,
        IDaggerfallWaterSurface water)
    {
        player.ActiveWater = player.PositionBefore is { } waterPoint
            ? water.WaterAt(_swimming.ActiveVolume(player.Environment.WaterVolumes.Span), waterPoint.ToVector())
            : _swimming.ActiveVolume(player.Environment.WaterVolumes.Span);
        player.WaterWalking = _effects.GrantsWaterWalking(DaggerfallActorIdentity.PlayerEntityId);
        bool wallAhead = _spatial.TryProbeClimbWall(_control, CharacterWallProbeDirection.Forward, out SpatialHit forwardHit, player.Environment)
            && MathF.Abs(forwardHit.Normal.Y) <= ClimbWallNormalLimit;
        bool wallAtFeet = _climbing.IsAttached
            && _spatial.TryProbeClimbWallAtFeet(_control, CharacterWallProbeDirection.Forward, out SpatialHit footHit, player.Environment)
            && MathF.Abs(footHit.Normal.Y) <= ClimbWallNormalLimit;
        bool wallBehind = !player.MotionBefore.Grounded && player.Locomotion.ForwardIntent < 0f
            && _spatial.TryProbeClimbWall(_control, CharacterWallProbeDirection.Backward, out SpatialHit rearHit, player.Environment)
            && MathF.Abs(rearHit.Normal.Y) <= ClimbWallNormalLimit;
        player.Climb = _climbing.BeginStep(player.Locomotion, player.MotionBefore, wallAhead, wallAtFeet, wallBehind,
            player.CanMove && _ride.IsOnFoot && player.ActiveWater is null,
            _actors.Player.Stats, _character.Race.Id == "khajiit",
            _effects.EnhancesClimbing(DaggerfallActorIdentity.PlayerEntityId),
            update.DeltaSeconds, () => checked((int)_random.DrawKeyed(new KeyedRngRequest(
                DaggerfallRandomKey.Seed, "daggerfall.climbing.v1", $"generation:{generation}:step:{simulationStep}", 1, 100)).Value));
        player.Levitation = _levitation.Resolve(new DaggerfallLevitationContext(
            _effects.GrantsLevitation(DaggerfallActorIdentity.PlayerEntityId),
            Swimming: _swimming.IsSwimming && !player.WaterWalking,
            Climbing: player.Climb.Climbing,
            CanMove: player.CanMove,
            UpHeld: player.Locomotion.UpHeld,
            DownHeld: player.Locomotion.DownHeld,
            VerticalSpeed: _tuning.LevitationVerticalSpeed));
        player.Locomotion = player.Locomotion with
        {
            Controls = player.Locomotion.Controls with
            {
                VerticalVelocity = player.Climb.VerticalVelocity ?? player.Levitation.VerticalVelocity,
                CrouchRequested = !player.Levitation.IsLevitating && player.Locomotion.Controls.CrouchRequested,
            },
            Climbing = player.Climb.Climbing,
            Running = !player.Levitation.IsLevitating && player.Locomotion.Running,
            JumpRequested = !player.Levitation.IsLevitating && player.Locomotion.JumpRequested,
        };
        if (player.ActiveWater is { } active && !player.WaterWalking && !player.Climb.Climbing && player.CanMove && !_ride.OnShip)
        {
            float verticalIntent = player.Locomotion.UpHeld ? 1f : player.Locomotion.DownHeld ? -1f : 0f;
            player.Locomotion = player.Locomotion with
            {
                Controls = player.Locomotion.Controls with
                {
                    Movement = _swimming.Movement(active, verticalIntent),
                    VerticalVelocity = null,
                    JumpPressed = false,
                    JumpHeld = false,
                    CrouchRequested = false,
                },
                Running = false,
                JumpRequested = false,
            };
        }
    }

    /// <summary>The player's control restrictions on the step's locomotion, then slowfall's descent.</summary>
    internal void ConstrainLocomotion(ref DaggerfallPlayerStep player)
    {
        player.Locomotion = player.Locomotion with { Controls = player.Restrictions.Restrict(player.Locomotion.Controls) };
        // Slowfall is a passive descent constraint, not voluntary movement. Engine still owns
        // the accepted pose and collision. Climb, levitation and water support take precedence.
        player.SlowfallDescending = player.Slowfall && !player.MotionBefore.Grounded && player.MotionBefore.ControlledVelocity.Y <= 0f
            && player.ActiveWater is null && !player.Climb.Climbing && !player.Levitation.IsLevitating;
        if (player.SlowfallDescending)
            player.Locomotion = player.Locomotion with { Controls = player.Locomotion.Controls with { VerticalVelocity = -_tuning.SlowfallDescentSpeed } };
        player.ReleasedVerticalDrive = _spatial.IsVerticalDriven && !player.Locomotion.Controls.VerticalVelocity.HasValue;
    }

    /// <summary>The Engine's character step over the shaped controls.</summary>
    internal void Move(ref DaggerfallPlayerStep player, ProductUpdateState update) =>
        player.Movement = _spatial.Step(_control, update, player.Environment, player.Locomotion.Controls);

    /// <summary>
    /// Swimming, the landing and climbing settle against the movement the Engine accepted. A fall measured after a
    /// released vertical drive or a slowfall descent starts where the drive let go.
    /// </summary>
    /// <param name="minuteNow">The calendar's minute now, for swimming when the update gave the step none.</param>
    internal void CompleteLocomotion(ref DaggerfallPlayerStep player, ProductUpdateState update, long? swimmingMinuteBefore,
        long? swimmingMinuteAfter, long minuteNow)
    {
        CharacterMotion landingBefore = (player.ReleasedVerticalDrive || player.SlowfallDescending) && player.PositionBefore is WorldPoint releasePosition
            ? player.MotionBefore with { PeakY = releasePosition.Y, FallOriginY = releasePosition.Y }
            : player.MotionBefore;
        player.WasSwimming = _swimming.IsSwimming;
        player.Swimming = player.Movement is { } movement
            ? _swimming.Complete(
                movement,
                _effects.GrantsWaterBreathing(DaggerfallActorIdentity.PlayerEntityId),
                _actors.Player.Stats.GetStat(Endurance).ValueInt,
                update.DeltaSeconds,
                update.DeltaSeconds * _gameSecondsPerRealSecond,
                swimmingMinuteAfter ?? minuteNow,
                _recordSkillUse, swimmingMinuteBefore)
            : DaggerfallSwimmingStep.None;
        player.Landing = _locomotion.CompleteStep(player.Locomotion, landingBefore, player.Movement,
            update.DeltaSeconds * _gameSecondsPerRealSecond, _actors.Player.Stats,
            _recordSkillUse, player.Swimming.Swimming);
        _climbing.CompleteStep(player.Climb, player.Movement, _recordSkillUse);
    }

    /// <summary>
    /// The step's one trigger reconcile. Engine replaces its whole trigger geometry and overlap set on every reconcile,
    /// so water volumes, the active dungeon action triggers and the player go into one request: a request missing either
    /// family would drop its rows (actor navigation then finds no water) and report false exits and re-entries. The
    /// player's swimming reads the overlap facts here; the caller hands the result to the dungeon actions.
    /// </summary>
    /// <param name="actionColliders">The active dungeon actions' trigger colliders (Interiors'), read as the request is built.</param>
    internal SpatialTriggerReconcileResult ReconcileTriggers(CharacterStepEnvironment environment, ulong simulationStep,
        IEnumerable<SpatialEntityCollider> actionColliders)
    {
        Rusty.Engine.Entities.EntityId player = _actors.Player.Actor.Entity;
        if (!_waterColliders.Water.Equals(environment.WaterVolumes))
            _waterColliders = (environment.WaterVolumes, [.. environment.WaterVolumes.ToArray().Select(SpatialMovementSystem.ProjectWaterCollider)]);
        SpatialEntityCollider[] triggers =
        [
            .. _waterColliders.Colliders,
            .. actionColliders,
            _spatial.ProjectCharacterCollider(_control, player.Value),
        ];
        SpatialTriggerReconcileResult reconciliation = _spatial.ReconcileTriggers(simulationStep, triggers);
        _swimming.ObserveTriggers(reconciliation.Facts.Span, player.Value, environment.WaterVolumes.Span);
        return reconciliation;
    }
}
