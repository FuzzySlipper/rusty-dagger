namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Where a published catalog record came from: a record id the documented inventory
/// carries, and the path that inventory documents. The import tool refuses a citation
/// the inventory does not contain, so a published catalog cannot invent provenance.
/// </summary>
internal sealed record DaggerfallCatalogCitation(string SourceRecordId, string Path);

/// <summary>One indexed key of a classic index space: an attribute, skill or resistance.</summary>
internal sealed record DaggerfallCatalogKey(string Id, int Index, DaggerfallCatalogCitation Source);

/// <summary>A playable race identity with the donor's own race value.</summary>
internal sealed record DaggerfallRaceDefinition(string Id, int DonorRaceId, DaggerfallCatalogCitation Source);

/// <summary>
/// One decoded career: the classic record's identity, the skills and attributes it names
/// by key, and the elements it resists or is immune to. The skill and element lists omit
/// the carrier's terminal value, which names no skill.
/// </summary>
internal sealed record DaggerfallCareerDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> PrimarySkills,
    IReadOnlyList<string> MajorSkills,
    IReadOnlyList<string> MinorSkills,
    IReadOnlyList<string> Attributes,
    int HitPointsPerLevel,
    float AdvancementMultiplier,
    IReadOnlyList<string> ResistanceElements,
    IReadOnlyList<string> ImmunityElements,
    int ResistanceFlags,
    int ImmunityFlags,
    int LowToleranceFlags,
    int CriticalWeaknessFlags,
    DaggerfallCatalogCitation Source)
{
    internal IEnumerable<string> SkillReferences => PrimarySkills.Concat(MajorSkills).Concat(MinorSkills);

    /// <summary>
    /// The four classic effect-flag bytes as published. The element lists are the
    /// interpretation this contract owns; the bytes are kept because they carry more
    /// than elements — paralysis and the low-tolerance and critical-weakness flags have
    /// no key here yet, and a later task needs them without reopening a source file.
    /// </summary>
    internal IEnumerable<(string Name, int Value)> FlagBytes =>
    [
        ("resistanceFlags", ResistanceFlags),
        ("immunityFlags", ImmunityFlags),
        ("lowToleranceFlags", LowToleranceFlags),
        ("criticalWeaknessFlags", CriticalWeaknessFlags),
    ];
}

/// <summary>A key that names a record another catalog owns.</summary>
internal sealed record DaggerfallCatalogReference(string Id, DaggerfallCatalogCitation Source);

/// <summary>A namespace this contract declares and a named later task supplies.</summary>
internal sealed record DaggerfallPendingCatalogDefinition(string Id, int OwnerTask, string Reason);

/// <summary>
/// The normalized reference catalogs a runtime consumer resolves keys through. Runtime
/// code reads these records and never opens a source file: the keys, their indices and
/// their provenance are all published data.
/// </summary>
internal sealed class DaggerfallCatalogSet(
    IReadOnlyList<DaggerfallCatalogKey> attributes,
    IReadOnlyList<DaggerfallCatalogKey> skills,
    IReadOnlyList<DaggerfallCatalogKey> resistances,
    IReadOnlyList<DaggerfallRaceDefinition> races,
    IReadOnlyList<DaggerfallCareerDefinition> careers,
    IReadOnlyList<string> careerNameCollisions,
    IReadOnlyList<DaggerfallCatalogReference> enemies,
    IReadOnlyList<DaggerfallCatalogReference> itemTemplates,
    IReadOnlyList<DaggerfallPendingCatalogDefinition> pending,
    IReadOnlyList<string> sourceRecords)
{
    /// <summary>The classic element keys, in the index order the catalog publishes.</summary>
    internal static readonly string[] ElementKeys = ["fire", "frost", "disease-or-poison", "shock", "magic"];

    internal IReadOnlyList<DaggerfallCatalogKey> Attributes { get; } = Array.AsReadOnly(attributes.ToArray());

    internal IReadOnlyList<DaggerfallCatalogKey> Skills { get; } = Array.AsReadOnly(skills.ToArray());

    internal IReadOnlyList<DaggerfallCatalogKey> Resistances { get; } = Array.AsReadOnly(resistances.ToArray());

    internal IReadOnlyList<DaggerfallRaceDefinition> Races { get; } = Array.AsReadOnly(races.ToArray());

    internal IReadOnlyList<DaggerfallCareerDefinition> Careers { get; } = Array.AsReadOnly(careers.ToArray());

    /// <summary>Career names more than one supplied record carries, so a name is not a key.</summary>
    internal IReadOnlyList<string> CareerNameCollisions { get; } = Array.AsReadOnly(careerNameCollisions.ToArray());

    internal IReadOnlyList<DaggerfallCatalogReference> Enemies { get; } = Array.AsReadOnly(enemies.ToArray());

    internal IReadOnlyList<DaggerfallCatalogReference> ItemTemplates { get; } = Array.AsReadOnly(itemTemplates.ToArray());

    internal IReadOnlyList<DaggerfallPendingCatalogDefinition> Pending { get; } = Array.AsReadOnly(pending.ToArray());

    /// <summary>
    /// The documented inventory records the pack says it drew from. A citation outside
    /// this set is refused, so a consumer can check provenance without the inventory.
    /// </summary>
    internal IReadOnlyList<string> SourceRecords { get; } = Array.AsReadOnly(sourceRecords.ToArray());

    private Dictionary<string, DaggerfallCareerDefinition> CareersById { get; } =
        careers.ToDictionary(career => career.Id, StringComparer.Ordinal);

    private Dictionary<string, DaggerfallRaceDefinition> RacesById { get; } =
        races.ToDictionary(race => race.Id, StringComparer.Ordinal);

    internal bool TryGetCareer(string id, out DaggerfallCareerDefinition career) => CareersById.TryGetValue(id, out career!);

    internal DaggerfallCareerDefinition RequireCareer(string id) =>
        CareersById.TryGetValue(id, out DaggerfallCareerDefinition? career)
            ? career
            : throw new InvalidOperationException($"The published catalogs carry no career '{id}'.");

    internal bool TryGetRace(string id, out DaggerfallRaceDefinition race) => RacesById.TryGetValue(id, out race!);

    internal DaggerfallRaceDefinition RequireRace(string id) =>
        RacesById.TryGetValue(id, out DaggerfallRaceDefinition? race)
            ? race
            : throw new InvalidOperationException($"The published catalogs carry no race '{id}'.");

    internal DaggerfallCareerDefinition RequireCareerByName(string name)
    {
        DaggerfallCareerDefinition[] matches = [.. Careers.Where(career => StringComparer.Ordinal.Equals(career.Name, name))];
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(matches.Length == 0
                ? $"The published catalogs carry no career named '{name}'."
                : $"The published catalogs carry {matches.Length} careers named '{name}'; resolve by career id instead.");
    }
}
