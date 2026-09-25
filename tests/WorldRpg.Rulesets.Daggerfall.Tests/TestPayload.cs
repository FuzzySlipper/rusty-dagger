using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The one parsed copy of the base payload that facts share. Reading it parses a 75 MiB document, so
/// parsing it inside every fact — as the suite used to, hundreds of times per run — cost far more than
/// the assertions did. The parsed value is immutable once read, which is what makes sharing it across
/// parallel test classes safe; a fact that deliberately reads tampered bytes still reads its own.
/// </summary>
internal static class TestPayload
{
    private static readonly Lazy<DaggerfallDefinitions> Shared = new(Read);

    /// <summary>The parsed base payload.</summary>
    internal static DaggerfallDefinitions Definitions => Shared.Value;

    /// <summary>The payload's path, for the facts that inspect the file itself.</summary>
    internal static string Path => System.IO.Path.Combine(Root().FullName, "content", "worldrpg", "payloads", "daggerfall.base.json");

    /// <summary>The payload's bytes, for the facts that hand them to a reader of their own.</summary>
    internal static byte[] Bytes => File.ReadAllBytes(Path);

    private static DaggerfallDefinitions Read() => DaggerfallBaseContent.Read(Bytes);

    private static DirectoryInfo Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "content", "worldrpg", "payloads", "daggerfall.base.json")))
                return directory;
        }
        throw new InvalidOperationException("No directory above the test output holds the base payload.");
    }
}
