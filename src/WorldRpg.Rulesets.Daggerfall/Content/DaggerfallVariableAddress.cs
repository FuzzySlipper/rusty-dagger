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
public readonly record struct DaggerfallVariableAddress(DaggerfallVariableScope Scope, int Owner, int Key)
{
    /// <summary>How many globals the donor numbers.</summary>
    public const int GlobalCount = 64;
}
