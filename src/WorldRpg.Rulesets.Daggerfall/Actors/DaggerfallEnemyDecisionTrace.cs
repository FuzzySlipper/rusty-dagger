using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>How one gate of an enemy's decision came out: it let the decision through, stopped it, or picked among choices.</summary>
internal enum DaggerfallGateOutcome { Passed, Refused, Chose }

/// <summary>One value a gate weighed: a roll and the range it was drawn in, a distance, a reach, a threshold.</summary>
internal readonly record struct DaggerfallGateValue(string Name, double Value);

/// <summary>
/// One gate of an enemy's decision: the policy that asked it (perception, target, motor, attack, spell, control,
/// override), the gate's name, its outcome, a short reason code and the values it weighed. Any policy that decides for
/// an enemy reports its gates in this shape, so a trace reads the same whichever policy chose.
/// </summary>
internal sealed record DaggerfallDecisionGate(string Policy, string Gate, DaggerfallGateOutcome Outcome, string Reason,
    params DaggerfallGateValue[] Values)
{
    /// <summary>Whether two gates said the same thing, whatever values they weighed.</summary>
    internal bool SameAs(DaggerfallDecisionGate other) =>
        Policy == other.Policy && Gate == other.Gate && Outcome == other.Outcome && Reason == other.Reason;
}

/// <summary>One decision of the traced enemy: its gates in the order asked and the state it ended in, repeated over the steps from <see cref="FirstStep"/> to <see cref="LastStep"/>.</summary>
internal sealed class DaggerfallDecisionRecord(ulong generation, ulong step, EnemyBehaviorState state, IReadOnlyList<DaggerfallDecisionGate> gates)
{
    internal ulong Generation { get; private set; } = generation;
    internal ulong FirstStep { get; } = step;
    internal ulong LastStep { get; private set; } = step;
    internal int Repeats { get; private set; } = 1;
    internal EnemyBehaviorState State { get; } = state;
    /// <summary>The gates of the latest repeat, so their values are the newest.</summary>
    internal IReadOnlyList<DaggerfallDecisionGate> Gates { get; private set; } = gates;

    internal bool SameAs(EnemyBehaviorState state, IReadOnlyList<DaggerfallDecisionGate> gates) =>
        State == state && Gates.Count == gates.Count && Gates.Zip(gates).All(pair => pair.First.SameAs(pair.Second));

    internal void Repeat(ulong generation, ulong step, IReadOnlyList<DaggerfallDecisionGate> gates)
    {
        Generation = generation;
        LastStep = step;
        Repeats++;
        Gates = gates;
    }
}

/// <summary>
/// The opt-in decision trace of one enemy (<c>actor.trace</c>): a short ring of its recent decisions, a decision that
/// repeats folded into the one before it. Only the followed actor opens a gate list; every other actor gets none, so
/// nothing is recorded or allocated while the trace is off. Beside its decisions the ring holds what became of each
/// of its strikes and casts, the target it meant beside the one it reached (<see cref="Observe"/>), so a blow that
/// fell on a bystander shows as one. Run-only: never saved.
/// </summary>
internal sealed class DaggerfallEnemyDecisionTrace
{
    internal const int Capacity = 16;
    private readonly LinkedList<DaggerfallDecisionRecord> _ring = new();
    /// <summary>The target each of the traced actor's strikes was started at, by the step it started on.</summary>
    private readonly Dictionary<ulong, long> _aimedAt = [];
    /// <summary>The target each of the traced actor's casts was meant for, by the cast's sequence, oldest first.</summary>
    private readonly Dictionary<long, long> _castFor = [];
    private readonly Queue<long> _casts = new();

    /// <summary>The actor being traced, or null when the trace is off.</summary>
    internal long? ActorId { get; private set; }

    /// <summary>Recent decisions, oldest first.</summary>
    internal IReadOnlyCollection<DaggerfallDecisionRecord> Recent => _ring;

    internal DaggerfallDecisionRecord? Latest => _ring.Last?.Value;

    /// <summary>Follows one actor from now, forgetting what was recorded of another; null turns the trace off.</summary>
    internal void Follow(long? actorId)
    {
        if (ActorId == actorId) return;
        ActorId = actorId;
        _ring.Clear();
        _aimedAt.Clear();
        _castFor.Clear();
        _casts.Clear();
    }

    /// <summary>A gate list for this actor's decision when it is the one traced; null otherwise.</summary>
    internal List<DaggerfallDecisionGate>? Open(long actorId) => ActorId == actorId ? [] : null;

    /// <summary>Records a decision opened with <see cref="Open"/>.</summary>
    internal void Close(List<DaggerfallDecisionGate>? gates, EnemyBehaviorState state, ulong generation, ulong step)
    {
        if (gates is null) return;
        foreach (DaggerfallDecisionGate cast in gates.Where(gate => gate.Policy == "spell" && gate.Gate == "cast"))
            if (cast.Values.FirstOrDefault(value => value.Name == "sequence") is { Name: not null } sequence
                && cast.Values.FirstOrDefault(value => value.Name == "intended") is { Name: not null } intended)
                RememberCast((long)sequence.Value, (long)intended.Value);
        if (Latest is { } latest && latest.SameAs(state, gates))
        {
            latest.Repeat(generation, step, gates);
            return;
        }
        Add(new DaggerfallDecisionRecord(generation, step, state, gates));
    }

    /// <summary>
    /// Hears a delivered fact: what became of the traced actor's strikes (hit, turned aside, whiffed, stopped by cover)
    /// and casts, each as one delivery gate with the target meant and the one reached. Contact delivery's own facts
    /// carry both, so nothing here asks combat or magic.
    /// </summary>
    internal void Observe(IProductFact fact)
    {
        if (ActorId is not long traced) return;
        switch (fact)
        {
            case EnemyAttackStartedFact started when started.AttackerId == traced:
                _aimedAt[started.OriginatingSimulationStep] = started.TargetId;
                break;
            case AttackHitFact hit when hit.AttackerId == traced:
                Strike(hit.OriginatingGeneration, hit.OriginatingSimulationStep, hit.TargetId, "hit", new DaggerfallGateValue("healthLost", hit.ActualHealthLost));
                break;
            case AttackNegatedFact negated when negated.AttackerId == traced:
                Strike(negated.OriginatingGeneration, negated.OriginatingSimulationStep, negated.TargetId, $"negated-{negated.Negation.ToString().ToLowerInvariant()}");
                break;
            case AttackWhiffedFact whiffed when whiffed.AttackerId == traced:
                Strike(whiffed.OriginatingGeneration, whiffed.OriginatingSimulationStep, null, "whiffed");
                break;
            case RangedShotBlockedFact blocked when blocked.AttackerId == traced:
                Strike(blocked.OriginatingGeneration, blocked.OriginatingSimulationStep, null, "blocked-by-cover");
                break;
            case SpellCastFact { CasterId: long caster } cast when caster == traced:
                // A cast keeps the target it was meant for through its later delivery facts; one released outside a
                // traced decision has none.
                long? meant = cast.Sequence is long sequence && _castFor.TryGetValue(sequence, out long aimed) ? aimed : null;
                long[] reached = [.. cast.Effects.Select(effect => effect.TargetId).OfType<long>().Distinct()];
                Delivery("cast", Latest?.Generation ?? 0, Latest?.LastStep ?? 0, meant, reached.Length == 0 ? null : reached.Contains(meant ?? 0) ? meant : reached[0],
                    reached.Length == 0 ? $"reached-no-one:{cast.Outcome}" : cast.Outcome.ToString(), new DaggerfallGateValue("targetsReached", reached.Length));
                break;
        }
    }

    private void RememberCast(long sequence, long intended)
    {
        if (_castFor.TryAdd(sequence, intended)) _casts.Enqueue(sequence);
        if (_casts.Count > Capacity) _castFor.Remove(_casts.Dequeue());
    }

    private void Strike(ulong generation, ulong step, long? struck, string outcome, params DaggerfallGateValue[] values)
    {
        long? intended = _aimedAt.Remove(step, out long aimed) ? aimed : null;
        Delivery("strike", generation, step, intended, struck, outcome, values);
    }

    /// <summary>One delivery: Passed when it reached the target meant, Refused when it reached no one or someone else.</summary>
    private void Delivery(string gate, ulong generation, ulong step, long? intended, long? reached, string outcome, params DaggerfallGateValue[] values)
    {
        bool bystander = reached is not null && intended is not null && reached != intended;
        DaggerfallDecisionGate delivered = new("delivery", gate, reached is not null && !bystander ? DaggerfallGateOutcome.Passed : DaggerfallGateOutcome.Refused,
            bystander ? $"{outcome}:bystander" : outcome,
            [new("intended", intended ?? 0), new("reached", reached ?? 0), .. values]);
        Add(new DaggerfallDecisionRecord(generation, step, Latest?.State ?? EnemyBehaviorState.Idle, [delivered]));
    }

    private void Add(DaggerfallDecisionRecord record)
    {
        _ring.AddLast(record);
        if (_ring.Count > Capacity) _ring.RemoveFirst();
    }
}
