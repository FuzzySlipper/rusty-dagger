using System.Text.Json;
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
        // The canon cards deal every seat a deck.
        Assert.Empty(CardSet.Canon().Problems(canon));
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
                TestBoards.Region("march", false, null, 2, 2, 3, new Dictionary<string, int>(), [])
                    with { Offices = new Dictionary<Office, string> { [Office.Watch] = "nobody" } },
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
        Assert.Contains("region 'march' Watch is held by unknown seat 'nobody'", problems);
    }

    [Fact]
    public void Every_canon_seat_is_held_by_a_cast_individual_with_a_persona_and_a_canon_agenda()
    {
        BoardSetup canon = BoardSetup.Canon();
        HashSet<string> cast = CastIndividuals();

        Assert.All(canon.Seats, seat =>
        {
            Assert.Contains(seat.Holder, cast);
            Assert.True(canon.Personas.ContainsKey(seat.Holder), $"holder '{seat.Holder}' has no persona");
            Assert.InRange(seat.Agenda?.Count ?? 0, 2, BoardSetup.AgendaLimit);
        });
        Assert.Equal("lady-brisienna", canon.Seats.Single(seat => seat.Id == "empire").Holder);
        Assert.Equal("the-underking", canon.Seats.Single(seat => seat.Id == "underking").Holder);
        // The canon examples the design names: Sentinel takes Betony, Daggerfall holds it, the Underking counters the Necromancers.
        Assert.Contains(new Goal(GoalKind.Take, "betony"), canon.Seats.Single(seat => seat.Id == "sentinel").Agenda!);
        Assert.Contains(new Goal(GoalKind.Hold, "betony"), canon.Seats.Single(seat => seat.Id == "daggerfall").Agenda!);
        Assert.Contains(new Goal(GoalKind.Counter, "necromancers"), canon.Seats.Single(seat => seat.Id == "underking").Agenda!);
    }

    [Fact]
    public void A_seat_needs_a_holder_with_a_persona_and_goals_it_can_pursue()
    {
        BoardSetup setup = TestBoards.TwoCrowns().WithGuild();
        setup = setup with
        {
            Seats =
            [
                setup.Seats[0] with { Holder = "stranger" },
                setup.Seats[1] with { Holder = "stranger", Agenda = [new(GoalKind.Take, "nowhere"), new(GoalKind.Office, "Mayor"), new(GoalKind.Counter, "east"), new(GoalKind.Recognition, "west")] },
                setup.Seats[2] with { Holder = null!, Agenda = [new(GoalKind.Hold, "march"), new(GoalKind.Retaliate, "west")] },
            ],
            Personas = new Dictionary<string, Persona>(setup.Personas)
            {
                ["west-holder"] = new(new Dictionary<GoalKind, int> { [GoalKind.Take] = 4 }, new Dictionary<CardFamily, int> { [CardFamily.Force] = -3 }, 5),
            },
        };

        IReadOnlyList<string> problems = setup.Problems();

        Assert.Contains("seat 'west' holder 'stranger' has no persona", problems);
        Assert.Contains("holder 'stranger' holds 'west' and 'east'", problems);
        Assert.Contains("seat 'guild' has no holder", problems);
        Assert.Contains("persona 'west-holder' weighs Take 4, outside 0..3", problems);
        Assert.Contains("persona 'west-holder' leans -3 on Force, outside -2..2", problems);
        Assert.Contains("persona 'west-holder' risk 5 is outside 0..3", problems);
        Assert.Contains("seat 'east' has 4 standing goals, more than 3", problems);
        Assert.Contains("seat 'east' goal take nowhere: names an unknown region", problems);
        Assert.Contains("seat 'east' goal office mayor: names an unknown office", problems);
        Assert.Contains("seat 'east' goal counter east: names no other seat", problems);
        Assert.DoesNotContain(problems, problem => problem.Contains("recognition west", StringComparison.Ordinal));
        Assert.Contains("seat 'guild' goal hold march: only a crown holds land", problems);
        Assert.Contains("seat 'guild' goal retaliate west: is never authored: grudges open it", problems);
    }

    /// <summary>The ids of the cast individuals (single-person cast spawners in daggerfall.base), from the repository's content.</summary>
    private static HashSet<string> CastIndividuals()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, CastFile))) root = root.Parent;
        Assert.NotNull(root);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, CastFile)));
        return [.. document.RootElement.GetProperty("specs").EnumerateArray().Select(individual => individual.GetProperty("id").GetString()!)];
    }

    private const string CastFile = "content/worldrpg/packs/daggerfall.base/actors/specs/cast.json";

    [Fact]
    public void A_seat_cannot_start_with_more_pieces_than_its_supply()
    {
        Dictionary<Office, string> all = Enum.GetValues<Office>().ToDictionary(office => office, _ => "west");
        BoardSetup crowded = TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["west"] = 3 }, marchOffices: all);
        crowded = crowded with { Regions = [crowded.Regions[0] with { Offices = all }, crowded.Regions[1], crowded.Regions[2]] };

        // Two pieces and four offices in the capital, three pieces and four offices in the march.
        Assert.Contains("seat 'west' starts with 13 pieces, more than its supply of 10", crowded.Problems());
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
            File.WriteAllText(unknown, """{ "seed": 1, "seats": [], "regions": [], "relations": [], "personas": {}, "rivers": [] }""");

            Assert.Contains("relations", Assert.Throws<BoardSetupException>(() => BoardSetup.Load(missing)).Message, StringComparison.Ordinal);
            Assert.Contains("rivers", Assert.Throws<BoardSetupException>(() => BoardSetup.Load(unknown)).Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
