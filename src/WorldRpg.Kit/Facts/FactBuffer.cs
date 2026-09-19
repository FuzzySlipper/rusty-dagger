namespace WorldRpg.Kit.Facts;

/// <summary>Marker for product facts delivered after an admitted simulation step.</summary>
public interface IWorldRpgFact;

/// <summary>
/// Delivers each stable batch once. Facts appended by a reaction wait for the next delivery.
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
        List<TFact> stable = _pending;
        _pending = [];
        ArgumentNullException.ThrowIfNull(react);
        foreach (TFact fact in stable) react(fact);
    }
}
