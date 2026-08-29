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
        for (int index = 0; index < stable.Count; index++)
        {
            try { react(stable[index]); }
            catch
            {
                // Preserve the failed fact and its untouched successors ahead of facts
                // appended by earlier reactions; callers may retry without losing work.
                _pending.InsertRange(0, stable.Skip(index));
                throw;
            }
        }
    }
}
