namespace Daggerfall.Import.Arena2;

/// <summary>What the enumeration established about one UI media file.</summary>
public enum UiMediaDisposition
{
    /// <summary>The file is bound to a published consumer.</summary>
    Admitted,

    /// <summary>The file is supplied and no published consumer binds it yet.</summary>
    RequiredPending,

}

/// <summary>Whether the decoder read one UI media file, and by which path.</summary>
public enum UiMediaDecode
{
    /// <summary>The file carries a standard IMG record.</summary>
    Header,

    /// <summary>The file is a headerless UI canvas, which the classic UI publication reads.</summary>
    Headerless,

    /// <summary>Neither decoder path read the file.</summary>
    Unread,
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
    /// The documented families and the file count the inventory records for each, in
    /// documentation order.
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

    /// <summary>Enumerates the documented UI media families.</summary>
    /// <param name="sources">File name and bytes for every supplied file.</param>
    /// <param name="admitted">File names a published consumer binds.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    public static UiMediaInventory Enumerate(
        IEnumerable<(string Path, ReadOnlyMemory<byte> Bytes)> sources,
        IReadOnlySet<string> admitted,
        string source)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        List<UiMediaRecord> files = [];
        foreach ((string path, ReadOnlyMemory<byte> bytes) in sources.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            string family = FamilyOf(path, source);
            bool bound = admitted.Contains(path);
            UiMediaDisposition disposition = bound ? UiMediaDisposition.Admitted : UiMediaDisposition.RequiredPending;
            string consumer = bound ? "content/ui/ui-manifest.json" : string.Empty;
            IndexedImg? image = null;
            UiMediaDecode decode = UiMediaDecode.Unread;
            string reason = string.Empty;
            try
            {
                image = ImgDecoder.Decode(bytes.Span, path);
                decode = UiMediaDecode.Header;
            }
            catch (Arena2FormatException headerFailure)
            {
                // The classic UI publication reads some canvases through the headerless path,
                // so a file the standard reader refuses is not yet unread.
                try
                {
                    image = ImgDecoder.DecodeHeaderlessUiCanvas(bytes.Span, path);
                    decode = UiMediaDecode.Headerless;
                }
                catch (Arena2FormatException headerlessFailure)
                {
                    reason = $"{headerFailure.Message} The headerless UI canvas path also refused it: {headerlessFailure.Message}";
                }
            }

            string binding = bound
                ? "Bound by the published UI asset manifest."
                : "No published consumer binds this file; the binding is required-pending. Candidates named by this task's inventory: F095, F100, F102, F104, F105, F106.";
            files.Add(new UiMediaRecord(
                path,
                family,
                image?.Width ?? 0,
                image?.Height ?? 0,
                consumer,
                disposition,
                decode,
                decode == UiMediaDecode.Unread
                    ? $"{binding} Neither decoder path read it: {reason}"
                    : $"{binding} Read as {(decode == UiMediaDecode.Header ? "a standard IMG record" : "a headerless UI canvas")}, {image!.Width} by {image.Height} pixels."));
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
