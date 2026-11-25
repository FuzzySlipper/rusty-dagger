using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class BoardSetupTests
{
    [Fact]
    public void The_canon_start_loads_with_its_four_crowns_and_their_capitals()
    {
        BoardSetup canon = BoardSetup.Canon();

        Assert.Empty(canon.Problems());
        Assert.Equal(["daggerfall", "wayrest", "sentinel", "orsinium"], canon.Seats.Select(seat => seat.Id));
        Assert.All(canon.Seats, seat => Assert.Equal(seat.Id, canon.Regions.Single(region => region.Id == seat.Home).Allegiance));
        Assert.Equal("daggerfall", canon.Regions.Single(region => region.Id == "betony").Allegiance);
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
