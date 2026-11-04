using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The session's side of the player's attack clock: admission, the blow and the facts presentation follows.</summary>
internal sealed partial class DaggerfallSession : IDaggerfallPlayerAttacks
{
    private DaggerfallPlayerAttackClock _attackClock = null!;
    private DaggerfallPlayerStamina _stamina = null!;
    private ActorReactions _reactions = null!;
    private int _hitStopSteps;

    /// <summary>Whether a landed blow is catching world time now.</summary>
    internal bool HitStopped => _hitStopSteps > 0;

    /// <summary>A body the player threw has landed.</summary>
    private void ThrownLanding(ActorThrowLanding landing)
    {
        if (State.Actors.TryGet(landing.ActorId, out ActorState thrown)
            && _vitality.ResolveThrownLanding(State.Actors.Player.Actor, thrown.Actor, landing.Distance) is { } fall)
            AppendDamage(fall, DaggerfallDamageCause.Fall, 0);
    }

    /// <summary>
    /// The blow's weight on screen: the view knocks and world time catches for an instant. A world some other owner has
    /// slowed or held, such as a playtest's, is left alone.
    /// </summary>
    private void FeelStrike(ActorStruckFact struck)
    {
        DaggerfallStrikeTuning tuning = _tuning.Attacks.Strike;
        _camera.Punch(-(struck.Heavy ? tuning.HeavyPunchDegrees : tuning.PunchDegrees), 0d);
        if (_engine.GameplayTime.Read().Rate == 1d) _hitStopSteps = struck.Heavy ? tuning.HeavyHitStopSteps : tuning.HitStopSteps;
    }
    private readonly List<AttackImpactNotice> _playerImpacts = [];
    private (ulong Generation, ulong SimulationStep, double Seconds) _attackStep;
    private string? _attack;
    private SwingSweep? _swing;
    // The last swing's sweep, for the developer overlay.
    private SwingSweep? _lastSwing;

    /// <summary>Advances the player's attack by one admitted step, retiring it when the player can no longer fight.</summary>
    private void StepPlayerAttack(double seconds, ulong generation, ulong simulationStep)
    {
        _attackStep = (generation, simulationStep, seconds);
        if (State.WeaponDrawn && !State.Effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId).PhysicalAttacks && !State.Actors.Player.IsDefeated)
            _attackClock.Step(seconds);
        else RetirePlayerAttack(generation);
    }

    private void RetirePlayerAttack(ulong generation)
    {
        if (!_attackClock.Busy) return;
        _attackClock.Cancel();
        State.Kit.Attacks.InterruptPendingAttack(DaggerfallActorIdentity.PlayerEntityId, generation);
    }

    string IDaggerfallPlayerAttacks.WeaponSkill => _combat.PlayerWeaponSkill();

    int IDaggerfallPlayerAttacks.ChainLength => _tuning.Attacks.Combatant.ChainLength(PlayerStat(_combat.PlayerWeaponSkill()));

    bool IDaggerfallPlayerAttacks.Winded => _combat.PlayerWinded;

    double IDaggerfallPlayerAttacks.TimeScale(string attack)
    {
        DaggerfallCombatantTuning tuning = _tuning.Attacks.Combatant;
        // A bow's shot already runs on the classic frame time, which Speed sets.
        double speed = attack == DaggerfallAttackIds.Shot ? 0d : tuning.SpeedPerPointAbove50 * (PlayerStat(DaggerfallMechanicsIds.Speed.Value) - 50);
        double scale = 1d - speed
            - tuning.SkillAt100 * Math.Clamp(PlayerStat(_combat.PlayerWeaponSkill()), 0, 100) / 100d;
        if (attack == DaggerfallAttackIds.PowerIn) scale -= tuning.StrengthPerPointAbove50 * (PlayerStat(DaggerfallMechanicsIds.Strength.Value) - 50);
        if (_combat.PlayerWinded) scale *= tuning.WindedTimeScale;
        scale *= _combat.PlayerRushTimeScale;
        return Math.Max(tuning.MinimumTimeScale, scale);
    }

    private int PlayerStat(string stat) => State.Actors.Player.Stats.GetStat(StatId.Parse(stat)).ValueInt;

    DaggerfallAttackTiming? IDaggerfallPlayerAttacks.ShotTiming
    {
        get
        {
            if (!_combat.PlayerHoldsBow()) return null;
            double frame = _combat.InspectPlayerTiming().FrameSeconds;
            return new(DaggerfallFormulaPolicy.BowWeaponHitFrame * frame, frame, frame);
        }
    }

    bool IDaggerfallPlayerAttacks.Start(string attack)
    {
        var (generation, simulationStep, seconds) = _attackStep;
        LookReceipt look = _input.ResolveCurrentLook(State.PlayerControl);
        int chain = _tuning.Attacks.Combatant.ChainLength(PlayerStat(_combat.PlayerWeaponSkill()));
        _combat.SelectPlayerAttack(attack, finisher: attack == DaggerfallAttackIds.Fast[chain - 1]);
        _attack = attack;
        _swing = null;
        State.Kit.Attacks.TryPlayerMelee(State.PlayerControl, look, generation, simulationStep, seconds, _facts);
        if (State.Actors.Player.Actor.Get<AttackState>().Pending?.Request.SimulationStep != simulationStep) return false;
        // A swing admitted here lowers a raised guard before anything resolves against it.
        StepGuard(0d, generation, simulationStep);
        // WeaponManager sends Attack to an action on the environment after the ordinary hit query. The safe
        // Engine hit reports a static surface rather than a source id, so the normalized placement resolver
        // is the product-side identity join.
        _ = TryTriggerDungeonActionRay(look.Forward, _tuning.MeleeTargeting.MaximumDistance, DaggerfallDungeonActionEvent.Attack);
        TryBashDoorWithSwing(look);
        return true;
    }

    bool IDaggerfallPlayerAttacks.Sweep(double cycle)
    {
        // A shot's flight decides what it meets, and a player with no pose has nothing to sweep from.
        if (_attack == DaggerfallAttackIds.Shot || StrikePose(DaggerfallActorIdentity.PlayerEntityId) is not { } pose) return true;
        _lastSwing = _swing ??= new SwingSweep(_contact, DaggerfallActorIdentity.PlayerEntityId, _tuning.Attacks.Arc(_attack!, _combat.PlayerWeaponSkill(), _combat.PlayerWeaponTemplate()));
        if (_swing.Advance(pose.Eye, pose.Forward, (float)cycle, _sites?.CharacterEnvironment(State.PlayerControl.Motion)) is not long struck) return false;
        _combat.SetSweptTarget(struck);
        return true;
    }

    void IDaggerfallPlayerAttacks.Impact()
    {
        if (State.Actors.Player.Actor.Get<AttackState>().Pending is not { } pending) return;
        _playerImpacts.Add(new AttackImpactNotice(DaggerfallActorIdentity.PlayerEntityId, pending.Request.TargetId ?? 0,
            pending.Request.Generation, pending.Request.SimulationStep, Expired: false));
    }

    void IDaggerfallPlayerAttacks.Motion(string attack, DaggerfallAttackTiming? timing) =>
        _facts.Append(new PlayerAttackMotionFact(attack, timing, _attackStep.Generation, _attackStep.SimulationStep));

    void IDaggerfallPlayerAttacks.Rest() =>
        _facts.Append(new PlayerAttackRestFact(_attackStep.Generation, _attackStep.SimulationStep));
}
