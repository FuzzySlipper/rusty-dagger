using Daggerfall.Import.Audio;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class ClassicMusicCatalogueTests
{
    [Fact]
    public void Every_cue_is_a_donor_file_that_the_engine_admits_without_conversion()
    {
        // Compressed admission is what lets a cue be the donor's own file: the import carries the source
        // bytes rather than a downsampled rewrite of them.
        Assert.All(ClassicMusicCatalogue.All, cue => Assert.EndsWith(".ogg", cue.SourceFile, StringComparison.Ordinal));
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
        foreach (string context in new[]{"night", "tavern", "shop", "magesguild", "temple", "templeneutral", "templebad", "knight", "rain", "snow", "interior", "palace", "overcast"})
            Assert.Contains(ClassicMusicCatalogue.All, cue => cue.Context == context);
        Assert.Contains(ClassicMusicCatalogue.All, cue => cue.Context == "dungeon");
        Assert.Contains(ClassicMusicCatalogue.All, cue => cue.Context == "sunny");
        Assert.Contains(ClassicMusicCatalogue.All, cue => cue.Context == "sunny-fm");
        Assert.True(ClassicMusicCatalogue.All.Count(cue => cue.Context == "sunny") >= 4);
    }
}
