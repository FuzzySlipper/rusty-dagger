using System.Globalization;
using System.Text.RegularExpressions;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Source.Tool.Donor;

/// <summary>Reads the four spell-cost arrays in the donor's <c>FormulaHelper.CalculateCastingCost</c> by name.</summary>
internal static class DonorSpellCosts
{
    private const string DonorSourcePath = "Assets/Scripts/Game/Formulas/FormulaHelper.cs";
    private static readonly Regex Number = new(@"0[xX][0-9A-Fa-f]+|\d+", RegexOptions.CultureInvariant);

    /// <summary>Our table's effects and coefficient rows, read from the donor's formula source text.</summary>
    public static (IReadOnlyList<SpellCostEffect> Effects, IReadOnlyList<IReadOnlyList<int>> Rows) Read(string donorFormulaHelper)
    {
        ArgumentNullException.ThrowIfNull(donorFormulaHelper);
        int[] indices = Array(donorFormulaHelper, "effectIndices");
        int[] coefficients = Array(donorFormulaHelper, "effectCoefficients");
        int[] settings = Array(donorFormulaHelper, "settingsTypes");
        int[] schools = Array(donorFormulaHelper, "effectMagicSchools");
        if (indices.Length != settings.Length * 12 || coefficients.Length % 4 != 0 || schools.Length != settings.Length)
        {
            throw new InvalidOperationException($"The donor's spell-cost tables in {DonorSourcePath} do not agree in shape: {indices.Length} indices, {coefficients.Length} coefficients, {settings.Length} settings types, {schools.Length} schools.");
        }

        return (Arena2MagicEffectCostTable.Effects(indices, settings, schools), Arena2MagicEffectCostTable.Rows(coefficients));
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
