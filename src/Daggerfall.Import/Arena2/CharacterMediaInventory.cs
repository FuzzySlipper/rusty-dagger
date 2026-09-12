namespace Daggerfall.Import.Arena2;

/// <summary>What the enumeration established about one character media file.</summary>
public enum CharacterMediaDisposition
{
    /// <summary>The file is bound to a published consumer.</summary>
    Bound,

    /// <summary>The file is supplied and no published consumer binds it yet.</summary>
    Unbound,

    /// <summary>The file's format has no decoder in this repository yet.</summary>
    Unsupported,
}

/// <summary>How far the repository's decoders read one character media file.</summary>
public enum CharacterMediaDecode
{
    /// <summary>Read as a standard IMG record.</summary>
    Header,

    /// <summary>Read as a headerless UI canvas.</summary>
    Headerless,

    /// <summary>Read as a CIF record with addressable frames.</summary>
    Cif,

    /// <summary>No decoder reads this format yet.</summary>
    NotRead,
}

/// <summary>One supplied character media file: its family, its canvas, and its binding.</summary>
public sealed record CharacterMediaRecord(
    string Path,
    string Family,
    int Width,
    int Height,
    int Frames,
    string Key,
    string UseCandidate,
    string Consumer,
    CharacterMediaDisposition Disposition,
    CharacterMediaDecode Decode,
    string Note);

/// <summary>
/// The character, face and story-art inventory: every file in the documented families with
/// its canvas, its identity key, the use it is a candidate for, and its binding.
/// </summary>
/// <remarks>
/// This records source facts and candidate uses only. It creates no character-creation or
/// social runtime behavior, and a file no decoder reads is retained with the format named
/// rather than dropped or approximated.
/// </remarks>
public sealed class CharacterMediaInventory
{
    /// <summary>
    /// The documented families with the file count the inventory records for each and the
    /// use the family is a candidate for. The face count includes the single FACES.CIF.
    /// </summary>
    public static readonly (string Prefix, int Count, string Use)[] DocumentedFamilies =
    [
        ("BODY", 32, "body art by race and gender"),
        ("FACE", 17, "face art by race and gender"),
        ("CHAR", 9, "character-sheet art"),
        ("CUST", 10, "custom character art"),
        ("NITE", 4, "night and rest art"),
        ("SCBG", 9, "story and cutscene backgrounds"),
        ("CEL", 3, "class portraits"),
        ("BSS", 3, "ambient story sprites"),
    ];

    private CharacterMediaInventory(string source, IReadOnlyList<CharacterMediaRecord> files)
    {
        Source = source;
        Files = files;
    }

    /// <summary>Logical source identity supplied to <see cref="Enumerate"/>.</summary>
    public string Source { get; }

    /// <summary>Every supplied file, ordered by family and then path.</summary>
    public IReadOnlyList<CharacterMediaRecord> Files { get; }

    /// <summary>The files a published consumer binds.</summary>
    public IEnumerable<CharacterMediaRecord> Bound => Files.Where(file => file.Disposition == CharacterMediaDisposition.Bound);

    /// <summary>The files with no published consumer yet.</summary>
    public IEnumerable<CharacterMediaRecord> Unbound => Files.Where(file => file.Disposition == CharacterMediaDisposition.Unbound);

    /// <summary>The files whose format has no decoder here.</summary>
    public IEnumerable<CharacterMediaRecord> Unsupported => Files.Where(file => file.Disposition == CharacterMediaDisposition.Unsupported);

    /// <summary>The files of one documented family.</summary>
    public IEnumerable<CharacterMediaRecord> Family(string prefix) => Files.Where(file => StringComparer.Ordinal.Equals(file.Family, prefix));

    /// <summary>
    /// Identity keys claimed by more than one file. Two files claiming one key would make a
    /// lookup ambiguous, so the clash is reported rather than resolved by ordering.
    /// </summary>
    public IEnumerable<(string Key, IReadOnlyList<string> Files)> DuplicateKeys =>
        Files.GroupBy(file => file.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => (group.Key, (IReadOnlyList<string>)group.Select(file => file.Path).Order(StringComparer.Ordinal).ToArray()));

    /// <summary>
    /// Enumerates the documented character media families for a caller that names no
    /// consumer.
    /// </summary>
    public static CharacterMediaInventory Enumerate(
        IEnumerable<(string Path, ReadOnlyMemory<byte> Bytes)> sources,
        IReadOnlySet<string> bound,
        string source) =>
        Enumerate(sources, bound, UnstatedConsumer, source);

    /// <summary>The label a bound record carries when its caller names no consumer.</summary>
    public const string UnstatedConsumer = "an unstated consumer";

    /// <summary>Enumerates the documented character media families.</summary>
    /// <param name="sources">File name and bytes for every supplied file.</param>
    /// <param name="bound">File names a published consumer binds.</param>
    /// <param name="consumer">The consumer that binds those files, named by the caller.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    public static CharacterMediaInventory Enumerate(
        IEnumerable<(string Path, ReadOnlyMemory<byte> Bytes)> sources,
        IReadOnlySet<string> bound,
        string consumer,
        string source)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        List<CharacterMediaRecord> files = [];
        foreach ((string path, ReadOnlyMemory<byte> bytes) in sources.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            (string family, string use) = FamilyOf(path, source);
            bool isBound = bound.Contains(path);
            string key = KeyOf(path);
            int width = 0, height = 0, frames = 1;
            CharacterMediaDecode decode = CharacterMediaDecode.NotRead;
            string reason = string.Empty;
            string extension = System.IO.Path.GetExtension(path).ToUpperInvariant();
            try
            {
                if (extension is ".CEL" or ".BSS")
                {
                    reason = $"The {extension} format has no decoder in this repository yet.";
                }
                else if (extension == ".CIF")
                {
                    WeaponCifArchive archive = WeaponCifArchive.Parse(bytes.Span, path);
                    WeaponCifRecordInfo info = archive.GetRecordInfo(0);
                    width = info.Width;
                    height = info.Height;
                    frames = info.FrameCount;
                    decode = CharacterMediaDecode.Cif;
                }
                else if (ImgDecoder.TryDecodeUi(bytes.Span, path, out IndexedImg? ui, out UiMediaDecode canvas, out reason))
                {
                    width = ui!.Width;
                    height = ui.Height;
                    decode = canvas == UiMediaDecode.Header ? CharacterMediaDecode.Header : CharacterMediaDecode.Headerless;
                }
            }
            catch (Arena2FormatException failure)
            {
                reason = failure.Message;
            }

            // "No decoder reads this file" is one fact whatever the format: the CEL and BSS
            // families have no reader at all, and the weapon CIF reader refuses face CIFs.
            bool unsupported = decode == CharacterMediaDecode.NotRead;
            string binding = isBound
                ? $"Bound by {consumer}."
                : "No published consumer binds this file; it is retained unbound with its candidate use.";
            files.Add(new CharacterMediaRecord(
                path,
                family,
                width,
                height,
                frames,
                key,
                use,
                isBound ? consumer : string.Empty,
                unsupported ? CharacterMediaDisposition.Unsupported : isBound ? CharacterMediaDisposition.Bound : CharacterMediaDisposition.Unbound,
                decode,
                decode == CharacterMediaDecode.NotRead ? $"{binding} {reason}" : $"{binding} Candidate use: {use}."));
        }

        return new CharacterMediaInventory(source, files);
    }

    /// <summary>The identity key a file contributes: its name without extension, upper-cased.</summary>
    private static string KeyOf(string path) => System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant();

    private static (string Family, string Use) FamilyOf(string path, string source)
    {
        string name = System.IO.Path.GetFileName(path);
        string extension = System.IO.Path.GetExtension(name).ToUpperInvariant();
        (string Prefix, int Count, string Use) family = extension switch
        {
            ".CEL" or ".BSS" => DocumentedFamilies.Single(entry => entry.Prefix == extension[1..]),
            _ => DocumentedFamilies.FirstOrDefault(entry => entry.Prefix != "CEL" && entry.Prefix != "BSS" && name.StartsWith(entry.Prefix, StringComparison.OrdinalIgnoreCase)),
        };
        return family.Prefix is null
            ? throw new Arena2FormatException(source, 0, $"'{name}' is in none of the documented character media families")
            : (family.Prefix, family.Use);
    }
}
