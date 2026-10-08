namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Where a published catalog record came from: a record id the documented inventory
/// carries, and the path that inventory documents. The import tool refuses a citation
/// the inventory does not contain, so a published catalog cannot invent provenance.
/// </summary>
internal sealed record DaggerfallCatalogCitation(string Path);

/// <summary>One indexed key of a classic index space: an attribute, skill or resistance.</summary>
internal sealed record DaggerfallCatalogKey(string Id, int Index, DaggerfallCatalogCitation Source);

/// <summary>A playable race identity with the donor's own race value.</summary>
internal sealed record DaggerfallRaceDefinition(string Id, int DonorRaceId, DaggerfallCatalogCitation Source)
{
    internal int ResistanceFlags { get; init; }
    internal int ImmunityFlags { get; init; }
    internal int LowToleranceFlags { get; init; }
    internal int CriticalWeaknessFlags { get; init; }
    internal DaggerfallDiseaseCareerTolerance Tolerance(int effectFlag) => DaggerfallCareerTolerances.Tolerance(
        ResistanceFlags, ImmunityFlags, LowToleranceFlags, CriticalWeaknessFlags, effectFlag);
    internal Policies.DaggerfallMagicRaceToleranceFlags MagicTolerances => new(
        (Policies.DaggerfallMagicEffectFlags)ResistanceFlags,
        (Policies.DaggerfallMagicEffectFlags)(ImmunityFlags & ~ResistanceFlags),
        (Policies.DaggerfallMagicEffectFlags)(LowToleranceFlags & ~(ResistanceFlags | ImmunityFlags)),
        (Policies.DaggerfallMagicEffectFlags)(CriticalWeaknessFlags & ~(ResistanceFlags | ImmunityFlags | LowToleranceFlags)));
}

/// <summary>
/// One decoded career: the classic record's identity, trained skills, and authored initial
/// attribute bases. Attribute keys and values retain the donor's shared source order.
/// </summary>
internal sealed record DaggerfallCareerDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> PrimarySkills,
    IReadOnlyList<string> MajorSkills,
    IReadOnlyList<string> MinorSkills,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<int> AttributeValues,
    int HitPointsPerLevel,
    int SpellPointMultiplierMilli,
    float AdvancementMultiplier,
    IReadOnlyList<string> ResistanceElements,
    IReadOnlyList<string> ImmunityElements,
    int ResistanceFlags,
    int ImmunityFlags,
    int LowToleranceFlags,
    int CriticalWeaknessFlags,
    int AttackModifierFlags,
    DaggerfallCareerSpecials Specials,
    IReadOnlyList<string> ExpertProficiencies,
    IReadOnlyList<string> ForbiddenEquipment,
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

/// <summary>
/// A career's classic special abilities: the ability bits and the single-condition values the
/// career record carries. A preset career publishes them from its classic record, a custom career
/// derives them from its chosen traits, and an enemy's career class carries its preset's, so every
/// consumer reads one shape. Conditions compare for equality, as the donor does.
/// </summary>
internal sealed record DaggerfallCareerSpecials(
    int AbilityFlags,
    int DarknessPoweredMagery,
    int LightPoweredMagery,
    int RapidHealing,
    int Regeneration,
    int SpellAbsorption)
{
    internal const int AcuteHearingBit = 1, AthleticismBit = 2, AdrenalineRushBit = 4, NoRegenSpellPointsBit = 8,
        SunDamageBit = 16, HolyDamageBit = 32, AbilityMask = 0x3f;
    /// <summary>Light- and darkness-powered magery values.</summary>
    internal const int MageryNormal = 0, MageryUnable = 1, MageryReduced = 2;
    /// <summary>Rapid-healing and spell-absorption condition values.</summary>
    internal const int InLight = 1, InDarkness = 2, Always = 4;
    /// <summary>Regeneration condition values, which add water and move always to eight.</summary>
    internal const int RegenerateInLight = 1, RegenerateInDarkness = 2, RegenerateInWater = 4, RegenerateAlways = 8;

    internal static DaggerfallCareerSpecials None { get; } = new(0, 0, 0, 0, 0, 0);

    internal bool AcuteHearing => (AbilityFlags & AcuteHearingBit) != 0;
    internal bool Athleticism => (AbilityFlags & AthleticismBit) != 0;
    internal bool AdrenalineRush => (AbilityFlags & AdrenalineRushBit) != 0;
    internal bool NoRegenSpellPoints => (AbilityFlags & NoRegenSpellPointsBit) != 0;
    internal bool SunDamage => (AbilityFlags & SunDamageBit) != 0;
    internal bool HolyDamage => (AbilityFlags & HolyDamageBit) != 0;

    /// <summary>The donor's light/dark rapid-healing condition; light is outdoors by day.</summary>
    internal bool RapidHealingApplies(bool light) =>
        RapidHealing == Always || RapidHealing == (light ? InLight : InDarkness);

    /// <summary>The donor's career absorption condition; light is outdoors by day.</summary>
    internal bool AbsorbsSpells(bool light) =>
        SpellAbsorption == Always || SpellAbsorption == (light ? InLight : InDarkness);

    /// <summary>The donor's regeneration condition; darkness here is night or a dungeon.</summary>
    internal bool Regenerates(bool dark, bool swimming) => Regeneration switch
    {
        RegenerateAlways => true,
        RegenerateInDarkness => dark,
        RegenerateInLight => !dark,
        RegenerateInWater => swimming,
        _ => false,
    };

    /// <summary>The magery value that applies now: darkness-powered magery suffers in light and the reverse.</summary>
    internal int MageryPenalty(bool dark) => dark ? LightPoweredMagery : DarknessPoweredMagery;

    /// <summary>Whether every value is one the classic record and the custom creator can carry.</summary>
    internal bool IsClassic => (AbilityFlags & ~AbilityMask) == 0
        && DarknessPoweredMagery is >= MageryNormal and <= MageryReduced && LightPoweredMagery is >= MageryNormal and <= MageryReduced
        && RapidHealing is 0 or InLight or InDarkness or Always && SpellAbsorption is 0 or InLight or InDarkness or Always
        && Regeneration is 0 or RegenerateInLight or RegenerateInDarkness or RegenerateInWater or RegenerateAlways;

    internal IEnumerable<(string Name, int Value)> Fields =>
    [
        ("specialAbilityFlags", AbilityFlags), ("darknessPoweredMagery", DarknessPoweredMagery), ("lightPoweredMagery", LightPoweredMagery),
        ("rapidHealingFlags", RapidHealing), ("regenerationFlags", Regeneration), ("spellAbsorptionFlags", SpellAbsorption),
    ];
}

/// <summary>A key that names a record another catalog owns.</summary>
internal sealed record DaggerfallCatalogReference(string Id, DaggerfallCatalogCitation Source);

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
    IReadOnlyList<string> sourcePaths, DaggerfallClassQuestionnaire? classQuestionnaire = null)
{
    /// <summary>The classic element keys, in the index order the catalog publishes.</summary>
    internal static readonly string[] ElementKeys = ["fire", "frost", "disease-or-poison", "shock", "magic"];

    internal DaggerfallClassQuestionnaire? ClassQuestionnaire { get; } = classQuestionnaire;

    internal IReadOnlyList<DaggerfallCatalogKey> Attributes { get; } = Array.AsReadOnly(attributes.ToArray());

    internal IReadOnlyList<DaggerfallCatalogKey> Skills { get; } = Array.AsReadOnly(skills.ToArray());

    internal IReadOnlyList<DaggerfallCatalogKey> Resistances { get; } = Array.AsReadOnly(resistances.ToArray());

    internal IReadOnlyList<DaggerfallRaceDefinition> Races { get; } = Array.AsReadOnly(races.ToArray());

    internal IReadOnlyList<DaggerfallCareerDefinition> Careers { get; } = Array.AsReadOnly(careers.ToArray());

    /// <summary>Career names more than one supplied record carries, so a name is not a key.</summary>
    internal IReadOnlyList<string> CareerNameCollisions { get; } = Array.AsReadOnly(careerNameCollisions.ToArray());

    internal IReadOnlyList<DaggerfallCatalogReference> Enemies { get; } = Array.AsReadOnly(enemies.ToArray());

    internal IReadOnlyList<DaggerfallCatalogReference> ItemTemplates { get; } = Array.AsReadOnly(itemTemplates.ToArray());

    /// <summary>
    /// The documented source paths the pack says it drew from. A citation outside this set is
    /// refused, so a consumer can check provenance without the inventory.
    /// </summary>
    internal IReadOnlyList<string> SourcePaths { get; } = Array.AsReadOnly(sourcePaths.ToArray());

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
