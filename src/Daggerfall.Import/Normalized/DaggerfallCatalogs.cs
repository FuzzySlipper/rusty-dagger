using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// Where a catalog record comes from: a record id the documented inventory carries, and
/// the path that inventory documents for it. A citation the inventory does not contain
/// is refused rather than published, so a catalog can never invent provenance.
/// </summary>
public sealed record DaggerfallCatalogSource(string RecordId, string Path)
{
    public void Validate(IReadOnlySet<string> inventoryRecordIds)
    {
        ArgumentNullException.ThrowIfNull(inventoryRecordIds);
        NormalizedImportDocument.RequireLogicalId(RecordId, nameof(RecordId));
        if (Path is null)
        {
            throw new ArgumentNullException(nameof(Path), $"Catalog source '{RecordId}' must state the documented path it came from.");
        }

        if (!inventoryRecordIds.Contains(RecordId))
        {
            throw new InvalidOperationException($"Catalog source '{RecordId}' is not a record the documented inventory carries.");
        }
    }
}

/// <summary>One indexed key of a classic index space: an attribute, skill or resistance.</summary>
public sealed record DaggerfallIndexedKey(string Id, int Index, DaggerfallCatalogSource Source)
{
    public void Validate(IReadOnlySet<string> inventoryRecordIds)
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (Index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Index), Index, $"Catalog key '{Id}' has a negative index.");
        }

        Source.Validate(inventoryRecordIds);
    }
}

/// <summary>
/// A key that names a record another catalog owns, without an index: an enemy the actor
/// catalog defines, or an item template the item catalog defines. The reference is what
/// a consumer resolves; the record itself belongs to its owning task.
/// </summary>
public sealed record DaggerfallReferenceKey(string Id, DaggerfallCatalogSource Source)
{
    public void Validate(IReadOnlySet<string> inventoryRecordIds)
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        Source.Validate(inventoryRecordIds);
    }
}

/// <summary>A playable race identity with the donor's own race value.</summary>
public sealed record DaggerfallRaceKey(string Id, int DonorRaceId, DaggerfallCatalogSource Source)
{
    public void Validate(IReadOnlySet<string> inventoryRecordIds)
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (DonorRaceId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DonorRaceId), DonorRaceId, $"Race '{Id}' must carry the donor's positive race value.");
        }

        Source.Validate(inventoryRecordIds);
    }
}

/// <summary>
/// One decoded career: the classic record's identity, the skills and attributes it names
/// by key, and the elements it resists or is immune to. The references are keys so a
/// dangling one is a validation error rather than a value nobody can resolve.
/// </summary>
public sealed record DaggerfallCareerRecord(
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
    DaggerfallCatalogSource Source)
{
    public IEnumerable<string> SkillReferences => PrimarySkills.Concat(MajorSkills).Concat(MinorSkills);

    public void Validate(IReadOnlySet<string> inventoryRecordIds, IReadOnlySet<string> skillKeys, IReadOnlySet<string> attributeKeys, IReadOnlySet<string> elementKeys)
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException($"Career '{Id}' must carry its decoded name.", nameof(Name));
        }

        if (HitPointsPerLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HitPointsPerLevel), HitPointsPerLevel, $"Career '{Id}' must carry positive hit points per level.");
        }

        if (!(AdvancementMultiplier > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(AdvancementMultiplier), AdvancementMultiplier, $"Career '{Id}' must carry a positive advancement multiplier.");
        }

        NormalizedImportDocument.ValidateUnique(PrimarySkills, value => value, $"career '{Id}' primary skill");
        NormalizedImportDocument.ValidateUnique(MajorSkills, value => value, $"career '{Id}' major skill");
        NormalizedImportDocument.ValidateUnique(MinorSkills, value => value, $"career '{Id}' minor skill");
        NormalizedImportDocument.ValidateUnique(SkillReferences, value => value, $"career '{Id}' skill");
        RequireReferences(SkillReferences, skillKeys, $"career '{Id}' names skill");
        RequireReferences(ResistanceElements, elementKeys, $"career '{Id}' resists element");
        RequireReferences(ImmunityElements, elementKeys, $"career '{Id}' is immune to element");
        RequireReferences(Attributes, attributeKeys, $"career '{Id}' names attribute");
        Source.Validate(inventoryRecordIds);
    }

    private static void RequireReferences(IEnumerable<string> values, IReadOnlySet<string> candidates, string owner)
    {
        foreach (string value in values)
        {
            if (!candidates.Contains(value))
            {
                throw new InvalidOperationException($"{owner} '{value}', which the catalog does not carry.");
            }
        }
    }
}

/// <summary>A key a later task supplies the records for, with the task that owns it.</summary>
public sealed record DaggerfallPendingCatalog(string Id, int OwnerTask, string Reason)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (OwnerTask <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OwnerTask), OwnerTask, $"Pending catalog '{Id}' must name the task that supplies it.");
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            throw new ArgumentException($"Pending catalog '{Id}' must state why it is pending.", nameof(Reason));
        }
    }
}

/// <summary>
/// The normalized reference catalogs a runtime consumer resolves keys through. This is
/// the contract CAP-DATA is satisfied by: every key is indexed, carries inventory-backed
/// provenance, and refers to other keys rather than to source bytes.
/// </summary>
public sealed record DaggerfallCatalogs(
    int SchemaVersion,
    IReadOnlyList<DaggerfallIndexedKey> Attributes,
    IReadOnlyList<DaggerfallIndexedKey> Skills,
    IReadOnlyList<DaggerfallIndexedKey> Resistances,
    IReadOnlyList<DaggerfallRaceKey> Races,
    IReadOnlyList<DaggerfallCareerRecord> Careers,
    IReadOnlyList<string> CareerNameCollisions,
    IReadOnlyList<DaggerfallReferenceKey> Enemies,
    IReadOnlyList<DaggerfallReferenceKey> ItemTemplates,
    IReadOnlyList<DaggerfallPendingCatalog> Pending)
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>The eight classic attributes a career record carries a value for.</summary>
    public const int ClassicAttributeCount = 8;

    /// <summary>The five classic resistance elements.</summary>
    public const int ElementCount = 5;

    /// <summary>The named element keys, in classic index order.</summary>
    public static readonly string[] ElementKeys = ["fire", "frost", "disease-or-poison", "shock", "magic"];

    public void Validate(IReadOnlySet<string> inventoryRecordIds)
    {
        ArgumentNullException.ThrowIfNull(inventoryRecordIds);
        NormalizedImportDocument.RequireSchemaVersion(SchemaVersion, nameof(SchemaVersion));
        ArgumentNullException.ThrowIfNull(Attributes);
        ArgumentNullException.ThrowIfNull(Skills);
        ArgumentNullException.ThrowIfNull(Resistances);
        ArgumentNullException.ThrowIfNull(Races);
        ArgumentNullException.ThrowIfNull(Careers);
        ArgumentNullException.ThrowIfNull(Enemies);
        ArgumentNullException.ThrowIfNull(ItemTemplates);
        ArgumentNullException.ThrowIfNull(Pending);

        ValidateIndexed(Attributes, "attribute", inventoryRecordIds);
        ValidateIndexed(Skills, "skill", inventoryRecordIds);
        ValidateIndexed(Resistances, "resistance", inventoryRecordIds);
        if (Resistances.Count != ElementCount)
        {
            throw new InvalidOperationException($"The resistance catalog carries {Resistances.Count} keys where the classic carrier has {ElementCount}.");
        }

        Dictionary<string, DaggerfallIndexedKey> attributes = Attributes.ToDictionary(key => key.Id, StringComparer.Ordinal);
        Dictionary<string, DaggerfallIndexedKey> skills = Skills.ToDictionary(key => key.Id, StringComparer.Ordinal);
        Dictionary<string, DaggerfallIndexedKey> elements = Resistances.ToDictionary(key => key.Id, StringComparer.Ordinal);

        NormalizedImportDocument.ValidateUnique(Races, race => race.Id, "race");
        NormalizedImportDocument.ValidateUnique(Races, race => race.DonorRaceId.ToString(), "race donor value");
        foreach (DaggerfallRaceKey race in Races)
        {
            race.Validate(inventoryRecordIds);
        }

        NormalizedImportDocument.ValidateUnique(Careers, career => career.Id, "career");
        // Two supplied career records share the name "Knight", so a name is display data
        // and the carrier's own file identity is the key. The collision is recorded
        // rather than assumed away, and a consumer resolving by name can see it.
        string[] collisions = [.. Careers
            .GroupBy(career => career.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)];
        ArgumentNullException.ThrowIfNull(CareerNameCollisions);
        if (!CareerNameCollisions.Order(StringComparer.Ordinal).SequenceEqual(collisions, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"The career name collisions the catalog records are [{string.Join(", ", CareerNameCollisions)}] where the careers carry [{string.Join(", ", collisions)}].");
        }

        foreach (DaggerfallCareerRecord career in Careers)
        {
            career.Validate(inventoryRecordIds, skills.Keys.ToHashSet(StringComparer.Ordinal), attributes.Keys.ToHashSet(StringComparer.Ordinal), elements.Keys.ToHashSet(StringComparer.Ordinal));
            if (career.Attributes.Count != ClassicAttributeCount)
            {
                throw new InvalidOperationException($"Career '{career.Id}' carries {career.Attributes.Count} attribute values where the classic record carries {ClassicAttributeCount}.");
            }
        }

        ValidateReferenced(Enemies, "enemy", inventoryRecordIds);
        ValidateReferenced(ItemTemplates, "item template", inventoryRecordIds);
        NormalizedImportDocument.ValidateUnique(Pending, value => value.Id, "pending catalog");
        foreach (DaggerfallPendingCatalog pending in Pending)
        {
            pending.Validate();
        }
    }

    private static void ValidateReferenced(IReadOnlyList<DaggerfallReferenceKey> keys, string kind, IReadOnlySet<string> inventoryRecordIds)
    {
        NormalizedImportDocument.ValidateUnique(keys, key => key.Id, kind);
        foreach (DaggerfallReferenceKey key in keys)
        {
            key.Validate(inventoryRecordIds);
        }
    }

    private static void ValidateIndexed(IReadOnlyList<DaggerfallIndexedKey> keys, string kind, IReadOnlySet<string> inventoryRecordIds)
    {
        NormalizedImportDocument.ValidateUnique(keys, key => key.Id, kind);
        NormalizedImportDocument.ValidateUnique(keys, key => key.Index.ToString(), $"{kind} index");
        foreach (DaggerfallIndexedKey key in keys)
        {
            key.Validate(inventoryRecordIds);
        }

        int[] indices = keys.Select(key => key.Index).Order().ToArray();
        for (int expected = 0; expected < indices.Length; expected++)
        {
            if (indices[expected] != expected)
            {
                throw new InvalidOperationException($"The {kind} catalog indices must be contiguous from zero; index {expected} is missing.");
            }
        }
    }
}

/// <summary>
/// Canonical JSON for <see cref="DaggerfallCatalogs"/>. The pack carries this shape and
/// the ruleset reads it, so both sides of the contract agree on one spelling.
/// </summary>
public static class DaggerfallCatalogSerializer
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    };

    public static byte[] Serialize(DaggerfallCatalogs catalogs, IReadOnlySet<string> inventoryRecordIds)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        catalogs.Validate(inventoryRecordIds);
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(catalogs, Options);
    }
}
