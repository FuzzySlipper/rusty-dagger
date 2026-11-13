using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The player's attack around its clock: what the clock asks (the weapon's skill, the chain, the pace, the shot), the
/// step that advances or retires it, the guard's step, admission, the swing's sweep, the blow, and the motion facts
/// presentation follows. A cast is released through Magic's spell release.
/// </summary>
/// <param name="look">The player's look now.</param>
/// <param name="weaponDrawn">Whether the player's weapon is drawn.</param>
/// <param name="environment">The world environment contact queries run against now.</param>
/// <param name="reportCast">How a cast outcome is told to the player.</param>
/// <param name="strikeWorld">A swing admitted along a look meets the world: the environment's actions and doors.</param>
internal sealed class DaggerfallPlayerAttack(DaggerfallAttackTuning tuning, DaggerCombatRules combat, ActorsState actors,
    PlayerControlState control, Func<LookReceipt> look, Func<bool> weaponDrawn, DaggerfallEffectLifecycle effects,
    IAttackCapabilities<IProductFact> attacks, DaggerfallContactGuard guard, DaggerfallStrikeAim aim, ActorContact contact,
    Func<CharacterStepEnvironment?> environment, DaggerfallCasting casting, DaggerfallSpellRelease spellRelease,
    DaggerfallDefinitions definitions, FactBuffer<IProductFact> facts, Action<DaggerfallCastOutcome> reportCast,
    Action<LookReceipt> strikeWorld) : IDaggerfallPlayerAttacks
{
    private DaggerfallPlayerAttackClock? _clock;
    private readonly List<AttackImpactNotice> _impacts = [];
    private (ulong Generation, ulong SimulationStep, double Seconds) _step;
    private string? _attack;
    private SwingSweep? _swing;
    // Whether the attack in progress started aimed at a civilian.
    private bool _harmsCivilians;

    /// <summary>The attack clock this owner answers.</summary>
    internal DaggerfallPlayerAttackClock Clock => _clock ??= new(tuning, this);

    /// <summary>The last swing's sweep, for the developer overlay.</summary>
    internal SwingSweep? LastSwing { get; private set; }

    /// <summary>Advances the player's attack by one admitted step, retiring it when the player can no longer fight.</summary>
    internal void Step(double seconds, ulong generation, ulong simulationStep)
    {
        _step = (generation, simulationStep, seconds);
        if ((weaponDrawn() || Clock.Casting) && !effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId).PhysicalAttacks && !actors.Player.IsDefeated)
            Clock.Step(seconds);
        else Retire(generation);
    }

    /// <summary>Ends an attack in progress, interrupting its pending blow under the generation that admitted it.</summary>
    internal void Retire(ulong generation)
    {
        if (!Clock.Busy) return;
        Clock.Cancel();
        attacks.InterruptPendingAttack(DaggerfallActorIdentity.PlayerEntityId, generation);
    }

    /// <summary>
    /// Advances the player's guard by <paramref name="seconds"/>. An attack keeps the guard down from its
    /// press until the clock has finished its motion.
    /// </summary>
    internal void StepGuard(double seconds, ulong generation, ulong simulationStep) => guard.Step(seconds,
        canGuard: !combat.PlayerWinded && weaponDrawn() && !effects.ControlsFor(DaggerfallActorIdentity.PlayerEntityId).PhysicalAttacks && !actors.Player.IsDefeated,
        swinging: actors.Player.Actor.Get<AttackState>().Pending is not null || Clock.Busy,
        generation, simulationStep, facts);

    /// <summary>The blows the player's attacks reached since the last call, for the impacts to land.</summary>
    internal AttackImpactNotice[] TakeImpacts()
    {
        AttackImpactNotice[] taken = [.. _impacts];
        _impacts.Clear();
        return taken;
    }

    string IDaggerfallPlayerAttacks.WeaponSkill => combat.PlayerWeaponSkill();

    int IDaggerfallPlayerAttacks.ChainLength => tuning.Combatant.ChainLength(PlayerStat(combat.PlayerWeaponSkill()));

    bool IDaggerfallPlayerAttacks.Winded => combat.PlayerWinded;

    double IDaggerfallPlayerAttacks.TimeScale(string attack)
    {
        DaggerfallCombatantTuning combatant = tuning.Combatant;
        double speed = combatant.SpeedPerPointAbove50 * (PlayerStat(DaggerfallMechanicsIds.Speed.Value) - 50);
        double skill = attack == DaggerfallAttackIds.Cast ? 0d : combatant.SkillAt100 * Math.Clamp(PlayerStat(combat.PlayerWeaponSkill()), 0, 100) / 100d;
        double scale = 1d - speed - skill;
        if (attack == DaggerfallAttackIds.PowerIn) scale -= combatant.StrengthPerPointAbove50 * (PlayerStat(DaggerfallMechanicsIds.Strength.Value) - 50);
        if (combat.PlayerWinded) scale *= combatant.WindedTimeScale;
        scale *= combat.PlayerRushTimeScale;
        return Math.Max(combatant.MinimumTimeScale, scale);
    }

    private int PlayerStat(string stat) => actors.Player.Stats.GetStat(StatId.Parse(stat)).ValueInt;

    DaggerfallShotTuning? IDaggerfallPlayerAttacks.Shot => combat.PlayerHoldsBow() ? tuning.Shot : null;

    bool IDaggerfallPlayerAttacks.ShotReady => combat.InspectPlayerAttackRefusal() is null;

    bool IDaggerfallPlayerAttacks.Start(string attack)
    {
        var (generation, simulationStep, seconds) = _step;
        _harmsCivilians = aim.AimedAtCivilian();
        if (attack == DaggerfallAttackIds.Cast)
        {
            if (!spellRelease.Ready(reportCast)) return false;
            _attack = attack;
            StepGuard(0d, generation, simulationStep);
            return true;
        }
        LookReceipt current = look();
        int chain = tuning.Combatant.ChainLength(PlayerStat(combat.PlayerWeaponSkill()));
        combat.SelectPlayerAttack(attack, finisher: attack == DaggerfallAttackIds.Fast[chain - 1], _harmsCivilians);
        _attack = attack;
        _swing = null;
        attacks.TryPlayerMelee(control, current, generation, simulationStep, seconds, facts);
        if (actors.Player.Actor.Get<AttackState>().Pending?.Request.SimulationStep != simulationStep) return false;
        // A swing admitted here lowers a raised guard before anything resolves against it.
        StepGuard(0d, generation, simulationStep);
        strikeWorld(current);
        return true;
    }

    bool IDaggerfallPlayerAttacks.Sweep(double cycle)
    {
        // A shot's flight decides what it meets, a cast meets nothing, and a player with no pose has nothing to sweep from.
        if (_attack is DaggerfallAttackIds.Shot or DaggerfallAttackIds.Cast || aim.StrikePose(DaggerfallActorIdentity.PlayerEntityId) is not { } pose) return true;
        LastSwing = _swing ??= new SwingSweep(contact, DaggerfallActorIdentity.PlayerEntityId, tuning.Arc(_attack!, combat.PlayerWeaponSkill(), combat.PlayerWeaponTemplate()),
            passes: actor => aim.PassesThrough(actor, _harmsCivilians));
        if (_swing.Advance(pose.Eye, pose.Forward, (float)cycle, environment()) is not long struck) return false;
        combat.SetSweptTarget(struck);
        return true;
    }

    void IDaggerfallPlayerAttacks.Impact()
    {
        if (_attack == DaggerfallAttackIds.Cast) { spellRelease.Release(look().Forward, _harmsCivilians, reportCast); return; }
        if (actors.Player.Actor.Get<AttackState>().Pending is not { } pending) return;
        _impacts.Add(new AttackImpactNotice(DaggerfallActorIdentity.PlayerEntityId, pending.Request.TargetId ?? 0,
            pending.Request.Generation, pending.Request.SimulationStep, Expired: false));
    }

    void IDaggerfallPlayerAttacks.Motion(string attack, DaggerfallAttackTiming? timing)
    {
        if (attack != DaggerfallAttackIds.Cast) facts.Append(new PlayerAttackMotionFact(attack, timing, _step.Generation, _step.SimulationStep));
        else if (casting.ReadyFor(DaggerfallActorIdentity.PlayerEntityId) is { } ready)
            facts.Append(new PlayerCastMotionFact(definitions.Magic.Spells[ready.SpellKey].Element, timing!.Value, _step.Generation, _step.SimulationStep));
    }

    void IDaggerfallPlayerAttacks.Rest() =>
        facts.Append(new PlayerAttackRestFact(_step.Generation, _step.SimulationStep));
}
