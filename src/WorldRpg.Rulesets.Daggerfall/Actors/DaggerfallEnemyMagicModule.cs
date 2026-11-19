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

/// <summary>One selected enemy spell and the pose supplied to the common casting owner.</summary>
internal readonly record struct DaggerfallEnemySpellAttempt(
    long ActorId,
    string SpellKey,
    DaggerfallSpellTarget Target,
    WorldPoint Origin,
    WorldPoint Aim,
    Vector3 Direction);

/// <summary>Evidence for the last admitted enemy spell decision for one live actor.</summary>
internal sealed record DaggerfallEnemySpellEvidence(
    long ActorId,
    string SpellKey,
    DaggerfallSpellTarget Target,
    DaggerfallCastOutcome Outcome,
    ulong Generation,
    ulong SimulationStep);

/// <summary>
/// Donor-shaped spell selection at the existing enemy behavior owner.  This module chooses from
/// authored mobile/class lists and delegates readiness, cost, release, collision, and effect
/// lifecycle to <see cref="DaggerfallCasting"/> through its executor callback.
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
    private readonly Func<DaggerfallEnemySpellAttempt, bool> _rangedPathClear;
    private readonly Func<DaggerfallEnemySpellAttempt, DaggerfallCastResult> _execute;
    private readonly Dictionary<long, DaggerfallEnemySpellEvidence> _last = [];

    internal DaggerfallEnemyMagicModule(
        DaggerfallEnemySpells spells,
        DaggerfallMagicCatalogSet catalog,
        DaggerfallCasting casting,
        IRandomService random,
        Func<long, DaggerfallActorDefinition?> definition,
        Func<int, DaggerfallMobileDefinition?> mobileCatalog,
        Func<DaggerfallEnemySpellAttempt, bool> rangedPathClear,
        Func<DaggerfallEnemySpellAttempt, DaggerfallCastResult> execute)
    {
        _spells = spells ?? throw new ArgumentNullException(nameof(spells));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _casting = casting ?? throw new ArgumentNullException(nameof(casting));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _mobileCatalog = mobileCatalog ?? throw new ArgumentNullException(nameof(mobileCatalog));
        _rangedPathClear = rangedPathClear ?? throw new ArgumentNullException(nameof(rangedPathClear));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    internal IReadOnlyDictionary<long, DaggerfallEnemySpellEvidence> LastEvidence => _last;

    internal void Clear()
    {
        _last.Clear();
    }

    /// <summary>
    /// Decides one spell from the perception receipt already admitted by the behavior owner.  It
    /// deliberately does not issue another visibility query or retain a timer: the common pending
    /// cast set and active effects are the only duplicate/cancellation authorities.
    /// </summary>
    internal bool Decide(ActorState actor, PursuitTarget target, PerceptionReadoutResult? visibility,
        DaggerfallEnemyPerceptionDecision? perception, double targetRateOfApproach,
        ulong generation, ulong simulationStep, float deltaSeconds, List<DaggerfallDecisionGate>? gates = null)
    {
        if (actor.IsDefeated || target.DurableId != DaggerfallActorIdentity.PlayerEntityId
            || visibility is not { } receipt || perception is not { InSight: true, PursuitVisible: true })
            return Refuse("eligible", actor.IsDefeated ? "defeated" : target.DurableId != DaggerfallActorIdentity.PlayerEntityId ? "target-not-player"
                : visibility is null ? "no-visibility" : "player-not-in-sight");
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
                    else if (!_rangedPathClear(attempt))
                        gates?.Add(new(Policy, "path", DaggerfallGateOutcome.Refused, "path-blocked"));
                    else return TryExecute(attempt, IntendedTarget(selected, actor, target), generation, simulationStep, gates);
                }
            }
        }

        // TargetRateOfApproach is a donor EnhancedCombatAI input. The compiled ruleset has no
        // corresponding enhanced mode, so classic touch eligibility remains the authored melee
        // distance even when the target is closing during this update.
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
        return TryExecute(touchAttempt, IntendedTarget(touchSpell, actor, target), generation, simulationStep, gates);

        bool Refuse(string gate, string reason, params DaggerfallGateValue[] values)
        {
            gates?.Add(new(Policy, gate, DaggerfallGateOutcome.Refused, reason, values));
            return false;
        }
    }

    /// <summary>
    /// EnemyMotor.CanCastRangedSpell: inside the ranged band a caster without a bow attack has a
    /// ranged spell ready while it has magicka, a ranged spell whose effects are not already on the
    /// target, and a clear missile path. The motor holds or strafes while this is true.
    /// </summary>
    internal bool HasRangedSpellReady(ActorState actor, PursuitTarget target, double distance)
    {
        if (actor.IsDefeated || distance <= MinimumRangedDistance || distance >= MaximumRangedDistance) return false;
        DaggerfallActorDefinition? definition = _definition(actor.DurableId);
        if (definition is null || !_spells.IsCaster(definition.MobileId) || HasBow(definition)) return false;
        if (!actor.Stats.TryGetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value), out Track? magicka) || magicka.Current <= 0) return false;
        string? ready = RangedSpellKeys(definition).FirstOrDefault(key => !_casting.EffectsAlreadyOnTarget(key, target.DurableId));
        return ready is not null && TryBuildAttempt(actor, ready, target, out DaggerfallEnemySpellAttempt attempt) && _rangedPathClear(attempt);
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
        attempt = new(actor.DurableId, key, targetMode, actor.Position, target.Position, direction);
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

    /// <summary>Who a spell is meant for: the caster for a caster-only spell, otherwise the pursued target.</summary>
    private long IntendedTarget(string key, ActorState actor, PursuitTarget target) =>
        TargetOf(key) == DaggerfallSpellTarget.CasterOnly ? actor.DurableId : target.DurableId;

    /// <summary>
    /// Releases a spell. A traced caster's cast gate carries the target it was meant for and the cast's sequence, which
    /// its delivery facts carry too, so the trace can set what each cast reached beside what it meant.
    /// </summary>
    private bool TryExecute(DaggerfallEnemySpellAttempt attempt, long intended, ulong generation, ulong simulationStep, List<DaggerfallDecisionGate>? gates)
    {
        DaggerfallCastResult result = _execute(attempt);
        _last[attempt.ActorId] = new(attempt.ActorId, attempt.SpellKey, attempt.Target, result.Outcome,
            generation, simulationStep);
        // A cast that reached no one is still a released, paid action (a contact touch that met no body).
        bool admitted = result.Outcome is DaggerfallCastOutcome.Released or DaggerfallCastOutcome.DeliveryCompleted or DaggerfallCastOutcome.Missed;
        gates?.Add(new(Policy, "cast", admitted ? DaggerfallGateOutcome.Passed : DaggerfallGateOutcome.Refused, result.Outcome.ToString(),
            [new("intended", intended), .. result.Bundle is { } bundle ? [new DaggerfallGateValue("sequence", bundle.Sequence)] : Array.Empty<DaggerfallGateValue>()]));
        return admitted;
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
