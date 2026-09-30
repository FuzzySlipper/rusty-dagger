namespace Daggerfall.Import.Publication;

/// <summary>
/// The one vocabulary every published source path is written in, whatever directory the operator supplied
/// the bytes from: an Arena2 file is <c>arena2/NAME</c> and a Daggerfall Unity file is
/// <c>daggerfall-unity/PATH</c>, relative to that checkout's root. The documented inventory
/// (<c>data/content-source-manifest.csv</c>) spells its rows the same way, so a section that checks its
/// label against the inventory compares like with like.
/// </summary>
public static class PublishedSourcePath
{
    /// <summary>The root every Arena2 corpus path is relative to.</summary>
    public const string Arena2Root = "arena2";

    /// <summary>The root every Daggerfall Unity path is relative to.</summary>
    public const string DonorRoot = "daggerfall-unity";

    /// <summary>An Arena2 corpus file or directory, by its path under the corpus directory.</summary>
    public static string Arena2(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return $"{Arena2Root}/{relativePath}";
    }

    /// <summary>A Daggerfall Unity file or directory, by its path under the checkout root.</summary>
    public static string Donor(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return $"{DonorRoot}/{relativePath}";
    }
}
