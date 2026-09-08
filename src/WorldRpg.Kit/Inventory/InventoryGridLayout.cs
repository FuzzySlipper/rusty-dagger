namespace WorldRpg.Kit.Inventory;

/// <summary>Presentation positions only. Callers reconcile keys from authoritative inventory reads.</summary>
public sealed class InventoryGridLayout(int capacity)
{
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);
    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public ulong Revision { get; private set; }
    public IReadOnlyDictionary<string, int> Positions => new Dictionary<string, int>(_positions);
    public int? Position(string key) => _positions.TryGetValue(key, out int value) ? value : null;
    public string? At(int position) => _positions.FirstOrDefault(pair => pair.Value == position).Key;

    public void Reconcile(IEnumerable<string> currentKeys)
    {
        string[] keys = currentKeys.Distinct(StringComparer.Ordinal).ToArray();
        HashSet<string> current = new(keys, StringComparer.Ordinal);
        bool changed = false;
        foreach (string removed in _positions.Keys.Where(key => !current.Contains(key)).ToArray())
            changed |= _positions.Remove(removed);
        foreach (string key in keys.Where(key => !_positions.ContainsKey(key)))
        {
            int free = Enumerable.Range(0, Capacity).FirstOrDefault(index => !_positions.ContainsValue(index), -1);
            if (free < 0) break; // Overflow remains in the authoritative read, never silently discarded.
            _positions.Add(key, free);
            changed = true;
        }
        if (changed) Revision++;
    }

    public void Move(string key, int target)
    {
        if (target < 0 || target >= Capacity) throw new ArgumentOutOfRangeException(nameof(target));
        if (!_positions.TryGetValue(key, out int source)) throw new ArgumentException("The item has no grid position.", nameof(key));
        if (source == target) return;
        string? displaced = At(target);
        _positions[key] = target;
        if (displaced is not null) _positions[displaced] = source;
        Revision++;
    }

    public void Place(string key, int target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_positions.ContainsKey(key)) { Move(key, target); return; }
        if (target < 0 || target >= Capacity) throw new ArgumentOutOfRangeException(nameof(target));
        string? displaced = At(target);
        if (displaced is not null) _positions.Remove(displaced);
        _positions.Add(key, target);
        Revision++;
    }
}
