namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Which context a variable belongs to: the meanings never mix.</summary>
public enum DaggerfallVariableScope
{
    /// <summary>One of the 64 quest globals the donor numbers 0 through 63.</summary>
    Global,
    /// <summary>A variable kept per classic region, addressed by region first.</summary>
    Region,
    /// <summary>A variable kept per faction identity, addressed by faction first.</summary>
    Faction,
}

/// <summary>One variable address: its scope, its owner inside the scope, and its key.</summary>
/// <param name="Scope">The scope the key lives in.</param>
/// <param name="Owner">The region or faction identity; zero for globals.</param>
/// <param name="Key">The numeric key, identical keys in different scopes being different variables.</param>
public readonly record struct DaggerfallVariableAddress(DaggerfallVariableScope Scope, int Owner, int Key);

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
    /// <summary>How many globals the donor numbers.</summary>
    public const int GlobalCount = 64;

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
                if (address.Owner != 0 || address.Key is < 0 or >= GlobalCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(address), address.Key, $"A quest global names key {address.Key} outside the donor's {GlobalCount}.");
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
