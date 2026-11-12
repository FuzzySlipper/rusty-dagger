using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Arena2;

/// <summary>One effect type in our spell-cost table: its settings type, its school and the coefficient row each subtype slot uses.</summary>
public sealed record SpellCostEffect(int Type, int SettingsType, string School, IReadOnlyList<int> SubtypeRows);

/// <summary>
/// Our spell-cost table, taken once from the arrays in the donor's <c>FormulaHelper.CalculateCastingCost</c>, which
/// the donor reversed from the original executable.
/// </summary>
public sealed record SpellCostTable(TakenFrom Source, IReadOnlyList<SpellCostEffect> Effects, IReadOnlyList<IReadOnlyList<int>> CoefficientRows);

/// <summary>One effect variant's classic cost row: its settings type, magic school and four coefficients.</summary>
public sealed record Arena2MagicEffectCost(int Type, int SubType, int SettingsType, string School, IReadOnlyList<int> Coefficients);

/// <summary>
/// The classic spell-cost tables the donor reversed from the original executable: which coefficient row
/// each effect variant uses, the four coefficients of each row, the settings type of each effect and the
/// magic school it belongs to.
/// </summary>
/// <remarks>
/// The tables live in no Arena2 file; ours were taken from the donor's <c>FormulaHelper.CalculateCastingCost</c>
/// (<see cref="SpellCostTable"/>). A variant is resolved exactly as the donor
/// resolves it: the coefficient index is <c>effectIndices[12 * type + subType]</c>, with a variant that has no
/// subtype reading the type's first slot.
/// </remarks>
public sealed class Arena2MagicEffectCostTable
{
    private const int SubtypeSlots = 12;
    private const int CoefficientsPerRow = 4;

    /// <summary>The magic schools in the order the donor's school table indexes them.</summary>
    private static readonly string[] Schools = ["alteration", "restoration", "destruction", "mysticism", "thaumaturgy", "illusion"];

    private readonly int[] effectIndices;
    private readonly int[] effectCoefficients;
    private readonly int[] settingsTypes;
    private readonly int[] effectSchools;

    private Arena2MagicEffectCostTable(int[] effectIndices, int[] effectCoefficients, int[] settingsTypes, int[] effectSchools)
    {
        this.effectIndices = effectIndices;
        this.effectCoefficients = effectCoefficients;
        this.settingsTypes = settingsTypes;
        this.effectSchools = effectSchools;
    }

    /// <summary>The tables our spell-cost table carries, refused when their shapes disagree.</summary>
    public static Arena2MagicEffectCostTable From(SpellCostTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Effects.Select((effect, index) => effect.Type != index || effect.SubtypeRows.Count != SubtypeSlots).Any(wrong => wrong)
            || table.CoefficientRows.Any(row => row.Count != CoefficientsPerRow))
        {
            throw new InvalidOperationException($"The spell-cost table taken from {table.Source.Path} does not hold {SubtypeSlots} subtype rows for each effect type in order and {CoefficientsPerRow} coefficients in each row.");
        }

        int[] schools = [.. table.Effects.Select(effect => System.Array.IndexOf(Schools, effect.School))];
        if (schools.Any(school => school < 0))
            throw new InvalidOperationException($"The spell-cost table taken from {table.Source.Path} names a school outside [{string.Join(", ", Schools)}].");
        return new([.. table.Effects.SelectMany(effect => effect.SubtypeRows)], [.. table.CoefficientRows.SelectMany(row => row)],
            [.. table.Effects.Select(effect => effect.SettingsType)], schools);
    }

    /// <summary>
    /// Our spell-cost table from the donor's four arrays, by effect type: the settings type, the school name and the
    /// coefficient row each of the twelve subtype slots uses, with the rows themselves beside them.
    /// </summary>
    public static IReadOnlyList<SpellCostEffect> Effects(int[] indices, int[] settingsTypes, int[] schools) =>
        [.. settingsTypes.Select((settings, type) => new SpellCostEffect(type, settings, schools[type] < Schools.Length ? Schools[schools[type]] : $"school-{schools[type]}",
            indices.AsSpan(type * SubtypeSlots, SubtypeSlots).ToArray()))];

    /// <summary>The donor's flat coefficient array as rows of four.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> Rows(int[] coefficients) => [.. coefficients.Chunk(CoefficientsPerRow)];

    /// <summary>The cost row of one effect variant, refused when the donor's tables do not define it.</summary>
    public Arena2MagicEffectCost Resolve(int type, int subType)
    {
        if (type < 0 || type >= settingsTypes.Length || subType < -1 || subType >= SubtypeSlots)
        {
            throw new InvalidOperationException($"Spell effect ({type}, {subType}) is outside the donor's spell-cost tables.");
        }

        int row = effectIndices[(SubtypeSlots * type) + Math.Max(subType, 0)];
        int school = effectSchools[type];
        if ((row * CoefficientsPerRow) + CoefficientsPerRow > effectCoefficients.Length || school >= Schools.Length)
        {
            throw new InvalidOperationException($"Spell effect ({type}, {subType}) names a coefficient row or school the donor's tables do not carry.");
        }

        return new Arena2MagicEffectCost(type, subType, settingsTypes[type], Schools[school],
            effectCoefficients.AsSpan(row * CoefficientsPerRow, CoefficientsPerRow).ToArray());
    }
}
