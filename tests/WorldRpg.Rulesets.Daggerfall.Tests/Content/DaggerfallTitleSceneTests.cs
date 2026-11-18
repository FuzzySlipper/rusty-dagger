using System.Text;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTitleSceneTests
{
    private const string PayloadPath = "worldrpg/packs/daggerfall.art-replacements/payload.json";

    private static JsonNode Payload() => JsonNode.Parse(TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content", PayloadPath)).AsSpan())!;

    private static DaggerfallArtReplacements Read(JsonNode payload) =>
        DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(payload.ToJsonString()), "daggerfall.art-replacements");

    [Fact]
    public void The_art_pack_draws_the_title_as_a_town_seen_through_a_window_from_files_it_carries()
    {
        DaggerfallArtReplacements art = Read(Payload());
        DaggerfallTitleScene scene = Assert.IsType<DaggerfallTitleScene>(art.TitleScene);
        // The scene lies over the title's own replacement, the town painting.
        DaggerfallArtReplacement town = Assert.IsType<DaggerfallArtReplacement>(art.For("screen.title"));
        Assert.EndsWith("/title-scene/town.webp", town.Path, StringComparison.Ordinal);
        Assert.Contains(scene.Lights, light => light.Lamp);
        Assert.Contains(scene.Lights, light => !light.Lamp);
        Assert.Equal(2, scene.Window.Flames.Count);
        // Every layer is the pack's own file, of the length the payload states, and among the images the set publishes.
        foreach (DaggerfallArtImage image in scene.Images.Append(new(town.MediaId, town.Path, town.ByteLength)))
        {
            Assert.Equal(image.ByteLength, TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content", image.Path)).Length);
            Assert.Contains(art.Images, published => published.MediaId == image.MediaId);
        }
    }

    [Fact]
    public void A_scene_that_does_not_fit_its_town_or_window_is_refused_by_name()
    {
        static void Refused(Action<JsonNode> edit, string reason)
        {
            JsonNode payload = Payload();
            edit(payload["artReplacements"]!["titleScene"]!);
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Read(payload));
            Assert.Contains("daggerfall.art-replacements", refused.Message, StringComparison.Ordinal);
            Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
        }

        Refused(scene => scene["sea"]!["y"] = 5000, "its sea lies outside the town");
        Refused(scene => scene["lights"]!.AsArray().Add(new JsonObject { ["x"] = -1, ["y"] = 10, ["lamp"] = false }), "a light lies outside the town");
        Refused(scene => scene["window"]!["opening"]!["width"] = 99999, "its window's opening lies outside the window");
        Refused(scene => scene["clouds"]!["drift"] = 0, "its drift is not above zero");
        Refused(scene => scene["window"]!["mediaId"] = "", "a layer without a usable identity");
    }

    [Fact]
    public void A_scene_without_the_title_it_lays_over_is_refused()
    {
        JsonNode payload = Payload();
        JsonArray images = payload["artReplacements"]!["images"]!.AsArray();
        images.Remove(images.Single(image => (string?)image!["replaces"] == "screen.title"));
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Read(payload));
        Assert.Contains("without the sized title image it lays over", refused.Message, StringComparison.Ordinal);
    }
}
