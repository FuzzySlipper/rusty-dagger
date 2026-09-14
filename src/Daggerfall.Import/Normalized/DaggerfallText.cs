using System.Globalization;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// Which source family a text key belongs to. The kind is part of the key rather than a property of
/// the pack because one key space has to address text a later source supplies: a book, a biography,
/// a rumor and a generated name are each addressed by the identity their own source gives them.
/// </summary>
public enum DaggerfallTextKind
{
    /// <summary>A record of the classic text resource, addressed by the key its directory carries.</summary>
    Resource,

    /// <summary>A supplied book, addressed by the file identity the classic message field names.</summary>
    Book,

    /// <summary>A supplied biography, addressed by its class and biography indices.</summary>
    Biography,

    /// <summary>A rumor record, addressed by its own position and region.</summary>
    Rumor,

    /// <summary>A generated name, addressed by the bank and index its table states.</summary>
    Name,
}

/// <summary>One addressable text value: which source family it belongs to, and its identity there.</summary>
public sealed record DaggerfallTextKey(DaggerfallTextKind Kind, string Id)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A published text key names a source family the contract does not declare.");
        }

        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
    }

    public override string ToString() => $"{Kind}:{Id}";
}

/// <summary>
/// One supplied text source: the language its bytes are written in, the bytes it carries, and what
/// its directory declared.
/// </summary>
/// <param name="Kind">The source family whose keys this source's records belong to.</param>
/// <param name="RecordId">The documented inventory record this source is read under.</param>
/// <param name="Path">The logical source path, which is also the identity its records name.</param>
/// <param name="Language">The language tag the source's text is written in.</param>
/// <param name="ByteLength">The source's byte length.</param>
/// <param name="DeclaredLength">The byte length the source's own directory declares, where it declares one.</param>
/// <param name="Records">How many records the source declares.</param>
public sealed record DaggerfallTextSource(
    DaggerfallTextKind Kind,
    string RecordId,
    string Path,
    string Language,
    long ByteLength,
    int DeclaredLength,
    int Records)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A published text source names a source family the contract does not declare.");
        }

        NormalizedImportDocument.RequireLogicalId(RecordId, nameof(RecordId));
        NormalizedImportDocument.RequireLogicalPath(Path, nameof(Path));
        NormalizedImportDocument.RequireLogicalId(Language, nameof(Language));
        if (ByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteLength), ByteLength, $"Text source '{Path}' must retain a positive byte length.");
        }

        if (DeclaredLength < 0 || Records < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Records), Records, $"Text source '{Path}' cannot declare a negative length or record count.");
        }
    }
}

/// <summary>
/// A source family this contract declares keys for but does not fill, and the task that supplies it.
/// </summary>
/// <param name="Kind">The declared source family.</param>
/// <param name="OwnerTask">The task that supplies records for the family.</param>
/// <param name="Reason">What the family addresses and where its records come from.</param>
public sealed record DaggerfallTextPendingKind(DaggerfallTextKind Kind, int OwnerTask, string Reason)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A pending text family names a source family the contract does not declare.");
        }

        if (OwnerTask <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OwnerTask), OwnerTask, $"Pending text family '{Kind}' must name the task that supplies it.");
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            throw new ArgumentException($"Pending text family '{Kind}' must state what its records are and who supplies them.", nameof(Reason));
        }
    }
}

/// <summary>
/// One element of a published token stream.
/// </summary>
/// <param name="Code">The element's kind: a run of characters, a named code, or a code with no name.</param>
/// <param name="Text">The run's characters, stated exactly for a run and omitted for a code.</param>
/// <param name="Value">
/// The source byte, stated only for a code this contract does not name. A named code already carries
/// its byte in the name it is published under, so repeating it would be a second copy of the same fact
/// that a consumer could find disagreeing with the first.
/// </param>
/// <param name="X">
/// The payload a position or font prefix states, stated exactly for those two codes. It is not
/// omitted when it is zero, because zero is a position a prefix can state.
/// </param>
public sealed record DaggerfallTextToken(
    Arena2TextCode Code,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? Value = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? X = null)
{
    public void Validate(DaggerfallTextKey key)
    {
        if (!Enum.IsDefined(Code))
        {
            throw new ArgumentOutOfRangeException(nameof(Code), Code, $"Published text key '{key}' carries an element kind the contract does not declare.");
        }

        bool prefixed = Code is Arena2TextCode.FontPrefix or Arena2TextCode.PositionPrefix;
        if (Code == Arena2TextCode.Text)
        {
            if (string.IsNullOrEmpty(Text) || Value is not null || X is not null)
            {
                throw new InvalidOperationException($"Published text key '{key}' carries a text element that holds no text or states a value a run does not have.");
            }

            return;
        }

        if (Text is not null || (X is not null) != prefixed || (X is { } x && x is < 0 or > 0xff))
        {
            throw new InvalidOperationException($"Published text key '{key}' carries code {Code} with text, or with a payload it does not take.");
        }

        if (Code == Arena2TextCode.Unknown)
        {
            if (Value is not { } value || value is < 0 or > 0xff || Enum.IsDefined((Arena2TextCode)value))
            {
                throw new InvalidOperationException($"Published text key '{key}' carries an unnamed code with no byte, or with byte {Value} that a name covers.");
            }

            return;
        }

        if (Value is not null)
        {
            throw new InvalidOperationException($"Published text key '{key}' states byte {Value} for the named code {Code}.");
        }
    }
}

/// <summary>
/// One published text value: its key, the source it was read from, and the token stream and macros
/// its text carries.
/// </summary>
/// <param name="Key">The value's address in the text key space.</param>
/// <param name="Source">The logical path of the source that carries it.</param>
/// <param name="Index">The value's ordinal in the source's own order.</param>
/// <param name="Offset">The source byte its text begins at.</param>
/// <param name="ByteLength">The bytes it spans including its terminator, zero when unreadable.</param>
/// <param name="Subrecords">How many variants its separator bytes divide it into, at least one when read.</param>
/// <param name="State">Whether its bytes could be read.</param>
/// <param name="Reason">Why it could not be read, empty when it could.</param>
/// <param name="Macros">The distinct macro symbols its text carries, in first-appearance order.</param>
/// <param name="Tokens">Its token stream, empty when it could not be read.</param>
public sealed record DaggerfallTextRecord(
    DaggerfallTextKey Key,
    string Source,
    int Index,
    int Offset,
    int ByteLength,
    int Subrecords,
    Arena2TextState State,
    string Reason,
    IReadOnlyList<string> Macros,
    IReadOnlyList<DaggerfallTextToken> Tokens)
{
    public void Validate()
    {
        Key.Validate();
        NormalizedImportDocument.RequireLogicalId(Source, nameof(Source));
        if (Index < 0 || Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Index), Index, $"Published text key '{Key}' cannot carry a negative ordinal or offset.");
        }

        // A record is either read or it is not, and the two states say different things to a consumer:
        // a read record's length and variants describe bytes, and a malformed one's absence of text is
        // the fact. A record claiming both would leave a consumer unable to tell an empty value from a
        // value that was never readable.
        if (State == Arena2TextState.Read)
        {
            if (ByteLength < 1 || Subrecords < 1)
            {
                throw new InvalidOperationException($"Published text key '{Key}' is readable but spans {ByteLength} bytes in {Subrecords} variants.");
            }

            if (Reason.Length != 0)
            {
                throw new InvalidOperationException($"Published text key '{Key}' is readable and still states a reason it is not.");
            }
        }
        else if (State == Arena2TextState.Malformed)
        {
            if (string.IsNullOrWhiteSpace(Reason))
            {
                throw new InvalidOperationException($"Published text key '{Key}' is malformed and states no reason, so nothing says why it carries no text.");
            }

            if (Tokens.Count != 0 || Subrecords != 0)
            {
                throw new InvalidOperationException($"Published text key '{Key}' is malformed and still carries {Tokens.Count} tokens in {Subrecords} variants.");
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(State), State, $"Published text key '{Key}' carries a state the contract does not declare.");
        }

        NormalizedImportDocument.ValidateUnique(Macros, macro => macro, $"text macro of '{Key}'");
        foreach (string macro in Macros)
        {
            if (!macro.StartsWith(TextMacroScanner.Marker))
            {
                throw new InvalidOperationException($"Published text key '{Key}' names macro '{macro}', which does not begin with the macro marker.");
            }
        }

        foreach (DaggerfallTextToken token in Tokens)
        {
            token.Validate(Key);
        }
    }
}

/// <summary>One distinct macro symbol the published text carries.</summary>
/// <param name="Symbol">The symbol as the source spells it, marker included.</param>
/// <param name="Records">How many published values carry it.</param>
/// <param name="Occurrences">How many times the corpus spells it.</param>
/// <param name="Disposition">How the donor's own macro table accounts for it.</param>
public sealed record DaggerfallTextMacro(string Symbol, int Records, int Occurrences, TextMacroDisposition Disposition)
{
    public void Validate()
    {
        if (!Symbol.StartsWith(TextMacroScanner.Marker))
        {
            throw new ArgumentException($"Published macro '{Symbol}' does not begin with the macro marker.", nameof(Symbol));
        }

        if (Records <= 0 || Occurrences < Records)
        {
            throw new ArgumentOutOfRangeException(nameof(Occurrences), Occurrences, $"Published macro '{Symbol}' is carried by {Records} values in {Occurrences} occurrences, which cannot both hold.");
        }

        if (Arena2TextMacroSymbols.Classify(Symbol) != Disposition)
        {
            throw new InvalidOperationException($"Published macro '{Symbol}' states the disposition {Disposition}, which is not how the donor's macro table accounts for it.");
        }
    }
}

/// <summary>
/// The published text of the supplied classic sources: what a lookup resolves, what the corpus spells
/// its values with, and which families the contract declares but does not fill.
/// </summary>
/// <param name="SchemaVersion">Shape version of this section.</param>
/// <param name="Sources">Every source the records were read from.</param>
/// <param name="PendingKinds">Families this contract declares keys for and does not fill.</param>
/// <param name="Records">Every text value the sources carry, in source order.</param>
/// <param name="Macros">The distinct macro symbols the values carry, in symbol order.</param>
public sealed record DaggerfallText(
    int SchemaVersion,
    IReadOnlyList<DaggerfallTextSource> Sources,
    IReadOnlyList<DaggerfallTextPendingKind> PendingKinds,
    IReadOnlyList<DaggerfallTextRecord> Records,
    IReadOnlyList<DaggerfallTextMacro> Macros)
{
    public const int CurrentSchemaVersion = 1;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Text schema must be {CurrentSchemaVersion} but is {SchemaVersion}.");
        }

        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(PendingKinds);
        ArgumentNullException.ThrowIfNull(Records);
        ArgumentNullException.ThrowIfNull(Macros);
        if (Sources.Count == 0)
        {
            throw new InvalidOperationException("A published text section must name the sources it read.");
        }

        NormalizedImportDocument.ValidateUnique(Sources, source => source.Path, "text source");
        foreach (DaggerfallTextSource source in Sources)
        {
            source.Validate();
        }

        // A family is either filled by a source or pending on the task that supplies it. Recording it
        // as both would let a consumer read an empty key space as a supplied one, or a supplied one as
        // still missing, and neither reading is a fact about the corpus.
        NormalizedImportDocument.ValidateUnique(PendingKinds, pending => pending.Kind.ToString(), "pending text family");
        foreach (DaggerfallTextPendingKind pending in PendingKinds)
        {
            pending.Validate();
            if (Sources.Any(source => source.Kind == pending.Kind))
            {
                throw new InvalidOperationException($"Text family '{pending.Kind}' is published as pending and carried by a source, so a consumer cannot tell whether its keys resolve.");
            }
        }

        Dictionary<string, DaggerfallTextSource> byPath = Sources.ToDictionary(source => source.Path, StringComparer.Ordinal);
        HashSet<DaggerfallTextKey> keys = [];
        HashSet<string> grouped = new(StringComparer.Ordinal);
        Dictionary<string, int> recordsPerSource = new(StringComparer.Ordinal);
        Dictionary<string, (int Records, int Occurrences)> macroCounts = new(StringComparer.Ordinal);
        int previousIndex = -1;
        string previousSource = string.Empty;
        foreach (DaggerfallTextRecord record in Records)
        {
            record.Validate();

            // A record whose source or family the section does not declare has no provenance: the key
            // would resolve, and nothing would say where its text or its language came from.
            if (!byPath.TryGetValue(record.Source, out DaggerfallTextSource? source))
            {
                throw new InvalidOperationException($"Published text key '{record.Key}' names source '{record.Source}', which the section does not carry.");
            }

            if (record.Key.Kind != source.Kind)
            {
                throw new InvalidOperationException($"Published text key '{record.Key}' belongs to family '{source.Kind}' by its source and to '{record.Key.Kind}' by its key.");
            }

            // Two records claiming one key leave one of them unreachable through every lookup the pack
            // offers, which is the collision the source reader already refuses at its own level.
            if (!keys.Add(record.Key))
            {
                throw new InvalidOperationException($"Published text carries '{record.Key}' twice, so one of them is unreachable.");
            }

            recordsPerSource[record.Source] = recordsPerSource.GetValueOrDefault(record.Source) + 1;
            if (!StringComparer.Ordinal.Equals(record.Source, previousSource))
            {
                // Each source's records are published as a group in its own order, so a consumer reads
                // one source's values by ordinal without interleaving another's.
                if (!grouped.Add(record.Source))
                {
                    throw new InvalidOperationException($"Published text source '{record.Source}' is interleaved with another source rather than grouped.");
                }

                previousSource = record.Source;
                previousIndex = -1;
            }

            if (record.Index <= previousIndex)
            {
                throw new InvalidOperationException($"Published text source '{record.Source}' carries key '{record.Key}' at ordinal {record.Index} after ordinal {previousIndex}, so its records are not in source order.");
            }

            previousIndex = record.Index;
            foreach (string macro in record.Macros)
            {
                (int Records, int Occurrences) counts = macroCounts.GetValueOrDefault(macro);
                macroCounts[macro] = (counts.Records + 1, counts.Occurrences + Occurrences(record, macro));
            }
        }

        foreach (DaggerfallTextSource source in Sources)
        {
            int published = recordsPerSource.GetValueOrDefault(source.Path);
            if (published != source.Records)
            {
                throw new InvalidOperationException($"Text source '{source.Path}' declares {source.Records} records and publishes {published}.");
            }
        }

        // The published macro index is derived from the records, so it has to agree with them in both
        // directions: an index that dropped a symbol would leave a consumer expanding text with a macro
        // nothing accounts for, and one that invented an entry would report a symbol the corpus lacks.
        NormalizedImportDocument.ValidateUnique(Macros, macro => macro.Symbol, "published text macro");
        foreach (DaggerfallTextMacro macro in Macros)
        {
            macro.Validate();
            if (!macroCounts.TryGetValue(macro.Symbol, out (int Records, int Occurrences) expected))
            {
                throw new InvalidOperationException($"Published macro '{macro.Symbol}' is carried by no published value.");
            }

            if (macro.Records != expected.Records || macro.Occurrences != expected.Occurrences)
            {
                throw new InvalidOperationException($"Published macro '{macro.Symbol}' states {macro.Records} values in {macro.Occurrences} occurrences where the records carry {expected.Records} in {expected.Occurrences}.");
            }
        }

        foreach (string symbol in macroCounts.Keys)
        {
            if (!Macros.Any(macro => StringComparer.Ordinal.Equals(macro.Symbol, symbol)))
            {
                throw new InvalidOperationException($"Published text carries macro '{symbol}', which the published macro index does not account for.");
            }
        }
    }

    /// <summary>How many times one record spells a symbol, which is its occurrences in that record's text runs.</summary>
    internal static int Occurrences(DaggerfallTextRecord record, string symbol) =>
        record.Tokens
            .Where(token => token.Code == Arena2TextCode.Text && token.Text is not null)
            .Sum(token => TextMacroScanner.Scan(token.Text!).Count(carried => StringComparer.Ordinal.Equals(carried, symbol)));
}

/// <summary>
/// Builds the published text from the classic text resource and the documented inventory.
/// </summary>
/// <remarks>
/// The inventory decides the source's identity: the caller's label has to be the path the documented
/// record places the resource at, so a pack cannot cite text to a file the repository does not
/// document. The language is stated by the caller rather than inferred, because no classic text file
/// declares one and a reader that guessed would be inventing the fact this section exists to retain.
/// </remarks>
public static class DaggerfallTextBuilder
{
    /// <summary>The inventory family the classic text resource is documented under.</summary>
    public const string TextFamily = "CNT-016";

    /// <summary>The task that supplies the supplied books' own records.</summary>
    public const int BookOwnerTask = 7951;

    /// <summary>The task that supplies the name, biography and rumor tables' own records.</summary>
    public const int NameBiographyRumorOwnerTask = 7941;

    public static DaggerfallText Build(byte[] bytes, string label, IReadOnlyList<SourceInventoryRow> inventory, string language)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(inventory);
        SourceInventoryRow family = inventory.FirstOrDefault(row => row.RowType == "family" && StringComparer.Ordinal.Equals(row.Id, TextFamily))
            ?? throw new InvalidOperationException($"The documented inventory does not carry family '{TextFamily}'.");
        if (!StringComparer.Ordinal.Equals(family.PathOrPattern, label))
        {
            throw new InvalidOperationException($"The text resource was read as '{label}', but the documented inventory places {TextFamily} at '{family.PathOrPattern}'.");
        }

        Arena2TextCatalog catalog = TextResourceReader.Read(bytes, label);
        List<DaggerfallTextRecord> records = [];
        foreach (Arena2TextRecord record in catalog.Records)
        {
            records.Add(new DaggerfallTextRecord(
                new DaggerfallTextKey(DaggerfallTextKind.Resource, record.Id.ToString(CultureInfo.InvariantCulture)),
                label,
                record.Index,
                record.Offset,
                record.ByteLength,
                record.Subrecords,
                record.State,
                record.Reason,
                record.Macros,
                [.. record.Tokens.Select(Publish)]));
        }

        DaggerfallText published = new(
            DaggerfallText.CurrentSchemaVersion,
            [new DaggerfallTextSource(DaggerfallTextKind.Resource, family.Id, label, language, bytes.LongLength, catalog.HeaderLength, catalog.Records.Count)],
            [
                // The families whose keys this contract declares and whose records their own tasks
                // supply: a reference into one of them is a legal key the pack does not yet carry,
                // which is a boundary rather than a missing value.
                new DaggerfallTextPendingKind(DaggerfallTextKind.Book, BookOwnerTask, "Supplied book files carry their own metadata, pages and message mappings; this contract declares the key space they resolve through."),
                new DaggerfallTextPendingKind(DaggerfallTextKind.Biography, NameBiographyRumorOwnerTask, "Biography text files carry the question and answer records a character consumes; this contract declares the key space they resolve through."),
                new DaggerfallTextPendingKind(DaggerfallTextKind.Rumor, NameBiographyRumorOwnerTask, "Rumor records carry their own region, type and text; this contract declares the key space they resolve through."),
                new DaggerfallTextPendingKind(DaggerfallTextKind.Name, NameBiographyRumorOwnerTask, "Name banks carry the generated name tables; this contract declares the key space they resolve through."),
            ],
            [.. records.OrderBy(record => record.Source, StringComparer.Ordinal).ThenBy(record => record.Index)],
            [.. MacroIndex(records)]);
        published.Validate();
        return published;
    }

    /// <summary>
    /// One read token as the section publishes it: a run keeps its characters, a named code keeps its
    /// name, a code with no name keeps the byte it came from, and only the two prefixes keep the payload
    /// they state.
    /// </summary>
    private static DaggerfallTextToken Publish(Arena2TextToken token) => new(
        token.Code,
        token.Code == Arena2TextCode.Text ? token.Text : null,
        token.Code == Arena2TextCode.Unknown ? token.Value : null,
        token.Code is Arena2TextCode.FontPrefix or Arena2TextCode.PositionPrefix ? token.X : null);

    /// <summary>
    /// The distinct macro symbols the records carry, how many values and occurrences each accounts for,
    /// and how the donor's own table accounts for the symbol.
    /// </summary>
    private static IEnumerable<DaggerfallTextMacro> MacroIndex(IReadOnlyList<DaggerfallTextRecord> records)
    {
        Dictionary<string, (int Records, int Occurrences)> counts = new(StringComparer.Ordinal);
        foreach (DaggerfallTextRecord record in records)
        {
            foreach (string symbol in record.Macros)
            {
                (int Records, int Occurrences) current = counts.GetValueOrDefault(symbol);
                counts[symbol] = (current.Records + 1, current.Occurrences + DaggerfallText.Occurrences(record, symbol));
            }
        }

        return counts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DaggerfallTextMacro(pair.Key, pair.Value.Records, pair.Value.Occurrences, Arena2TextMacroSymbols.Classify(pair.Key)));
    }
}
