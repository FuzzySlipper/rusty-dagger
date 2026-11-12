using System.Text.Json;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>
/// The Daggerfall Unity file the values in one of our tables were taken from: its <c>daggerfall-unity/PATH</c>
/// label, the Daggerfall Unity commit, and the digest and length of the bytes they were read from. A published
/// section cites the donor file with it exactly as it did when the import still read that file, and the drift check
/// (<c>daggerfall-source-tool donor-drift</c>) compares it with a Daggerfall Unity checkout.
/// </summary>
public sealed record TakenFrom(string Path, string Commit, ContentDigest ContentDigest, long ByteLength)
{
    /// <summary>The donor file as a published section cites it.</summary>
    public PublishedSource Cited() => new(Path, ContentDigest, ByteLength);

    /// <summary>The donor file a table was taken from, by the bytes it was read from.</summary>
    public static TakenFrom Of(string path, string commit, ReadOnlySpan<byte> bytes) =>
        new(path, commit, ContentDigest.Compute(bytes), bytes.Length);
}

/// <summary>
/// Reads our own tables taken once from Daggerfall Unity, strictly, so a member no type declares is refused rather
/// than ignored. They are the published section dialect, indented, with text written as it reads.
/// </summary>
public static class TakenTable
{
    /// <summary>Reads one table, naming the file when it holds none.</summary>
    public static T Read<T>(ReadOnlySpan<byte> bytes, string label) where T : class =>
        JsonSerializer.Deserialize<T>(bytes, PublishedJson.SectionRead) ?? throw new InvalidDataException($"'{label}' holds no table.");
}
