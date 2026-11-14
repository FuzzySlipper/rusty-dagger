using System.Text.Json.Serialization;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>An authored placement's saved actor: its pose, stats and the states a site keeps for it.</summary>
internal sealed record DaggerfallActorSave(long EntityId, float X, float Y, float Z, float HeadingRadians, DaggerfallStatsSave Stats)
{
    public string? WabbajackDefinition { get; init; }
    public bool ForcedHostile { get; init; }
    [JsonRequired] public bool LanguagePacified { get; init; }
    [JsonRequired] public bool MagicallyPacified { get; init; }
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(HeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved actor pose must be finite.");
        ArgumentNullException.ThrowIfNull(Stats);
    }
}

/// <summary>
/// One dynamically spawned actor: the definition it was registered from plus its pose and full
/// Engine stat meaning. Authored placement actors need no definition reference because the
/// selected content already names theirs.
/// </summary>
internal sealed record DaggerfallDynamicActorSave(long EntityId, string Definition, float X, float Y, float Z, float HeadingRadians, DaggerfallStatsSave Stats)
{
    [JsonRequired] public int Level { get; init; }
    [JsonRequired] public int SpawnDistance { get; init; }
    [JsonRequired] public bool Passive { get; init; }
    public bool WabbajackActive { get; init; }
    public bool ForcedHostile { get; init; }
    [JsonRequired] public bool LanguagePacified { get; init; }
    [JsonRequired] public bool MagicallyPacified { get; init; }
    public bool PlayerAllied { get; init; }
    public DaggerfallCorruptionOrigin? CorruptionOrigin { get; init; }
    /// <summary>The atlas gender chosen at spawn; absent for an actor without mobile presentation.</summary>
    public string? Gender { get; init; }
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(HeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved dynamic actor pose must be finite.");
        if (string.IsNullOrWhiteSpace(Definition))
            throw new ArgumentException("A saved dynamic actor must name its definition.", nameof(Definition));
        ArgumentNullException.ThrowIfNull(Stats);
        if (Level < 1) throw new ArgumentOutOfRangeException(nameof(Level), "Saved actor level must be positive.");
        if (SpawnDistance is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(SpawnDistance));
        CorruptionOrigin?.Validate();
    }
}
