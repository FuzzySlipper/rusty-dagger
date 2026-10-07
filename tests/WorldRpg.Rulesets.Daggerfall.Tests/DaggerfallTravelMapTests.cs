using System.Buffers.Binary;
using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The travel map's placement of the published map art and the views it reads from travel destinations.</summary>
public sealed class DaggerfallTravelMapTests
{
    private static readonly SharedFixture<IReadOnlyList<DaggerfallTravelDestination>> All = new(() =>
        new DaggerfallTravelPolicy(new DaggerfallSiteContext(TestPayload.Definitions.Locations), TestPayload.Definitions.Grids,
            DaggerfallTuning.Defaults.Transport).AllDestinations());

    [Fact]
    public void Every_image_the_travel_map_draws_is_published_map_art_of_the_size_it_lays_out()
    {
        string root = TestData.RepositoryRoot;
        using JsonDocument inventory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content", DaggerfallUiArt.InventoryPath)));
        Dictionary<string, string> paths = inventory.RootElement.GetProperty("artifacts").EnumerateArray()
            .Where(artifact => artifact.TryGetProperty("mediaId", out JsonElement id) && id.GetString()!.StartsWith("map.", StringComparison.Ordinal))
            .ToDictionary(artifact => artifact.GetProperty("mediaId").GetString()!, artifact => artifact.GetProperty("path").GetString()!);
        DaggerfallTravelMapView view = new DaggerfallTravelMap(All.Value).Read([], [], null, null, 0);
        List<DaggerfallTravelMapImage> images = [view.World];
        foreach (DaggerfallTravelMapRegion region in view.Regions)
            for (int page = 0; page < DaggerfallTravelMap.PageCount(region.Region); page++)
                images.Add(new DaggerfallTravelMap(All.Value).Read([], [], null, region.Region, page).Sheet!.Image);
        Assert.Equal([.. DaggerfallTravelMap.MediaIds.Order(StringComparer.Ordinal)], [.. images.Select(image => image.MediaId).Order(StringComparer.Ordinal)]);
        Assert.Equal(DaggerfallTravelMap.MediaIds.Count, DaggerfallTravelMap.MediaIds.Distinct(StringComparer.Ordinal).Count());
        foreach (DaggerfallTravelMapImage image in images)
        {
            Assert.True(paths.TryGetValue(image.MediaId, out string? path), $"{image.MediaId} is not published map art");
            byte[] header = File.ReadAllBytes(Path.Combine(root, "content", path!)).AsSpan(0, 24).ToArray();
            Assert.Equal((image.Width, image.Height), (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20))));
        }
        // The world view draws the whole world map inside its canvas.
        Assert.True(view.World.Left <= 0 && view.World.Top <= 0
            && view.World.Right >= DaggerfallTravelMapPixel.Width && view.World.Bottom >= DaggerfallTravelMapPixel.Height);
    }

    [Fact]
    public void Every_location_of_a_mapped_region_falls_on_one_of_its_sheets()
    {
        DaggerfallTravelMap map = new(All.Value);
        IReadOnlyList<string> names = TestPayload.Definitions.BuildingNames.RegionNames;
        foreach (IGrouping<int, DaggerfallTravelDestination> region in All.Value.GroupBy(destination => destination.Id.Region))
        {
            int pages = DaggerfallTravelMap.PageCount(region.Key);
            if (pages == 0) continue;
            HashSet<DaggerfallSiteId> drawn = [];
            for (int page = 0; page < pages; page++)
                drawn.UnionWith(map.Read(All.Value, names, null, region.Key, page).Sheet!.Destinations.Select(destination => destination.Id));
            Assert.Equal([.. region.Select(destination => destination.Id).OrderBy(id => id.Region).ThenBy(id => id.Index)], [.. drawn.OrderBy(id => id.Region).ThenBy(id => id.Index)]);
        }
        // The one region with locations and no travel-map art: the donor's travel window offers no sheet
        // for it either, so its sites stay reachable through the destination search.
        Assert.Equal(new[] { 31 }, All.Value.Select(destination => destination.Id.Region).Distinct().Where(region => DaggerfallTravelMap.PageCount(region) == 0));
    }

    [Fact]
    public void A_sheet_draws_only_the_destinations_it_is_given_and_regions_count_them()
    {
        DaggerfallTravelMap map = new(All.Value);
        IReadOnlyList<string> names = TestPayload.Definitions.BuildingNames.RegionNames;
        DaggerfallTravelDestination[] region = [.. All.Value.Where(destination => destination.Id.Region == 17)];
        DaggerfallTravelDestination[] known = [.. region.Where((_, index) => index % 3 == 0)];
        DaggerfallTravelMapPixel player = known[0].MapPixel;
        DaggerfallTravelMapView view = map.Read(known, names, player, 17, 0);
        Assert.Equal(player, view.Player);
        DaggerfallTravelMapRegion entry = Assert.Single(view.Regions, candidate => candidate.Region == 17);
        Assert.Equal(names[17], entry.Name);
        Assert.Equal(known.Length, entry.Discovered);
        Assert.All(view.Regions.Where(candidate => candidate.Region != 17), candidate => Assert.Equal(0, candidate.Discovered));
        // A region's name sits where its locations are: inside its own sheet and on the world view.
        Assert.True(view.Sheet!.Image.Shows(new((int)entry.X, (int)entry.Y)));
        Assert.True(view.World.Shows(new((int)entry.X, (int)entry.Y)));
        Assert.Equal(17, view.Sheet.Region);
        Assert.Equal(names[17], view.Sheet.Name);
        Assert.Equal((0, 1), (view.Sheet.Page, view.Sheet.Pages));
        Assert.Equal(known.Select(destination => destination.Id), view.Sheet.Destinations.Select(destination => destination.Id));
        Assert.Null(map.Read(known, names, null, 17, 1).Sheet);
        Assert.Null(map.Read(known, names, null, 31, 0).Sheet);
    }

    [Fact]
    public void A_destination_is_drawn_in_the_donor_s_filter_family()
    {
        Assert.Equal("dungeon", DaggerfallSiteKinds.MapCategory(DaggerfallSiteKind.DungeonLabyrinth));
        Assert.Equal("dungeon", DaggerfallSiteKinds.MapCategory(DaggerfallSiteKind.Graveyard));
        Assert.Equal("dungeon", DaggerfallSiteKinds.MapCategory(DaggerfallSiteKind.Coven));
        Assert.Equal("home", DaggerfallSiteKinds.MapCategory(DaggerfallSiteKind.HomeWealthy));
        Assert.Equal("temple", DaggerfallSiteKinds.MapCategory(DaggerfallSiteKind.ReligionCult));
        Assert.Equal("town", DaggerfallSiteKinds.MapCategory(DaggerfallSiteKind.Tavern));
        Assert.Equal("town", DaggerfallSiteKinds.MapCategory(DaggerfallSiteKind.TownVillage));
    }
}
