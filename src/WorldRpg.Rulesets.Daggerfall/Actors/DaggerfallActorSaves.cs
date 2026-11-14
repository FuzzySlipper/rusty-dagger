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

/// <summary>The player actor's saved pose, stats and drawn weapon.</summary>
internal sealed record DaggerfallPlayerSave(float X, float Y, float Z, float YawRadians, float PitchRadians, DaggerfallStatsSave Stats)
{
    /// <summary>Whether the player held the weapon drawn; a sheathed weapon stays sheathed across a load.</summary>
    [JsonRequired] public bool WeaponDrawn { get; init; } = true;
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(YawRadians) || !float.IsFinite(PitchRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved player pose must be finite.");
        ArgumentNullException.ThrowIfNull(Stats);
    }
}

/// <summary>One persisted NPC: its identity, kind, site, appearance, role and presence.</summary>
internal sealed record DaggerfallNpcEntry(
    long DurableId,
    int Kind,
    string StableKey,
    int Region,
    string Location,
    string Building,
    string Race,
    string Gender,
    int BillboardArchive,
    int BillboardRecord,
    int NameSeed,
    int FactionId,
    string Role,
    string[] Services,
    int Presence,
    float? X,
    float? Y,
    float? Z)
{
    [JsonRequired] public DaggerfallWorldProfileKeySave? Profile { get; init; }
    [JsonRequired] public string? DisplayName { get; init; }
    [JsonRequired] public string? ProfileId { get; init; }
}

/// <summary>The session's NPCs in durable order.</summary>
internal sealed record DaggerfallNpcSave(DaggerfallNpcEntry[] Entries)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Entries);
        HashSet<long> identities = [];
        foreach (DaggerfallNpcEntry entry in Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.DurableId <= 0 || !identities.Add(entry.DurableId))
                throw new ArgumentException("Saved NPC identities must be positive and unique.", nameof(Entries));
            if (!Enum.IsDefined((DaggerfallNpcKind)entry.Kind) || !Enum.IsDefined((DaggerfallNpcPresence)entry.Presence))
            {
                throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, "A saved NPC names a kind or presence the contract does not declare.");
            }

            ArgumentNullException.ThrowIfNull(entry.Services);
            if (entry.Profile is { } profile) _ = profile.Require();
            // A detached pose is whole and finite; a bound NPC without one is posed by its saved actor.
            if ((entry.X is null) != (entry.Y is null) || (entry.X is null) != (entry.Z is null)
                || entry.X is float x && !float.IsFinite(x) || entry.Y is float y && !float.IsFinite(y)
                || entry.Z is float z && !float.IsFinite(z))
                throw new ArgumentException($"Saved NPC {entry.DurableId} has no finite profile position.");
            if (entry.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(entry.DisplayName);
        }
    }
}
