using Daggerfall.Import.Audio;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class ClassicMusicCatalogueTests
{
    [Fact]
    public void The_published_cue_set_fits_the_engines_budget_and_leaves_the_effects_their_share()
    {
        // The Engine admits 64 clips and 32 MiB in total, and the imported effect clips share that budget
        // with the score, so the music may not consume all of it. A cue added without regard to either
        // limit fails here rather than at load.
        Assert.NotEmpty(ClassicMusicCatalogue.All);
        Assert.True(ClassicMusicCatalogue.All.Count <= ClassicMusicCatalogue.MaximumCueCount);
        int total = ClassicMusicCatalogue.TotalBytes(ClassicMusicCatalogue.All);
        Assert.True(total <= ClassicMusicCatalogue.MaximumTotalBytes - ClassicMusicCatalogue.EffectHeadroomBytes,
            $"The declared cues need {total} bytes, leaving the effect clips less than {ClassicMusicCatalogue.EffectHeadroomBytes}.");
    }

    [Fact]
    public void Every_cue_is_a_donor_file_that_the_engine_admits_without_conversion()
    {
        // Compressed admission is what lets a cue be the donor's own file: the import carries the source
        // bytes rather than a downsampled rewrite of them.
        Assert.All(ClassicMusicCatalogue.All, cue => Assert.EndsWith(".ogg", cue.SourceFile, StringComparison.Ordinal));
        Assert.All(ClassicMusicCatalogue.All, cue => Assert.True(cue.SourceBytes > 0, $"{cue.MediaId} has no source length."));
        Assert.All(ClassicMusicCatalogue.All, cue => Assert.Equal("music.", cue.MediaId[..6]));
        Assert.All(ClassicMusicCatalogue.All, cue => Assert.False(string.IsNullOrWhiteSpace(cue.Context)));
        Assert.Equal(ClassicMusicCatalogue.All.Count,
            ClassicMusicCatalogue.All.Select(cue => cue.MediaId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ClassicMusicCatalogue.All.Count,
            ClassicMusicCatalogue.All.Select(cue => cue.SourceFile).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_cue_set_covers_the_contexts_the_donor_plays_in()
    {
        // The set is deliberately small, so what it covers is worth stating: the dungeon context and both
        // versions of the sunny playlists, which are the ones the donor's song manager names first.
        Assert.Contains(ClassicMusicCatalogue.All, cue => cue.Context == "dungeon");
        Assert.Contains(ClassicMusicCatalogue.All, cue => cue.Context == "sunny");
        Assert.Contains(ClassicMusicCatalogue.All, cue => cue.Context == "sunny-fm");
        Assert.True(ClassicMusicCatalogue.All.Count(cue => cue.Context == "sunny") >= 4);
    }
}
