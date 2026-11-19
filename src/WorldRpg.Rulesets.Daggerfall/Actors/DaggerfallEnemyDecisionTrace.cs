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
/// nothing is recorded or allocated while the trace is off. Run-only: never saved.
/// </summary>
internal sealed class DaggerfallEnemyDecisionTrace
{
    internal const int Capacity = 16;
    private readonly LinkedList<DaggerfallDecisionRecord> _ring = new();

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
    }

    /// <summary>A gate list for this actor's decision when it is the one traced; null otherwise.</summary>
    internal List<DaggerfallDecisionGate>? Open(long actorId) => ActorId == actorId ? [] : null;

    /// <summary>Records a decision opened with <see cref="Open"/>.</summary>
    internal void Close(List<DaggerfallDecisionGate>? gates, EnemyBehaviorState state, ulong generation, ulong step)
    {
        if (gates is null) return;
        if (Latest is { } latest && latest.SameAs(state, gates))
        {
            latest.Repeat(generation, step, gates);
            return;
        }
        _ring.AddLast(new DaggerfallDecisionRecord(generation, step, state, gates));
        if (_ring.Count > Capacity) _ring.RemoveFirst();
    }
}
