using System.Text.Json;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The map/travel consumer contract: a published region map joins to its normalized region and
/// to a media descriptor with bytes on disk, while artwork never carries coordinates of its own.
/// </summary>
public sealed class MapMediaLocationJoinTests
{
    [Fact]
    public void Region_maps_join_to_normalized_regions_and_bytes_on_disk()
    {
        string root = TestData.RepositoryRoot;
        JsonDocument pack = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.imported.json")));
        JsonDocument sidecar = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold/media/classic/manifest.json")));

        // The normalized regions the locations task publishes.
        HashSet<int> regions = [.. pack.RootElement.GetProperty("locations").GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("region").GetInt32())];
        Assert.Equal(62, regions.Count);

        JsonElement images = sidecar.RootElement.GetProperty("mapMedia");
        Dictionary<string, string> descriptors = sidecar.RootElement.GetProperty("media").GetProperty("resources").EnumerateArray()
            .ToDictionary(descriptor => descriptor.GetProperty("id").GetString()!, descriptor => descriptor.GetProperty("relativePath").GetString()!);
        int joined = 0;
        foreach (JsonElement region in sidecar.RootElement.GetProperty("mapRegions").EnumerateArray())
        {
            int id = region.GetProperty("region").GetInt32();
            Assert.Contains(id, regions);
            foreach (JsonElement media in region.GetProperty("mediaIds").EnumerateArray())
            {
                string mediaId = media.GetString()!;
                JsonElement image = images.EnumerateArray().Single(entry => entry.GetProperty("mediaId").GetString() == mediaId);
                Assert.Contains(id, image.GetProperty("regions").EnumerateArray().Select(entry => entry.GetInt32()));
                string path = Assert.Contains(mediaId, descriptors);
                Assert.True(File.Exists(Path.Combine(root, "content/worldrpg/imports/privateers-hold", path)), $"Published map image '{mediaId}' has no bytes at '{path}'.");
                joined++;
            }
        }

        Assert.Equal(
            images.EnumerateArray().Count(image => image.GetProperty("regions").EnumerateArray().Any()),
            joined);
        // The images that draw no region are travel and town chrome: they resolve through their
        // donor call-site bindings rather than through a region.
        Assert.Equal(
            images.EnumerateArray().Count(image => !image.GetProperty("regions").EnumerateArray().Any()),
            images.EnumerateArray().Count(image => image.GetProperty("kind").GetString() != "regionMap"));
    }
}
