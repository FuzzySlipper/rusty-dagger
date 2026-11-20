using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>The menu a scored tactic chooses from: an intelligent enemy's whole kit, or a beast's few moves.</summary>
internal enum DaggerfallScoredPreset
{
    /// <summary>Every weapon it carries, every spell it can afford, moving for a lane, fleeing; its record's tendencies.</summary>
    Intelligent,
    /// <summary>Its bite, a lunge at a gap, circling when blocked, fleeing; fixed tendencies.</summary>
    Beast,
}

/// <summary>What a scored option does.</summary>
internal enum DaggerfallTacticOptionKind
{
    /// <summary>Strikes with one attack it carries, closing first when out of reach.</summary>
    Strike,
    /// <summary>A beast's fast close and strike at a target that has left a gap.</summary>
    Lunge,
    /// <summary>Releases one spell.</summary>
    Spell,
    /// <summary>Moves around its target for a clear lane, or a beast circling a body in its way.</summary>
    Reposition,
    /// <summary>Runs away and then leaves the target alone for a while.</summary>
    Flee,
}

/// <summary>
/// One option a scored tactic weighed: what it does, its score with the keyed noise added, why it could not be taken
/// (null when it could) and the considerations behind the score. A strike names its attack and a spell its key.
/// </summary>
internal sealed record DaggerfallTacticOption(DaggerfallTacticOptionKind Kind, string Name, double Score, string? Unavailable,
    DaggerfallGateValue[] Considerations)
{
    internal string? Attack { get; init; }
    internal string? Spell { get; init; }
    internal double Reach { get; init; }
    internal bool Ranged { get; init; }
}

/// <summary>A scored tactic's memory of one actor, kept in its perception memory: transient, never saved.</summary>
internal sealed class DaggerfallEnemyTacticMemory
{
    /// <summary>The option chosen in the step stamped beside it, which the step's action carries out.</summary>
    internal DaggerfallTacticOption? Chosen;
    internal ulong ChosenGeneration;
    internal ulong ChosenStep;
    /// <summary>Seconds before its next spell.</summary>
    internal float CastCooldown;
    internal int RepositionDirection = 1;
    /// <summary>The flee under way, if any: from whom, to where, the direction tried and the time left.</summary>
    internal long? FleeingFrom;
    internal WorldPoint FleeDestination;
    internal int FleeAttempt;
    internal float FleeSecondsLeft;
    internal bool FleeStarted;
    /// <summary>Flights already finished; each halves the next flee's worth, so a beaten enemy fights when it returns.</summary>
    internal int Fled;

    internal void Clear()
    {
        Chosen = null;
        CastCooldown = 0f;
        FleeingFrom = null;
        Fled = 0;
    }
}

/// <summary>
/// One decision core that scores the options an enemy has now and takes the best, in two presets
/// (<see cref="DaggerfallScoredPreset"/>). The intelligent preset lists each weapon it carries (a bow while it has
/// arrows and an unbroken bow, its melee weapon), each spell on its list it can afford, moving around for a clear lane
/// and fleeing; the beast preset its bite, a lunge at a gap, circling when blocked and fleeing. Nobody marks an enemy
/// melee or ranged: what it carries is what it can do. Each option is scored from distance and reach, the lane, its
/// own and its target's health, the resources it has left and the allies fighting beside it, plus a little keyed
/// noise, and weighed by the tendencies its record authors (courage and discipline), the beast's fixed ones, and the
/// tuning's rule numbers. A traced enemy's every option is a gate: its score, considerations and why it lost.
/// <para>Fleeing is an intent the scoring starts and then holds: it runs to a navigated point away from the target,
/// turning aside when the Engine cannot walk a way, then leaves that target alone for a while unless struck.</para>
/// </summary>
internal sealed class DaggerfallScoredEnemyTactic : IDaggerfallEnemyTactic
{
    internal const string IntelligentName = "intelligent";
    internal const string BeastName = "beast";
    /// <summary>The policy name a traced enemy's tactic gates carry.</summary>
    internal const string Policy = "tactic";
    private const string NoiseScope = "daggerfall.enemy-tactic";

    private readonly DaggerfallScoredPreset _preset;
    private readonly Func<long, IReadOnlyList<DaggerfallEnemyAttackOption>> _attacks;
    private readonly Func<long, long, bool> _lineOfFire;
    private readonly Func<ActorState, PursuitTarget, IReadOnlyList<DaggerfallEnemySpellOption>> _spells;
    private readonly Func<ActorState, string, PursuitTarget, DaggerfallEnemySpellAttempt?> _aim;
    private readonly Func<long, DaggerfallTacticTendencies?> _tendencies;
    private readonly DaggerfallContactEngagement _engagement;
    private readonly DaggerfallPursuitManeuverTuning _maneuvers;
    private readonly DaggerfallEnemyTacticTuning _tuning;
    private readonly IRandomService _random;

    /// <param name="attacks">The attacks each enemy carries now (combat's).</param>
    /// <param name="lineOfFire">Whether an enemy's shot at a target would meet it first: the line a bow holds fire on (combat's).</param>
    /// <param name="spells">The spells an enemy could cast at a target now, with their costs; empty while a cast is pending.</param>
    /// <param name="aim">A chosen spell aimed from where the enemy stands, or null when it cannot be aimed.</param>
    /// <param name="tendencies">The tendencies an enemy's mobile record authors, or null.</param>
    internal DaggerfallScoredEnemyTactic(DaggerfallScoredPreset preset,
        Func<long, IReadOnlyList<DaggerfallEnemyAttackOption>> attacks, Func<long, long, bool> lineOfFire,
        Func<ActorState, PursuitTarget, IReadOnlyList<DaggerfallEnemySpellOption>> spells,
        Func<ActorState, string, PursuitTarget, DaggerfallEnemySpellAttempt?> aim,
        Func<long, DaggerfallTacticTendencies?> tendencies,
        DaggerfallContactEngagement engagement, DaggerfallPursuitManeuverTuning maneuvers, DaggerfallEnemyTacticTuning tuning, IRandomService random)
    {
        _preset = preset;
        _attacks = attacks ?? throw new ArgumentNullException(nameof(attacks));
        _lineOfFire = lineOfFire ?? throw new ArgumentNullException(nameof(lineOfFire));
        _spells = spells ?? throw new ArgumentNullException(nameof(spells));
        _aim = aim ?? throw new ArgumentNullException(nameof(aim));
        _tendencies = tendencies ?? throw new ArgumentNullException(nameof(tendencies));
        _engagement = engagement ?? throw new ArgumentNullException(nameof(engagement));
        _maneuvers = maneuvers ?? throw new ArgumentNullException(nameof(maneuvers));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public bool WeighsTendencies => _preset == DaggerfallScoredPreset.Intelligent;

    public PursuitPolicy Move(DaggerfallEnemyMoveObservation seen, List<DaggerfallDecisionGate>? gates)
    {
        DaggerfallEnemyPerceptionMemory senses = seen.Actor.Actor.Get<DaggerfallEnemyPerceptionMemory>();
        DaggerfallEnemyTacticMemory memory = senses.Tactic;
        memory.CastCooldown = Math.Max(0f, memory.CastCooldown - seen.DeltaSeconds);
        memory.Chosen = null;
        bool inSight = seen.Senses?.InSight ?? seen.Pursuit.Visible;
        bool known = seen.Pursuit.Visible && (inSight || seen.Senses?.InEarshot == true);
        PursuitPolicy movement = seen.Movement with { AllowAttack = false, TargetObserved = known, SearchLastKnownPosition = seen.MaySearch,
            SearchDistance = _maneuvers.SearchDistance, SearchStep = _maneuvers.SearchStep };

        if (senses.LeavesAlone(seen.Target.DurableId, seen.GameMinute))
        {
            gates?.Add(new(Policy, "intent", DaggerfallGateOutcome.Chose, "leave-alone", new DaggerfallGateValue("untilMinute", senses.DisengagedUntilMinute),
                new DaggerfallGateValue("minute", seen.GameMinute)));
            return movement with { Maneuver = PursuitState.Idle, TargetObserved = false, SearchLastKnownPosition = false };
        }
        if (memory.FleeingFrom is long fleeing && fleeing != seen.Target.DurableId) memory.FleeingFrom = null;
        if (memory.FleeingFrom is not null && Fleeing(seen, senses, memory, movement, gates) is { } flight) return flight;

        DaggerfallTacticTendencies tendencies = _preset == DaggerfallScoredPreset.Beast ? _tuning.Beast.Tendencies
            : _tendencies(seen.Actor.DurableId) ?? _tuning.Intelligent;
        List<DaggerfallTacticOption> options = Options(seen, tendencies, inSight);
        DaggerfallTacticOption? best = options.Where(option => option.Unavailable is null)
            .Aggregate((DaggerfallTacticOption?)null, (chosen, option) => chosen is null || option.Score > chosen.Score ? option : chosen);
        if (gates is not null)
            foreach (DaggerfallTacticOption option in options)
                gates.Add(new(Policy, option.Name, ReferenceEquals(option, best) ? DaggerfallGateOutcome.Chose : DaggerfallGateOutcome.Refused,
                    option.Unavailable ?? (ReferenceEquals(option, best) ? "best" : "outscored"), [new("score", option.Score), .. option.Considerations]));
        if (best is null) return movement with { Maneuver = PursuitState.Chase };
        memory.Chosen = best;
        memory.ChosenGeneration = seen.Generation;
        memory.ChosenStep = seen.Step;
        return best.Kind switch
        {
            DaggerfallTacticOptionKind.Flee => StartFlee(seen, memory, movement, gates),
            DaggerfallTacticOptionKind.Reposition => movement with { Maneuver = PursuitState.Strafe, SpeedMultiplier = .5f,
                StrafeDirection = Reposition(seen, memory) },
            DaggerfallTacticOptionKind.Lunge => movement with { Maneuver = PursuitState.Chase, AllowAttack = inSight,
                SpeedMultiplier = _tuning.Beast.LungeSpeedMultiplier, AttackReach = best.Reach },
            DaggerfallTacticOptionKind.Strike when _preset == DaggerfallScoredPreset.Beast =>
                movement with { Maneuver = PursuitState.Chase, AllowAttack = inSight, AttackReach = best.Reach },
            DaggerfallTacticOptionKind.Strike => Motor(seen, best.Reach, best.Ranged, inSight, known, gates) with { AttackReach = best.Reach },
            // A spell takes the action; the motor only spaces the caster for it, and no blow follows.
            _ => Motor(seen, best.Ranged ? null : DaggerfallEnemyMagicModule.MeleeDistance, best.Ranged, inSight, known, gates) with { AllowAttack = false },
        };
    }

    public DaggerfallEnemyActionChoice Act(DaggerfallEnemyActionObservation seen, List<DaggerfallDecisionGate>? gates)
    {
        DaggerfallEnemyTacticMemory memory = seen.Actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Tactic;
        if (memory.Chosen is not { } chosen || memory.ChosenGeneration != seen.Generation || memory.ChosenStep != seen.Step)
            return new(null, false);
        if (!seen.ActionOpen)
        {
            gates?.Add(new(Policy, "action", DaggerfallGateOutcome.Refused, "action-taken-this-update"));
            return new(null, false);
        }
        switch (chosen.Kind)
        {
            case DaggerfallTacticOptionKind.Strike or DaggerfallTacticOptionKind.Lunge:
                return new(null, true, chosen.Attack);
            case DaggerfallTacticOptionKind.Spell:
                DaggerfallEnemySpellAttempt? attempt = _aim(seen.Actor, chosen.Spell!, seen.Target);
                if (attempt is null) gates?.Add(new(Policy, "action", DaggerfallGateOutcome.Refused, "no-aim"));
                return new(attempt, false);
            default:
                return new(null, false);
        }
    }

    public void Admitted(ActorState actor, DaggerfallEnemyActionChoice choice, DaggerfallEnemyAdmission admission)
    {
        if (admission == DaggerfallEnemyAdmission.Cast)
            actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Tactic.CastCooldown = _tuning.CastIntervalSeconds;
    }

    /// <summary>Every option on this preset's menu, scored, the ones it cannot take now marked with why.</summary>
    private List<DaggerfallTacticOption> Options(DaggerfallEnemyMoveObservation seen, DaggerfallTacticTendencies tendencies, bool inSight)
    {
        DaggerfallEnemyTacticWeights weights = _tuning.Weights;
        double distance = seen.Pursuit.Distance;
        double finisher = weights.Finisher * (1d - seen.TargetHealth);
        List<DaggerfallTacticOption> options = [];
        bool laneWanted = false;
        DaggerfallTacticOption? melee = null;
        foreach (DaggerfallEnemyAttackOption attack in _attacks(seen.Actor.DurableId))
        {
            double reach = _engagement.EngageReach(attack.Reach, attack.Ranged);
            if (attack.Ranged)
            {
                double resources = attack.Ammunition is int arrows ? arrows / (arrows + 1d) : 1d;
                string? unavailable = attack.Ammunition == 0 ? "no-arrows" : !inSight ? "not-in-sight"
                    : distance <= attack.MinimumDistance ? "too-close" : distance >= attack.Reach ? "too-far" : null;
                if (unavailable is null && !_lineOfFire(seen.Actor.DurableId, seen.Target.DurableId)) { unavailable = "lane-blocked"; laneWanted = true; }
                options.Add(Scored(new(DaggerfallTacticOptionKind.Strike, $"strike:{attack.Action}", weights.Bow * (.5d + .5d * resources) + finisher, unavailable,
                    [new("distance", distance), new("reach", attack.Reach), new("minimum", attack.MinimumDistance), new("ammunition", attack.Ammunition ?? -1),
                        new("targetHealth", seen.TargetHealth)]) { Attack = attack.Action, Reach = reach, Ranged = true }));
                continue;
            }
            bool inReach = distance <= reach;
            melee = Scored(new(DaggerfallTacticOptionKind.Strike, $"strike:{attack.Action}", weights.Melee * (inReach ? 1d : weights.Closing) + finisher, null,
                [new("distance", distance), new("reach", reach), new("inReach", inReach ? 1 : 0), new("targetHealth", seen.TargetHealth)]) { Attack = attack.Action, Reach = reach });
            options.Add(melee);
        }

        if (_preset == DaggerfallScoredPreset.Intelligent)
            options.AddRange(Spells(seen, inSight, finisher, ref laneWanted));
        else if (melee is not null)
        {
            bool gap = seen.Motor.TargetParalyzed || seen.Motor.TargetBackTurned || seen.Motor.TargetBowOrSheathed;
            string? lunge = !inSight ? "not-in-sight" : distance <= melee.Reach ? "in-reach" : distance > _tuning.Beast.LungeDistance ? "too-far" : !gap ? "no-gap" : null;
            options.Add(Scored(new(DaggerfallTacticOptionKind.Lunge, "lunge", weights.Melee + finisher, lunge,
                [new("distance", distance), new("lungeDistance", _tuning.Beast.LungeDistance), new("gap", gap ? 1 : 0)]) { Attack = melee.Attack, Reach = melee.Reach }));
            // A beast cannot shoot; what blocks it is a body or cover between it and its prey, or a way it cannot walk.
            bool blocked = seen.Actor.Pursuit.State == PursuitState.Blocked
                || inSight && distance > melee.Reach && !_lineOfFire(seen.Actor.DurableId, seen.Target.DurableId);
            options.Add(Scored(new(DaggerfallTacticOptionKind.Reposition, "circle", weights.Reposition, blocked ? null : "not-blocked",
                [new("blocked", blocked ? 1 : 0)])));
        }

        if (_preset == DaggerfallScoredPreset.Intelligent)
            options.Add(Scored(new(DaggerfallTacticOptionKind.Reposition, "reposition", weights.Reposition * tendencies.Discipline,
                laneWanted ? null : "lane-not-wanted", [new("discipline", tendencies.Discipline)])));

        int allies = Math.Min(3, seen.Allies.Count(ally => System.Numerics.Vector3.Distance(ally.ToVector(), seen.Actor.Position.ToVector()) <= _tuning.Flee.AllyRadius));
        double threshold = _tuning.Flee.Health * Math.Max(0d, 1d - tendencies.Courage - _tuning.Flee.AllyCourage * allies);
        int fled = seen.Actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Tactic.Fled;
        options.Add(Scored(new(DaggerfallTacticOptionKind.Flee, "flee",
            threshold <= 0d ? 0d : weights.Flee * (1d + (threshold - seen.Health) / threshold) * Math.Pow(.5d, fled),
            seen.Health < threshold ? null : "steady",
            [new("health", seen.Health), new("threshold", threshold), new("courage", tendencies.Courage), new("allies", allies), new("fled", fled)])));
        return options;

        DaggerfallTacticOption Scored(DaggerfallTacticOption option) => option.Unavailable is not null ? option
            : option with { Score = option.Score + Noise(seen, option.Name) };
    }

    /// <summary>Each spell on the caster's list, scored by how it reaches at this distance and what it leaves of its magicka.</summary>
    private IEnumerable<DaggerfallTacticOption> Spells(DaggerfallEnemyMoveObservation seen, bool inSight, double finisher, ref bool laneWanted)
    {
        IReadOnlyList<DaggerfallEnemySpellOption> spells = _spells(seen.Actor, seen.Target);
        if (spells.Count == 0) return [];
        double magicka = 0d, most = 1d;
        if (seen.Actor.Stats.TryGetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value), out Track? track))
            (magicka, most) = (track.Current, Math.Max(1d, track.Maximum.Value));
        double distance = seen.Pursuit.Distance;
        List<DaggerfallTacticOption> options = [];
        foreach (DaggerfallEnemySpellOption spell in spells)
        {
            bool ranged = spell.Target is DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange;
            string? unavailable = spell.Cost > magicka ? "magicka" : spell.AlreadyOnTarget ? "already-on-target" : !inSight ? "not-in-sight"
                : seen.Actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Tactic.CastCooldown > 0f ? "cast-interval"
                : ranged ? distance <= DaggerfallEnemyMagicModule.MinimumRangedDistance ? "too-close" : distance >= DaggerfallEnemyMagicModule.MaximumRangedDistance ? "too-far" : null
                : spell.Target is DaggerfallSpellTarget.CasterOnly ? null
                : distance > DaggerfallEnemyMagicModule.MeleeDistance ? "out-of-touch" : null;
            if (unavailable is null && ranged && !seen.LaneClear.Value) { unavailable = "lane-blocked"; laneWanted = true; }
            double left = Math.Max(0d, magicka - spell.Cost) / most;
            // A spell on itself helps but does not end the fight, so it is worth less than one at the target.
            double worth = _tuning.Weights.Spell * (.5d + .5d * left) * (spell.Target is DaggerfallSpellTarget.CasterOnly ? .5d : 1d)
                + (spell.Target is DaggerfallSpellTarget.CasterOnly ? 0d : finisher);
            DaggerfallTacticOption option = new(DaggerfallTacticOptionKind.Spell, $"spell:{spell.SpellKey}", worth, unavailable,
                [new("distance", distance), new("cost", spell.Cost), new("magicka", magicka), new("left", left), new("targetHealth", seen.TargetHealth)])
                { Spell = spell.SpellKey, Ranged = ranged };
            options.Add(unavailable is null ? option with { Score = worth + Noise(seen, option.Name) } : option);
        }
        return options;
    }

    /// <summary>Starts a flee from the target: the first navigated destination straight away from it, held for the tuned time.</summary>
    private PursuitPolicy StartFlee(DaggerfallEnemyMoveObservation seen, DaggerfallEnemyTacticMemory memory, PursuitPolicy movement,
        List<DaggerfallDecisionGate>? gates)
    {
        memory.FleeingFrom = seen.Target.DurableId;
        memory.FleeAttempt = 0;
        memory.FleeSecondsLeft = _tuning.Flee.Seconds;
        memory.FleeStarted = false;
        memory.FleeDestination = PursuitFlee.Destination(seen.Actor.Position, seen.Target.Position, _tuning.Flee.Distance, 0);
        return FleeTo(memory, movement, seen, gates);
    }

    /// <summary>
    /// A flee under way: on to its destination, turned aside after a step the Engine could not walk, and done on arriving
    /// or when its time runs out, when the actor leaves the target alone for a while. Out of directions it is cornered
    /// and fights (null: the options decide).
    /// </summary>
    private PursuitPolicy? Fleeing(DaggerfallEnemyMoveObservation seen, DaggerfallEnemyPerceptionMemory senses, DaggerfallEnemyTacticMemory memory,
        PursuitPolicy movement, List<DaggerfallDecisionGate>? gates)
    {
        memory.FleeSecondsLeft -= seen.DeltaSeconds;
        if (memory.FleeStarted && seen.Actor.Pursuit.NavigationFailures > 0 && ++memory.FleeAttempt < PursuitFlee.Directions)
            memory.FleeDestination = PursuitFlee.Destination(seen.Actor.Position, seen.Target.Position, _tuning.Flee.Distance, memory.FleeAttempt);
        memory.FleeStarted = true;
        if (memory.FleeAttempt >= PursuitFlee.Directions)
        {
            memory.FleeingFrom = null;
            memory.Fled++;
            gates?.Add(new(Policy, "intent", DaggerfallGateOutcome.Refused, "flee:cornered", new DaggerfallGateValue("attempts", memory.FleeAttempt)));
            return null;
        }
        System.Numerics.Vector3 remaining = memory.FleeDestination.ToVector() - seen.Actor.Position.ToVector();
        remaining.Y = 0f;
        if (remaining.Length() <= _tuning.Flee.Arrival || memory.FleeSecondsLeft <= 0f)
        {
            memory.FleeingFrom = null;
            memory.Fled++;
            senses.DisengagedFrom = seen.Target.DurableId;
            senses.DisengagedUntilMinute = seen.GameMinute + _tuning.Flee.DisengageMinutes;
            gates?.Add(new(Policy, "intent", DaggerfallGateOutcome.Passed, remaining.Length() <= _tuning.Flee.Arrival ? "flee:arrived" : "flee:time",
                new DaggerfallGateValue("untilMinute", senses.DisengagedUntilMinute)));
            return movement with { Maneuver = PursuitState.Idle, TargetObserved = false, SearchLastKnownPosition = false };
        }
        return FleeTo(memory, movement, seen, gates);
    }

    private PursuitPolicy FleeTo(DaggerfallEnemyTacticMemory memory, PursuitPolicy movement, DaggerfallEnemyMoveObservation seen, List<DaggerfallDecisionGate>? gates)
    {
        System.Numerics.Vector3 remaining = memory.FleeDestination.ToVector() - seen.Actor.Position.ToVector();
        remaining.Y = 0f;
        gates?.Add(new(Policy, "intent", DaggerfallGateOutcome.Chose, "flee", new DaggerfallGateValue("remaining", remaining.Length()),
            new DaggerfallGateValue("secondsLeft", memory.FleeSecondsLeft), new DaggerfallGateValue("attempt", memory.FleeAttempt)));
        return movement with { Maneuver = PursuitState.Flee, Destination = memory.FleeDestination, SpeedMultiplier = _tuning.Flee.SpeedMultiplier,
            SearchLastKnownPosition = false };
    }

    /// <summary>Which way it moves around its target for a lane: the way it went, turned back after a step it could not walk.</summary>
    private static int Reposition(DaggerfallEnemyMoveObservation seen, DaggerfallEnemyTacticMemory memory)
    {
        if (seen.Actor.Pursuit.NavigationFailures > 0) memory.RepositionDirection = -memory.RepositionDirection;
        return memory.RepositionDirection;
    }

    /// <summary>The donor's EnemyMotor spacing for an attack at <paramref name="reach"/> (holding its band for a shot or ranged spell).</summary>
    private PursuitPolicy Motor(DaggerfallEnemyMoveObservation seen, double? reach, bool ranged, bool inSight, bool known, List<DaggerfallDecisionGate>? gates)
    {
        DaggerfallEnemyMotorMemory memory = seen.Actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().Motor;
        long actor = seen.Actor.DurableId, target = seen.Target.DurableId;
        return DaggerfallEnemyMotorPolicy.Select(memory, seen.Movement, _maneuvers, seen.Motor, seen.Pursuit with { Reach = reach }, ranged, inSight, known,
            seen.MaySearch, seen.DeltaSeconds,
            (key, min, max) => checked((int)_random.DrawKeyed(new KeyedRngRequest(0, "daggerfall.enemy-motor",
                $"actor:{actor}:target:{target}:generation:{seen.Generation}:step:{seen.Step}:decision:{memory.Decision}:{key}", min, max)).Value),
            gates);
    }

    /// <summary>A little keyed noise for one option, so two alike enemies don't choose in step.</summary>
    private double Noise(DaggerfallEnemyMoveObservation seen, string option) => _tuning.Noise == 0d ? 0d
        : _tuning.Noise * _random.DrawKeyed(new KeyedRngRequest(DaggerfallRandomKey.Seed, NoiseScope,
            $"actor:{seen.Actor.DurableId}:target:{seen.Target.DurableId}:generation:{seen.Generation}:step:{seen.Step}:option:{option}", 0, 1000)).Value / 1000d;
}
