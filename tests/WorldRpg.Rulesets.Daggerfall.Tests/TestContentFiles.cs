using System.Collections.Concurrent;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Content files the suite reads under the repository's <c>content/</c> path: a tracked file from the checkout,
/// and a file the import derived from the real data container, or, for the classic media it leaves out, the classic-art
/// container beside it, read through the shared Engine test host.
/// </summary>
/// <remarks>
/// The product sees the same union: its tracked content beside the opened container, at the same
/// content-root-relative paths. A fixture keeps naming <c>content/&lt;path&gt;</c> and never which of the two
/// holds it. Reads are cached, since many fixtures read the same large payloads.
/// </remarks>
internal static class TestContentFiles
{
    private static readonly ConcurrentDictionary<string, byte[]> Bytes = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string[]> Listings = new(StringComparer.Ordinal);

    private static string ContentRoot { get; } = Path.Combine(TestData.RepositoryRoot, "content");

    /// <summary>A file's bytes; callers must not modify the shared array.</summary>
    internal static byte[] Read(string path) => Bytes.GetOrAdd(Path.GetFullPath(path), ReadFresh);

    /// <summary>A file's bytes read afresh, for large bundle bodies a fixture reads only when the product opens them.</summary>
    internal static byte[] ReadUncached(string path) => ReadFresh(Path.GetFullPath(path));

    /// <summary>A file's bytes read afresh into an array the caller may change, as <see cref="File.ReadAllBytes(string)"/> reads them.</summary>
    internal static byte[] ReadCopy(string path) => ReadFresh(Path.GetFullPath(path));

    /// <summary>A file's text read afresh, as <see cref="File.ReadAllText(string)"/> reads it.</summary>
    internal static string ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : System.Text.Encoding.UTF8.GetString(ReadFresh(Path.GetFullPath(path)));

    /// <summary>Whether a tracked or derived file exists at this path.</summary>
    internal static bool Exists(string path) => File.Exists(path) || DerivedPath(Path.GetFullPath(path)) is not null;

    /// <summary>Every file under a directory, recursively: the tracked ones in the order the file system lists them, then the derived ones in path order.</summary>
    internal static string[] AllFiles(string directory) =>
        Listings.GetOrAdd(Path.GetFullPath(directory), value =>
        {
            string[] tracked = Directory.Exists(value) ? Directory.GetFiles(value, "*", SearchOption.AllDirectories) : [];
            if (ContentRelative(value) is not { } relative || !TestDataContainers.Exists(TestDataContainers.Data)) return tracked;
            string prefix = relative.Length == 0 ? string.Empty : relative + "/";
            HashSet<string> seen = new(tracked, StringComparer.Ordinal);
            return [.. tracked, .. Derived.SelectMany(container => TestDataContainers.Entries(container).Keys)
                .Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(path => Path.Combine(ContentRoot, path.Replace('/', Path.DirectorySeparatorChar)))
                .Where(seen.Add)];
        });

    /// <summary>The containers derived files are read from: the data container, then the classic-art container where a real import packed one.</summary>
    private static IEnumerable<string> Derived => new[] { TestDataContainers.Data, TestDataContainers.ClassicArt }.Where(TestDataContainers.Exists);

    /// <summary>The content-root-relative path of a derived file a container carries, or null.</summary>
    internal static string? DerivedPath(string path) => Holder(path)?.Relative;

    private static (string Container, string Relative)? Holder(string path) =>
        ContentRelative(path) is { Length: > 0 } relative && Derived.FirstOrDefault(container => TestDataContainers.Contains(container, relative)) is { } held
            ? (held, relative) : null;

    private static byte[] ReadFresh(string path) =>
        File.Exists(path) ? File.ReadAllBytes(path)
        : Holder(path) is { } held ? TestDataContainers.Read(held.Container, held.Relative)
        : throw new FileNotFoundException($"'{path}' is neither tracked nor in the data or classic-art container.", path);

    private static string? ContentRelative(string path)
    {
        string relative = Path.GetRelativePath(ContentRoot, path);
        if (relative == ".") return string.Empty;
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
