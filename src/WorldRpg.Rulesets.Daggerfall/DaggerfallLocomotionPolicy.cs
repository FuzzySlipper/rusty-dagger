using Rusty.Engine;
using Rusty.Engine.Input;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Ruleset-owned classic walk, run, crouch and fatigue policy over Engine character-controller receipts.</summary>
internal sealed class DaggerfallLocomotionPolicy
{
    private static readonly StatId Speed = StatId.Parse(DaggerfallMechanicsIds.Speed.Value);
    private static readonly StatId Running = StatId.Parse(DaggerfallMechanicsIds.Running.Value);
    private static readonly TrackId Stamina = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);
    private readonly DaggerfallLocomotionTuning _tuning;
    private FpsInput _input;
    private bool _jumpInFlight;
    private double _runningGameSeconds;

    internal DaggerfallLocomotionPolicy(DaggerfallLocomotionTuning tuning, DaggerfallControlSettings controls)
    {
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _input = CreateInput(controls ?? throw new ArgumentNullException(nameof(controls)));
    }

    /// <summary>Starts reading the newly selected physical keys with no retained movement from the former binding set.</summary>
    internal void Rebind(DaggerfallControlSettings controls) => _input = CreateInput(controls ?? throw new ArgumentNullException(nameof(controls)));

    /// <summary>Focus and product-mode changes must not leave an unseen physical release driving a later movement step.</summary>
    internal void Neutralize() => _input.Physical.Clear();

    /// <summary>Builds one Engine command from the current physical controls and live player mechanics.</summary>
    internal DaggerfallLocomotionStep BeginStep(ReadOnlySpan<ProductInputEvent> inputs, float seconds, StatsComponent stats, bool canMove)
    {
        ArgumentNullException.ThrowIfNull(stats);
        FpsInputFrame frame = _input.Consume(inputs, seconds);
        Track stamina = stats.GetTrack(Stamina);
        bool hasJumpFatigue = stamina.Current >= _tuning.JumpFatigueCost;
        bool running = canMove && stamina.Current > 0d && frame.SprintHeld;
        bool crouching = !running && frame.CrouchHeld;
        int speed = stats.GetStat(Speed).ValueInt;
        int runningSkill = stats.GetStat(Running).ValueInt;
        int jumpingSkill = stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Jumping.Value)).ValueInt;
        float groundSpeed = running ? RunSpeed(speed, runningSkill) : crouching ? CrouchSpeed(speed) : WalkSpeed(speed);
        CharacterStepControls controls = new(
            JumpPressed: canMove && hasJumpFatigue && frame.JumpPressed,
            JumpHeld: canMove && hasJumpFatigue && frame.JumpHeld,
            CrouchRequested: crouching,
            ForwardSpeed: groundSpeed,
            BackwardSpeed: groundSpeed,
            StrafeSpeed: groundSpeed,
            JumpSpeed: JumpSpeed(jumpingSkill, crouching));
        return new(controls, running, canMove && hasJumpFatigue && (frame.JumpPressed || frame.JumpHeld));
    }

    /// <summary>Charges and records only accepted Engine motion, never an input request that Engine kept grounded or blocked.</summary>
    internal DaggerfallLanding? CompleteStep(DaggerfallLocomotionStep step, CharacterMotion before, CharacterStepReceipt? receipt, double gameSeconds, StatsComponent stats, Action<DaggerfallSkillUse> recordSkillUse)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(recordSkillUse);
        if (!double.IsFinite(gameSeconds) || gameSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(gameSeconds));
        if (receipt is not { } accepted) return null;

        bool moved = accepted.Displacement.X != 0f || accepted.Displacement.Z != 0f;
        if (step.Running && moved)
        {
            _runningGameSeconds += gameSeconds;
            recordSkillUse(new DaggerfallSkillUse(DaggerfallMechanicsIds.Running.Value, DaggerfallSkillUseReason.Running, DaggerfallSkillUseOutcome.Accepted));
        }

        bool rising = accepted.Motion.ControlledVelocity.Y > 0f && before.ControlledVelocity.Y <= 0f;
        if (!_jumpInFlight && step.JumpRequested && rising)
        {
            Track stamina = stats.GetTrack(Stamina);
            if (stamina.TrySpend(_tuning.JumpFatigueCost))
            {
                _jumpInFlight = true;
                recordSkillUse(new DaggerfallSkillUse(DaggerfallMechanicsIds.Jumping.Value, DaggerfallSkillUseReason.Jumping, DaggerfallSkillUseOutcome.Accepted));
            }
        }
        if (accepted.Motion.Grounded) _jumpInFlight = false;
        return !before.Grounded && accepted.Motion.Grounded && before.PeakY > accepted.Transform.Translation.Y
            ? new DaggerfallLanding(before.PeakY - accepted.Transform.Translation.Y)
            : null;
    }

    /// <summary>Consumes each calendar minute exactly once; the session calendar remains the only time authority.</summary>
    internal void AdvanceCalendarMinutes(long before, long after, StatsComponent stats)
    {
        if (after < before) throw new ArgumentOutOfRangeException(nameof(after));
        if (after == before) return;
        ArgumentNullException.ThrowIfNull(stats);
        Track stamina = stats.GetTrack(Stamina);
        long minutes = checked(after - before);
        long runningMinutes = Math.Min(minutes, checked((long)Math.Ceiling(_runningGameSeconds / DaggerfallCalendar.SecondsPerMinute)));
        long idleMinutes = checked(minutes - runningMinutes);
        double fatigue = checked((runningMinutes * (long)_tuning.RunningFatiguePerGameMinute) + (idleMinutes * (long)_tuning.IdleFatiguePerGameMinute));
        _ = stamina.Spend(Math.Min(stamina.Current, fatigue));
        _runningGameSeconds = 0d;
    }

    internal float WalkSpeed(int liveSpeed)
    {
        float drag = .5f * (100f - Math.Max(_tuning.MinimumWalkSpeedAttribute, liveSpeed));
        return (liveSpeed + _tuning.WalkBase - drag) / _tuning.ClassicToEngineSpeedRatio;
    }

    internal float CrouchSpeed(int liveSpeed) => (liveSpeed + _tuning.CrouchBase) / _tuning.ClassicToEngineSpeedRatio;

    internal float RunSpeed(int liveSpeed, int runningSkill) =>
        ((liveSpeed + _tuning.WalkBase) / _tuning.ClassicToEngineSpeedRatio)
        * (_tuning.RunBaseMultiplier + (runningSkill / _tuning.RunningSkillDivisor));

    internal float JumpSpeed(int jumpingSkill, bool crouching)
    {
        float speed = _tuning.JumpBaseSpeed * (1f + ((jumpingSkill * _tuning.JumpSkillMultiplier) / 100f));
        return crouching ? speed * _tuning.CrouchedJumpMultiplier : speed;
    }

    internal DaggerfallLocomotionSave Capture() => new(_runningGameSeconds);

    internal void Restore(DaggerfallLocomotionSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        _runningGameSeconds = saved.RunningGameSeconds;
        _jumpInFlight = false;
    }

    private static FpsInput CreateInput(DaggerfallControlSettings controls)
    {
        KeyboardControl Key(string action) => controls.KeysFor(action)
            .Select(key => Enum.TryParse(key, out KeyboardControl control) ? control : KeyboardControl.None)
            .FirstOrDefault(control => control != KeyboardControl.None);
        FpsInputBindings selected = FpsInputBindings.Standard with
        {
            ForwardKey = Key("move.forward"), BackwardKey = Key("move.backward"),
            LeftKey = Key("move.left"), RightKey = Key("move.right"),
            JumpKey = Key("jump"), CrouchKey = Key("crouch"), SprintKey = Key("run"),
        };
        return new FpsInput(FpsInputConfig.Standard with { Bindings = selected });
    }
}

internal readonly record struct DaggerfallLocomotionStep(CharacterStepControls Controls, bool Running, bool JumpRequested);

/// <summary>
/// One actual airborne-to-supported transition reported by the Engine character controller.
/// The peak belongs to the Engine continuation, so an unloaded session cannot manufacture a fall.
/// </summary>
internal readonly record struct DaggerfallLanding(float Distance)
{
    internal DaggerfallLanding Validate() => float.IsFinite(Distance) && Distance >= 0f
        ? this
        : throw new ArgumentOutOfRangeException(nameof(Distance));
}

/// <summary>Classic authored movement constants retained as one validated ruleset tuning handle.</summary>
internal sealed record DaggerfallLocomotionTuning(
    float ClassicToEngineSpeedRatio,
    float WalkBase,
    float CrouchBase,
    float RunBaseMultiplier,
    float RunningSkillDivisor,
    int MinimumWalkSpeedAttribute,
    int IdleFatiguePerGameMinute,
    int RunningFatiguePerGameMinute,
    int JumpFatigueCost,
    float JumpBaseSpeed,
    float JumpSkillMultiplier,
    float CrouchedJumpMultiplier)
{
    internal static DaggerfallLocomotionTuning Classic { get; } = new(39.5f, 150f, 50f, 1.35f, 200f, 30, 11, 88, 11, 4.5f, .5f, .8f);

    internal DaggerfallLocomotionTuning Validate()
    {
        if (!float.IsFinite(ClassicToEngineSpeedRatio) || ClassicToEngineSpeedRatio <= 0f) throw new ArgumentOutOfRangeException(nameof(ClassicToEngineSpeedRatio));
        if (!float.IsFinite(WalkBase) || !float.IsFinite(CrouchBase) || !float.IsFinite(RunBaseMultiplier) || !float.IsFinite(RunningSkillDivisor)
            || !float.IsFinite(JumpBaseSpeed) || !float.IsFinite(JumpSkillMultiplier) || !float.IsFinite(CrouchedJumpMultiplier)
            || RunningSkillDivisor <= 0f || RunBaseMultiplier <= 0f || JumpBaseSpeed <= 0f || JumpSkillMultiplier < 0f || CrouchedJumpMultiplier <= 0f) throw new ArgumentOutOfRangeException(nameof(WalkBase));
        if (MinimumWalkSpeedAttribute < 0 || IdleFatiguePerGameMinute < 0 || RunningFatiguePerGameMinute < 0 || JumpFatigueCost <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumWalkSpeedAttribute));
        return this;
    }
}

/// <summary>Durable movement work awaiting the next calendar-minute fatigue charge; physical held input is deliberately not saved.</summary>
internal sealed record DaggerfallLocomotionSave(double RunningGameSeconds)
{
    internal void Validate()
    {
        if (!double.IsFinite(RunningGameSeconds) || RunningGameSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(RunningGameSeconds));
    }
}
