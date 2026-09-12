namespace Daggerfall.Import.Arena2;

/// <summary>What the enumeration established about one UI media file.</summary>
public enum UiMediaDisposition
{
    /// <summary>The file is bound to a published consumer.</summary>
    Admitted,

    /// <summary>The file is supplied and no published consumer binds it yet.</summary>
    RequiredPending,

}

/// <summary>One supplied UI media file: its family, its canvas, and what binds it.</summary>
public sealed record UiMediaRecord(
    string Path,
    string Family,
    int Width,
    int Height,
    string Consumer,
    UiMediaDisposition Disposition,
    UiMediaDecode Decode,
    string Note);

/// <summary>
/// The classic UI media inventory: every file in the documented families with its canvas
/// and its binding. A file no published consumer binds is retained as required-pending
/// rather than dropped, because the family exists whether or not a window consumes it yet.
/// </summary>
/// <remarks>
/// This records source facts. It names no DOM projection, action or window topology, and
/// the files it admits are the ones the published UI asset manifest already binds.
/// </remarks>
public sealed class UiMediaInventory
{
    /// <summary>
    /// The families in documentation order with the count the corpus supplies for each.
    /// Every count matches the documented CNT-020 vector except INFO, where the corpus
    /// carries two files and the documentation records one: INFO01I0.IMG is supplied and
    /// undocumented, which the reconciliation test asserts rather than hides.
    /// </summary>
    public static readonly (string Prefix, int Count)[] DocumentedFamilies =
    [
        ("MAIN", 6), ("GILD", 2), ("BANK", 4), ("SHOP", 9), ("TALK", 4), ("REST", 3),
        ("INFO", 2), ("INVE", 18), ("ITEM", 2), ("BOOK00I0", 1), ("SCRL", 12),
    ];

    private UiMediaInventory(string source, IReadOnlyList<UiMediaRecord> files)
    {
        Source = source;
        Files = files;
    }

    /// <summary>Logical source identity supplied to <see cref="Enumerate"/>.</summary>
    public string Source { get; }

    /// <summary>Every supplied file, ordered by family and then path.</summary>
    public IReadOnlyList<UiMediaRecord> Files { get; }

    /// <summary>The files a published consumer binds.</summary>
    public IEnumerable<UiMediaRecord> Admitted => Files.Where(file => file.Disposition == UiMediaDisposition.Admitted);

    /// <summary>The files no published consumer binds yet.</summary>
    public IEnumerable<UiMediaRecord> RequiredPending => Files.Where(file => file.Disposition == UiMediaDisposition.RequiredPending);

    /// <summary>The supplied files neither decoder path reads.</summary>
    public IEnumerable<UiMediaRecord> Unread => Files.Where(file => file.Decode == UiMediaDecode.Unread);

    /// <summary>
    /// Enumerates the documented UI media families for a caller that names no consumer.
    /// </summary>
    public static UiMediaInventory Enumerate(
        IEnumerable<(string Path, ReadOnlyMemory<byte> Bytes)> sources,
        IReadOnlySet<string> admitted,
        string source) =>
        Enumerate(sources, admitted, UnstatedConsumer, source);

    /// <summary>The label a bound record carries when its caller names no consumer.</summary>
    public const string UnstatedConsumer = "an unstated consumer";

    /// <summary>Enumerates the documented UI media families.</summary>
    /// <param name="sources">File name and bytes for every supplied file.</param>
    /// <param name="admitted">File names a published consumer binds.</param>
    /// <param name="consumer">The consumer that binds those files, named by the caller.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    public static UiMediaInventory Enumerate(
        IEnumerable<(string Path, ReadOnlyMemory<byte> Bytes)> sources,
        IReadOnlySet<string> admitted,
        string consumer,
        string source)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        List<UiMediaRecord> files = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach ((string path, ReadOnlyMemory<byte> bytes) in sources.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            // A path is the identity and the binding is matched against it, so a missing or
            // repeated path is refused here rather than producing two records for one file.
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A UI media file was supplied without a path, which is the identity the enumeration records and the consumer binds against.", nameof(sources));
            }

            if (!seen.Add(path))
            {
                throw new ArgumentException($"'{path}' was supplied more than once, so its record would be ambiguous.", nameof(sources));
            }

            string family = FamilyOf(path, source);
            // The family is matched case-insensitively, so the binding is too: a consumer
            // naming MAIN00I0.IMG binds main00i0.img rather than half-matching it.
            bool bound = admitted.Contains(path) || admitted.Any(name => StringComparer.OrdinalIgnoreCase.Equals(name, path));
            UiMediaDisposition disposition = bound ? UiMediaDisposition.Admitted : UiMediaDisposition.RequiredPending;
            bool read = ImgDecoder.TryDecodeUi(bytes.Span, path, out IndexedImg? image, out UiMediaDecode decode, out string reason);
            string binding = bound
                ? $"Bound by {consumer}."
                : "No published consumer binds this file; the binding is required-pending. Candidates named by this task's inventory: F095, F100, F102, F104, F105, F106.";
            files.Add(new UiMediaRecord(
                path,
                family,
                image?.Width ?? 0,
                image?.Height ?? 0,
                bound ? consumer : string.Empty,
                disposition,
                decode,
                read
                    ? $"{binding} Read as {(decode == UiMediaDecode.Header ? "a standard IMG record" : "a headerless UI canvas")}, {image!.Width} by {image.Height} pixels."
                    : $"{binding} Neither decoder path read it: {reason}"));
        }

        return new UiMediaInventory(source, files);
    }

    /// <summary>The supplied files of one documented family.</summary>
    public IEnumerable<UiMediaRecord> Family(string prefix) => Files.Where(file => StringComparer.Ordinal.Equals(file.Family, prefix));

    private static string FamilyOf(string path, string source)
    {
        string name = System.IO.Path.GetFileName(path);
        string? family = DocumentedFamilies
            .Select(entry => entry.Prefix)
            .FirstOrDefault(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return family ?? throw new Arena2FormatException(source, 0, $"'{name}' is in none of the documented UI media families");
    }
}
