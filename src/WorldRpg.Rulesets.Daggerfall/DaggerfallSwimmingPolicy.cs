using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Ruleset values for Engine-admitted swimming and the classic breath cadence.</summary>
internal sealed record DaggerfallSwimmingTuning(
    float Speed,
    float Acceleration,
    float Drag,
    float GravityScale,
    float Buoyancy,
    float BreathSecondsPerPoint)
{
    internal static DaggerfallSwimmingTuning Classic { get; } = new(2f, 12f, 4f, 0f, 1f, 19f / 60f);

    internal DaggerfallSwimmingTuning Validate()
    {
        if (!float.IsFinite(Speed) || Speed <= 0f) throw new ArgumentOutOfRangeException(nameof(Speed));
        if (!float.IsFinite(Acceleration) || Acceleration <= 0f) throw new ArgumentOutOfRangeException(nameof(Acceleration));
        if (!float.IsFinite(Drag) || Drag < 0f) throw new ArgumentOutOfRangeException(nameof(Drag));
        if (!float.IsFinite(GravityScale) || GravityScale < 0f) throw new ArgumentOutOfRangeException(nameof(GravityScale));
        if (!float.IsFinite(Buoyancy) || Buoyancy < 0f) throw new ArgumentOutOfRangeException(nameof(Buoyancy));
        if (!float.IsFinite(BreathSecondsPerPoint) || BreathSecondsPerPoint <= 0f) throw new ArgumentOutOfRangeException(nameof(BreathSecondsPerPoint));
        return this;
    }
}

/// <summary>Current swimming continuation, including the timer that may be saved mid-submersion.</summary>
internal sealed record DaggerfallSwimmingSave(
    bool Swimming,
    bool HeadSubmerged,
    int CurrentBreath,
    double BreathSeconds,
    ulong? ActiveWaterTrigger = null)
{
    internal static DaggerfallSwimmingSave Empty { get; } = new(false, false, 0, 0d);

    internal DaggerfallSwimmingSave Validate()
    {
        if (CurrentBreath < 0) throw new ArgumentOutOfRangeException(nameof(CurrentBreath));
        if (!double.IsFinite(BreathSeconds) || BreathSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(BreathSeconds));
        if (ActiveWaterTrigger is 0) throw new ArgumentOutOfRangeException(nameof(ActiveWaterTrigger));
        if (!Swimming && HeadSubmerged) throw new ArgumentException("A saved submerged state must be swimming.", nameof(HeadSubmerged));
        if (!Swimming && ActiveWaterTrigger is not null) throw new ArgumentException("An inactive saved swimmer cannot retain a water trigger.", nameof(ActiveWaterTrigger));
        return this;
    }
}

/// <summary>Accepted movement facts and consequence request produced by one swimming step.</summary>
internal readonly record struct DaggerfallSwimmingStep(
    bool Swimming,
    bool HeadSubmerged,
    bool Drowning,
    double GameSeconds)
{
    internal static DaggerfallSwimmingStep None => new(false, false, false, 0d);
}

/// <summary>
/// Daggerfall swimming policy over the Engine's movement facts and trigger admission. The policy
/// retains only continuation values that the ruleset owns; water geometry and overlap remain Engine
/// and world projection responsibilities.
/// </summary>
internal sealed class DaggerfallSwimmingPolicy
{
    private readonly DaggerfallSwimmingTuning _tuning;
    private ulong? _activeWaterTrigger;
    private bool _swimming;
    private bool _headSubmerged;
    private int _currentBreath;
    private double _breathSeconds;

    internal DaggerfallSwimmingPolicy(DaggerfallSwimmingTuning tuning) => _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();

    internal bool IsSwimming => _swimming;
    internal bool HeadSubmerged => _headSubmerged;
    internal int CurrentBreath => _currentBreath;
    internal ulong? ActiveWaterTrigger => _activeWaterTrigger;

    /// <summary>Applies Engine enter/exit facts to the current water identity without retaining geometry.</summary>
    internal void ObserveTriggers(ReadOnlySpan<SpatialTriggerFact> facts, ulong playerEntity, ReadOnlySpan<CharacterWaterVolume> volumes)
    {
        if (playerEntity == 0) throw new ArgumentOutOfRangeException(nameof(playerEntity));
        HashSet<ulong> admitted = [];
        foreach (CharacterWaterVolume volume in volumes)
            admitted.Add(volume.Validate().Trigger);

        foreach (SpatialTriggerFact fact in facts)
        {
            if (fact.Subject != playerEntity || !admitted.Contains(fact.Trigger)) continue;
            if (fact.Enter) _activeWaterTrigger = fact.Trigger;
            else if (_activeWaterTrigger == fact.Trigger) _activeWaterTrigger = null;
        }
        if (_activeWaterTrigger is ulong active && !admitted.Contains(active)) _activeWaterTrigger = null;
    }

    internal CharacterWaterVolume? ActiveVolume(ReadOnlySpan<CharacterWaterVolume> volumes)
    {
        if (_activeWaterTrigger is not ulong active) return null;
        foreach (CharacterWaterVolume volume in volumes)
            if (volume.Validate().Trigger == active) return volume;
        return null;
    }

    /// <summary>Builds one product movement request; Engine resolves capsule immersion and accepted mode.</summary>
    internal CharacterMovementRequest Movement(CharacterWaterVolume volume, float verticalIntent)
    {
        volume.Validate();
        if (!float.IsFinite(verticalIntent)) throw new ArgumentOutOfRangeException(nameof(verticalIntent));
        return new(
            CharacterMovementMode.Swimming,
            Math.Clamp(verticalIntent, -1f, 1f),
            _tuning.Speed,
            _tuning.Acceleration,
            _tuning.Drag,
            volume.Minimum,
            volume.Maximum,
            _tuning.GravityScale,
            _tuning.Buoyancy,
            0f);
    }

    /// <summary>
    /// Settles the accepted Engine movement. Breath is initialized only when the head first needs air,
    /// is advanced by elapsed admitted real seconds, and is reset when the head leaves the water. A
    /// terminal consequence is requested once when the cadence reaches zero.
    /// </summary>
    internal DaggerfallSwimmingStep Complete(CharacterStepReceipt? receipt, bool waterBreathing, int endurance,
        double realSeconds, double gameSeconds, long gameMinute, Action<DaggerfallSkillUse> recordSkillUse)
    {
        ArgumentNullException.ThrowIfNull(recordSkillUse);
        if (endurance < 0) throw new ArgumentOutOfRangeException(nameof(endurance));
        if (!double.IsFinite(realSeconds) || realSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(realSeconds));
        if (!double.IsFinite(gameSeconds) || gameSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(gameSeconds));
        if (receipt is not { } accepted) return DaggerfallSwimmingStep.None;

        _swimming = accepted.Movement.Mode == CharacterMovementMode.Swimming;
        _headSubmerged = _swimming && accepted.Movement.HeadSubmerged;
        if (_swimming)
        {
            recordSkillUse(new DaggerfallSkillUse(
                "swimming",
                DaggerfallSkillUseReason.Swimming,
                DaggerfallSkillUseOutcome.Accepted,
                gameMinute));
        }

        if (!_headSubmerged || waterBreathing)
        {
            // The donor keeps no stale breath pool after surfacing or while water breathing is active;
            // removing the effect underwater therefore starts a fresh admitted deep breath.
            _currentBreath = 0;
            _breathSeconds = 0d;
            return new DaggerfallSwimmingStep(_swimming, _headSubmerged, false, gameSeconds);
        }

        int maximumBreath = Math.Max(1, DaggerfallFormulaPolicy.MaxBreath(endurance));
        if (_currentBreath <= 0) _currentBreath = maximumBreath;
        _breathSeconds = checked(_breathSeconds + realSeconds);
        bool drowning = false;
        while (_breathSeconds >= _tuning.BreathSecondsPerPoint && _currentBreath > 0)
        {
            _breathSeconds -= _tuning.BreathSecondsPerPoint;
            _currentBreath--;
        }
        if (_currentBreath <= 0)
        {
            _currentBreath = 0;
            drowning = true;
        }
        return new DaggerfallSwimmingStep(_swimming, _headSubmerged, drowning, gameSeconds);
    }

    internal DaggerfallSwimmingSave Capture() => new DaggerfallSwimmingSave(
        _swimming,
        _headSubmerged,
        _currentBreath,
        _breathSeconds,
        _activeWaterTrigger).Validate();

    internal void Restore(DaggerfallSwimmingSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        _swimming = saved.Swimming;
        _headSubmerged = saved.HeadSubmerged;
        _currentBreath = saved.CurrentBreath;
        _breathSeconds = saved.BreathSeconds;
        _activeWaterTrigger = saved.ActiveWaterTrigger;
    }
}
