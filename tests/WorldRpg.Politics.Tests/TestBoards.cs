namespace WorldRpg.Politics.Tests;

/// <summary>Small boards built in code: two crowns, each with a capital, and a march between them, and a guild.</summary>
internal static class TestBoards
{
    public static BoardSetup TwoCrowns(
        string? marchAllegiance = "west",
        int marchControl = 2,
        int marchOrder = 3,
        IReadOnlyDictionary<string, int>? marchInfluence = null,
        int relation = 0) => new(
        7,
        [
            new SeatSetup("west", "West", SeatKind.Crown, "west-holder", "west-capital", 4, 4, 4),
            new SeatSetup("east", "East", SeatKind.Crown, "east-holder", "east-capital", 4, 4, 4),
        ],
        [
            Region("west-capital", true, "west", 5, 4, 4, new Dictionary<string, int> { ["west"] = 2 }, ["march"]),
            Region("march", false, marchAllegiance, marchControl, 2, marchOrder, marchInfluence ?? new Dictionary<string, int>(), ["west-capital", "east-capital"]),
            Region("east-capital", true, "east", 5, 4, 4, new Dictionary<string, int> { ["east"] = 2 }, ["march"]),
        ],
        relation == 0 ? [] : [new RelationSetup("west", "east", relation)]);

    /// <summary>The setup with a guild seated: a power with no holder, no home and the given grants.</summary>
    public static BoardSetup WithGuild(this BoardSetup setup, IReadOnlyList<SeatGrant>? grants = null) => setup with
    {
        Seats = [.. setup.Seats, new SeatSetup("guild", "Guild", SeatKind.Power, null, null, 4, 4, 4, grants)],
    };

    public static RegionSetup Region(string id, bool capital, string? allegiance, int control, int prosperity, int order,
        IReadOnlyDictionary<string, int> influence, IReadOnlyList<string> borders) =>
        new(id, id, 0, capital, allegiance, control, prosperity, order, influence, borders);

    public static Card Card(string id) => StarterCards.All.Single(card => card.Id == id);
}
