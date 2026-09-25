namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What a poison afflicts each minute it ticks: a vital, or one of the eight attributes.</summary>
internal enum DaggerfallPoisonTarget
{
    Health,
    Fatigue,
    Magicka,
    Attribute,
}

/// <summary>
/// One per-minute effect of a poison. The donor rolls its bounds with Unity's exclusive maximum, so the
/// inclusive bounds recorded here are already one lower than the donor's argument; a fixed effect has the
/// same value for both bounds. <see cref="IsPositive"/> marks the arms the donor has to take back when the
/// poison runs its course, because they help the victim rather than harm them.
/// </summary>
internal sealed record DaggerfallPoisonEffect(
    DaggerfallPoisonTarget Target, string? Attribute, int Minimum, int Maximum, bool IsPositive = false)
{
    internal void Validate()
    {
        if (Target == DaggerfallPoisonTarget.Attribute && string.IsNullOrWhiteSpace(Attribute))
            throw new InvalidOperationException("A poison's attribute effect must name its attribute.");
        if (Target != DaggerfallPoisonTarget.Attribute && Attribute is not null)
            throw new InvalidOperationException("Only an attribute effect names an attribute.");
        if (Minimum > Maximum)
            throw new InvalidOperationException($"A poison effect's bounds are inverted: {Minimum}..{Maximum}.");
        if (Minimum == 0 && Maximum == 0)
            throw new InvalidOperationException("A poison effect that never changes anything is not an effect.");
    }
}

/// <summary>Which of the two classic uses a poison variant has: a weapon poison or a drug.</summary>
internal enum DaggerfallPoisonKind
{
    /// <summary>Variants 0 to 7: applied to a weapon and delivered by a strike.</summary>
    WeaponPoison,

    /// <summary>Variants 8 to 11: taken as a drug, and the ones a character can also buy.</summary>
    Drug,
}

/// <summary>
/// One of the twelve classic poison archetypes: what it does each minute, how long before it starts, and
/// how long it lasts.
/// </summary>
internal sealed record DaggerfallPoisonArchetype(
    int Variant,
    string Name,
    DaggerfallPoisonKind Kind,
    int MinimumOnsetMinutes,
    int MaximumOnsetMinutes,
    int MinimumDurationMinutes,
    int MaximumDurationMinutes,
    IReadOnlyList<DaggerfallPoisonEffect> Effects)
{
    /// <summary>The donor's effect key for this archetype, which the admission policy already names.</summary>
    internal string Key => $"Poison-{Name}";
}

/// <summary>
/// The classic poison archetypes the donor's poison effect defines, in its own variant order.
/// </summary>
/// <remarks>
/// The two halves of an archetype have different homes, and this table is only one of them. The pack
/// carries the twelve as classic spell records — the twelve bang-named entries the importer reads
/// alongside the ordinary spells — and those records own each archetype's name, its classic variant
/// order and which classic effects it applies, including the effect rows the donor's switch interprets.
/// This table owns what the records do not carry: the onset window, the duration window and the per-tick
/// magnitude of each arm, which the donor states as its own constants rather than reading from the record.
/// Joining the two is the consumer's work; until it does, an archetype's variant numbers are the donor's
/// (128 onwards) and its names are the donor's spelling of the record's.
/// </remarks>
internal static class DaggerfallPoisonArchetypes
{
    /// <summary>The classic variant number the first archetype occupies; the last is eleven above it.</summary>
    internal const int FirstVariant = 128;
    internal const int VariantCount = 12;

    /// <summary>The variant number that begins the drug half of the table.</summary>
    internal const int FirstDrugVariant = 136;

    private const string Strength = "strength";
    private const string Intelligence = "intelligence";
    private const string Willpower = "willpower";
    private const string Agility = "agility";
    private const string Endurance = "endurance";
    private const string Personality = "personality";
    private const string Speed = "speed";
    private const string Luck = "luck";

    private static readonly DaggerfallPoisonEffect Health2to11 = new(DaggerfallPoisonTarget.Health, null, 2, 11);

    internal static IReadOnlyList<DaggerfallPoisonArchetype> All { get; } =
    [
        new(128, "Nux_Vomica", DaggerfallPoisonKind.WeaponPoison, 4, 4, 3, 10, [Health2to11]),
        new(129, "Arsenic", DaggerfallPoisonKind.WeaponPoison, 10, 10, 20, 1000,
        [
            new(DaggerfallPoisonTarget.Health, null, 2, 2),
            new(DaggerfallPoisonTarget.Attribute, Endurance, -1, -1),
        ]),
        new(130, "Moonseed", DaggerfallPoisonKind.WeaponPoison, 0, 0, 1, 4, [new(DaggerfallPoisonTarget.Health, null, 1, 9)]),
        new(131, "Drothweed", DaggerfallPoisonKind.WeaponPoison, 5, 10, 5, 30,
        [
            new(DaggerfallPoisonTarget.Attribute, Strength, -9, -5),
            new(DaggerfallPoisonTarget.Attribute, Agility, -4, -1),
            new(DaggerfallPoisonTarget.Attribute, Speed, -4, -1),
        ]),
        new(132, "Somnalius", DaggerfallPoisonKind.WeaponPoison, 0, 0, 2, 10, [new(DaggerfallPoisonTarget.Fatigue, null, 10, 99)]),
        new(133, "Pyrrhic_Acid", DaggerfallPoisonKind.WeaponPoison, 0, 0, 1, 2, [new(DaggerfallPoisonTarget.Health, null, 1, 29)]),
        new(134, "Magebane", DaggerfallPoisonKind.WeaponPoison, 2, 2, 5, 20,
        [
            new(DaggerfallPoisonTarget.Attribute, Willpower, -4, -1),
            new(DaggerfallPoisonTarget.Magicka, null, 5, 14),
        ]),
        new(135, "Thyrwort", DaggerfallPoisonKind.WeaponPoison, 0, 0, 1, 3,
        [
            new(DaggerfallPoisonTarget.Attribute, Willpower, -19, -5),
            new(DaggerfallPoisonTarget.Attribute, Personality, -19, -10),
        ]),
        new(136, "Indulcet", DaggerfallPoisonKind.Drug, 2, 12, 2, 6,
        [
            new(DaggerfallPoisonTarget.Fatigue, null, 10, 99),
            new(DaggerfallPoisonTarget.Attribute, Luck, 4, 9, IsPositive: true),
        ]),
        new(137, "Sursum", DaggerfallPoisonKind.Drug, 1, 4, 2, 2,
        [
            new(DaggerfallPoisonTarget.Attribute, Intelligence, -29, -10),
            new(DaggerfallPoisonTarget.Attribute, Strength, 5, 19, IsPositive: true),
        ]),
        new(138, "Quaesto_Vil", DaggerfallPoisonKind.Drug, 2, 12, 1, 4,
        [
            new(DaggerfallPoisonTarget.Attribute, Willpower, -3, -1),
            new(DaggerfallPoisonTarget.Fatigue, null, 5, 9, IsPositive: true),
        ]),
        new(139, "Aegrotat", DaggerfallPoisonKind.Drug, 0, 0, 5, 20,
        [
            new(DaggerfallPoisonTarget.Attribute, Endurance, -4, -1),
            new(DaggerfallPoisonTarget.Magicka, null, 5, 9, IsPositive: true),
        ]),
    ];

    /// <summary>The archetype a classic variant number names, when it names one.</summary>
    internal static bool TryResolve(int variant, out DaggerfallPoisonArchetype archetype)
    {
        foreach (DaggerfallPoisonArchetype candidate in All)
        {
            if (candidate.Variant != variant) continue;
            archetype = candidate;
            return true;
        }
        archetype = null!;
        return false;
    }

    /// <summary>The problems with a table, empty when it is sound; the classic set is fixed, so this guards a transcription.</summary>
    internal static IReadOnlyList<string> Validate(IEnumerable<DaggerfallPoisonArchetype> archetypes)
    {
        ArgumentNullException.ThrowIfNull(archetypes);
        List<string> problems = [];
        HashSet<int> seen = [];
        foreach (DaggerfallPoisonArchetype archetype in archetypes)
        {
            if (archetype.Variant < FirstVariant || archetype.Variant >= FirstVariant + VariantCount)
                problems.Add($"Poison archetype '{archetype.Name}' names variant {archetype.Variant}, outside the classic twelve.");
            if (string.IsNullOrWhiteSpace(archetype.Name))
                problems.Add($"Poison archetype {archetype.Variant} has no name.");
            if (archetype.MinimumOnsetMinutes > archetype.MaximumOnsetMinutes)
                problems.Add($"Poison archetype '{archetype.Name}' has an inverted onset window.");
            if (archetype.MinimumDurationMinutes > archetype.MaximumDurationMinutes)
                problems.Add($"Poison archetype '{archetype.Name}' has an inverted duration window.");
            if (archetype.MinimumDurationMinutes < 1)
                problems.Add($"Poison archetype '{archetype.Name}' lasts less than a minute.");
            if (archetype.Effects.Count == 0)
                problems.Add($"Poison archetype '{archetype.Name}' does nothing.");
            foreach (DaggerfallPoisonEffect effect in archetype.Effects) effect.Validate();
            if (!seen.Add(archetype.Variant))
                problems.Add($"Poison archetype '{archetype.Name}' repeats variant {archetype.Variant}.");
        }
        return problems;
    }

    /// <summary>The kind a classic variant has, by the donor's own split at the ninth variant.</summary>
    internal static DaggerfallPoisonKind KindOf(int variant) =>
        variant >= FirstDrugVariant ? DaggerfallPoisonKind.Drug : DaggerfallPoisonKind.WeaponPoison;
}
