namespace Daggerfall.Import.Arena2;

/// <summary>
/// Finds the macro symbols a text run carries.
/// </summary>
/// <remarks>
/// The grammar is the donor's own: a symbol begins at <c>%</c> and runs to the first character its
/// expansion loop treats as a terminator, or to the end of the run. A symbol of <c>%</c> alone is
/// therefore a symbol with an empty name rather than no symbol, and a symbol immediately followed by
/// <c>|</c> is glued to the characters after it — the donor eats that bar so its value and the suffix
/// read as one word. Both forms are retained as the source spells them: this scanner reports the
/// symbols a run carries and never rewrites the run, because expansion belongs to the consumer that
/// has the context to expand against.
/// </remarks>
public static class TextMacroScanner
{
    /// <summary>The character a symbol begins at.</summary>
    public const char Marker = '%';

    /// <summary>
    /// The characters that end a symbol. A bar is one of them because the donor consumes it as glue
    /// between a value and its suffix rather than part of the name.
    /// </summary>
    private static readonly char[] Terminators = [' ', '%', '.', ',', '\'', '?', '!', '/', '(', ')', '{', '}', '[', ']', '"', ';', ':', '|'];

    /// <summary>Every symbol the run carries, in the order it carries them, repeats included.</summary>
    public static IReadOnlyList<string> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<string> symbols = [];
        int position = 0;
        while (position < text.Length)
        {
            int marker = text.IndexOf(Marker, position);
            if (marker < 0)
            {
                break;
            }

            int end = marker + 1;
            while (end < text.Length && !char.IsWhiteSpace(text[end]) && Array.IndexOf(Terminators, text[end]) < 0)
            {
                end++;
            }

            symbols.Add(text[marker..end]);
            position = end;
        }

        return symbols;
    }

    /// <summary>The distinct symbols the run carries, in the order it first carries each.</summary>
    public static IReadOnlyList<string> Distinct(string text)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> symbols = [];
        foreach (string symbol in Scan(text))
        {
            if (seen.Add(symbol))
            {
                symbols.Add(symbol);
            }
        }

        return symbols;
    }
}
