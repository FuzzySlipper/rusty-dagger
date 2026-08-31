namespace WorldRpg.Kit.Facts;

/// <summary>Marker for product facts delivered after an admitted simulation step.</summary>
public interface IWorldRpgFact;

/// <summary>
/// Delivers a stable snapshot. Facts appended by a reaction wait for the next admitted update.
/// </summary>
public sealed class FactBuffer<TFact> where TFact : IWorldRpgFact
{
    private List<TFact> _pending = [];

    public void Append(TFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        _pending.Add(fact);
    }

    public void Deliver(Action<TFact> react)
    {
        FactDelivery delivery = Prepare();
        try { delivery.Deliver(react); delivery.Commit(); }
        catch { delivery.Rollback(); throw; }
    }

    /// <summary>Retains one stable batch until its owner explicitly commits or rolls it back.</summary>
    public FactDelivery Prepare()
    {
        List<TFact> stable = _pending;
        _pending = [];
        return new FactDelivery(this, stable);
    }

    /// <summary>Archives stable batches across one admitted outer update for atomic replay.</summary>
    public FactTransaction BeginTransaction() => new(this);

    public sealed class FactTransaction
    {
        private readonly FactBuffer<TFact> owner;
        private readonly List<List<TFact>> batches = [];
        private bool completed;
        internal FactTransaction(FactBuffer<TFact> owner) => this.owner = owner;
        public void Deliver(Action<TFact> react)
        {
            ArgumentNullException.ThrowIfNull(react);
            if (completed) throw new InvalidOperationException("Fact transaction is complete.");
            List<TFact> stable = owner._pending;
            owner._pending = [];
            if (stable.Count == 0) return;
            batches.Add(stable);
            foreach (TFact fact in stable) react(fact);
        }
        public void Commit() { if (!completed) completed = true; }
        public void Rollback()
        {
            if (completed) return;
            for (int index = batches.Count - 1; index >= 0; index--) owner._pending.InsertRange(0, batches[index]);
            completed = true;
        }
    }

    public sealed class FactDelivery
    {
        private readonly FactBuffer<TFact> owner;
        private readonly List<TFact> facts;
        private bool completed;
        internal FactDelivery(FactBuffer<TFact> owner, List<TFact> facts) { this.owner = owner; this.facts = facts; }
        public void Deliver(Action<TFact> react)
        {
            ArgumentNullException.ThrowIfNull(react);
            if (completed) throw new InvalidOperationException("Fact delivery is complete.");
            foreach (TFact fact in facts) react(fact);
        }
        public void Commit() { if (!completed) completed = true; }
        public void Rollback() { if (!completed) { owner._pending.InsertRange(0, facts); completed = true; } }
    }
}
