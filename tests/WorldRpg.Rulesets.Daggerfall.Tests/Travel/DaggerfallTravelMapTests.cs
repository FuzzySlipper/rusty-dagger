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

    private static bool Shows(DaggerfallTravelMapImage image, DaggerfallTravelMapPixel pixel) =>
        pixel.X >= image.Left && pixel.X < image.Right && pixel.Y >= image.Top && pixel.Y < image.Bottom;

    [Fact]
    public void Every_image_the_classic_map_draws_is_published_map_art_of_the_size_it_lays_out()
    {
        string root = TestData.RepositoryRoot;
        using JsonDocument inventory = JsonDocument.Parse(TestContentFiles.ReadCopy(Path.Combine(root, "content", DaggerfallUiArt.InventoryPath)));
        Dictionary<string, string> paths = inventory.RootElement.GetProperty("artifacts").EnumerateArray()
            .Where(artifact => artifact.TryGetProperty("mediaId", out JsonElement id) && id.GetString()!.StartsWith("map.", StringComparison.Ordinal))
            .ToDictionary(artifact => artifact.GetProperty("mediaId").GetString()!, artifact => artifact.GetProperty("path").GetString()!);
        DaggerfallTravelMapBase classic = new DaggerfallTravelMap(All.Value).Read([], [], null, null, null, DaggerfallWorldNames.None).Base;
        Assert.Null(classic.Atlas);
        DaggerfallTravelMapImage[] images = [classic.ClassicWorld, .. classic.ClassicSheets.Select(sheet => sheet.Image)];
        Assert.Equal([.. DaggerfallTravelMap.MediaIds.Order(StringComparer.Ordinal)], [.. images.Select(image => image.MediaId).Order(StringComparer.Ordinal)]);
        Assert.Equal(DaggerfallTravelMap.MediaIds.Count, DaggerfallTravelMap.MediaIds.Distinct(StringComparer.Ordinal).Count());
        foreach (DaggerfallTravelMapImage image in images)
        {
            Assert.True(paths.TryGetValue(image.MediaId, out string? path), $"{image.MediaId} is not published map art");
            byte[] header = TestContentFiles.ReadCopy(Path.Combine(root, "content", path!)).AsSpan(0, 24).ToArray();
            Assert.Equal((image.Width, image.Height), (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20))));
        }
        // The world image draws the whole world map inside its canvas.
        Assert.True(classic.ClassicWorld.Left <= 0 && classic.ClassicWorld.Top <= 0
            && classic.ClassicWorld.Right >= DaggerfallTravelMapPixel.Width && classic.ClassicWorld.Bottom >= DaggerfallTravelMapPixel.Height);
    }

    [Fact]
    public void Every_location_of_a_region_with_classic_art_falls_on_one_of_its_sheets()
    {
        IReadOnlyList<DaggerfallTravelMapSheet> sheets = new DaggerfallTravelMap(All.Value).Read([], [], null, null, null, DaggerfallWorldNames.None).Base.ClassicSheets;
        foreach (IGrouping<int, DaggerfallTravelDestination> region in All.Value.GroupBy(destination => destination.Id.Region))
        {
            DaggerfallTravelMapSheet[] own = [.. sheets.Where(sheet => sheet.Region == region.Key)];
            if (own.Length == 0) continue;
            Assert.All(region, destination => Assert.Contains(own, sheet => Shows(sheet.Image, destination.MapPixel)));
        }
        // The one region with locations and no classic sheet: the donor's travel window offers none for it either.
        Assert.Equal(new[] { 31 }, All.Value.Select(destination => destination.Id.Region).Distinct().Where(region => sheets.All(sheet => sheet.Region != region)));
    }

    [Fact]
    public void The_map_draws_only_the_places_it_is_given_names_every_region_where_its_places_are_and_counts_them()
    {
        DaggerfallTravelMap map = new(All.Value);
        IReadOnlyList<string> names = TestPayload.Definitions.BuildingNames.RegionNames;
        DaggerfallTravelDestination[] region = [.. All.Value.Where(destination => destination.Id.Region == 17)];
        DaggerfallTravelDestination[] known = [.. region.Where((_, index) => index % 3 == 0)];
        DaggerfallTravelMapPixel player = known[0].MapPixel;
        DaggerfallTravelMapView view = map.Read(known, names, player, 90, null, TestPayload.Definitions.WorldNames);
        Assert.Equal((player, 90d), (view.Player, view.Heading));
        Assert.Equal(known.Select(destination => destination.Id), view.Places.Select(destination => destination.Id));
        Assert.Contains(view.Names.Names, name => name.Name == "Iliac Bay");
        DaggerfallTravelMapRegion entry = Assert.Single(view.Regions, candidate => candidate.Region == 17);
        Assert.Equal(names[17], entry.Name);
        Assert.Equal(known.Length, entry.Discovered);
        Assert.All(view.Regions.Where(candidate => candidate.Region != 17), candidate => Assert.Equal(0, candidate.Discovered));
        // Every region with places is named, the one without classic art too, at the middle of its places.
        Assert.Contains(view.Regions, candidate => candidate.Region == 31);
        Assert.Equal(region.Average(destination => (double)destination.MapPixel.X), entry.X, 6);
    }

    [Fact]
    public void Cities_are_named_across_a_region_towns_and_great_dungeons_close_in_and_the_rest_closest()
    {
        Assert.Equal(1, DaggerfallSiteKinds.MapTier(DaggerfallSiteKind.TownCity));
        Assert.Equal(2, DaggerfallSiteKinds.MapTier(DaggerfallSiteKind.TownHamlet));
        Assert.Equal(2, DaggerfallSiteKinds.MapTier(DaggerfallSiteKind.DungeonLabyrinth));
        Assert.Equal(3, DaggerfallSiteKinds.MapTier(DaggerfallSiteKind.HomeFarms));
        Assert.Equal(3, DaggerfallSiteKinds.MapTier(DaggerfallSiteKind.TownVillage));
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
