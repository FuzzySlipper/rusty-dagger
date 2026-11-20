using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>One selected enemy spell, the pose supplied to the common casting owner and the target it is aimed at.</summary>
/// <param name="Target">How the spell reaches: on the caster, by touch, at range or around the caster.</param>
/// <param name="TargetId">The target the caster pursues and aims at, the player or another actor.</param>
internal readonly record struct DaggerfallEnemySpellAttempt(
    long ActorId,
    string SpellKey,
    DaggerfallSpellTarget Target,
    WorldPoint Origin,
    WorldPoint Aim,
    Vector3 Direction,
    long TargetId);

/// <summary>
/// One spell an enemy could cast, as a tactic weighs it: how it reaches, the magicka it costs this caster, and whether
/// its effects already hold on whom it would reach.
/// </summary>
internal readonly record struct DaggerfallEnemySpellOption(string SpellKey, DaggerfallSpellTarget Target, int Cost, bool AlreadyOnTarget);

/// <summary>Evidence for the last admitted enemy spell decision for one live actor.</summary>
internal sealed record DaggerfallEnemySpellEvidence(
    long ActorId,
    string SpellKey,
    DaggerfallSpellTarget Target,
    DaggerfallCastOutcome Outcome,
    ulong Generation,
    ulong SimulationStep);

/// <summary>
/// The classic enemy spell choice, which the legacy tactic makes from authored mobile and class lists, and the release
/// of a chosen spell, which the behaviour module admits: readiness, cost, release, collision and effect lifecycle are
/// <see cref="DaggerfallCasting"/>'s through the executor callback. Its target is whichever the enemy pursues.
/// </summary>
internal sealed class DaggerfallEnemyMagicModule
{
    internal const double MinimumRangedDistance = 6d;
    internal const double MaximumRangedDistance = 51.2d;
    internal const double MeleeDistance = 2.25d;
    internal const double RangedFacingCosine = .9238795325112867d; // cos(22.5 degrees)
    private const string SelectionScope = "daggerfall.enemy-spell-selection.v1";
    /// <summary>The policy name a traced enemy's spell gates carry.</summary>
    internal const string Policy = "spell";

    private readonly DaggerfallEnemySpells _spells;
    private readonly DaggerfallMagicCatalogSet _catalog;
    private readonly DaggerfallCasting _casting;
    private readonly IRandomService _random;
    private readonly Func<long, DaggerfallActorDefinition?> _definition;
    private readonly Func<long, WorldPoint, PursuitTarget, bool> _laneClear;
    private readonly Func<DaggerfallEnemySpellAttempt, DaggerfallCastResult> _execute;
    private readonly Dictionary<long, DaggerfallEnemySpellEvidence> _last = [];

    internal DaggerfallEnemyMagicModule(
        DaggerfallEnemySpells spells,
        DaggerfallMagicCatalogSet catalog,
        DaggerfallCasting casting,
        IRandomService random,
        Func<long, DaggerfallActorDefinition?> definition,
        Func<int, DaggerfallMobileDefinition?> mobileCatalog,
        Func<long, WorldPoint, PursuitTarget, bool> laneClear,
        Func<DaggerfallEnemySpellAttempt, DaggerfallCastResult> execute)
    {
        _spells = spells ?? throw new ArgumentNullException(nameof(spells));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _casting = casting ?? throw new ArgumentNullException(nameof(casting));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _mobileCatalog = mobileCatalog ?? throw new ArgumentNullException(nameof(mobileCatalog));
        _laneClear = laneClear ?? throw new ArgumentNullException(nameof(laneClear));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    internal IReadOnlyDictionary<long, DaggerfallEnemySpellEvidence> LastEvidence => _last;

    internal void Clear()
    {
        _last.Clear();
    }

    /// <summary>Whether a missile from the actor where it stands now would reach its target, the lane the classic choice keeps clear.</summary>
    internal bool LaneClear(ActorState actor, PursuitTarget target) => _laneClear(actor.DurableId, actor.Position, target);

    /// <summary>
    /// The classic choice of one spell at the pursued target from the perception receipt already admitted by the
    /// behaviour owner: a keyed pick from the ranged list inside the ranged band and a one-in-forty roll, its lane
    /// clear, else a touch or self spell at melee distance; null when none. It issues no visibility query and keeps no
    /// timer: the common pending cast set and active effects are the only duplicate and cancellation authorities.
    /// <paramref name="senses"/> is the senses' judgement of this target where they judge it (the player's stealth
    /// and illusions); elsewhere the Engine's sight alone decides.
    /// </summary>
    internal DaggerfallEnemySpellAttempt? Choose(ActorState actor, PursuitTarget target, PerceptionReadoutResult? visibility,
        DaggerfallEnemyPerceptionDecision? senses, Lazy<bool> laneClear, ulong generation, ulong simulationStep, List<DaggerfallDecisionGate>? gates = null)
    {
        if (actor.IsDefeated || visibility is not { } receipt || senses is not (null or { InSight: true, PursuitVisible: true }))
            return Refuse("eligible", actor.IsDefeated ? "defeated" : visibility is null ? "no-visibility"
                : target.DurableId == DaggerfallActorIdentity.PlayerEntityId ? "player-not-in-sight" : "target-not-in-sight");
        if (_casting.HasPending(actor.DurableId)) return Refuse("eligible", "casting");
        DaggerfallActorDefinition? definition = _definition(actor.DurableId);
        if (definition is null || !_spells.IsCaster(definition.MobileId)) return Refuse("eligible", "not-a-caster");

        PerceptionPair? observed = receipt.Pairs.ToArray()
            .Where(pair => pair.Observer == checked((ulong)actor.DurableId)
                && pair.Target == checked((ulong)target.DurableId))
            .OrderBy(pair => pair.Distance)
            .Select(pair => (PerceptionPair?)pair)
            .FirstOrDefault();
        if (observed is not { } pair || pair.Kind != PerceptionPairKind.Visible
            || !double.IsFinite(pair.Distance) || pair.Distance < 0d) return Refuse("eligible", "not-visible");

        List<string> ranged = RangedSpellKeys(definition);
        bool inRangedBand = pair.Distance > MinimumRangedDistance && pair.Distance < MaximumRangedDistance
            && pair.FacingCosine >= RangedFacingCosine && ranged.Count > 0 && !HasBow(definition);
        gates?.Add(new(Policy, "ranged-band", inRangedBand ? DaggerfallGateOutcome.Passed : DaggerfallGateOutcome.Refused,
            ranged.Count == 0 ? "no-ranged-spell" : HasBow(definition) ? "has-bow" : pair.FacingCosine < RangedFacingCosine ? "not-facing"
                : pair.Distance <= MinimumRangedDistance ? "too-close" : pair.Distance >= MaximumRangedDistance ? "too-far" : "in-band",
            new DaggerfallGateValue("distance", pair.Distance), new DaggerfallGateValue("minimum", MinimumRangedDistance),
            new DaggerfallGateValue("maximum", MaximumRangedDistance), new DaggerfallGateValue("facingCosine", pair.FacingCosine)));
        if (inRangedBand)
        {
            string selected = Select(ranged, actor.DurableId, generation, simulationStep, "ranged");
            gates?.Add(new(Policy, "ranged-pick", DaggerfallGateOutcome.Chose, selected, new DaggerfallGateValue("choices", ranged.Count)));
            if (_casting.EffectsAlreadyOnTarget(selected, target.DurableId))
                gates?.Add(new(Policy, "ranged-roll", DaggerfallGateOutcome.Refused, "already-on-target"));
            else
            {
                int roll = Draw(actor.DurableId, generation, simulationStep, "ranged-gate", 1, 40);
                gates?.Add(new(Policy, "ranged-roll", roll == 1 ? DaggerfallGateOutcome.Passed : DaggerfallGateOutcome.Refused,
                    roll == 1 ? "cast" : "wait", new DaggerfallGateValue("roll", roll), new DaggerfallGateValue("passOn", 1), new DaggerfallGateValue("maximum", 40)));
                if (roll == 1)
                {
                    if (!TryBuildAttempt(actor, selected, target, out DaggerfallEnemySpellAttempt attempt))
                        gates?.Add(new(Policy, "path", DaggerfallGateOutcome.Refused, "no-aim"));
                    else if (!laneClear.Value)
                        gates?.Add(new(Policy, "path", DaggerfallGateOutcome.Refused, "path-blocked"));
                    else return attempt;
                }
            }
        }

        // TargetRateOfApproach is a donor EnhancedCombatAI input. The classic spell choice has no
        // corresponding enhanced mode, so touch eligibility remains the authored melee distance even
        // when the target is closing during this update.
        if (pair.Distance > MeleeDistance) return Refuse("touch-band", "out-of-touch",
            new DaggerfallGateValue("distance", pair.Distance), new DaggerfallGateValue("maximum", MeleeDistance));
        List<string> touch = SpellKeys(definition)
            .Where(key => TargetOf(key) is DaggerfallSpellTarget.ByTouch or DaggerfallSpellTarget.CasterOnly)
            .ToList();
        if (touch.Count == 0) return Refuse("touch-band", "no-touch-spell");
        string touchSpell = Select(touch, actor.DurableId, generation, simulationStep, "touch");
        gates?.Add(new(Policy, "touch-pick", DaggerfallGateOutcome.Chose, touchSpell, new DaggerfallGateValue("choices", touch.Count)));
        if (_casting.EffectsAlreadyOnTarget(touchSpell,
                TargetOf(touchSpell) == DaggerfallSpellTarget.CasterOnly ? actor.DurableId : target.DurableId)) return Refuse("touch-pick", "already-on-target");
        if (!TryBuildAttempt(actor, touchSpell, target, out DaggerfallEnemySpellAttempt touchAttempt)) return Refuse("path", "no-aim");
        return touchAttempt;

        DaggerfallEnemySpellAttempt? Refuse(string gate, string reason, params DaggerfallGateValue[] values)
        {
            gates?.Add(new(Policy, gate, DaggerfallGateOutcome.Refused, reason, values));
            return null;
        }
    }

    /// <summary>
    /// Releases a chosen spell; whether it took the step's action. A traced caster's cast gate carries the target it
    /// was meant for (the caster for a caster-only spell, otherwise its target) and the cast's sequence, which its
    /// delivery facts carry too, so the trace can set what each cast reached beside what it meant.
    /// </summary>
    internal bool Cast(DaggerfallEnemySpellAttempt attempt, ulong generation, ulong simulationStep, List<DaggerfallDecisionGate>? gates = null)
    {
        long intended = attempt.Target == DaggerfallSpellTarget.CasterOnly ? attempt.ActorId : attempt.TargetId;
        DaggerfallCastResult result = _execute(attempt);
        _last[attempt.ActorId] = new(attempt.ActorId, attempt.SpellKey, attempt.Target, result.Outcome,
            generation, simulationStep);
        // A cast that reached no one is still a released, paid action (a contact touch that met no body).
        bool admitted = result.Outcome is DaggerfallCastOutcome.Released or DaggerfallCastOutcome.DeliveryCompleted or DaggerfallCastOutcome.Missed;
        gates?.Add(new(Policy, "cast", admitted ? DaggerfallGateOutcome.Passed : DaggerfallGateOutcome.Refused, result.Outcome.ToString(),
            [new("intended", intended), .. result.Bundle is { } bundle ? [new DaggerfallGateValue("sequence", bundle.Sequence)] : Array.Empty<DaggerfallGateValue>()]));
        return admitted;
    }

    /// <summary>
    /// Every spell on the enemy's authored list that casting would admit from it, with its cost to this caster; none
    /// while a cast of its own is still pending. Whether it can pay is the tactic's to weigh against its magicka.
    /// </summary>
    internal IReadOnlyList<DaggerfallEnemySpellOption> SpellOptions(ActorState actor, PursuitTarget target)
    {
        if (actor.IsDefeated || _casting.HasPending(actor.DurableId) || _definition(actor.DurableId) is not { } definition) return [];
        List<DaggerfallEnemySpellOption> options = [];
        foreach (string key in SpellKeys(definition))
        {
            if (_casting.AvailableSpellCost(actor.DurableId, key) is not int cost) continue;
            DaggerfallSpellTarget reaches = TargetOf(key);
            options.Add(new(key, reaches, cost,
                _casting.EffectsAlreadyOnTarget(key, reaches == DaggerfallSpellTarget.CasterOnly ? actor.DurableId : target.DurableId)));
        }
        return options;
    }

    /// <summary>A chosen spell aimed from where the actor stands at its target, or null when it cannot be aimed.</summary>
    internal DaggerfallEnemySpellAttempt? Aim(ActorState actor, string spellKey, PursuitTarget target) =>
        TryBuildAttempt(actor, spellKey, target, out DaggerfallEnemySpellAttempt attempt) ? attempt : null;

    /// <summary>
    /// EnemyMotor.CanCastRangedSpell: inside the ranged band a caster without a bow attack has a
    /// ranged spell ready while it has magicka, a ranged spell whose effects are not already on the
    /// target, and a clear missile lane. The motor holds or strafes while this is true.
    /// </summary>
    internal bool HasRangedSpellReady(ActorState actor, PursuitTarget target, double distance) =>
        HasRangedSpellReady(actor, target, distance, new Lazy<bool>(() => LaneClear(actor, target)));

    /// <inheritdoc cref="HasRangedSpellReady(ActorState, PursuitTarget, double)"/>
    /// <param name="laneClear">The lane to the target, read only when everything else holds.</param>
    internal bool HasRangedSpellReady(ActorState actor, PursuitTarget target, double distance, Lazy<bool> laneClear)
    {
        if (actor.IsDefeated || distance <= MinimumRangedDistance || distance >= MaximumRangedDistance) return false;
        DaggerfallActorDefinition? definition = _definition(actor.DurableId);
        if (definition is null || !_spells.IsCaster(definition.MobileId) || HasBow(definition)) return false;
        if (!actor.Stats.TryGetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value), out Track? magicka) || magicka.Current <= 0) return false;
        string? ready = RangedSpellKeys(definition).FirstOrDefault(key => !_casting.EffectsAlreadyOnTarget(key, target.DurableId));
        return ready is not null && TryBuildAttempt(actor, ready, target, out _) && laneClear.Value;
    }

    private List<string> RangedSpellKeys(DaggerfallActorDefinition definition) => SpellKeys(definition)
        .Where(key => TargetOf(key) is DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange)
        .ToList();

    /// <summary>EnemyMotor.hasBowAttack: ranged attack 1 is a bow unless the mobile casts magic without ranged attack 2.</summary>
    private bool HasBow(DaggerfallActorDefinition definition) =>
        definition.MobileId is int mobileId && _mobileCatalog(mobileId) is { HasRangedAttack1: true } mobile
        && (mobile.CastsMagic == false || mobile.HasRangedAttack2);

    private IReadOnlyList<string> SpellKeys(DaggerfallActorDefinition definition) =>
        _spells.For(definition).Where(key => _catalog.Spells.ContainsKey(key)).ToArray();

    private readonly Func<int, DaggerfallMobileDefinition?> _mobileCatalog;

    private DaggerfallSpellTarget TargetOf(string key) =>
        DaggerfallMagicCostPolicy.TargetForRangeType(_catalog.Spells[key].RangeType);

    private bool TryBuildAttempt(ActorState actor, string key, PursuitTarget target,
        out DaggerfallEnemySpellAttempt attempt)
    {
        attempt = default;
        if (!TryReadTarget(key, out DaggerfallSpellTarget targetMode)) return false;
        Vector3 origin = actor.Position.ToVector();
        Vector3 aim = target.Position.ToVector();
        Vector3 delta = aim - origin;
        if (!TryNormalize(delta, out Vector3 direction)) return false;
        attempt = new(actor.DurableId, key, targetMode, actor.Position, target.Position, direction, target.DurableId);
        return true;
    }

    private bool TryReadTarget(string key, out DaggerfallSpellTarget target)
    {
        if (!_catalog.Spells.TryGetValue(key, out DaggerfallSpellDefinition? spell))
        {
            target = default;
            return false;
        }
        target = DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType);
        return true;
    }

    private string Select(IReadOnlyList<string> keys, long actorId, ulong generation, ulong simulationStep, string lane)
    {
        int index = Draw(actorId, generation, simulationStep, $"{lane}-selection:{keys.Count}", 0, keys.Count - 1);
        return keys[index];
    }

    private int Draw(long actorId, ulong generation, ulong simulationStep, string purpose, int minimum, int maximum) =>
        checked((int)_random.DrawKeyed(new KeyedRngRequest(
            DaggerfallRandomKey.Seed,
            CombatRandomKey.EnemyScope,
            $"{SelectionScope}:generation:{generation}:step:{simulationStep}:actor:{actorId}:spell:{purpose}",
            minimum, maximum)).Value);

    private static bool TryNormalize(Vector3 value, out Vector3 normalized)
    {
        normalized = default;
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)
            || value.LengthSquared() <= .000001f) return false;
        normalized = Vector3.Normalize(value);
        return true;
    }
}
