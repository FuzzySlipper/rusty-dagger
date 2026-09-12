namespace Daggerfall.Import.Arena2;

/// <summary>Which classic quest-source family a file belongs to.</summary>
public enum QuestSourceFamily
{
    /// <summary>A `.QBN` quest-logic binary.</summary>
    QuestBinary,

    /// <summary>A `.QRC` quest-resource companion.</summary>
    QuestResources,
}

/// <summary>How a quest source file pairs with its companion.</summary>
public enum QuestSourcePairing
{
    /// <summary>Both the binary and its resource companion are supplied.</summary>
    Paired,

    /// <summary>The binary is supplied and its companion is not.</summary>
    BinaryOnly,

    /// <summary>The resource companion is supplied and its binary is not.</summary>
    ResourcesOnly,
}

/// <summary>
/// One supplied quest source file: the path exactly as it is stored, the stem it pairs
/// on, and the companion it pairs with. The stem is compared case-insensitively because
/// the corpus stores six resource companions in lower case, but the path is never
/// normalized: it is the source identity a consumer cites.
/// </summary>
public sealed record QuestSourceFile(
    string Path,
    string FileName,
    string Stem,
    QuestSourceFamily Family,
    QuestSourcePairing Pairing,
    string? CompanionPath);

/// <summary>
/// The supplied classic quest source corpus: every `.QBN` and `.QRC` path with its exact
/// casing and its pairing disposition. This type preserves identity; it assigns no quest
/// meaning, and the envelope decoders it feeds claim no more than the bytes support.
/// </summary>
public sealed class QuestSourceInventory
{
    /// <summary>The binary family's extension.</summary>
    public const string BinaryExtension = ".QBN";

    /// <summary>The resource family's extension.</summary>
    public const string ResourcesExtension = ".QRC";

    private readonly Dictionary<string, QuestSourceFile> byPath;

    private QuestSourceInventory(string source, IReadOnlyList<QuestSourceFile> files)
    {
        Source = source;
        Files = files;
        byPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
    }

    /// <summary>Logical source identity supplied to <see cref="Enumerate"/>.</summary>
    public string Source { get; }

    /// <summary>Every supplied file, in path order.</summary>
    public IReadOnlyList<QuestSourceFile> Files { get; }

    /// <summary>The quest-logic binaries.</summary>
    public IEnumerable<QuestSourceFile> Binaries => Files.Where(file => file.Family == QuestSourceFamily.QuestBinary);

    /// <summary>The resource companions.</summary>
    public IEnumerable<QuestSourceFile> Resources => Files.Where(file => file.Family == QuestSourceFamily.QuestResources);

    /// <summary>The binaries whose resource companion is not supplied.</summary>
    public IEnumerable<QuestSourceFile> BinaryOnly => Files.Where(file => file.Pairing == QuestSourcePairing.BinaryOnly);

    /// <summary>The resource companions whose binary is not supplied.</summary>
    public IEnumerable<QuestSourceFile> ResourcesOnly => Files.Where(file => file.Pairing == QuestSourcePairing.ResourcesOnly);

    /// <summary>Enumerates the supplied quest source paths.</summary>
    /// <param name="paths">The paths to enumerate, each used exactly as supplied.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    public static QuestSourceInventory Enumerate(IEnumerable<string> paths, string source)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        List<QuestSourceFile> files = [];
        // Pairing compares stems case-insensitively, so two files in one family whose
        // stems differ only by case would make the pairing ambiguous.
        Dictionary<string, string> binaries = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> resources = new(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            if (path is null)
            {
                throw new ArgumentException($"Quest source path {index} is null; a path is the identity a consumer cites.", nameof(paths));
            }

            index++;
            string fileName = System.IO.Path.GetFileName(path);
            QuestSourceFamily family = FamilyOf(fileName, path, source);
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(stem))
            {
                // A stem is what files pair on, so a file without one has no identity to
                // pair by and would otherwise pair on the empty string with any other.
                throw new Arena2FormatException(source, 0, $"'{path}' has no stem before its extension, so it has no identity to pair on");
            }

            Dictionary<string, string> familyStems = family == QuestSourceFamily.QuestBinary ? binaries : resources;
            if (!familyStems.TryAdd(stem, path))
            {
                throw new Arena2FormatException(source, 0, $"'{path}' and '{familyStems[stem]}' claim the same quest stem '{stem}'");
            }

            files.Add(new QuestSourceFile(path, fileName, stem, family, default, CompanionPath: null));
        }

        // Pairing is by case-insensitive stem, and the companion path keeps its own
        // casing: the comparison never rewrites the identity it matches.
        List<QuestSourceFile> paired = [];
        foreach (QuestSourceFile file in files)
        {
            bool isBinary = file.Family == QuestSourceFamily.QuestBinary;
            Dictionary<string, string> companions = isBinary ? resources : binaries;
            string? companion = companions.GetValueOrDefault(file.Stem);
            paired.Add(file with
            {
                CompanionPath = companion,
                Pairing = companion is not null
                    ? QuestSourcePairing.Paired
                    : isBinary ? QuestSourcePairing.BinaryOnly : QuestSourcePairing.ResourcesOnly,
            });
        }

        return new QuestSourceInventory(source, paired);
    }

    /// <summary>Gets a file by its exact stored path.</summary>
    public bool TryGetByPath(string path, out QuestSourceFile? file)
    {
        ArgumentNullException.ThrowIfNull(path);
        return byPath.TryGetValue(path, out file);
    }

    private static QuestSourceFamily FamilyOf(string fileName, string path, string source)
    {
        if (fileName.EndsWith(BinaryExtension, StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(ResourcesExtension, StringComparison.OrdinalIgnoreCase))
        {
            return QuestSourceFamily.QuestBinary;
        }

        if (fileName.EndsWith(ResourcesExtension, StringComparison.OrdinalIgnoreCase))
        {
            return QuestSourceFamily.QuestResources;
        }

        // The supplied path is the identity, so the diagnostic cites it rather than the
        // file name derived from it, which is empty for a directory-like input.
        throw new Arena2FormatException(source, 0, $"'{path}' is in neither the {BinaryExtension} nor the {ResourcesExtension} family");
    }
}
