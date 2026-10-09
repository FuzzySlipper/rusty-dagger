using System.Collections.Concurrent;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The checkout's content files, read from disk once per test run. The generated content tree is
/// large (the imported payload alone is hundreds of megabytes) and does not change while tests run,
/// so every fixture shares one copy of each file's bytes and of each directory listing. Fixtures
/// still build their own content fakes over these bytes, so no fake state is shared.
/// </summary>
internal static class TestContentFiles
{
    private static readonly ConcurrentDictionary<string, byte[]> Bytes = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string[]> Listings = new(StringComparer.Ordinal);

    /// <summary>A file's bytes; callers must not modify the shared array.</summary>
    internal static byte[] Read(string path) => Bytes.GetOrAdd(Path.GetFullPath(path), File.ReadAllBytes);

    /// <summary>A file's bytes read afresh, for large bundle bodies a fixture reads only when the product opens them.</summary>
    internal static byte[] ReadUncached(string path) => File.ReadAllBytes(path);

    /// <summary>Every file under a directory, recursively, in the order the file system lists them.</summary>
    internal static string[] AllFiles(string directory) =>
        Listings.GetOrAdd(Path.GetFullPath(directory), value => Directory.GetFiles(value, "*", SearchOption.AllDirectories));
}
