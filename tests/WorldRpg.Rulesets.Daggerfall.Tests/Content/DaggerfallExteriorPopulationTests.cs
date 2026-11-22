using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The donor's bounded pool of wandering townsfolk an assembled exterior places on its clear ground: the people of the
/// location's own region, never another region's.
/// </summary>
public sealed class DaggerfallExteriorPopulationTests
{
    /// <summary>Every automap cell of every block of <paramref name="site"/>, as clear ground.</summary>
    private static (int X, int Z)[] AllClear(DaggerfallSiteRecord site) =>
        [.. site.Exterior!.Blocks.SelectMany(block => Enumerable.Range(0, 64 * 64).Select(cell => (block.X * 64 + cell % 64, block.Y * 64 + cell / 64)))];

    private static DaggerfallSiteRecord Town(int region, string name) =>
        TestPayload.Definitions.Locations.Records.Single(site => site.Id.Region == region && site.Name == name && site.Exterior is not null);

    [Theory]
    [InlineData(17, "Daggerfall")]
    [InlineData(20, "Sentinel")]
    [InlineData(16, "Wrothgaria")]
    public void A_town_places_the_people_of_its_own_region(int region, string name)
    {
        DaggerfallSiteRecord town = Town(region, name);
        DaggerfallFactionDefinition people = Assert.Single(TestPayload.Definitions.Factions.Factions.Values,
            faction => faction.Type == 15 && faction.Region == region && faction.Flats.Count != 0);

        IReadOnlyList<DaggerfallPopulationPlacement> placed = DaggerfallExteriorPopulation.Create(TestPayload.Definitions, town, AllClear(town));

        Assert.NotEmpty(placed);
        Assert.All(placed, placement =>
        {
            Assert.Equal(people.Id, placement.FactionId);
            Assert.Contains((placement.BillboardArchive << 7) | placement.BillboardRecord, people.Flats);
        });
    }

    [Fact]
    public void A_region_without_its_own_people_has_no_wandering_townsfolk_rather_than_another_regions()
    {
        // Region 31 (the player's ships) has no people faction of its own.
        Assert.DoesNotContain(TestPayload.Definitions.Factions.Factions.Values, faction => faction.Type == 15 && faction.Region == 31);
        DaggerfallSiteRecord site = TestPayload.Definitions.Locations.Records.First(site => site.Id.Region == 31 && site.Exterior is { Blocks.Count: > 0 });

        Assert.Empty(DaggerfallExteriorPopulation.Create(TestPayload.Definitions, site, AllClear(site)));
    }
}
