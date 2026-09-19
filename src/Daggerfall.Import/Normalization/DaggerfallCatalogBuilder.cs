using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// Builds the normalized reference catalogs from what the documented inventory admits:
/// the eight playable races the inventory names, the classic attribute, skill and
/// element index spaces the career carrier uses, the careers decoded from the supplied
/// CLASS*.CFG records, and references to the enemy and item keys other catalogs own.
/// Every citation is checked against the inventory, so a catalog cannot cite a source
/// the repository does not document.
/// </summary>
public static class DaggerfallCatalogBuilder
{
    /// <summary>Inventory families the catalogs cite.</summary>
    public const string RaceFamily = "CNT-009";
    public const string CareerFamily = "CNT-010";
    public const string EnemyFamily = "CNT-007";
    public const string ItemTemplateFamily = "CNT-011";

    /// <summary>Tasks that supply the namespaces this contract declares but does not fill.</summary>
    public const int FactionCatalogOwnerTask = 7967;
    public const int RegionCatalogOwnerTask = 7938;

    public static DaggerfallCatalogs Build(
        IReadOnlyList<SourceInventoryRow> inventory,
        IReadOnlyList<string> vocabularyAttributes,
        IReadOnlyList<string> vocabularySkills,
        IReadOnlyList<(string FileName, byte[] Bytes)> careers,
        IReadOnlyList<string> enemyIds,
        IReadOnlyList<string> itemTemplateIds)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(vocabularyAttributes);
        ArgumentNullException.ThrowIfNull(vocabularySkills);
        ArgumentNullException.ThrowIfNull(careers);
        ArgumentNullException.ThrowIfNull(enemyIds);
        ArgumentNullException.ThrowIfNull(itemTemplateIds);
        IReadOnlySet<string> inventoryIds = inventory.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        DaggerfallCatalogSource family(string id, string path) => new(id, path);

        SourceInventoryRow careerFamily = RequireFamily(inventory, CareerFamily);
        SourceInventoryRow raceFamily = RequireFamily(inventory, RaceFamily);
        (string EnemySourceId, string EnemySourcePath) = FamilyCitation(inventory, EnemyFamily);
        (string ItemSourceId, string ItemSourcePath) = FamilyCitation(inventory, ItemTemplateFamily);
        if (vocabularySkills.Count != ClassCfgDecoder.SkillCount)
        {
            throw new InvalidOperationException($"The skill vocabulary carries {vocabularySkills.Count} keys where the classic index space has {ClassCfgDecoder.SkillCount}.");
        }

        if (vocabularyAttributes.Count < DaggerfallCatalogs.ClassicAttributeCount)
        {
            throw new InvalidOperationException($"The attribute vocabulary carries {vocabularyAttributes.Count} keys where a career record names {DaggerfallCatalogs.ClassicAttributeCount}.");
        }

        DaggerfallCatalogSource careerCitation = family(CareerFamily, careerFamily.PathOrPattern);
        List<DaggerfallIndexedKey> attributes = [];
        for (int index = 0; index < DaggerfallCatalogs.ClassicAttributeCount; index++)
        {
            attributes.Add(new DaggerfallIndexedKey(vocabularyAttributes[index], index, careerCitation));
        }

        List<DaggerfallIndexedKey> skills = [];
        for (int index = 0; index < vocabularySkills.Count; index++)
        {
            skills.Add(new DaggerfallIndexedKey(vocabularySkills[index], index, careerCitation));
        }

        List<DaggerfallIndexedKey> resistances = [];
        for (int index = 0; index < DaggerfallCatalogs.ElementCount; index++)
        {
            resistances.Add(new DaggerfallIndexedKey(DaggerfallCatalogs.ElementKeys[index], index, careerCitation));
        }

        List<DaggerfallRaceKey> races = [];
        string[] raceNames = raceFamily.RecordOrStem.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int index = 0; index < raceNames.Length; index++)
        {
            // The donor's race values are contiguous from one in its own enumeration order.
            races.Add(new DaggerfallRaceKey(Key(raceNames[index]), index + 1, family(RaceFamily, raceFamily.PathOrPattern)));
        }

        List<DaggerfallCareerRecord> careerRecords = [];
        foreach ((string fileName, byte[] bytes) in careers)
        {
            ClassCfgRecord decoded = ClassCfgDecoder.Decode(bytes, fileName);
            // A career naming a skill the class index space does not have is a defect in a
            // class carrier; the enemy family reads the same record shape and tolerates it.
            if (decoded.SkillIndicesBeyondTerminal.Length != 0)
            {
                throw new InvalidOperationException($"Career carrier '{fileName}' names skill indices [{string.Join(", ", decoded.SkillIndicesBeyondTerminal)}], past the {ClassCfgDecoder.SkillCount} classic skills and their terminal no-skill value.");
            }
            SourceInventoryRow carrier = RequireCareerFile(inventory, fileName);
            careerRecords.Add(new DaggerfallCareerRecord(
                CareerKey(fileName),
                decoded.Name,
                [.. decoded.PrimarySkill(skills)],
                [.. decoded.MajorSkill(skills)],
                [.. decoded.MinorSkill(skills)],
                [.. attributes.Select(key => key.Id)],
                decoded.HitPointsPerLevel,
                decoded.AdvancementMultiplier,
                [.. FlaggedElements(decoded.ResistanceFlags)],
                [.. FlaggedElements(decoded.ImmunityFlags)],
                decoded.ResistanceFlags,
                decoded.ImmunityFlags,
                decoded.LowToleranceFlags,
                decoded.CriticalWeaknessFlags,
                new DaggerfallCatalogSource(carrier.Id, carrier.PathOrPattern)));
        }

        DaggerfallCatalogs catalogs = new(
            attributes,
            skills,
            resistances,
            races,
            careerRecords,
            [.. careerRecords
                .GroupBy(career => career.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .Order(StringComparer.Ordinal)],
            [.. enemyIds.Select(id => new DaggerfallReferenceKey(id, family(EnemySourceId, EnemySourcePath)))],
            [.. itemTemplateIds.Select(id => new DaggerfallReferenceKey(id, family(ItemSourceId, ItemSourcePath)))],
            [
                new DaggerfallPendingCatalog("factions", FactionCatalogOwnerTask, "FACTION.TXT identities, relations and bindings are supplied by that task; this contract declares the namespace and validates references into it."),
                new DaggerfallPendingCatalog("regionReferences", RegionCatalogOwnerTask, "MAPS.BSA region, location and map-table records are supplied by that task; this contract declares the namespace and validates references into it."),
            ],
            []);
        // The published sources are exactly what the records cite; Validate refuses a set
        // that disagrees, so this cannot drift from the citations a consumer reads.
        catalogs = catalogs with { Sources = [.. catalogs.CitedSources().Order(StringComparer.Ordinal)] };
        catalogs.Validate(inventoryIds);
        return catalogs;
    }

    private static IEnumerable<string> PrimarySkill(this ClassCfgRecord record, IReadOnlyList<DaggerfallIndexedKey> skills) =>
        Skill(record.PrimarySkill1, skills).Concat(Skill(record.PrimarySkill2, skills)).Concat(Skill(record.PrimarySkill3, skills));

    private static IEnumerable<string> MajorSkill(this ClassCfgRecord record, IReadOnlyList<DaggerfallIndexedKey> skills) =>
        Skill(record.MajorSkill1, skills).Concat(Skill(record.MajorSkill2, skills)).Concat(Skill(record.MajorSkill3, skills));

    private static IEnumerable<string> MinorSkill(this ClassCfgRecord record, IReadOnlyList<DaggerfallIndexedKey> skills) =>
        Skill(record.MinorSkill1, skills)
            .Concat(Skill(record.MinorSkill2, skills))
            .Concat(Skill(record.MinorSkill3, skills))
            .Concat(Skill(record.MinorSkill4, skills))
            .Concat(Skill(record.MinorSkill5, skills))
            .Concat(Skill(record.MinorSkill6, skills));

    /// <summary>
    /// One skill slot. The terminal value names no skill, so it contributes no reference
    /// rather than a key the catalog cannot resolve; a real Knight record holds it.
    /// </summary>
    private static IEnumerable<string> Skill(int index, IReadOnlyList<DaggerfallIndexedKey> skills) =>
        index == ClassCfgDecoder.NoSkillIndex ? [] : [skills[index].Id];

    /// <summary>
    /// The elements a classic effect-flag byte marks, in key order. The bit is the
    /// classic effect flag rather than the element's index, so fire is bit 3 and magic
    /// bit 1; a flag with no element in this key space, paralysis above all, marks none.
    /// </summary>
    private static IEnumerable<string> FlaggedElements(byte flags)
    {
        for (int index = 0; index < DaggerfallCatalogs.ElementCount; index++)
        {
            if ((flags & DaggerfallCatalogs.ElementFlagMasks[index]) != 0)
            {
                yield return DaggerfallCatalogs.ElementKeys[index];
            }
        }
    }

    private static SourceInventoryRow RequireFamily(IReadOnlyList<SourceInventoryRow> inventory, string familyId) =>
        SourceInventoryRow.RequireFamily(inventory, familyId);

    private static (string Id, string Path) FamilyCitation(IReadOnlyList<SourceInventoryRow> inventory, string familyId)
    {
        SourceInventoryRow row = RequireFamily(inventory, familyId);
        return (row.Id, row.PathOrPattern);
    }

    /// <summary>The inventory file record for one supplied career carrier.</summary>
    private static SourceInventoryRow RequireCareerFile(IReadOnlyList<SourceInventoryRow> inventory, string fileName) =>
        inventory.FirstOrDefault(value =>
            value.RowType == "file"
            && StringComparer.Ordinal.Equals(value.FamilyId, CareerFamily)
            && StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(value.PathOrPattern), fileName))
        ?? throw new InvalidOperationException($"The documented inventory does not carry career file '{fileName}'.");

    /// <summary>A stable key from a documented display name.</summary>
    private static string Key(string name)
    {
        string key = string.Concat(name.Select((value, index) =>
            char.IsUpper(value) && index > 0 ? $"-{char.ToLowerInvariant(value)}" : char.ToLowerInvariant(value).ToString()));
        return key;
    }

    /// <summary>The career identity is the carrier's own file identity, since names are not unique keys.</summary>
    private static string CareerKey(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
}
