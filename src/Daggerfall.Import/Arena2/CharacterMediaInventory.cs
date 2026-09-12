namespace Daggerfall.Import.Arena2;

/// <summary>One supplied character media file: its family, its canvases, and its binding.</summary>
public sealed record CharacterMediaRecord(
    string Path,
    string Family,
    IReadOnlyList<Arena2Canvas> Canvases,
    string Key,
    string UseCandidate,
    string Consumer,
    MediaBinding Binding,
    Arena2CanvasKind Decode,
    string Note)
{
    /// <summary>How many addressable canvases the file supplies.</summary>
    public int CanvasCount => Canvases.Count;
}

/// <summary>
/// The character, face and story-art inventory: every file in the documented families with the
/// canvases it carries, its identity key, the use it is a candidate for, and its binding.
/// </summary>
/// <remarks>
/// This records source facts and candidate uses only. It creates no character-creation or
/// social runtime behavior, and a file no reader reads is retained with the format named
/// rather than dropped or approximated. Canvases are enumerated per file rather than assumed
/// one per file: a face CIF is a sequence of IMG records and FACES.CIF is a fixed-cell grid,
/// so a file count is not a canvas count. Binding and decodability are separate facts, so
/// <see cref="Bound"/>, <see cref="Unbound"/> and <see cref="Unsupported"/> answer three
/// different questions and a file can appear in more than one of them.
/// <para>
/// What this does <em>not</em> establish: no canvas here carries a palette or a companion-media
/// reference, and no cross-check derives either. A publisher that emits these canvases has to
/// derive both and refuse a canvas whose palette or companion is absent; the absence is stated
/// here so that it is a known boundary rather than an assumed default.
/// </para>
/// </remarks>
public sealed class CharacterMediaInventory
{
    /// <summary>
    /// The documented families with the file count the corpus supplies for each and the use the
    /// family is a candidate for. The counts are corpus counts and the CNT-021 manifest row
    /// documents the same vector, which the reconciliation test asserts in both directions
    /// rather than trusting either side. The face count includes the single FACES.CIF.
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

    /// <summary>Every supplied file, ordered by path.</summary>
    public IReadOnlyList<CharacterMediaRecord> Files { get; }

    /// <summary>The files a published consumer binds.</summary>
    public IEnumerable<CharacterMediaRecord> Bound => Files.Where(file => file.Binding == MediaBinding.Admitted);

    /// <summary>The files no published consumer binds yet, whether or not they read.</summary>
    public IEnumerable<CharacterMediaRecord> Unbound => Files.Where(file => file.Binding == MediaBinding.RequiredPending);

    /// <summary>The files no reader read; a file a consumer binds is still listed here when it does not read.</summary>
    public IEnumerable<CharacterMediaRecord> Unsupported => Files.Where(file => file.Decode == Arena2CanvasKind.Unread);

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
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach ((string path, ReadOnlyMemory<byte> bytes) in sources.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A character media file was supplied without a path, which is the identity the enumeration records and the consumer binds against.", nameof(sources));
            }

            if (!seen.Add(path))
            {
                throw new ArgumentException($"'{path}' was supplied more than once, so its record would be ambiguous.", nameof(sources));
            }

            (string family, string use) = FamilyOf(path, source);
            bool isBound = bound.Contains(path) || bound.Any(name => StringComparer.OrdinalIgnoreCase.Equals(name, path));
            string key = KeyOf(path);
            Arena2CanvasSet canvases = Arena2CanvasReader.Read(bytes.Span, path);
            string bindingNote = isBound
                ? $"Bound by {consumer}."
                : "No published consumer binds this file; it is retained unbound with its candidate use.";
            files.Add(new CharacterMediaRecord(
                path,
                family,
                canvases.Canvases,
                key,
                use,
                isBound ? consumer : string.Empty,
                // Binding and decodability are separate facts, as in the UI inventory: a
                // bound file stays bound whether or not this repository reads it.
                isBound ? MediaBinding.Admitted : MediaBinding.RequiredPending,
                canvases.Kind,
                $"{bindingNote} {canvases.Description} Candidate use: {use}."));
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
