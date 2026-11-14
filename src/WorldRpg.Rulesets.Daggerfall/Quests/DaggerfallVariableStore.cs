namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Scoped quest and world variables: the donor's 64 globals plus per-region and per-faction
/// variables that persist for unloaded owners. Reads answer defaults for untouched variables;
/// writes name only admitted addresses: an unknown global key or an out-of-range region or
/// faction owner is refused rather than stored, because a condition branching on a misspelled
/// variable would read policy from a miss. There is no ambient access: the session owns one
/// store and hands it to the quest, dungeon and talk owners that read it.
/// </summary>
public sealed class DaggerfallVariableStore
{
    private readonly IReadOnlyDictionary<string, int> _globalKeys;

    /// <summary>Uses the aliases admitted from the selected content pack.</summary>
    public DaggerfallVariableStore(IReadOnlyDictionary<string, int> globalKeys) =>
        _globalKeys = globalKeys ?? throw new ArgumentNullException(nameof(globalKeys));

    private readonly Dictionary<DaggerfallVariableAddress, bool> _values = new();

    /// <summary>Reads one variable: false when never written, the written value otherwise.</summary>
    public bool Read(DaggerfallVariableAddress address)
    {
        Validate(address);
        return _values.TryGetValue(address, out bool value) && value;
    }

    /// <summary>Reads one global by its donor name.</summary>
    public bool ReadGlobal(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_globalKeys.TryGetValue(name, out int key))
        {
            throw new ArgumentOutOfRangeException(nameof(name), name, "No quest global carries this name.");
        }

        return Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Global, 0, key));
    }

    /// <summary>Writes one variable; answers whether the value changed.</summary>
    public bool Write(DaggerfallVariableAddress address, bool value)
    {
        Validate(address);
        if (Read(address) == value)
        {
            return false;
        }

        _values[address] = value;
        return true;
    }

    /// <summary>Writes one global by its donor name.</summary>
    public bool WriteGlobal(string name, bool value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_globalKeys.TryGetValue(name, out int key))
        {
            throw new ArgumentOutOfRangeException(nameof(name), name, "No quest global carries this name.");
        }

        return Write(new DaggerfallVariableAddress(DaggerfallVariableScope.Global, 0, key), value);
    }

    /// <summary>Every written variable with its value, in a stable order, for the save owner.</summary>
    internal IReadOnlyList<(DaggerfallVariableAddress Address, bool Value)> Capture() =>
        [.. _values.OrderBy(entry => entry.Key.Scope).ThenBy(entry => entry.Key.Owner).ThenBy(entry => entry.Key.Key).Select(entry => (entry.Key, entry.Value))];

    /// <summary>Restores written variables; refuses an address the store would not accept live.</summary>
    internal void Restore(IEnumerable<(DaggerfallVariableAddress Address, bool Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values.Clear();
        foreach ((DaggerfallVariableAddress address, bool value) in values)
        {
            Validate(address);
            _values[address] = value;
        }
    }

    private static void Validate(DaggerfallVariableAddress address)
    {
        if (!Enum.IsDefined(address.Scope))
        {
            throw new ArgumentOutOfRangeException(nameof(address), address.Scope, "A variable names a scope the contract does not declare.");
        }

        switch (address.Scope)
        {
            case DaggerfallVariableScope.Global:
                if (address.Owner != 0 || address.Key is < 0 or >= DaggerfallVariableAddress.GlobalCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(address), address.Key, $"A quest global names key {address.Key} outside the donor's {DaggerfallVariableAddress.GlobalCount}.");
                }

                break;
            case DaggerfallVariableScope.Region:
                if (address.Owner is < 0 or > 61 || address.Key < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(address), address.Owner, "A region variable names no classic region or no key.");
                }

                break;
            case DaggerfallVariableScope.Faction:
                if (address.Owner < 0 || address.Key < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(address), address.Owner, "A faction variable names no faction or no key.");
                }

                break;
        }
    }
}
