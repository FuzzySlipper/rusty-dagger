using System.Text.Json;
using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class SaveTests
{
    /// <summary>
    /// The canon game saved after a year, with a nudge queued for the next Sunday, and resumed from its JSON, plays the
    /// same second year as a game never stopped.
    /// </summary>
    [Fact]
    public void A_game_saved_and_resumed_mid_way_plays_on_exactly_as_one_never_stopped()
    {
        WorldNudge nudge = new QuestOutcome("wayrest", ["menevia"], [new CardEffect(CardEffectKind.Influence, 1)]);
        Board unbroken = Board.Create(BoardSetup.Canon(), CardSet.Canon());
        Board first = Board.Create(BoardSetup.Canon(), CardSet.Canon());
        int year = TurnTrace.TurnsIn(1);
        for (int turn = 0; turn < year; turn++)
        {
            BoardTurn.Play(unbroken);
            BoardTurn.Play(first);
        }
        unbroken.Queue(nudge);
        first.Queue(nudge);

        Board resumed = Board.Load(BoardSetup.Canon(), CardSet.Canon(), BoardSave.FromJson(first.Save().ToJson(), "test save"));

        Assert.Equal(Year(unbroken, year), Year(resumed, year));
        Assert.Equal(unbroken.Save().ToJson(), resumed.Save().ToJson());
    }

    [Fact]
    public void A_save_missing_a_field_or_not_fitting_its_table_is_refused_naming_what_is_wrong()
    {
        BoardSave save = Board.Create(BoardSetup.Canon(), CardSet.Canon()).Save();
        string json = save.ToJson();

        BoardSetupException missing = Assert.Throws<BoardSetupException>(() => BoardSave.FromJson(json.Replace("\"turn\":0,", "", StringComparison.Ordinal), "cut save"));
        Assert.Contains("turn", missing.Problems[0], StringComparison.Ordinal);

        BoardSave strange = save with { Seats = [save.Seats[0] with { Hand = ["no-such-card"] }, .. save.Seats.Skip(1)] };
        BoardSetupException unknown = Assert.Throws<BoardSetupException>(() => Board.Load(BoardSetup.Canon(), CardSet.Canon(), strange));
        Assert.Contains("seat 'daggerfall' holds unknown card 'no-such-card'", unknown.Problems);

        Assert.Throws<BoardSetupException>(() => Board.Load(TestBoards.TwoCrowns(), TestBoards.Cards, save));
    }

    [Fact]
    public void A_save_naming_seats_or_regions_its_table_lacks_is_refused_naming_each()
    {
        BoardSave save = Board.Create(BoardSetup.Canon(), CardSet.Canon()).Save();
        RegionSave region = save.Regions.First(held => held.Allegiance is null);
        BoardSave strange = save with
        {
            Regions = [.. save.Regions.Select(held => held == region
                ? held with { Allegiance = "no-such-seat", Influence = new Dictionary<string, int>(held.Influence) { ["ghosts"] = 1 } }
                : held)],
            Feuds = [new Feud("feud-1", "daggerfall", "nobody", 1, 0)],
            Queued = [new LairCleared("atlantis")],
        };

        BoardSetupException refused = Assert.Throws<BoardSetupException>(() => Board.Load(BoardSetup.Canon(), CardSet.Canon(), strange));

        Assert.Contains($"region '{region.Id}' owes allegiance to 'no-such-seat', which is no crown at the table", refused.Problems);
        Assert.Contains($"region '{region.Id}' influence names unknown seat 'ghosts'", refused.Problems);
        Assert.Contains("feud 'feud-1' names unknown seat 'nobody'", refused.Problems);

        BoardSave queued = save with { Queued = [new LairCleared("atlantis")] };
        Assert.Contains(Assert.Throws<BoardSetupException>(() => Board.Load(BoardSetup.Canon(), CardSet.Canon(), queued)).Problems,
            problem => problem.Contains("unknown region 'atlantis'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_slow_move_saved_with_targets_its_card_cannot_take_is_refused_and_one_that_merely_no_longer_fits_loads()
    {
        BoardSetup setup = TestBoards.TwoCrowns();
        Board board = TestBoards.Create(setup);
        board.Schedule(new PendingMove("0-east", "east", TestBoards.Card("march-on"), ["march"], "east-capital", 0, 2));
        board.Schedule(new PendingMove("0-west", "west", TestBoards.Card("march-on"), ["west-capital"], "west-capital", 0, 2));
        BoardSave save = board.Save();
        BoardSave atlantis = save with { Pending = [save.Pending[0] with { Targets = ["atlantis"] }] };
        BoardSave crowded = save with { Pending = [save.Pending[0] with { Targets = ["march", "east"] }] };

        Assert.Contains("move '0-east' targets 'atlantis', which is no region of the table",
            Assert.Throws<BoardSetupException>(() => Board.Load(setup, TestBoards.CardsFor(setup), atlantis)).Problems);
        Assert.Contains("move '0-east' has 2 targets where ContestedRegion takes 1",
            Assert.Throws<BoardSetupException>(() => Board.Load(setup, TestBoards.CardsFor(setup), crowded)).Problems);

        Board resumed = Board.Load(setup, TestBoards.CardsFor(setup), save);
        BoardTurn.Play(resumed);
        Assert.Contains(BoardTurn.Play(resumed).Arrivals, arrival => arrival is { Id: "0-west", Outcome: MoveOutcome.Failed });
    }

    [Fact]
    public void A_slow_move_saved_under_way_is_published_with_its_origin_when_it_arrives()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        board.Schedule(new PendingMove("0-east", "east", TestBoards.Card("march-on"), ["march"], "east-capital", 0, 2));
        Board resumed = Board.Load(TestBoards.TwoCrowns(), TestBoards.CardsFor(TestBoards.TwoCrowns()), BoardSave.FromJson(board.Save().ToJson(), "test save"));

        BoardTurn.Play(resumed);
        TurnTrace arrived = BoardTurn.Play(resumed);

        MoveRecord record = BoardOutputs.Of(resumed, TestBoards.Cards, arrived).Moves.Single(move => move.MoveId == "0-east");
        Assert.Equal(("east-capital", 0), (record.OriginRegion, record.Turn));
    }

    private static IReadOnlyList<string> Year(Board board, int turns)
    {
        List<string> lines = [];
        for (int turn = 0; turn < turns; turn++)
        {
            using StringWriter writer = new();
            BoardTraceWriter.WriteJsonLine(writer, BoardTurn.Play(board));
            lines.Add(writer.ToString());
        }
        return lines;
    }
}

public sealed class NudgeTests
{
    [Fact]
    public void A_quest_outcome_applies_its_effects_for_the_seat_at_the_next_Sunday()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        board.Queue(new QuestOutcome("east", ["march"], [new CardEffect(CardEffectKind.Influence, 1), new CardEffect(CardEffectKind.Order, 1)]));
        Assert.Equal(0, board.Region("march").InfluenceOf("east"));

        NudgeTrace applied = Assert.Single(BoardTurn.Play(board).Nudges);

        Assert.Equal(NudgeOutcome.Applied, applied.Outcome);
        Assert.Contains(new BoardChange("march", "influence:east", "0", "1"), applied.Changes);
        Assert.Contains(new BoardChange("march", "order", "3", "4"), applied.Changes);
    }

    [Fact]
    public void A_holder_change_seats_the_new_holder_and_opens_a_succession_the_chronicle_names()
    {
        BoardSetup setup = TestBoards.TwoCrowns() is var two
            ? two with { Personas = new Dictionary<string, Persona>(two.Personas) { ["west-heir"] = new(new Dictionary<GoalKind, int>()) } }
            : two;
        Board board = TestBoards.Create(setup);
        board.Queue(new HolderChanged("west", "west-heir"));

        TurnTrace turn = BoardTurn.Play(board);

        Assert.Equal("west-heir", board.Seat("west").Holder);
        Assert.Contains(turn.Chronicle, arc => arc is { Kind: ArcKind.Succession, Phase: ArcPhase.Peak, Subjects: ["west"] });
        List<TurnTrace> after = [];
        for (int week = 0; week < Nudges.SuccessionWeeks; week++) after.Add(BoardTurn.Play(board));

        Assert.Contains(after[^1].Chronicle, arc => arc is { Kind: ArcKind.Succession, Phase: ArcPhase.Settled });
        Assert.DoesNotContain(after[..^1].SelectMany(week => week.Chronicle), arc => arc.Kind == ArcKind.Succession);
    }

    [Fact]
    public void A_removed_office_holder_leaves_the_office_to_local_people()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchOffices: new Dictionary<Office, string> { [Office.Watch] = "east" }));
        board.Queue(new OfficeHolderRemoved("march", Office.Watch));

        NudgeTrace applied = Assert.Single(BoardTurn.Play(board).Nudges);

        Assert.Equal([new BoardChange("march", "office:watch", "east", "local")], applied.Changes);
    }

    [Fact]
    public void A_slow_move_is_delivered_early_delayed_or_intercepted_and_a_nudge_for_one_already_arrived_is_stale()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: "west"));
        Card march = TestBoards.Card("march-on");
        board.Schedule(new PendingMove("0-east", "east", march, ["march"], "east-capital", 0, 5));
        board.Schedule(new PendingMove("0-west", "west", march, ["march"], "west-capital", 0, 5));
        board.Schedule(new PendingMove("1-east", "east", march, ["march"], "east-capital", 0, 5));

        board.Queue(new MoveDelivered("0-east"));
        board.Queue(new MoveDelayed("0-west", 3));
        board.Queue(new MoveIntercepted("1-east"));
        board.Queue(new MoveIntercepted("9-nobody"));
        TurnTrace turn = BoardTurn.Play(board);

        Assert.Contains(turn.Arrivals, arrival => arrival.Id == "0-east");
        Assert.Equal("east-capital", BoardOutputs.Of(board, TestBoards.Cards, turn).Moves.Single(move => move.MoveId == "0-east").OriginRegion);
        Assert.Equal(8, board.Pending.Single(move => move.Id == "0-west").Arrives);
        Assert.DoesNotContain(board.Pending, move => move.Id == "1-east");
        Assert.Equal([NudgeOutcome.Applied, NudgeOutcome.Applied, NudgeOutcome.Applied, NudgeOutcome.Stale], turn.Nudges.Select(nudge => nudge.Outcome));
        Assert.Contains(BoardOutputs.Of(board, TestBoards.Cards, turn).Moves,
            move => move is { MoveId: "1-east", Outcome: MoveStatus.Intercepted, Card: "march-on", Turn: 0 });
    }

    [Fact]
    public void A_cleared_lair_lifts_its_monster_surge_and_eased_conditions_weaken_then_lift()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Region march = board.Region("march");
        board.AddCondition(march, new RegionCondition(ConditionKind.MonsterSurge, "orcs", 2, null, 0, 50));
        board.AddCondition(march, new RegionCondition(ConditionKind.MonsterSurge, "undead", 2, null, 0, 50));
        board.AddCondition(march, new RegionCondition(ConditionKind.Plague, null, 3, null, 0, 50));
        board.Queue(new LairCleared("march", "orcs"));
        board.Queue(new ConditionEased("march", ConditionKind.Plague, 2));

        BoardTurn.Play(board);

        Assert.DoesNotContain(march.Conditions, held => held.Detail == "orcs");
        Assert.Contains(march.Conditions, held => held.Detail == "undead");
        Assert.Equal(1, march.Conditions.Single(held => held.Kind == ConditionKind.Plague).Magnitude);

        board.Queue(new ConditionEased("march", ConditionKind.Plague, 1));
        BoardTurn.Play(board);

        Assert.DoesNotContain(march.Conditions, held => held.Kind == ConditionKind.Plague);
    }

    [Fact]
    public void The_main_quests_boon_is_recorded_on_its_seat_and_its_effects_applied()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        board.Queue(new MainQuestBoon("east", "numidium", [], [new CardEffect(CardEffectKind.Gain, Board.ResourceLimit, Resource.Force)]));

        TurnTrace turn = BoardTurn.Play(board);

        Assert.Equal(["numidium"], board.Seat("east").Boons);
        Assert.Contains(new BoardChange("east", "boon", "none", "numidium"), turn.Nudges[0].Changes);
        Assert.Equal(["numidium"], BoardOutputs.Of(board, TestBoards.Cards, turn).Seats.Single(seat => seat.Seat == "east").Boons);
    }

    [Fact]
    public void A_nudge_that_cannot_apply_to_the_board_is_refused_when_queued()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());

        Assert.Contains("unknown seat 'north'", Assert.Throws<ArgumentException>(() => board.Queue(new HolderChanged("north", "west-holder"))).Message);
        Assert.Contains("no persona", Assert.Throws<ArgumentException>(() => board.Queue(new HolderChanged("west", "stranger"))).Message);
        Assert.Contains("unknown region", Assert.Throws<ArgumentException>(() => board.Queue(new LairCleared("atlantis"))).Message);
        Assert.Contains("needs a region target",
            Assert.Throws<ArgumentException>(() => board.Queue(new QuestOutcome("west", [], [new CardEffect(CardEffectKind.Control, 1)]))).Message);
        Assert.Empty(board.Queued);
    }
}

public sealed class OutputTests
{
    /// <summary>
    /// The outputs' fields, as the world reads them: a change here is a change to the contract with the world, made on
    /// purpose with its readers (B2 and A6).
    /// </summary>
    [Fact]
    public void The_weeks_outputs_keep_a_stable_schema_with_every_field_written()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchAllegiance: null));
        board.AddCondition(board.Region("march"), new RegionCondition(ConditionKind.Raiders, null, 1, null, 0, 9));
        JsonElement outputs = JsonDocument.Parse(Json(BoardOutputs.Of(board, TestBoards.Cards, BoardTurn.Play(board)))).RootElement;

        Assert.Equal(["turn", "year", "day", "conditions", "regions", "seats", "moves", "chronicle"], Names(outputs));
        Assert.Equal(["region", "catalogRegion", "kind", "detail", "magnitude", "source", "start", "end"], Names(outputs.GetProperty("conditions")[0]));
        Assert.Equal(["region", "catalogRegion", "allegiance", "control", "prosperity", "order", "supply", "offices"], Names(outputs.GetProperty("regions")[1]));
        Assert.Equal(["seat", "holder", "relations", "wars", "feuds", "truces", "leader", "reservesCut", "boons"], Names(outputs.GetProperty("seats")[0]));
        Assert.Equal(["moveId", "turn", "seat", "card", "targets", "originRegion", "visibility", "outcome", "headlineKey", "pendingUntil", "officeKey"],
            Names(outputs.GetProperty("moves")[0]));
    }

    [Fact]
    public void A_challenge_for_a_local_office_names_the_office_piece_and_every_published_move_its_headline()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().Rich("east"));
        board.Hold("east", "cell-and-watch", "cell-and-watch", "cell-and-watch");

        TurnTrace turn = BoardTurn.Play(board);
        BoardOutputs outputs = BoardOutputs.Of(board, TestBoards.Cards, turn);

        MoveRecord challenge = outputs.Moves.Single(move => move.Seat == "east");
        Assert.Equal(("cell-and-watch", "office:march:watch", "politics.move.cell-and-watch", Visibility.Secret),
            (challenge.Card, challenge.OfficeKey, challenge.HeadlineKey, challenge.Visibility));
        Assert.Equal(turn.Moves.Count(move => move.Card != Gathering.Id), outputs.Moves.Count);
    }

    [Fact]
    public void The_chronicle_names_a_war_when_its_first_region_falls_and_when_it_ends()
    {
        Board board = TestBoards.Create(TestBoards.Frontier(relation: -3).Rich("west").Rich("east"));
        board.Turn = 1;
        Wars.Check(board);
        War war = Assert.Single(board.Wars);
        Assert.Empty(Chronicle.Check(board));

        TestBoards.Card("siege").Play(board, board.Seat("west"), ["east-march"]);

        Assert.Equal([(ArcKind.War, war.Id, ArcPhase.Peak, "west,east", 1, 1)], Chronicle.Check(board).Select(Describe));

        board.AddWeariness(war, "east", Wars.PeaceWeariness);
        board.Turn = 6;
        TestBoards.Card("sue-for-peace").Play(board, board.Seat("east"), [war.Id]);

        ArcRecord settled = Assert.Single(Chronicle.Check(board));
        Assert.Equal((ArcKind.War, ArcPhase.Settled, 6, "politics.arc.war.settled"), (settled.Kind, settled.Phase, settled.Turn, settled.HeadlineKey));
    }

    [Fact]
    public void The_chronicle_names_a_blockade_after_four_weeks_cut_off_and_when_the_region_is_fed()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns());
        Region march = board.Region("march");
        for (int week = 1; week <= Chronicle.BlockadeWeeks; week++)
        {
            board.Turn = week;
            board.SetSupply(march, RegionSupply.Cut);
            board.CountCut(march);
            IReadOnlyList<ArcRecord> named = Chronicle.Check(board);
            if (week < Chronicle.BlockadeWeeks) Assert.Empty(named);
            else Assert.Equal([(ArcKind.Blockade, "march:1", ArcPhase.Peak, "march", 1, week)], named.Select(Describe));
        }

        board.Turn++;
        board.SetSupply(march, RegionSupply.Fed);
        board.CountCut(march);

        Assert.Equal([(ArcKind.Blockade, "march:1", ArcPhase.Settled, "march", 1, board.Turn)], Chronicle.Check(board).Select(Describe));
    }

    private static (ArcKind, string, ArcPhase, string, int, int) Describe(ArcRecord arc) =>
        (arc.Kind, arc.Key, arc.Phase, string.Join(",", arc.Subjects), arc.Started, arc.Turn);

    private static string Json(BoardOutputs outputs)
    {
        using StringWriter writer = new();
        BoardTraceWriter.WriteJsonLine(writer, outputs);
        return writer.ToString();
    }

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];
}
