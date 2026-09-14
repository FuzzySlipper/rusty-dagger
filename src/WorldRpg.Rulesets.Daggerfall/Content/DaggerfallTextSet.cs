namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Which source family a published text key belongs to. A key is a family and an identity inside it
/// because one key space addresses text several sources supply: the classic resource's own records
/// today, and the books, biographies, rumors and generated names their own tasks publish into it.
/// </summary>
internal enum DaggerfallTextKind
{
    Resource,
    Book,
    Biography,
    Rumor,
    Name,
}

/// <summary>One addressable text value, as the published key states it.</summary>
internal readonly record struct DaggerfallTextKey(DaggerfallTextKind Kind, string Id)
{
    public override string ToString() => $"{Kind}:{Id}";
}

/// <summary>
/// The element kinds a published token stream carries. These are the donor's own codes, and only the
/// ones a source byte can produce: its highlight, question and answer kinds are values its callers
/// construct around a record rather than bytes a record holds.
/// </summary>
internal enum DaggerfallTextCode
{
    Text = -1,
    Unknown = -2,
    NewLineOffset = 0x00,
    SameLineOffset = 0x01,
    PullPreceeding = 0x02,
    EndOfPage = 0xf6,
    InputCursorPositioner = 0xf8,
    FontPrefix = 0xf9,
    PositionPrefix = 0xfb,
    JustifyLeft = 0xfc,
    JustifyCenter = 0xfd,
    SubrecordSeparator = 0xff,
}

/// <summary>
/// One element of a published text value: a run of characters, a named code, or a code with no name.
/// A run states its characters, an unnamed code states the byte it came from, and the two prefixes
/// state the payload they take; the rest is absent because the kind already says it.
/// </summary>
internal sealed record DaggerfallTextElement(DaggerfallTextCode Code, string? Text, int? Value, int? X);

/// <summary>Whether a published text value's bytes could be read.</summary>
internal enum DaggerfallTextState
{
    Read,
    Malformed,
}

/// <summary>
/// One published text value: its key, the language and source it was read from, and the token stream
/// and macros its text carries.
/// </summary>
internal sealed record DaggerfallTextValue(
    DaggerfallTextKey Key,
    string Source,
    string Language,
    int Index,
    int Offset,
    int ByteLength,
    int Subrecords,
    DaggerfallTextState State,
    string Reason,
    IReadOnlyList<string> Macros,
    IReadOnlyList<DaggerfallTextElement> Tokens)
{
    /// <summary>
    /// The value's text runs in order. A macro is expanded inside a run, so this is the text a
    /// consumer expands and reads rather than the codes that qualify how it is laid out.
    /// </summary>
    internal IEnumerable<string> TextRuns => Tokens.Where(token => token.Code == DaggerfallTextCode.Text).Select(token => token.Text!);
}

/// <summary>One source family the text contract declares keys for without carrying their records.</summary>
/// <param name="Kind">The declared family.</param>
/// <param name="OwnerTask">The task that supplies records for the family.</param>
/// <param name="Reason">What the family addresses and where its records come from.</param>
internal sealed record DaggerfallTextPendingKind(DaggerfallTextKind Kind, int OwnerTask, string Reason);

/// <summary>One distinct macro symbol the published text carries, and how the donor accounts for it.</summary>
/// <param name="Symbol">The symbol as the source spells it, marker included.</param>
/// <param name="Records">How many published values carry it.</param>
/// <param name="Occurrences">How many times the corpus spells it.</param>
/// <param name="Disposition">Whether the donor's macro table handles it, names it without a handler, or does not name it.</param>
internal sealed record DaggerfallTextMacro(string Symbol, int Records, int Occurrences, string Disposition);

/// <summary>What a lookup answered.</summary>
internal enum DaggerfallTextResolution
{
    /// <summary>The pack carries the key and its bytes were read.</summary>
    Resolved,

    /// <summary>The pack carries the key and states why its bytes could not be read.</summary>
    Malformed,

    /// <summary>The pack carries no such key.</summary>
    Missing,
}

/// <summary>
/// The published text a caller resolves values through. This is loaded from the pack alone: no source
/// file is read to answer a lookup, and a key the pack does not carry answers as a miss rather than as
/// an empty value, which is what keeps "this text is missing" distinguishable from "this text is empty".
/// </summary>
internal sealed record DaggerfallTextSet(
    IReadOnlyDictionary<DaggerfallTextKey, DaggerfallTextValue> Values,
    IReadOnlyDictionary<string, string> SourceLanguages,
    IReadOnlyList<DaggerfallTextPendingKind> PendingKinds,
    IReadOnlyList<DaggerfallTextMacro> Macros)
{
    /// <summary>Every key the pack carries, in key order.</summary>
    internal IEnumerable<DaggerfallTextKey> Keys => Values.Keys.OrderBy(key => key.Kind).ThenBy(key => key.Id, StringComparer.Ordinal);

    /// <summary>Resolves a key to what the pack carries for it, or reports that it carries nothing.</summary>
    internal DaggerfallTextResolution Resolve(DaggerfallTextKey key, out DaggerfallTextValue? value)
    {
        if (!Values.TryGetValue(key, out value))
        {
            return DaggerfallTextResolution.Missing;
        }

        return value.State == DaggerfallTextState.Read ? DaggerfallTextResolution.Resolved : DaggerfallTextResolution.Malformed;
    }

    /// <summary>
    /// The value a caller needs, or a failure that names the key and why it carries no text. A
    /// malformed value fails here rather than reading as empty, because a caller that rendered it
    /// would be presenting a source defect as text the game has nothing to say with.
    /// </summary>
    internal DaggerfallTextValue Require(DaggerfallTextKey key) =>
        Resolve(key, out DaggerfallTextValue? value) switch
        {
            DaggerfallTextResolution.Resolved => value!,
            DaggerfallTextResolution.Malformed => throw new InvalidOperationException($"Daggerfall text '{key}' is published as malformed: {value!.Reason}"),
            _ => throw new InvalidOperationException($"Daggerfall text does not contain '{key}'."),
        };
}
