using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// Builds the normalized reference catalogs from what the documented inventory admits:
/// the eight playable races the inventory names, the classic attribute, skill and
/// element index spaces the career carrier uses, and the careers decoded from the supplied
/// CLASS*.CFG records. Attributes and skills are published by classic index: the authored
/// vocabulary names them, and the ruleset joins the two when it loads.
/// Every citation is checked against the inventory, so a catalog cannot cite a source
/// the repository does not document.
/// </summary>
public static class DaggerfallCatalogBuilder
{
    /// <summary>Inventory families the catalogs cite.</summary>
    public const string RaceFamily = "CNT-009";
    public const string CareerFamily = "CNT-010";

    public static DaggerfallCatalogs Build(
        IReadOnlyList<SourceInventoryRow> inventory,
        IReadOnlyList<(string FileName, byte[] Bytes)> careers,
        string raceTemplateSource, byte[]? classRecommendations = null, DaggerfallText? classQuestionText = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(careers);
        IReadOnlySet<string> documentedPaths = inventory.Select(row => row.PathOrPattern).ToHashSet(StringComparer.Ordinal);
        DaggerfallCatalogSource family(string path) => new(path);

        SourceInventoryRow careerFamily = RequireFamily(inventory, CareerFamily);
        SourceInventoryRow raceFamily = RequireFamily(inventory, RaceFamily);
        DaggerfallCatalogSource careerCitation = family(careerFamily.PathOrPattern);
        List<DaggerfallClassicIndex> attributes = [.. Enumerable.Range(0, DaggerfallCatalogs.ClassicAttributeCount).Select(index => new DaggerfallClassicIndex(index, careerCitation))];
        List<DaggerfallClassicIndex> skills = [.. Enumerable.Range(0, ClassCfgDecoder.SkillCount).Select(index => new DaggerfallClassicIndex(index, careerCitation))];

        List<DaggerfallIndexedKey> resistances = [];
        for (int index = 0; index < DaggerfallCatalogs.ElementCount; index++)
        {
            resistances.Add(new DaggerfallIndexedKey(DaggerfallCatalogs.ElementKeys[index], index, careerCitation));
        }

        var raceFlags = DaggerfallRaceFlagsReader.Read(raceTemplateSource);
        List<DaggerfallRaceKey> races = [];
        string[] raceNames = raceFamily.RecordOrStem.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int index = 0; index < raceNames.Length; index++)
        {
            // The donor's race values are contiguous from one in its own enumeration order.
            string donorClass = raceNames[index].Replace(" ", "", StringComparison.Ordinal);
            if (!raceFlags.TryGetValue(donorClass, out var flags))
                throw new InvalidOperationException($"Documented race '{raceNames[index]}' has no donor RaceTemplate constructor.");
            races.Add(new DaggerfallRaceKey(Key(raceNames[index]), index + 1, family(raceFamily.PathOrPattern))
            { ResistanceFlags = flags.ResistanceFlags, ImmunityFlags = flags.ImmunityFlags,
                LowToleranceFlags = flags.LowToleranceFlags, CriticalWeaknessFlags = flags.CriticalWeaknessFlags });
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
                [.. decoded.PrimarySkill()],
                [.. decoded.MajorSkill()],
                [.. decoded.MinorSkill()],
                [.. decoded.Attributes.Select(value => checked((int)value))],
                decoded.HitPointsPerLevel,
                SpellPointMultiplierMilli(decoded.AbilityFlagsAndSpellPoints),
                decoded.AdvancementMultiplier,
                [.. FlaggedElements(decoded.ResistanceFlags)],
                [.. FlaggedElements(decoded.ImmunityFlags)],
                decoded.ResistanceFlags,
                decoded.ImmunityFlags,
                decoded.LowToleranceFlags,
                decoded.CriticalWeaknessFlags,
                decoded.AttackModifierFlags,
                decoded.AbilityFlagsAndSpellPoints & DaggerfallCatalogs.SpecialAbilityMask,
                (decoded.AbilityFlagsAndSpellPoints & 0x300) >> 8,
                (decoded.AbilityFlagsAndSpellPoints & 0xc0) >> 6,
                decoded.RapidHealing,
                decoded.Regeneration,
                decoded.SpellAbsorptionFlags,
                [.. ExpertProficiencySkills(decoded.WeaponArmorShields)],
                DaggerfallCareerEquipmentRestrictions.FromClassicFlags(decoded.ForbiddenMaterials, decoded.WeaponArmorShields),
                new DaggerfallCatalogSource(carrier.PathOrPattern)));
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
            []);
        // The published sources are exactly what the records cite; Validate refuses a set
        // that disagrees, so this cannot drift from the citations a consumer reads.
        if ((classRecommendations is null) != (classQuestionText is null))
            throw new ArgumentException("Class recommendations and their question text must be supplied together.");
        if (classRecommendations is not null)
            catalogs = catalogs with { ClassQuestionnaire = DaggerfallClassQuestionnaireBuilder.Build(classRecommendations, classQuestionText!) };
        catalogs = catalogs with { Sources = [.. catalogs.CitedSources().Order(StringComparer.Ordinal)] };
        catalogs.Validate(documentedPaths);
        return catalogs;
    }

    /// <summary>Interprets the CLASS carrier's three spell-point bits in the exact DFCareer order.</summary>
    private static int SpellPointMultiplierMilli(ushort abilityFlagsAndSpellPoints) =>
        ((abilityFlagsAndSpellPoints & 0x1c00) >> 8) switch
        {
            0 => 3000,
            4 => 2000,
            8 => 1750,
            12 => 1500,
            16 => 1000,
            20 => 500,
            _ => throw new InvalidOperationException("CLASS spell-point multiplier bits are not a classic value."),
        };

    private static IEnumerable<int> PrimarySkill(this ClassCfgRecord record) =>
        Skill(record.PrimarySkill1).Concat(Skill(record.PrimarySkill2)).Concat(Skill(record.PrimarySkill3));

    private static IEnumerable<int> MajorSkill(this ClassCfgRecord record) =>
        Skill(record.MajorSkill1).Concat(Skill(record.MajorSkill2)).Concat(Skill(record.MajorSkill3));

    private static IEnumerable<int> MinorSkill(this ClassCfgRecord record) =>
        Skill(record.MinorSkill1)
            .Concat(Skill(record.MinorSkill2))
            .Concat(Skill(record.MinorSkill3))
            .Concat(Skill(record.MinorSkill4))
            .Concat(Skill(record.MinorSkill5))
            .Concat(Skill(record.MinorSkill6));

    /// <summary>
    /// One skill slot. The terminal value names no skill, so it contributes no reference
    /// rather than a key the catalog cannot resolve; a real Knight record holds it.
    /// </summary>
    private static IEnumerable<int> Skill(int index) =>
        index == ClassCfgDecoder.NoSkillIndex ? [] : [index];

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

    /// <summary>
    /// The career's weapon-proficiency byte — the high byte of the classic weapon/armor/shields
    /// field — read as classic skill indices. The donor matches these bits against a weapon's skill value:
    /// bit <c>1 &lt;&lt; k</c> is the classic skill index <c>FirstWeaponSkillIndex + k</c> — short blade, long blade, hand to hand, axe, blunt weapon and archery, contiguous
    /// in the classic index space.
    /// </summary>
    private static IEnumerable<int> ExpertProficiencySkills(uint weaponArmorShields)
    {
        const int proficiencyBitCount = 6;
        const int firstWeaponSkillIndex = 28;
        uint value = (weaponArmorShields >> 16) & ((1u << proficiencyBitCount) - 1);
        for (int bit = 0; bit < proficiencyBitCount; bit++)
        {
            if ((value & (1u << bit)) != 0)
            {
                yield return firstWeaponSkillIndex + bit;
            }
        }
    }

    private static SourceInventoryRow RequireFamily(IReadOnlyList<SourceInventoryRow> inventory, string familyId) =>
        SourceInventoryRow.RequireFamily(inventory, familyId);

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
