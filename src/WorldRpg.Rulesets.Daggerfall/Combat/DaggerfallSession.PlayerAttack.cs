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
    private ActorReactions _reactions = null!;
    private int _hitStopSteps;
    // Whether the attack in progress started aimed at a civilian.
    private bool _attackHarmsCivilians;

    /// <summary>Who a blow is holding now.</summary>
    internal ActorReactions Reactions => _reactions;

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
        if ((State.WeaponDrawn || _attackClock.Casting) && !State.Effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId).PhysicalAttacks && !State.Actors.Player.IsDefeated)
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
        double speed = tuning.SpeedPerPointAbove50 * (PlayerStat(DaggerfallMechanicsIds.Speed.Value) - 50);
        double skill = attack == DaggerfallAttackIds.Cast ? 0d : tuning.SkillAt100 * Math.Clamp(PlayerStat(_combat.PlayerWeaponSkill()), 0, 100) / 100d;
        double scale = 1d - speed - skill;
        if (attack == DaggerfallAttackIds.PowerIn) scale -= tuning.StrengthPerPointAbove50 * (PlayerStat(DaggerfallMechanicsIds.Strength.Value) - 50);
        if (_combat.PlayerWinded) scale *= tuning.WindedTimeScale;
        scale *= _combat.PlayerRushTimeScale;
        return Math.Max(tuning.MinimumTimeScale, scale);
    }

    private int PlayerStat(string stat) => State.Actors.Player.Stats.GetStat(StatId.Parse(stat)).ValueInt;

    DaggerfallShotTuning? IDaggerfallPlayerAttacks.Shot => _combat.PlayerHoldsBow() ? _tuning.Attacks.Shot : null;

    bool IDaggerfallPlayerAttacks.ShotReady => _combat.InspectPlayerAttackRefusal() is null;

    /// <summary>
    /// Whether the view meets a civilian's body as an attack starts, allies and world aside. That one look decides for the
    /// attack's whole life: a player fighting beside civilians doesn't harm them, and one who means to harms any it meets.
    /// </summary>
    private bool AimedAtCivilian()
    {
        if (StrikePose(DaggerfallActorIdentity.PlayerEntityId) is not { } pose) return false;
        SegmentContact aim = _contact.Segment(DaggerfallActorIdentity.PlayerEntityId, pose.Eye, pose.Eye + pose.Forward * (float)_tuning.Attacks.AimRange,
            _sites?.Exterior.CharacterEnvironment(State.PlayerControl.Motion), IsPlayerAlly);
        return aim.ActorId is long met && IsCivilian(met);
    }

    private bool IsPlayerAlly(long actor) => EffectiveFoeTeam(actor) == "player-ally";

    private bool IsCivilian(long actor) => _roster.Definitions.TryGetValue(actor, out DaggerfallActorDefinition? definition) && definition.Kind == DaggerfallActorKinds.Civilian;

    /// <summary>Whether a player's attack goes through a body: always an ally, and a civilian unless it started aimed at one.</summary>
    internal bool PassesThrough(long actor, bool harmsCivilians) => IsPlayerAlly(actor) || !harmsCivilians && IsCivilian(actor);

    bool IDaggerfallPlayerAttacks.Start(string attack)
    {
        var (generation, simulationStep, seconds) = _attackStep;
        _attackHarmsCivilians = AimedAtCivilian();
        if (attack == DaggerfallAttackIds.Cast)
        {
            if (!ReadyCast()) return false;
            _attack = attack;
            StepGuard(0d, generation, simulationStep);
            return true;
        }
        LookReceipt look = _input.ResolveCurrentLook(State.PlayerControl);
        int chain = _tuning.Attacks.Combatant.ChainLength(PlayerStat(_combat.PlayerWeaponSkill()));
        _combat.SelectPlayerAttack(attack, finisher: attack == DaggerfallAttackIds.Fast[chain - 1], _attackHarmsCivilians);
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
        // A shot's flight decides what it meets, a cast meets nothing, and a player with no pose has nothing to sweep from.
        if (_attack is DaggerfallAttackIds.Shot or DaggerfallAttackIds.Cast || StrikePose(DaggerfallActorIdentity.PlayerEntityId) is not { } pose) return true;
        _lastSwing = _swing ??= new SwingSweep(_contact, DaggerfallActorIdentity.PlayerEntityId, _tuning.Attacks.Arc(_attack!, _combat.PlayerWeaponSkill(), _combat.PlayerWeaponTemplate()),
            passes: actor => PassesThrough(actor, _attackHarmsCivilians));
        if (_swing.Advance(pose.Eye, pose.Forward, (float)cycle, _sites?.Exterior.CharacterEnvironment(State.PlayerControl.Motion)) is not long struck) return false;
        _combat.SetSweptTarget(struck);
        return true;
    }

    void IDaggerfallPlayerAttacks.Impact()
    {
        if (_attack == DaggerfallAttackIds.Cast) { ReleaseCast(); return; }
        if (State.Actors.Player.Actor.Get<AttackState>().Pending is not { } pending) return;
        _playerImpacts.Add(new AttackImpactNotice(DaggerfallActorIdentity.PlayerEntityId, pending.Request.TargetId ?? 0,
            pending.Request.Generation, pending.Request.SimulationStep, Expired: false));
    }

    void IDaggerfallPlayerAttacks.Motion(string attack, DaggerfallAttackTiming? timing)
    {
        if (attack != DaggerfallAttackIds.Cast) _facts.Append(new PlayerAttackMotionFact(attack, timing, _attackStep.Generation, _attackStep.SimulationStep));
        else if (Casting.ReadyFor(DaggerfallActorIdentity.PlayerEntityId) is { } ready)
            _facts.Append(new PlayerCastMotionFact(_definitions.Magic.Spells[ready.SpellKey].Element, timing!.Value, _attackStep.Generation, _attackStep.SimulationStep));
    }

    void IDaggerfallPlayerAttacks.Rest() =>
        _facts.Append(new PlayerAttackRestFact(_attackStep.Generation, _attackStep.SimulationStep));
}
