using System.Text;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallLeftOutMediaTests
{
    private static readonly DaggerfallArtReplacements Art = DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes("""
        {"artReplacements": {"images": [{"replaces": "screen.title", "mediaId": "refreshed.screen.title", "path": "title.webp", "byteLength": 1,
          "sha256": "0000000000000001000000000000000200000000000000030000000000000004"}],
         "surfaces": [{"replaces": {"archive": 0, "record": 40}, "path": "surface.png", "byteLength": 1,
          "sha256": "0000000000000001000000000000000200000000000000030000000000000004", "width": 64, "height": 64}]}}
        """), "test.art");

    private static DaggerfallData Data(string leftOut)
    {
        ContentFake content = new([]);
        content.Add(InstalledContent.DescriptionPath, Encoding.UTF8.GetBytes($$"""{"kind": "worldrpg.data-container", "leftOut": {{leftOut}}}"""));
        return DataOf(content);
    }

    [Fact]
    public void A_left_out_identity_the_art_pack_still_draws_anew_composes()
    {
        DaggerfallLeftOutMedia.Check(Data("""{"media": ["screen.title"], "surfaces": ["0-40"], "sky": "the original panoramas"}"""), Art);
        // Without a left-out list, as a test's content or an older import has, nothing is checked and classic art needs no container.
        ContentFake whole = new([]);
        Assert.Null(DataOf(whole).ClassicArtMissing);
    }

    [Fact]
    public void A_left_out_identity_the_art_pack_no_longer_draws_is_refused_by_name()
    {
        DaggerfallData data = Data("""{"media": ["screen.title", "screen.death"], "surfaces": ["0-40", "0-41"], "models": ["444"]}""");
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => DaggerfallLeftOutMedia.Check(data, Art));
        Assert.Contains("media screen.death", refused.Message, StringComparison.Ordinal);
        Assert.Contains("surfaces 0-41", refused.Message, StringComparison.Ordinal);
        Assert.Contains("models 444", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("screen.title", refused.Message, StringComparison.Ordinal);
        // A bundle composed without the art pack draws none of them anew.
        Assert.Contains("surfaces 0-40", Assert.Throws<InvalidOperationException>(() => DaggerfallLeftOutMedia.Check(data, null)).Message, StringComparison.Ordinal);
        Assert.Equal($"{DaggerfallData.ClassicArtContainerName} is not installed.", data.ClassicArtMissing);
    }
}
