namespace WorldRpg.Politics.Tests;

/// <summary>
/// Small boards built in code: two crowns, each with a capital, and a march between them, and a guild; and a small
/// card set whose numbers stay put while the canon cards are tuned.
/// </summary>
internal static class TestBoards
{
    public static readonly CardSet Cards = new(
    [
        new("garrison", "Garrison", CardFamily.Force, 0, 0, 2, CardTarget.OwnRegion, Visibility.Public, [new(CardEffectKind.Control, 1)]),
        new("keep-the-peace", "Keep the Peace", CardFamily.Court, 1, 0, 0, CardTarget.OwnRegion, Visibility.Public, [new(CardEffectKind.Order, 1)]),
        new("trade-charter", "Trade Charter", CardFamily.Trade, 2, 0, 0, CardTarget.OwnRegion, Visibility.Public, [new(CardEffectKind.Prosperity, 1)]),
        new("levy", "Levy", CardFamily.Force, 0, 0, 0, CardTarget.OwnRegion, Visibility.Public,
            [new(CardEffectKind.Gain, 2, Resource.Coin), new(CardEffectKind.Gain, 1, Resource.Force), new(CardEffectKind.Order, -1)]),
        new("install-factor", "Install a Factor", CardFamily.Court, 0, 2, 0, CardTarget.ReachableRegion, Visibility.Public, [new(CardEffectKind.Influence, 1)]),
        new("discredit", "Discredit", CardFamily.Court, 0, 2, 0, CardTarget.ContestedRegion, Visibility.Public,
            [new(CardEffectKind.HolderInfluence, -1), new(CardEffectKind.Order, -1), new(CardEffectKind.Relation, -1)]),
        new("press-claim", "Press a Claim", CardFamily.Force, 0, 1, 1, CardTarget.ContestedRegion, Visibility.Public,
            [new(CardEffectKind.Control, -1), new(CardEffectKind.Relation, -1)]),
        new("annex", "Annex", CardFamily.Force, 0, 0, 4, CardTarget.ContestedRegion, Visibility.Public, [new(CardEffectKind.Annex)]),
        new("envoy", "Envoy", CardFamily.Court, 0, 1, 0, CardTarget.OtherSeat, Visibility.Public, [new(CardEffectKind.Relation, 1)]),
        new("march-on", "March On", CardFamily.Force, 0, 0, 3, CardTarget.ContestedRegion, Visibility.Public, [new(CardEffectKind.Control, -2)], Weeks: 2),
        new("reassign", "Reassign", CardFamily.Court, 0, 1, 0, CardTarget.RegionPair, Visibility.Public, [new(CardEffectKind.MoveInfluence, 1)]),
        new("open-route", "Open a Route", CardFamily.Trade, 3, 0, 0, CardTarget.RegionPair, Visibility.Public, [new(CardEffectKind.Prosperity, 1)]),
        new("undercut", "Undercut", CardFamily.Trade, 1, 0, 0, CardTarget.RegionRival, Visibility.Public,
            [new(CardEffectKind.RivalInfluence, -1), new(CardEffectKind.Relation, -1)]),
        new("plant-cell", "Plant a Cell", CardFamily.Arcane, 0, 0, 0, CardTarget.ReachableRegion, Visibility.Secret, [new(CardEffectKind.Influence, 2)]),
        new("cell-and-watch", "A Cell and the Watch", CardFamily.Arcane, 0, 0, 0, CardTarget.ReachableRegion, Visibility.Secret,
            [new(CardEffectKind.Influence, 1), new(CardEffectKind.ChallengeOffice, 3, Office: Office.Watch)]),
        new("plague-them", "Plague Them", CardFamily.Arcane, 0, 0, 0, CardTarget.ReachableRegion, Visibility.Secret,
            [new(CardEffectKind.AddCondition, 3, Condition: ConditionKind.Plague, Weeks: 4)]),
        new("heal", "Heal", CardFamily.Arcane, 0, 0, 0, CardTarget.ReachableRegion, Visibility.Public, [new(CardEffectKind.RemoveCondition, Condition: ConditionKind.Plague)]),
        new("whisper", "Whisper", CardFamily.Arcane, 0, 0, 0, CardTarget.OtherSeat, Visibility.Secret,
            [new(CardEffectKind.AddCourtCondition, 3, Court: CourtConditionKind.ParanoidCourt, Weeks: 8)]),
        new("hostages", "Hostages", CardFamily.Arcane, 0, 0, 0, CardTarget.OtherSeat, Visibility.Public,
            [new(CardEffectKind.AddCourtCondition, 1, Court: CourtConditionKind.HostageTreaty, Weeks: 8)]),
        new("suborn", "Suborn the Watch", CardFamily.Shadow, 2, 0, 0, CardTarget.ReachableRegion, Visibility.Secret,
            [new(CardEffectKind.ChallengeOffice, 1, Office: Office.Watch)]),
        new("extort", "Extort", CardFamily.Shadow, 1, 0, 0, CardTarget.OtherSeat, Visibility.Secret, [new(CardEffectKind.Take, 3, Resource.Coin)], Weeks: 1),
        new("barter", "Barter", CardFamily.Faith, 1, 0, 0, CardTarget.Deal, Visibility.Public,
            [new(CardEffectKind.Deal, Gives: ["grain", "coin:1/2", "force:1/4"], Asks: ["grain", "coin:1/2", "force:1/4"])]),
        new("border-raid", "Border Raid", CardFamily.War, 0, 0, 2, CardTarget.EnemyFront, Visibility.Public,
            [new(CardEffectKind.Control, -1), new(CardEffectKind.AddCondition, 1, Condition: ConditionKind.WarFront, Weeks: 8)]),
        new("siege", "Siege", CardFamily.War, 0, 0, 4, CardTarget.EnemyFront, Visibility.Public,
            [new(CardEffectKind.Control, -2), new(CardEffectKind.AddCondition, 3, Condition: ConditionKind.WarFront, Weeks: 8), new(CardEffectKind.Conquer)], Weeks: 2),
        new("muster", "Muster", CardFamily.War, 2, 0, 0, CardTarget.OwnFront, Visibility.Public, [new(CardEffectKind.Control, 1)]),
        new("sue-for-peace", "Sue for Peace", CardFamily.War, 0, 2, 0, CardTarget.War, Visibility.Public, [new(CardEffectKind.Peace)]),
        new("border-scuffle", "Border Scuffle", CardFamily.War, 0, 0, 2, CardTarget.FeudRival, Visibility.Public,
            [new(CardEffectKind.Order, -1), new(CardEffectKind.AddCondition, 1, Condition: ConditionKind.Skirmish, Weeks: 4)]),
        new("cattle-raid", "Cattle Raid", CardFamily.War, 0, 0, 2, CardTarget.FeudRival, Visibility.Public, [new(CardEffectKind.Take, 2, Resource.Coin)]),
        new("harass-factors", "Harass Their Factors", CardFamily.War, 0, 1, 1, CardTarget.FeudRival, Visibility.Public, [new(CardEffectKind.RivalInfluence, -1)]),
        new("guild-secret", "Guild Secret", CardFamily.Shadow, 0, 0, 8, CardTarget.None, Visibility.Secret, [new(CardEffectKind.Gain, 4, Resource.Favour)], Seat: "guild"),
    ]);

    /// <summary>A board from <paramref name="setup"/> with the test cards, less the signatures of seats not at its table.</summary>
    public static Board Create(BoardSetup setup, ulong? seed = null) => Board.Create(setup, CardsFor(setup), seed);

    /// <summary>The test cards, less the signatures of seats not at <paramref name="setup"/>'s table.</summary>
    public static CardSet CardsFor(BoardSetup setup) =>
        new([.. Cards.Cards.Where(card => card.Seat is null || setup.Seats.Any(seat => seat.Id == card.Seat))]);

    /// <summary>The setup with <paramref name="seat"/> at the top of every resource, so no cost weighs on its choices.</summary>
    public static BoardSetup Rich(this BoardSetup setup, string seat) => setup with
    {
        Seats = [.. setup.Seats.Select(other => other.Id == seat
            ? other with { Coin = Board.ResourceLimit, Favour = Board.ResourceLimit, Force = Board.ResourceLimit }
            : other)],
    };

    public static BoardSetup TwoCrowns(
        string? marchAllegiance = "west",
        int marchControl = 2,
        int marchOrder = 3,
        IReadOnlyDictionary<string, int>? marchInfluence = null,
        int relation = 0,
        IReadOnlyDictionary<Office, string>? marchOffices = null) => new(
        7,
        [
            new SeatSetup("west", "West", SeatKind.Crown, "west-holder", "west-capital", 4, 4, 4, [CardFamily.Trade, CardFamily.Court, CardFamily.Force]),
            new SeatSetup("east", "East", SeatKind.Crown, "east-holder", "east-capital", 4, 4, 4, [CardFamily.Trade, CardFamily.Court, CardFamily.Force]),
        ],
        [
            Region("west-capital", true, "west", 5, 4, 4, new Dictionary<string, int> { ["west"] = 2 }, ["march"]),
            Region("march", false, marchAllegiance, marchControl, 2, marchOrder, marchInfluence ?? new Dictionary<string, int>(), ["west-capital", "east-capital"])
                with { Offices = marchOffices },
            Region("east-capital", true, "east", 5, 4, 4, new Dictionary<string, int> { ["east"] = 2 }, ["march"]),
        ],
        relation == 0 ? [] : [new RelationSetup("west", "east", relation)],
        Personas);

    /// <summary>
    /// Two crowns and the land between them, in a line: West's capital, West's march, independent hill country too poor
    /// to feed itself, East's granary, which has grain to spare, and East's capital.
    /// </summary>
    public static BoardSetup Granary(int relation = 0) => new(
        7,
        [
            new SeatSetup("west", "West", SeatKind.Crown, "west-holder", "west-capital", 4, 4, 4, [CardFamily.Trade, CardFamily.Court, CardFamily.Force]),
            new SeatSetup("east", "East", SeatKind.Crown, "east-holder", "east-capital", 4, 4, 4, [CardFamily.Trade, CardFamily.Court, CardFamily.Force]),
        ],
        [
            Region("west-capital", true, "west", 5, 4, 4, new Dictionary<string, int> { ["west"] = 2 }, ["march"]),
            Region("march", false, "west", 3, 2, 3, new Dictionary<string, int>(), ["west-capital", "hills"]),
            Region("hills", false, null, 3, 1, 3, new Dictionary<string, int>(), ["march", "granary"]),
            Region("granary", false, "east", 3, 4, 3, new Dictionary<string, int>(), ["hills", "east-capital"]),
            Region("east-capital", true, "east", 5, 4, 4, new Dictionary<string, int> { ["east"] = 2 }, ["granary"]),
        ],
        relation == 0 ? [] : [new RelationSetup("west", "east", relation)],
        Personas);

    /// <summary>
    /// Two crowns whose marches border each other, in a line: West's capital, West's march, East's march and East's
    /// capital. The marches are the front of any war between them.
    /// </summary>
    public static BoardSetup Frontier(int relation) => new(
        7,
        [
            new SeatSetup("west", "West", SeatKind.Crown, "west-holder", "west-capital", 4, 4, 4, [CardFamily.Trade, CardFamily.Court, CardFamily.Force]),
            new SeatSetup("east", "East", SeatKind.Crown, "east-holder", "east-capital", 4, 4, 4, [CardFamily.Trade, CardFamily.Court, CardFamily.Force]),
        ],
        [
            Region("west-capital", true, "west", 5, 4, 4, new Dictionary<string, int> { ["west"] = 2 }, ["west-march"]),
            Region("west-march", false, "west", 3, 3, 3, new Dictionary<string, int> { ["west"] = 1 }, ["west-capital", "east-march"]),
            Region("east-march", false, "east", 2, 3, 3, new Dictionary<string, int> { ["east"] = 1, ["west"] = 1 }, ["west-march", "east-capital"]),
            Region("east-capital", true, "east", 5, 4, 4, new Dictionary<string, int> { ["east"] = 2 }, ["east-march"]),
        ],
        [new RelationSetup("west", "east", relation)],
        Personas);

    /// <summary>The setup with a guild seated: a power with no home, drawing shadow cards, with the given grants.</summary>
    public static BoardSetup WithGuild(this BoardSetup setup, IReadOnlyList<SeatGrant>? grants = null) => setup with
    {
        Seats = [.. setup.Seats, new SeatSetup("guild", "Guild", SeatKind.Power, "guild-holder", null, 4, 4, 4, [CardFamily.Shadow], grants)],
    };

    /// <summary>
    /// A persona for each test holder that cares for no goal and leans on no family, and is heedless of reprisal, so a
    /// test seat weighs prestige alone unless a test gives it a persona and an agenda.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Persona> Personas = new[] { "west-holder", "east-holder", "guild-holder" }
        .ToDictionary(holder => holder, _ => new Persona(new Dictionary<GoalKind, int>(), Risk: Persona.RiskLimit));

    /// <summary>The setup with <paramref name="seat"/> given <paramref name="agenda"/>, and its holder <paramref name="persona"/>.</summary>
    public static BoardSetup Intent(this BoardSetup setup, string seat, Persona persona, params Goal[] agenda)
    {
        SeatSetup chosen = setup.Seats.Single(other => other.Id == seat);
        return setup with
        {
            Seats = [.. setup.Seats.Select(other => other == chosen ? other with { Agenda = agenda } : other)],
            Personas = new Dictionary<string, Persona>(setup.Personas) { [chosen.Holder] = persona },
        };
    }

    public static RegionSetup Region(string id, bool capital, string? allegiance, int control, int prosperity, int order,
        IReadOnlyDictionary<string, int> influence, IReadOnlyList<string> borders) =>
        new(id, id, 0, capital, allegiance, control, prosperity, order, influence, borders);

    public static Card Card(string id) => Cards.ById(id);

    /// <summary>Replaces <paramref name="seat"/>'s hand with the named cards, as if it had drawn them.</summary>
    public static void Hold(this Board board, string seat, params string[] cards)
    {
        SeatDeck deck = board.Deck(board.Seat(seat));
        deck.hand.Clear();
        deck.hand.AddRange(cards.Select(Card));
    }
}
