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
        ArgumentNullException.ThrowIfNull(react);
        List<TFact> stable = _pending;
        _pending = [];
        foreach (TFact fact in stable) react(fact);
    }
}
