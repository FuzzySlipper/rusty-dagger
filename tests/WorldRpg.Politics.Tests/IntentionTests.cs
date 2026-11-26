using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class IntentionTests
{
    private static readonly Persona Conqueror = new(new Dictionary<GoalKind, int> { [GoalKind.Take] = 3 });

    [Fact]
    public void A_seat_pursuing_a_plan_keeps_its_target_across_turns()
    {
        BoardSetup setup = TestBoards.TwoCrowns(marchAllegiance: null, marchControl: 3, marchInfluence: new Dictionary<string, int> { ["west"] = 1 })
            .Intent("west", Conqueror, new Goal(GoalKind.Take, "march"));
        Board board = TestBoards.Create(setup);

        MoveTrace[] west = [.. Enumerable.Range(0, 4).Select(_ => BoardTurn.Play(board).Moves.Single(move => move.Seat == "west"))];

        Assert.All(west, move => Assert.Equal("take march", move.Plan));
        Assert.Equal(1, board.Seat("west").Plan!.Since);
        Assert.Contains(new BoardChange("west", "plan", "none", "take march"), west[0].Changes);
        // Taking the march means building influence there and breaking its control, and the plan shows in the moves.
        Region march = board.Region("march");
        Assert.True(march.InfluenceOf("west") > 1 || march.Control < 3, $"west made no progress on the march: {march}");
    }

    [Fact]
    public void An_unfinished_plan_that_is_still_possible_is_kept_however_long_it_takes()
    {
        BoardSetup setup = TestBoards.Granary()
            .Intent("west", Conqueror, new Goal(GoalKind.Take, "hills"), new Goal(GoalKind.Take, "granary"));
        Board board = TestBoards.Create(setup);
        Seat west = board.Seat("west");
        Intention.Review(board, west);
        Assert.Equal("take hills", west.Plan!.ToString());

        // A year on, with both targets still open and nothing threatening West, it is still taking the hills.
        board.Turn += 52;
        board.TakeChanges();
        Intention.Review(board, west);

        Assert.Equal("take hills", west.Plan!.ToString());
        Assert.Equal(0, west.Plan.Since);
        Assert.Empty(board.TakeChanges());
    }

    [Fact]
    public void A_plan_that_is_done_ends_and_the_next_goal_is_taken_up()
    {
        BoardSetup setup = TestBoards.TwoCrowns(marchAllegiance: null, marchControl: 3)
            .Intent("west", new Persona(new Dictionary<GoalKind, int> { [GoalKind.Footing] = 2, [GoalKind.Recognition] = 1 }),
                new Goal(GoalKind.Recognition, "east"), new Goal(GoalKind.Footing, "march"));
        Board board = TestBoards.Create(setup);
        Seat west = board.Seat("west");

        Intention.Review(board, west);
        Assert.Equal("footing march", west.Plan!.ToString());
        board.AdjustInfluence(board.Region("march"), west, Intention.FootingPieces);
        board.TakeChanges();
        Intention.Review(board, west);

        Assert.Equal("recognition east", west.Plan!.ToString());
        Assert.Equal(
            [new BoardChange("west", "plan", "footing march", "done"), new BoardChange("west", "plan", "none", "recognition east")],
            board.TakeChanges());
    }

    [Fact]
    public void A_threat_pre_empts_a_plan_that_matters_no_more_to_the_holder()
    {
        BoardSetup setup = TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["west"] = 1 })
            .Intent("west", new Persona(new Dictionary<GoalKind, int> { [GoalKind.Recognition] = 2, [GoalKind.Hold] = 2 }),
                new Goal(GoalKind.Recognition, "east"), new Goal(GoalKind.Hold, "march"));
        Board board = TestBoards.Create(setup);
        Seat west = board.Seat("west");
        Intention.Review(board, west);
        Assert.Equal("recognition east", west.Plan!.ToString());

        // East closes on the march: as much influence there as West.
        board.AdjustInfluence(board.Region("march"), board.Seat("east"), 1);
        Assert.True(Intention.Threatened(board, west, board.Region("march")));
        Intention.Review(board, west);

        Assert.Equal("hold march", west.Plan!.ToString());
        Assert.Contains(new BoardChange("west", "plan", "recognition east", "pre-empted by hold march"), board.TakeChanges());
    }

    [Fact]
    public void Retaliation_follows_an_attack_and_settles_the_grudge()
    {
        BoardSetup setup = TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["west"] = 1, ["east"] = 1 })
            .Rich("west").Rich("east")
            .Intent("west", new Persona(new Dictionary<GoalKind, int> { [GoalKind.Retaliate] = 3 }, Risk: Persona.RiskLimit));
        Board board = TestBoards.Create(setup);
        Seat west = board.Seat("west");
        Seat east = board.Seat("east");

        // East presses a claim on West's march, in the open.
        Strike(board, east, "press-claim", "march");

        Assert.Equal([new Grudge("east", Memory.GrudgeWeeks)], west.Grudges);
        // The claim cooled the two by 1 itself, so being struck cools them no further.
        Assert.Equal(-1, board.Relation("west", "east"));

        // West takes up striking back as its plan, and its best move is a blow at East.
        board.Hold("west", "undercut", "keep-the-peace");
        Intention.Review(board, west);
        MoveOption answer = MoveChooser.Options(board, west)[0];

        Assert.Equal("retaliate east", west.Plan!.ToString());
        Assert.Equal("undercut", answer.Card);
        Assert.Equal("east", answer.Targets[1]);

        // Striking back is payback: West's grudge is settled, and East opens none against it.
        board.TakeChanges();
        Strike(board, west, answer.Card, [.. answer.Targets]);
        Assert.Empty(west.Grudges);
        Assert.Empty(east.Grudges);
        Assert.Contains(new BoardChange("west", "grudge:east", $"until {Memory.GrudgeWeeks}", "settled"), board.TakeChanges());
        Intention.Review(board, west);
        Assert.Contains(new BoardChange("west", "plan", "retaliate east", "done"), board.TakeChanges());
    }

    [Fact]
    public void Being_struck_cools_the_relation_once_and_a_secret_blow_is_not_remembered()
    {
        BoardSetup setup = TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["west"] = 1, ["east"] = 1 }).Rich("east").WithGuild();
        Board board = TestBoards.Create(setup);
        Seat west = board.Seat("west");

        // A public blow that does not touch the relation itself cools it by 1.
        Strike(board, board.Seat("east"), "march-on", "march");
        Assert.Equal(-1, board.Relation("west", "east"));
        Assert.Single(west.Grudges);

        // A secret one leaves no grudge and no chill, since West does not know who struck.
        board.AdjustInfluence(board.Region("march"), board.Seat("guild"), 1);
        Strike(board, board.Seat("guild"), "plague-them", "march");
        Assert.Equal(0, board.Relation("west", "guild"));
        Assert.DoesNotContain(west.Grudges, grudge => grudge.Against == "guild");
    }

    [Fact]
    public void Repeated_help_warms_a_relation()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: "east", marchInfluence: new Dictionary<string, int> { ["west"] = 1 }).Rich("west"));
        Seat west = board.Seat("west");
        board.AddCondition(board.Region("march"), new RegionCondition(ConditionKind.Plague, null, 1, null, 0, 20));

        Strike(board, west, "heal", "march");
        Assert.Equal(0, board.Relation("west", "east"));
        board.AddCondition(board.Region("march"), new RegionCondition(ConditionKind.Plague, null, 1, null, 0, 20));
        Strike(board, west, "heal", "march");

        Assert.Equal(1, board.Relation("west", "east"));
    }

    [Fact]
    public void A_persona_swap_changes_choices_and_keeps_the_seat()
    {
        BoardSetup setup = TestBoards.TwoCrowns(marchControl: 2).Rich("west");
        setup = setup with
        {
            Personas = new Dictionary<string, Persona>(setup.Personas)
            {
                ["west-holder"] = new(new Dictionary<GoalKind, int>(), new Dictionary<CardFamily, int> { [CardFamily.Force] = 1 }, Persona.RiskLimit),
                ["west-heir"] = new(new Dictionary<GoalKind, int>(), new Dictionary<CardFamily, int> { [CardFamily.Trade] = 1 }, Persona.RiskLimit),
            },
        };
        Board board = TestBoards.Create(setup);
        Seat west = board.Seat("west");
        board.Hold("west", "garrison", "trade-charter");

        string soldier = MoveChooser.Options(board, west)[0].Card;
        board.SetHolder(west, "west-heir");
        string merchant = MoveChooser.Options(board, west)[0].Card;

        Assert.Equal("garrison", soldier);
        Assert.Equal("trade-charter", merchant);
        Assert.Equal("west-heir", west.Holder);
        Assert.Equal(["garrison", "trade-charter"], board.Deck(west).Hand.Select(card => card.Id));
        Assert.Equal(Board.ResourceLimit, west.Coin);
        Assert.Contains(new BoardChange("west", "holder", "west-holder", "west-heir"), board.TakeChanges());
        Assert.Throws<ArgumentException>(() => board.SetHolder(west, "nobody"));
    }

    [Fact]
    public void A_shadow_seat_gathers_towards_its_signature_card_then_plays_it()
    {
        Card ambush = new("ambush", "Ambush", CardFamily.Shadow, 0, 0, 3, CardTarget.RegionRival, Visibility.Public,
            [new(CardEffectKind.RivalInfluence, -1)], Seat: "guild");
        BoardSetup setup = TestBoards.TwoCrowns(marchInfluence: new Dictionary<string, int> { ["guild"] = 1 }).WithGuild();
        setup = setup with { Seats = [.. setup.Seats.Select(seat => seat with { Coin = 0, Favour = 0, Force = 0 })] };
        setup = setup.Intent("guild", new Persona(new Dictionary<GoalKind, int> { [GoalKind.Counter] = 3 }, Risk: Persona.RiskLimit),
            new Goal(GoalKind.Counter, "east"));
        Board board = Board.Create(setup, new CardSet([.. TestBoards.Cards.Cards, ambush]));
        SeatDeck deck = board.Deck(board.Seat("guild"));
        deck.hand.Clear();
        deck.hand.Add(ambush);

        List<MoveTrace> guild = [];
        while (guild.Count < 6 && guild.LastOrDefault()?.Card != "ambush")
            guild.Add(BoardTurn.Play(board).Moves.Single(move => move.Seat == "guild"));

        // It gathers for the weeks the ambush is out of reach, then strikes.
        Assert.Equal("ambush", guild[^1].Card);
        Assert.True(guild.Count >= 4, $"the guild struck in week {guild.Count}, before it could pay for the ambush");
        Assert.All(guild[..^1], move => Assert.Equal(Gathering.Id, move.Card));
        Assert.All(guild, move => Assert.Equal("counter east", move.Plan));
        // Gathering Force counts towards the ambush it saves for, so it scores above the resource alone.
        Assert.Contains(guild[..^1], move => move.Score > MoveChooser.SelfWeight);
        Assert.Equal("east", guild[^1].Targets[1]);
    }

    /// <summary>Plays <paramref name="card"/> for <paramref name="seat"/> on <paramref name="targets"/> as a weekly move does, remembered.</summary>
    private static void Strike(Board board, Seat seat, string card, params string[] targets)
    {
        Card played = TestBoards.Card(card);
        Prestige.Witness witness = Prestige.Witness.OfCard(board, seat, new Prestige.Before(board), played, targets);
        Assert.True(played.Fits(board, seat, targets));
        Assert.True(played.Play(board, seat, targets));
        Memory.Remember(board, witness);
    }
}
