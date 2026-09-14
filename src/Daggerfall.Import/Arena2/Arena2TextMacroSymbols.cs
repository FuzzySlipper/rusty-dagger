namespace Daggerfall.Import.Arena2;

/// <summary>How the donor's own macro table accounts for one macro symbol.</summary>
public enum TextMacroDisposition
{
    /// <summary>The donor's table maps the symbol to a handler.</summary>
    Handled,

    /// <summary>The donor's table names the symbol but maps it to no handler, so the donor leaves it unresolved too.</summary>
    DonorUnresolved,

    /// <summary>The donor's table does not name the symbol at all.</summary>
    Unrecognised,
}

/// <summary>
/// What the donor's macro table accounts for.
/// </summary>
/// <remarks>
/// Transcribed from <c>Assets/Scripts/Utility/MacroHelper.cs</c>: the keys of its
/// <c>macroHandlers</c> and <c>multilineMacroHandlers</c> tables, split by whether the entry maps to a
/// handler or to <c>null</c>. Keeping the two apart is what lets a published record say that the
/// corpus uses a symbol the donor itself never resolves, rather than reporting that symbol as
/// unrecognised and hiding a donor decision behind a gap in this table. The table is a source fact
/// rather than an implementation of expansion: nothing here evaluates a symbol.
/// </remarks>
public static class Arena2TextMacroSymbols
{
    /// <summary>Symbols the donor maps to a handler.</summary>
    private static readonly HashSet<string> Handled = new(StringComparer.Ordinal)
    {
        "%1am", "%1bm", "%1com", "%2am", "%2bm", "%a", "%ach",
        "%adr", "%adj", "%agi", "%an", "%ark", "%arm", "%ba",
        "%bch", "%bdr", "%bn", "%bt", "%clc", "%cld", "%clm",
        "%cn", "%cn2", "%cpn", "%cri", "%crn", "%ct", "%dae",
        "%dam", "%dat", "%di", "%dip", "%dng", "%dwr", "%enc",
        "%end", "%fa", "%fae", "%fcn", "%fe", "%fea", "%fl1",
        "%fl2", "%fn", "%fn2", "%fnpc", "%fon", "%fpa", "%fpc",
        "%fx1", "%fx2", "%g", "%g1", "%g2", "%g2self", "%g3",
        "%g4", "%gii", "%gdd", "%god", "%gtp", "%hea", "%hmd",
        "%hnr", "%hnt", "%hnt2", "%hpn", "%hpw", "%hs", "%imp",
        "%int", "%it", "%jok", "%key", "%kg", "%kno", "%lev",
        "%lp", "%ln", "%loc", "%lt1", "%ltn", "%luc", "%mad",
        "%map", "%mat", "%ml", "%mn", "%mn2", "%mod", "%n",
        "%nam", "%nrn", "%nt", "%ol1", "%olf", "%oth", "%pcf",
        "%pcn", "%pct", "%pen", "%per", "%po", "%pp1", "%pp2",
        "%pqn", "%pqp", "%q1", "%q2", "%q3", "%q4", "%q5",
        "%q6", "%q7", "%q8", "%q9", "%q10", "%q11", "%q12",
        "%q1a", "%q2a", "%q3a", "%q4a", "%q5a", "%q6a", "%q7a",
        "%q8a", "%q9a", "%q10a", "%q11a", "%q12a", "%q1b", "%q2b",
        "%q3b", "%q4b", "%q5b", "%q6b", "%q7b", "%q8b", "%q9b",
        "%q10b", "%q11b", "%q12b", "%qdt", "%qdat", "%qua", "%r1",
        "%r2", "%r3", "%r4", "%r5", "%ra", "%reg", "%rn",
        "%rt", "%spc", "%ski", "%spd", "%spt", "%str", "%sub",
        "%t", "%thd", "%tim", "%vam", "%vcn", "%wdm", "%wep",
        "%wil", "%wth", "%", "%pg", "%pg1", "%pg2", "%pg2self",
        "%pg3", "%pg4", "%G", "%G1", "%G2", "%G2self", "%G3",
        "%G4", "%hrn", "%pcl", "%day", "%dayn", "%days", "%mon",
        "%monn", "%year", "%min", "%hour", "%sign", "%sea", "%cbd",
        "%mpw",
    };

    /// <summary>Symbols the donor's table names with no handler.</summary>
    private static readonly HashSet<string> DonorUnresolvedSymbols = new(StringComparer.Ordinal)
    {
        "%1hn", "%2hn", "%3hn", "%cbl", "%dts", "%ef", "%hol",
        "%hrg", "%htwn", "%key2", "%mit", "%on", "%pdg", "%plq",
        "%pnq", "%ptm", "%qot", "%tcn", "%vn", "%wpn",
    };

    /// <summary>The symbols the donor's own table carries, whether or not it resolves them.</summary>
    public static int SymbolCount => Handled.Count + DonorUnresolvedSymbols.Count;

    /// <summary>How the donor's table accounts for one symbol.</summary>
    public static TextMacroDisposition Classify(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        if (Handled.Contains(symbol))
        {
            return TextMacroDisposition.Handled;
        }

        return DonorUnresolvedSymbols.Contains(symbol) ? TextMacroDisposition.DonorUnresolved : TextMacroDisposition.Unrecognised;
    }
}
