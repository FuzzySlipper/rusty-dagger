using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// One text value for the name, biography and rumor families: either plain prose published as a
/// single run, or classic text tokens mapped and scanned exactly as the text resource's own values
/// are. Macros are always derived from the text runs, never stated, so the section's macro index
/// agrees with the values by construction.
/// </summary>
internal static class FamilyTextRecords
{
    /// <summary>One plain-prose value: a single text run, or no tokens when the source leaves it empty.</summary>
    internal static DaggerfallTextRecord Plain(
        DaggerfallTextKind kind, string id, string source, int ordinal, int offset, int byteLength, string text)
    {
        List<DaggerfallTextToken> tokens = text.Length == 0
            ? []
            : [new DaggerfallTextToken(Arena2TextCode.Text, text)];
        return new DaggerfallTextRecord(
            new DaggerfallTextKey(kind, id),
            source,
            ordinal,
            offset,
            byteLength,
            1,
            Arena2TextState.Read,
            string.Empty,
            [.. tokens.SelectMany(token => TextMacroScanner.Distinct(token.Text!))],
            tokens);
    }

    /// <summary>One unreadable value: no tokens, no variants, and the reason it carries none.</summary>
    internal static DaggerfallTextRecord Malformed(
        DaggerfallTextKind kind, string id, string source, int ordinal, int offset, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new DaggerfallTextRecord(
            new DaggerfallTextKey(kind, id),
            source,
            ordinal,
            offset,
            0,
            0,
            Arena2TextState.Malformed,
            reason,
            [],
            []);
    }

    /// <summary>One classic-grammar value: mapped tokens, scanned macros, separator-counted variants.</summary>
    internal static DaggerfallTextRecord Tokens(
        DaggerfallTextKind kind, string id, string source, int ordinal, int offset, int byteLength, IReadOnlyList<Arena2TextToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        List<DaggerfallTextToken> published = tokens.Select(Publish).ToList();
        return new DaggerfallTextRecord(
            new DaggerfallTextKey(kind, id),
            source,
            ordinal,
            offset,
            byteLength,
            1 + published.Count(token => token.Code == Arena2TextCode.SubrecordSeparator),
            Arena2TextState.Read,
            string.Empty,
            [.. published.Where(token => token.Code == Arena2TextCode.Text).SelectMany(token => TextMacroScanner.Distinct(token.Text!)).Distinct(StringComparer.Ordinal)],
            published);
    }

    private static DaggerfallTextToken Publish(Arena2TextToken token) => new(
        token.Code,
        token.Code == Arena2TextCode.Text ? token.Text : null,
        token.Code == Arena2TextCode.Unknown ? token.Value : null,
        token.Code is Arena2TextCode.FontPrefix or Arena2TextCode.PositionPrefix ? token.X : null);
}
