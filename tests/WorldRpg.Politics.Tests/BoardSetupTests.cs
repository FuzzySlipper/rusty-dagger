using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class BoardSetupTests
{
    [Fact]
    public void The_canon_start_seats_every_great_power_on_every_political_region_of_the_bay()
    {
        BoardSetup canon = BoardSetup.Canon();

        Assert.Empty(canon.Problems());
        Assert.Equal(["daggerfall", "wayrest", "sentinel", "orsinium"], canon.Seats.Where(seat => seat.Kind == SeatKind.Crown).Select(seat => seat.Id));
        Assert.Equal(
            ["empire", "necromancers", "underking", "mages-guild", "fighters-guild", "thieves-guild", "dark-brotherhood", "temples", "balfiera"],
            canon.Seats.Where(seat => seat.Kind == SeatKind.Power).Select(seat => seat.Id));
        // The regions are the 44 with a court in the catalog, each once.
        Assert.Equal(44, canon.Regions.Select(region => region.CatalogRegion).Distinct().Count());
        Assert.Equal(44, canon.Regions.Count);
        Assert.Equal("daggerfall", canon.Regions.Single(region => region.Id == "betony").Allegiance);
        Assert.Equal(["betony", "daggerfall", "orsinium", "sentinel", "wayrest"],
            canon.Regions.Where(region => region.Allegiance is not null).Select(region => region.Id).Order(StringComparer.Ordinal));
        Assert.All(canon.Regions.Where(region => region.Allegiance is null), region => Assert.Equal(3, region.Control));
        // Every power starts with a foothold to act from.
        Assert.All(canon.Seats.Where(seat => seat.Kind == SeatKind.Power),
            seat => Assert.Contains(canon.Regions, region => region.Influence.GetValueOrDefault(seat.Id) > 0));
    }

    [Fact]
    public void Crowns_hold_a_home_capital_and_powers_hold_no_territory()
    {
        BoardSetup setup = TestBoards.TwoCrowns(marchAllegiance: "guild").WithGuild([new SeatGrant(Resource.Coin, 0, 0)]) with { };
        setup = setup with
        {
            Seats =
            [
                setup.Seats[0] with { Home = null },
                setup.Seats[1],
                setup.Seats[2] with { Home = "march" },
            ],
        };

        IReadOnlyList<string> problems = setup.Problems();

        Assert.Contains("crown 'west' has no home capital", problems);
        Assert.Contains("seat 'guild' is a power and holds no capital, but names home 'march'", problems);
        Assert.Contains("region 'march' owes allegiance to 'guild', which is not a crown", problems);
        Assert.Contains("seat 'guild' grant of 0 Coin is outside 1..12", problems);
        Assert.Contains("seat 'guild' grant of Coin every 0 weeks is not a schedule", problems);
        Assert.Empty(TestBoards.TwoCrowns().WithGuild().Problems());
    }

    [Fact]
    public void A_setup_names_every_problem_it_has()
    {
        BoardSetup setup = TestBoards.TwoCrowns() with
        {
            Regions =
            [
                TestBoards.Region("west-capital", true, "west", 6, 4, 4, new Dictionary<string, int> { ["north"] = 1 }, ["march"]),
                TestBoards.Region("march", false, "nobody", 2, 2, 3, new Dictionary<string, int> { ["west"] = 4 }, ["west-capital", "east-capital"]),
                TestBoards.Region("east-capital", true, null, 5, 4, 4, new Dictionary<string, int>(), []),
                TestBoards.Region("march", false, null, 2, 2, 3, new Dictionary<string, int>(), []),
            ],
            Relations = [new RelationSetup("west", "west", 0), new RelationSetup("east", "west", 4)],
        };

        IReadOnlyList<string> problems = setup.Problems();

        Assert.Contains("region 'march' is listed twice", problems);
        Assert.Contains("region 'west-capital' control 6 is outside 0..5", problems);
        Assert.Contains("region 'west-capital' holds influence of unknown seat 'north'", problems);
        Assert.Contains("region 'march' owes allegiance to unknown seat 'nobody'", problems);
        Assert.Contains("region 'march' influence of 'west' 4 is outside 0..3", problems);
        Assert.Contains("capital 'east-capital' owes allegiance to no seat", problems);
        Assert.Contains("region 'march' borders 'east-capital', which does not list it back", problems);
        Assert.Contains("seat 'east' home 'east-capital' is not a capital it holds", problems);
        Assert.Contains("relation west/west relates a seat to itself", problems);
        Assert.Contains("relation east/west 4 is outside -3..3", problems);
    }

    [Fact]
    public void A_board_file_with_a_missing_or_unknown_field_is_refused_rather_than_defaulted()
    {
        string directory = Directory.CreateTempSubdirectory("politics-").FullName;
        try
        {
            string missing = Path.Combine(directory, "missing.json");
            File.WriteAllText(missing, """{ "seed": 1, "seats": [], "regions": [] }""");
            string unknown = Path.Combine(directory, "unknown.json");
            File.WriteAllText(unknown, """{ "seed": 1, "seats": [], "regions": [], "relations": [], "rivers": [] }""");

            Assert.Contains("relations", Assert.Throws<BoardSetupException>(() => BoardSetup.Load(missing)).Message, StringComparison.Ordinal);
            Assert.Contains("rivers", Assert.Throws<BoardSetupException>(() => BoardSetup.Load(unknown)).Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
