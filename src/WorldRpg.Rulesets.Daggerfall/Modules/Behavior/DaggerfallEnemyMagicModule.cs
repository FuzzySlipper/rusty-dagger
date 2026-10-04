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
        ulong generation, ulong simulationStep, float deltaSeconds)
    {
        if (actor.IsDefeated || target.DurableId != DaggerfallActorIdentity.PlayerEntityId
            || visibility is not { } receipt || perception is not { InSight: true, PursuitVisible: true }) return false;
        if (_casting.HasPending(actor.DurableId)) return false;
        DaggerfallActorDefinition? definition = _definition(actor.DurableId);
        if (definition is null || !_spells.IsCaster(definition.MobileId)) return false;

        PerceptionPair? observed = receipt.Pairs.ToArray()
            .Where(pair => pair.Observer == checked((ulong)actor.DurableId)
                && pair.Target == checked((ulong)target.DurableId))
            .OrderBy(pair => pair.Distance)
            .Select(pair => (PerceptionPair?)pair)
            .FirstOrDefault();
        if (observed is not { } pair || pair.Kind != PerceptionPairKind.Visible
            || !double.IsFinite(pair.Distance) || pair.Distance < 0d) return false;

        DaggerfallMobileDefinition? mobile = definition.MobileId is int mobileId
            ? _definitionMobile(mobileId) : null;
        bool hasBow = mobile?.HasRangedAttack1 == true
            && (mobile.CastsMagic == false || mobile.HasRangedAttack2);

        List<string> ranged = SpellKeys(definition)
            .Where(key => TargetOf(key) is DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange)
            .ToList();
        if (pair.Distance > MinimumRangedDistance && pair.Distance < MaximumRangedDistance
            && pair.FacingCosine >= RangedFacingCosine && ranged.Count > 0 && !hasBow)
        {
            string selected = Select(ranged, actor.DurableId, generation, simulationStep, "ranged");
            if (!_casting.EffectsAlreadyOnTarget(selected, target.DurableId)
                && Draw(actor.DurableId, generation, simulationStep, "ranged-gate", 1, 40) == 1)
            {
                if (TryBuildAttempt(actor, selected, target, out DaggerfallEnemySpellAttempt attempt)
                    && _rangedPathClear(attempt))
                    return TryExecute(attempt, generation, simulationStep);
            }
        }

        // TargetRateOfApproach is a donor EnhancedCombatAI input. The compiled ruleset has no
        // corresponding enhanced mode, so classic touch eligibility remains the authored melee
        // distance even when the target is closing during this update.
        if (pair.Distance > MeleeDistance) return false;
        List<string> touch = SpellKeys(definition)
            .Where(key => TargetOf(key) is DaggerfallSpellTarget.ByTouch or DaggerfallSpellTarget.CasterOnly)
            .ToList();
        if (touch.Count == 0) return false;
        string touchSpell = Select(touch, actor.DurableId, generation, simulationStep, "touch");
        if (_casting.EffectsAlreadyOnTarget(touchSpell,
                TargetOf(touchSpell) == DaggerfallSpellTarget.CasterOnly ? actor.DurableId : target.DurableId)) return false;
        return TryBuildAttempt(actor, touchSpell, target, out DaggerfallEnemySpellAttempt touchAttempt)
            && TryExecute(touchAttempt, generation, simulationStep);
    }

    private IReadOnlyList<string> SpellKeys(DaggerfallActorDefinition definition) =>
        _spells.For(definition).Where(key => _catalog.Spells.ContainsKey(key)).ToArray();

    private DaggerfallMobileDefinition? _definitionMobile(int mobileId)
    {
        // The mobile catalog is exposed through the actor definition callback's owner.  A null
        // result is a conservative no-bow decision; the authored spell list remains authoritative.
        return _mobileCatalog(mobileId);
    }

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

    private bool TryExecute(DaggerfallEnemySpellAttempt attempt, ulong generation, ulong simulationStep)
    {
        DaggerfallCastResult result = _execute(attempt);
        _last[attempt.ActorId] = new(attempt.ActorId, attempt.SpellKey, attempt.Target, result.Outcome,
            generation, simulationStep);
        return result.Outcome is DaggerfallCastOutcome.Released or DaggerfallCastOutcome.DeliveryCompleted;
    }

    private string Select(IReadOnlyList<string> keys, long actorId, ulong generation, ulong simulationStep, string lane)
    {
        int index = Draw(actorId, generation, simulationStep, $"{lane}-selection:{keys.Count}", 0, keys.Count - 1);
        return keys[index];
    }

    private int Draw(long actorId, ulong generation, ulong simulationStep, string purpose, int minimum, int maximum) =>
        checked((int)_random.DrawKeyed(new KeyedRngRequest(
            CombatRandomKey.Seed,
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
