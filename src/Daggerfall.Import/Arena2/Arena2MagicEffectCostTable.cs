using System.Globalization;
using System.Text.RegularExpressions;

namespace Daggerfall.Import.Arena2;

/// <summary>One effect variant's classic cost row: its settings type, magic school and four coefficients.</summary>
public sealed record Arena2MagicEffectCost(int Type, int SubType, int SettingsType, string School, IReadOnlyList<int> Coefficients);

/// <summary>
/// The classic spell-cost tables the donor reversed from the original executable: which coefficient row
/// each effect variant uses, the four coefficients of each row, the settings type of each effect and the
/// magic school it belongs to.
/// </summary>
/// <remarks>
/// The tables live as array literals in the donor's <c>FormulaHelper.CalculateCastingCost</c>, not in any
/// Arena2 file, so they are read from that source text by name. A variant is resolved exactly as the donor
/// resolves it: the coefficient index is <c>effectIndices[12 * type + subType]</c>, with a variant that has no
/// subtype reading the type's first slot.
/// </remarks>
public sealed class Arena2MagicEffectCostTable
{
    /// <summary>The donor source file the tables are read from.</summary>
    public const string DonorSourcePath = "Assets/Scripts/Game/Formulas/FormulaHelper.cs";

    private const int SubtypeSlots = 12;
    private const int CoefficientsPerRow = 4;

    /// <summary>The magic schools in the order the donor's school table indexes them.</summary>
    private static readonly string[] Schools = ["alteration", "restoration", "destruction", "mysticism", "thaumaturgy", "illusion"];

    private static readonly Regex Number = new(@"0[xX][0-9A-Fa-f]+|\d+", RegexOptions.CultureInvariant);

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

    /// <summary>Reads the four tables from the donor's formula source text.</summary>
    public static Arena2MagicEffectCostTable Read(string donorFormulaHelper)
    {
        ArgumentNullException.ThrowIfNull(donorFormulaHelper);
        int[] indices = Array(donorFormulaHelper, "effectIndices");
        int[] coefficients = Array(donorFormulaHelper, "effectCoefficients");
        int[] settings = Array(donorFormulaHelper, "settingsTypes");
        int[] schools = Array(donorFormulaHelper, "effectMagicSchools");
        if (indices.Length % SubtypeSlots != 0 || coefficients.Length % CoefficientsPerRow != 0
            || settings.Length != indices.Length / SubtypeSlots || schools.Length != settings.Length)
        {
            throw new InvalidOperationException($"The donor's spell-cost tables in {DonorSourcePath} do not agree in shape: {indices.Length} indices, {coefficients.Length} coefficients, {settings.Length} settings types, {schools.Length} schools.");
        }

        return new Arena2MagicEffectCostTable(indices, coefficients, settings, schools);
    }

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

    private static int[] Array(string source, string name)
    {
        Match declaration = Regex.Match(source, $@"\b{Regex.Escape(name)}\s*=\s*\{{(?<body>[^}}]*)\}}", RegexOptions.CultureInvariant);
        if (!declaration.Success)
        {
            throw new InvalidOperationException($"The donor's spell-cost table '{name}' was not found in {DonorSourcePath}.");
        }

        string body = Regex.Replace(declaration.Groups["body"].Value, @"//[^\n]*", string.Empty, RegexOptions.CultureInvariant);
        return [.. Number.Matches(body).Select(match => match.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(match.Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture))];
    }
}
