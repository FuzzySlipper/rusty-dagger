namespace Daggerfall.Import.Publication;

/// <summary>
/// The one vocabulary every published source path is written in, whatever directory the operator supplied
/// the bytes from: an Arena2 file is <c>arena2/NAME</c>, so a published section does not change with where the
/// operator keeps the corpus.
/// </summary>
public static class PublishedSourcePath
{
    /// <summary>The root every Arena2 corpus path is relative to.</summary>
    public const string Arena2Root = "arena2";

    /// <summary>An Arena2 corpus file or directory, by its path under the corpus directory.</summary>
    public static string Arena2(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return $"{Arena2Root}/{relativePath}";
    }
}
