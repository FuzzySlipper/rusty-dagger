using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>The published catalog of the numeric sound archive.</summary>
public sealed class SoundCatalogTests
{
    [Fact]
    public void Catalogues_every_numeric_clip_in_the_archives_own_order()
    {
        SoundArchive archive = SoundArchive.Parse(File.ReadAllBytes(Corpus("DAGGER.SND")), "arena2/DAGGER.SND");
        DaggerfallSoundCatalog catalog = DaggerfallSoundCatalogBuilder.Build(archive);
        catalog.Validate();

        // Measured: the archive carries four hundred and fifty-nine numeric records, and the catalog is
        // the archive's own directory order because that ordinal is the identity a consumer keeps.
        Assert.Equal(459, archive.Count);
        Assert.Equal(459, catalog.Clips.Count);
        Assert.Equal(Enumerable.Range(0, 459), catalog.Clips.Select(clip => clip.Ordinal));
        Assert.Equal(catalog.Clips.Select(clip => clip.Ordinal), catalog.Clips.Select(clip => clip.Ordinal).Order());

        // Every clip carries a numeric identity and a stated reason, and the six the product publishes
        // are admitted while the rest say they have no consumer rather than being absent.
        // The numeric identity is not unique in this archive: four hundred and fifty-nine records carry
        // four hundred and fifty-eight distinct numbers, so exactly one number appears twice. That is
        // why the catalog's stable identity is the ordinal and the numeric id is carried beside it -
        // a consumer keying on the number would have one collision it could not resolve.
        Assert.Equal(458, catalog.Clips.Select(clip => clip.NumericId).Distinct().Count());
        Assert.Single(catalog.Clips.GroupBy(clip => clip.NumericId), group => group.Count() == 2);
        Assert.All(catalog.Clips, clip => Assert.False(string.IsNullOrWhiteSpace(clip.Reason)));
        Assert.Equal(6, catalog.Clips.Count(clip => clip.Disposition == DaggerfallSoundClipDisposition.Admitted));
        Assert.Equal(452, catalog.Clips.Count(clip => clip.Disposition == DaggerfallSoundClipDisposition.ReadableNoConsumer));

        // One record of the four hundred and fifty-nine carries no sample bytes at all, and it is the
        // case the task asks to be explicit rather than omitted: the catalog states it, names why, and
        // still counts it, so the archive cannot lose a clip without the count changing.
        DaggerfallSoundClip unsupported = Assert.Single(catalog.Clips, clip => clip.Disposition == DaggerfallSoundClipDisposition.Unsupported);
        Assert.Equal(0, unsupported.ByteLength);
        Assert.Contains("no sample bytes", unsupported.Reason, StringComparison.Ordinal);
        Assert.Equal(459, catalog.Clips.Count);
        Assert.All(catalog.Clips.Where(clip => clip.Disposition == DaggerfallSoundClipDisposition.Admitted),
            clip => Assert.StartsWith("audio.", clip.UsageCandidate, StringComparison.Ordinal));

        // A representative clip decodes to samples, which is what "readable" has to mean rather than a
        // record that merely exists in the directory.
        Arena2PcmClip swing = archive.GetClip(106);
        Assert.False(swing.PcmUnsigned8.IsEmpty);
        Assert.Equal("audio.melee.dagger.swing", catalog.Clips[106].UsageCandidate);
        Assert.NotEmpty(archive.CreateWave(106));

        // The catalog is deterministic: the same archive catalogues the same way twice.
        DaggerfallSoundCatalog again = DaggerfallSoundCatalogBuilder.Build(archive);
        Assert.Equal(
            catalog.Clips.Select(clip => (clip.Ordinal, clip.NumericId, clip.ByteLength, clip.Disposition, clip.UsageCandidate)),
            again.Clips.Select(clip => (clip.Ordinal, clip.NumericId, clip.ByteLength, clip.Disposition, clip.UsageCandidate)));
    }

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AGENTS.md")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
