using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Classic wall attachment and hold policy; the Engine still solves every character movement.</summary>
internal sealed class DaggerfallClimbingPolicy
{
    private static readonly StatId Climbing = StatId.Parse(DaggerfallMechanicsIds.Climbing.Value);
    private static readonly StatId Luck = StatId.Parse(DaggerfallMechanicsIds.Luck.Value);
    private static readonly TrackId Stamina = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);
    private readonly DaggerfallClimbingTuning _tuning;
    private float _startSeconds;
    private float _holdSeconds;
    private bool _attached;
    private bool _rappelling;
    private bool _slipping;
    private bool _restoredAttachmentPending;
    private long _operationSequence;
    private long _completedOperationSequence;

    internal DaggerfallClimbingPolicy(DaggerfallClimbingTuning tuning) => _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();

    internal bool IsAttached => _attached;

    /// <summary>Uses current Engine contact observations and physical intent; a check is recorded only after the ordinary Engine step is accepted.</summary>
    internal DaggerfallClimbStep BeginStep(DaggerfallLocomotionStep input, CharacterMotion motion,
        bool wallAhead, bool wallAtFeet, bool wallBehind, bool canMove, StatsComponent stats, bool khajiit,
        bool enhancedClimbing, float seconds, Func<int> rollHundred)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rollHundred);
        if (!float.IsFinite(seconds) || seconds <= 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!canMove || stats.GetTrack(Stamina).Current <= 0d || input.CrouchHeld || input.JumpRequested)
        {
            Detach();
            return default;
        }

        bool forward = input.ForwardIntent > 0f;
        bool backward = input.ForwardIntent < 0f;
        if (_restoredAttachmentPending)
        {
            _restoredAttachmentPending = false;
            if (_attached && ((_rappelling && wallBehind && backward) || (!_rappelling && (wallAhead || wallAtFeet) && forward)))
                return Step(0f, true, false, false);
        }
        if (_attached && _rappelling && forward && wallAhead) _rappelling = false;
        bool contact = _rappelling ? wallBehind : wallAhead || wallAtFeet;
        bool intent = _rappelling ? backward : forward;
        if (_attached && (!intent || !contact || (_slipping && motion.Grounded)))
        {
            Detach();
            return default;
        }

        float speed = Math.Max(0f, input.Controls.ForwardSpeed.GetValueOrDefault()) / _tuning.SpeedDivisor
            * (enhancedClimbing ? _tuning.EnhancedSpeedMultiplier : 1f);
        if (_attached)
        {
            if (_rappelling) return Step(-speed, true, false, true);
            _holdSeconds += seconds;
            bool checkedHold = false;
            float interval = _slipping ? _tuning.RegainCheckSeconds : _tuning.ContinueCheckSeconds;
            if (_holdSeconds >= interval)
            {
                _holdSeconds = 0f;
                checkedHold = true;
                _slipping = !PassCheck(_slipping ? _tuning.RegainBaseChance : _tuning.ContinueBaseChance,
                    stats, khajiit, enhancedClimbing, rollHundred);
            }
            return Step(_slipping ? null : speed, true, checkedHold, false);
        }

        // The donor's ledge backstep rappel attaches to a wall behind a falling player without a skill roll.
        if (backward && !motion.Grounded && motion.ControlledVelocity.Y < 0f && wallBehind)
        {
            _attached = true;
            _rappelling = true;
            _slipping = false;
            _holdSeconds = 0f;
            return Step(-speed, true, false, true);
        }

        if (!forward || !wallAhead)
        {
            _startSeconds = 0f;
            return default;
        }

        _startSeconds += seconds;
        if (_startSeconds < _tuning.StartCheckSeconds) return default;
        _startSeconds = 0f;
        bool success = PassCheck(motion.Grounded ? _tuning.StartBaseChance : _tuning.GraspBaseChance,
            stats, khajiit, enhancedClimbing, rollHundred);
        if (success)
        {
            _attached = true;
            _rappelling = false;
            _slipping = false;
            _holdSeconds = 0f;
        }
        return Step(success ? speed : null, success, true, false);
    }

    internal void CompleteStep(DaggerfallClimbStep step, CharacterStepReceipt? receipt, Action<DaggerfallSkillUse> record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (receipt is null || step.OperationId == 0 || step.OperationId <= _completedOperationSequence) return;
        _completedOperationSequence = step.OperationId;
        if (step.ChanceChecked)
            record(new DaggerfallSkillUse(DaggerfallMechanicsIds.Climbing.Value,
                DaggerfallSkillUseReason.ClimbingCheck, DaggerfallSkillUseOutcome.Attempted));
        if (step.Rappelling && receipt.Value.Displacement.Y < 0f)
            record(new DaggerfallSkillUse(DaggerfallMechanicsIds.Climbing.Value,
                DaggerfallSkillUseReason.Rappelling, DaggerfallSkillUseOutcome.Accepted));
        if (receipt.Value.Motion.Grounded && _slipping) Detach();
    }

    internal DaggerfallClimbingSave Capture() => new(_attached, _slipping, _startSeconds, _holdSeconds, _rappelling);

    internal void Restore(DaggerfallClimbingSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        _attached = saved.Attached;
        _slipping = saved.Slipping;
        _rappelling = saved.Rappelling;
        _startSeconds = saved.StartSeconds;
        _holdSeconds = saved.HoldSeconds;
        _restoredAttachmentPending = saved.Attached;
    }

    internal void Detach()
    {
        _attached = false;
        _slipping = false;
        _rappelling = false;
        _startSeconds = 0f;
        _holdSeconds = 0f;
        _restoredAttachmentPending = false;
    }

    private bool PassCheck(int baseChance, StatsComponent stats, bool khajiit, bool enhanced, Func<int> rollHundred)
    {
        int chance = DaggerfallFormulaPolicy.CalculateClimbingChance(stats.GetStat(Climbing).ValueInt,
            stats.GetStat(Luck).ValueInt, khajiit, enhanced, baseChance);
        int roll = rollHundred();
        if (roll is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(rollHundred));
        return roll <= chance;
    }

    private DaggerfallClimbStep Step(float? verticalVelocity, bool climbing, bool checkedChance, bool rappelling) =>
        new(verticalVelocity, climbing, checkedChance, rappelling, checked(++_operationSequence));
}

internal readonly record struct DaggerfallClimbStep(float? VerticalVelocity, bool Climbing, bool ChanceChecked, bool Rappelling, long OperationId);

internal sealed record DaggerfallClimbingSave(bool Attached, bool Slipping, float StartSeconds, float HoldSeconds, bool Rappelling = false)
{
    internal void Validate()
    {
        if (Slipping && !Attached || Rappelling && !Attached || Rappelling && Slipping
            || !float.IsFinite(StartSeconds) || StartSeconds < 0f
            || !float.IsFinite(HoldSeconds) || HoldSeconds < 0f)
            throw new ArgumentException("Saved climbing state is invalid.");
    }
}

/// <summary>Named donor climbing chances and intervals, in seconds and Engine world-speed units.</summary>
internal sealed record DaggerfallClimbingTuning(float StartCheckSeconds, float ContinueCheckSeconds,
    float RegainCheckSeconds, int StartBaseChance, int GraspBaseChance, int ContinueBaseChance,
    int RegainBaseChance, float SpeedDivisor, float EnhancedSpeedMultiplier)
{
    private const float ClassicSystemTickSeconds = .0549254f;
    internal static DaggerfallClimbingTuning Classic { get; } = new(
        14f * ClassicSystemTickSeconds, 15f * ClassicSystemTickSeconds, 5f * ClassicSystemTickSeconds,
        70, 40, 50, 20, 3f, 2f);

    internal DaggerfallClimbingTuning Validate()
    {
        if (!float.IsFinite(StartCheckSeconds) || StartCheckSeconds <= 0f
            || !float.IsFinite(ContinueCheckSeconds) || ContinueCheckSeconds <= 0f
            || !float.IsFinite(RegainCheckSeconds) || RegainCheckSeconds <= 0f
            || !float.IsFinite(SpeedDivisor) || SpeedDivisor <= 0f
            || !float.IsFinite(EnhancedSpeedMultiplier) || EnhancedSpeedMultiplier <= 0f
            || StartBaseChance is < 0 or > 100 || GraspBaseChance is < 0 or > 100
            || ContinueBaseChance is < 0 or > 100 || RegainBaseChance is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(DaggerfallClimbingTuning));
        return this;
    }
}
