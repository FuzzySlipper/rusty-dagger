using System.Text.Json.Nodes;
using WorldRpg.Kit;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The political board running in the world (B2, #11055): its weekly turns, its tokens, its save and the counterplay it hears.</summary>
public sealed class PoliticalBoardTests
{
    private const long Wednesday = 7 * 100 + 3;

    private static DaggerfallPoliticalCanon Canon => TestPayload.Definitions.PoliticalCanon!;

    private static DaggerfallPoliticalBoard NewBoard(DaggerfallWorldConditions conditions, long day = Wednesday, DaggerfallPoliticalBoardSave? saved = null,
        Func<string, DaggerfallSeatLeaderPersona>? seatPersona = null) =>
        new(Canon, TestPayload.Definitions.BoardSeats, TestPayload.Definitions.Factions, seatPersona ?? CanonHolder, conditions,
            TestPayload.Definitions.Locations.Records, null, day, saved);

    /// <summary>The seat held by its canon holder, as the world starts.</summary>
    private static DaggerfallSeatLeaderPersona CanonHolder(string seat) => Holding(seat, Canon.Setup.Seats.Single(held => held.Id == seat).Holder);

    private static DaggerfallSeatLeaderPersona Holding(string seat, string? cast, bool vacant = false) =>
        new(seat, seat, $"office:{seat}", DaggerfallOfficeKind.Ruler, 0, cast is null ? null : DaggerfallPeopleSlots.CastIndividualIdentity(cast),
            cast, null, cast ?? string.Empty, null, vacant, IsOverridden: true);

    private static DaggerfallOfficeChangedNotification Changed(DaggerfallOfficeKind kind, int? officeFaction, DaggerfallSiteId? site = null) =>
        new("office:changed", kind, DaggerfallOfficeChangeKind.VacancyOpened, DaggerfallOfficeChangeCause.Death, null, site, null, null, officeFaction,
            null, "Office", null, null, "the late holder", null, false, null, null, null, null, 10, 14_400, null, "was slain");

    private static int FactionOf(string seat) =>
        TestPayload.Definitions.BoardSeats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? binding) ? binding!.OfficeFactionId : throw new ArgumentException(seat);

    private static IReadOnlyList<DaggerfallConditionCounterplayRecord> None() => [];

    [Fact]
    public void The_base_pack_carries_the_canon_board_with_a_binding_for_every_seat()
    {
        DaggerfallPoliticalCanon canon = Canon;
        Assert.Equal(13, canon.Setup.Seats.Count);
        Assert.All(canon.Setup.Seats, seat => Assert.True(TestPayload.Definitions.BoardSeats.TryGetBySeatId(seat.Id, out _)));
    }

    [Fact]
    public void A_board_naming_a_region_seat_or_holder_the_world_lacks_is_refused_with_each_named()
    {
        JsonObject sections = TestPayload.Sections("politicalBoard");
        JsonObject board = sections["politicalBoard"]!.AsObject();
        board["regions"]!.AsArray().First(region => region!["id"]!.GetValue<string>() == "betony")!["catalogRegion"] = 99;
        JsonNode persona = board["personas"]!["king-gothryd"]!.DeepClone();
        board["personas"]!["king-nobody"] = persona;
        JsonObject extra = board["seats"]!.AsArray().First(seat => seat!["id"]!.GetValue<string>() == "balfiera")!.DeepClone().AsObject();
        extra["id"] = "free-company";
        extra["holder"] = "king-nobody";
        board["seats"]!.AsArray().Add(extra);

        DaggerfallContentException refused = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(sections)));
        Assert.Contains(refused.Diagnostics, problem => problem.Contains("'betony'", StringComparison.Ordinal) && problem.Contains("99", StringComparison.Ordinal));
        Assert.Contains(refused.Diagnostics, problem => problem.Contains("'king-nobody'", StringComparison.Ordinal));
        Assert.Contains(refused.Diagnostics, problem => problem.Contains("'free-company'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_turn_plays_on_each_sunday_reached_in_order_however_far_the_clock_jumps()
    {
        DaggerfallPoliticalBoard board = NewBoard(new DaggerfallWorldConditions());
        Assert.Equal(0, board.Board.Turn);
        Assert.Equal(7 * 100, board.LastTurnDay);

        board.AdvanceToDay(Wednesday + 3, None);
        Assert.Equal(0, board.Board.Turn);
        board.AdvanceToDay(7 * 101, None);
        Assert.Equal(1, board.Board.Turn);
        Assert.Equal(1, board.Latest!.Turn);

        // A three-week rest plays the three Sundays it crossed, one after another.
        board.AdvanceToDay(7 * 104 + 5, None);
        Assert.Equal(4, board.Board.Turn);
        Assert.Equal(7 * 104, board.LastTurnDay);
        Assert.Equal(7 * 105, board.NextTurnDay);
    }

    [Fact]
    public void The_canon_board_plays_the_same_weeks_in_every_playthrough()
    {
        DaggerfallPoliticalBoard first = NewBoard(new DaggerfallWorldConditions());
        DaggerfallPoliticalBoard second = NewBoard(new DaggerfallWorldConditions(), day: Wednesday + 7 * 30);
        first.AdvanceToDay(first.LastTurnDay + 7 * 20, None);
        second.AdvanceToDay(second.LastTurnDay + 7 * 20, None);
        Assert.Equal(first.Board.Save().ToJson(), second.Board.Save().ToJson());
    }

    [Fact]
    public void Every_board_condition_the_world_has_a_token_for_shows_on_its_classic_region()
    {
        DaggerfallWorldConditions conditions = new();
        DaggerfallPoliticalBoard board = NewBoard(conditions);
        board.AdvanceToDay(board.LastTurnDay + 7 * 26, None);
        int day = checked((int)board.LastTurnDay);

        List<(Region Region, RegionCondition Condition)> held = [.. board.Board.Regions.SelectMany(region => region.Conditions.Select(condition => (region, condition)))];
        Assert.NotEmpty(held);
        foreach ((Region region, RegionCondition condition) in held)
        {
            string id = condition.Detail is null ? $"board:{region.Id}:{Slug(condition.Kind)}" : $"board:{region.Id}:{Slug(condition.Kind)}:{condition.Detail}";
            DaggerfallWorldConditionToken token = Assert.Single(conditions.ActiveForRegion(region.CatalogRegion, day), active => active.TokenId == id);
            Assert.Equal(region.CatalogRegion, token.Region);
            Assert.Equal(board.TurnDay(condition.Start), token.StartedDay);
            Assert.InRange(token.Severity, DaggerfallWorldConditionToken.MinSeverity, DaggerfallWorldConditionToken.MaxSeverity);
        }
        // Nothing else of the board's shows: each token stands for a condition held, or a succession under way.
        int shown = board.Board.Regions.Select(region => region.CatalogRegion).Distinct()
            .Sum(region => conditions.ActiveForRegion(region, day).Count(token => token.TokenId.StartsWith(DaggerfallPoliticalBoard.TokenPrefix, StringComparison.Ordinal)));
        int successions = board.Board.Arcs.Count(arc => arc.Kind == ArcKind.Succession && board.Board.Seat(arc.Subjects[0]).Home is not null);
        Assert.Equal(held.Count + successions, shown);
    }

    [Fact]
    public void A_war_front_shows_as_border_tension_between_the_two_seats_factions()
    {
        DaggerfallWorldConditions conditions = new();
        DaggerfallPoliticalBoard board = NewBoard(conditions);
        Region region = board.Board.Region("betony");
        board.Board.AddCondition(region, new RegionCondition(ConditionKind.WarFront, "daggerfall,sentinel", 2, null, 0, 52));
        DaggerfallPoliticalBoardSave saved = board.Capture();
        DaggerfallPoliticalBoard reloaded = NewBoard(conditions, saved: saved);

        DaggerfallWorldConditionToken token = Assert.Single(reloaded.TokensFor(region.CatalogRegion), active => active.TokenId == "board:betony:war-front:daggerfall,sentinel");
        Assert.Equal(DaggerfallWorldConditionKind.BorderTension, token.Kind);
        Assert.Equal(2, token.Severity);
        Assert.True(TestPayload.Definitions.BoardSeats.TryGetBySeatId("daggerfall", out DaggerfallBoardSeatBinding? daggerfall));
        Assert.True(TestPayload.Definitions.BoardSeats.TryGetBySeatId("sentinel", out DaggerfallBoardSeatBinding? sentinel));
        Assert.Equal(daggerfall!.OfficeFactionId, token.SourceFactionId);
        Assert.Equal(sentinel!.OfficeFactionId, token.TargetFactionId);
    }

    [Fact]
    public void Counterplay_against_a_board_token_eases_its_condition_by_what_the_world_showed_at_the_next_sunday()
    {
        DaggerfallWorldConditions conditions = new();
        DaggerfallPoliticalBoard board = NewBoard(conditions);
        Region region = board.Board.Region("betony");
        board.Board.AddCondition(region, new RegionCondition(ConditionKind.MonsterSurge, "orcs", 3, null, 0, 52));
        board = NewBoard(conditions, saved: board.Capture());
        region = board.Board.Region("betony");
        const string id = "board:betony:monster-surge:orcs";

        DaggerfallConditionMutationOutcome eased = conditions.WeakenOrStrip(id, region.CatalogRegion, checked((int)board.LastTurnDay))!;
        Assert.Equal(2, eased.Updated!.Severity);
        DaggerfallConditionCounterplayRecord record = new("cp-1", DaggerfallConditionCounterplayKind.LairCleared, id, DaggerfallWorldConditionKind.MonsterSurge,
            region.CatalogRegion, 3, 2, false, checked((int)board.LastTurnDay), 0, SubjectTag: "orcs");
        // The authored-token record the board did not lay is passed over.
        DaggerfallConditionCounterplayRecord authored = record with { RecordId = "cp-2", TokenId = "cond:monster-surge:r17" };

        board.AdvanceToDay(board.NextTurnDay, () => [record, authored]);

        NudgeTrace nudge = Assert.Single(board.LatestTrace!.Nudges);
        Assert.Equal(new ConditionEased("betony", ConditionKind.MonsterSurge, 1, "orcs"), nudge.Nudge);
        Assert.Equal(NudgeOutcome.Applied, nudge.Outcome);
        Assert.Equal(2, region.Conditions.Single(condition => condition.Kind == ConditionKind.MonsterSurge).Magnitude);
    }

    [Fact]
    public void Wars_declared_and_the_chronicles_arcs_are_news_from_their_region_and_secret_moves_never_are()
    {
        DaggerfallPoliticalBoard board = NewBoard(new DaggerfallWorldConditions());
        List<DaggerfallPoliticalNewsItem> news = [];
        List<ArcRecord> arcs = [];
        int warsDeclared = 0;
        for (int week = 0; week < 52 * 3; week++)
        {
            int before = board.Board.Wars.Count(war => war.Started == board.Board.Turn + 1);
            news.AddRange(board.AdvanceToDay(board.NextTurnDay, None));
            arcs.AddRange(board.LatestTrace!.Chronicle);
            warsDeclared += board.Board.Wars.Count(war => war.Started == board.Board.Turn) - before;
        }
        Assert.NotEmpty(arcs);
        Assert.Equal(news.Count, news.Select(item => item.EventId).Distinct().Count());
        Assert.Equal(warsDeclared, news.Count(item => item.Headline.StartsWith("War Declared", StringComparison.Ordinal)));
        foreach (ArcRecord arc in arcs)
        {
            DaggerfallPoliticalNewsItem item = Assert.Single(news, item => item.EventId == $"politics:{arc.Turn}:arc:{arc.Key}:{arc.Phase}");
            Assert.Equal(board.TurnDay(arc.Turn) * 24 * 60, item.Minute);
            if (arc.Kind == ArcKind.Blockade)
                Assert.Equal(board.Board.Region(arc.Subjects[0]).CatalogRegion, item.Origin.Id.Region);
        }
        HashSet<string> secret = [.. Canon.Cards.Cards.Where(card => card.Visibility == Visibility.Secret).Select(card => card.Name)];
        Assert.DoesNotContain(news, item => secret.Any(name => item.Headline.Contains(name, StringComparison.Ordinal)));
    }

    [Fact]
    public void A_saved_board_plays_on_exactly_as_one_never_saved()
    {
        DaggerfallPoliticalBoard played = NewBoard(new DaggerfallWorldConditions());
        played.AdvanceToDay(played.LastTurnDay + 7 * 10, None);
        DaggerfallPoliticalBoard resumed = NewBoard(new DaggerfallWorldConditions(), saved: played.Capture());
        Assert.Equal(played.LastTurnDay, resumed.LastTurnDay);

        played.AdvanceToDay(played.LastTurnDay + 7 * 10, None);
        resumed.AdvanceToDay(resumed.LastTurnDay + 7 * 10, None);
        Assert.Equal(played.Board.Save().ToJson(), resumed.Board.Save().ToJson());
    }

    [Fact]
    public void A_session_plays_the_board_through_a_rest_saves_it_and_plays_on_after_a_restore()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        Assert.Equal(0, board.Board.Turn);
        long start = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar.Day;
        Assert.InRange(start - board.LastTurnDay, 0, 6);

        session.AdvanceElapsedTime((board.NextTurnDay - start + 14) * 86400);
        Assert.Equal(3, board.Board.Turn);

        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(saved);
        Assert.Equal(board.LastTurnDay, decoded.PoliticalBoard!.LastTurnDay);
        // The board's news spreads as word does.
        Assert.Contains(decoded.WordSpread.Wavefronts, wavefront => wavefront.EventId.StartsWith("politics:", StringComparison.Ordinal));

        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults), saved);
        Assert.Equal(board.Board.Save().ToJson(), restored.PoliticalBoard!.Board.Save().ToJson());

        session.AdvanceElapsedTime(7 * 86400);
        restored.AdvanceElapsedTime(7 * 86400);
        Assert.Equal(4, restored.PoliticalBoard.Board.Turn);
        Assert.Equal(board.Board.Save().ToJson(), restored.PoliticalBoard.Board.Save().ToJson());
    }

    [Fact]
    public void A_slow_move_rides_with_a_courier_whose_seizure_intercepts_it_and_whose_dispatch_is_then_forgotten()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        long day = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar.Day;
        PendingMove? ridden = null;
        while (ridden is null)
        {
            session.AdvanceElapsedTime((board.NextTurnDay - day) * 86400);
            day = board.NextTurnDay - DaggerfallPoliticalBoard.DaysPerWeek;
            ridden = board.Board.Pending.FirstOrDefault(move => move.Played == board.Board.Turn && move.Arrives > board.Board.Turn + 1
                && session.CourierDispatches.TryGet(move.Id, out DaggerfallCourierDispatchSave? staged) && staged!.Status == DaggerfallDispatchStatus.InTransit);
            Assert.True(board.Board.Turn < 26, "no slow move with a courier still on the road in half a year");
        }
        Assert.True(session.CourierDispatches.TryGet(ridden.Id, out DaggerfallCourierDispatchSave? dispatch));
        Assert.Equal(ridden.Seat, dispatch!.SeatId);
        Assert.Equal(ridden.Card.Id, dispatch.ActionKind);

        Assert.True(session.CourierDispatches.InterceptDispatch(ridden.Id, dispatch.DispatchedAtMinute + 1, checked((int)day)).Accepted);
        session.AdvanceElapsedTime(7 * 86400);

        NudgeTrace intercepted = Assert.Single(board.LatestTrace!.Nudges, nudge => nudge.Nudge == new MoveIntercepted(ridden.Id));
        Assert.Equal(NudgeOutcome.Applied, intercepted.Outcome);
        Assert.DoesNotContain(board.Board.Pending, move => move.Id == ridden.Id);
        Assert.False(session.CourierDispatches.TryGet(ridden.Id, out _));
        // Every courier still on the board's books carries a move still under way.
        Assert.All(session.CourierDispatches.All, courier => Assert.Contains(board.Board.Pending, move => move.Id == courier.DispatchId));
    }

    /// <summary>
    /// A courier that reaches its door before its move falls due (couriers are slow, so mostly a move already delayed
    /// for its courier) brings the move in then whether the clock steps over its arrival and the Sunday one at a time or
    /// jumps both at once (a rest, travel or prison), so the board never depends on how long the interval was.
    /// </summary>
    [Fact]
    public void A_courier_arriving_before_its_moves_sunday_brings_it_in_on_time_however_the_clock_jumps()
    {
        using DaggerfallSession stepped = FreshSession();
        using DaggerfallSession jumped = FreshSession();
        DaggerfallPoliticalBoard board = stepped.PoliticalBoard!;
        PendingMove? early = null;
        DaggerfallCourierDispatchSave? dispatch = null;
        while (early is null)
        {
            long week = (board.NextTurnDay - stepped.Calendar.DayNumber) * 86400;
            stepped.AdvanceElapsedTime(week);
            jumped.AdvanceElapsedTime(week);
            early = board.Board.Pending.FirstOrDefault(move => stepped.CourierDispatches.TryGet(move.Id, out dispatch) && dispatch!.Status == DaggerfallDispatchStatus.InTransit
                && dispatch.ExpectedArrivalMinute > stepped.Calendar.MinuteNumber
                && dispatch.ExpectedArrivalMinute < board.TurnDay(move.Arrives) * 1440);
            Assert.True(board.Board.Turn < 52, "no slow move whose courier arrives before its Sunday in a year");
        }
        long due = board.TurnDay(early.Arrives);
        long end = (due + 1) * 1440;

        stepped.AdvanceElapsedTime((dispatch!.ExpectedArrivalMinute + 1 - stepped.Calendar.MinuteNumber) * 60);
        stepped.AdvanceElapsedTime((end - stepped.Calendar.MinuteNumber) * 60);
        jumped.AdvanceElapsedTime((end - jumped.Calendar.MinuteNumber) * 60);

        Assert.Equal(stepped.Calendar.MinuteNumber, jumped.Calendar.MinuteNumber);
        Assert.Equal(board.Board.Save().ToJson(), jumped.PoliticalBoard!.Board.Save().ToJson());
        Assert.DoesNotContain(jumped.PoliticalBoard.Board.Pending, move => move.Id == early.Id);
    }

    /// <summary>
    /// Patch's Q2: a seat holder dying or replaced in the world gives the seat its new holder at the next Sunday and opens
    /// a succession. A successor with no persona, or a vacant seat, plays on with the predecessor's persona as a regency.
    /// </summary>
    [Fact]
    public void A_seat_holder_changed_in_the_world_takes_the_seat_at_the_next_sunday_and_one_without_a_persona_rules_as_a_regency()
    {
        Dictionary<string, DaggerfallSeatLeaderPersona> world = new(StringComparer.Ordinal);
        DaggerfallWorldConditions conditions = new();
        DaggerfallPoliticalBoard board = NewBoard(conditions, seatPersona: seat => world.TryGetValue(seat, out DaggerfallSeatLeaderPersona? held) ? held : CanonHolder(seat));

        // An office change that leaves the seat's holder as it was tells the board nothing.
        board.ObserveOfficeChanged(Changed(DaggerfallOfficeKind.Ruler, FactionOf("daggerfall")));
        Assert.Empty(board.Board.Queued);

        world["daggerfall"] = Holding("daggerfall", "lord-bridwell");
        board.ObserveOfficeChanged(Changed(DaggerfallOfficeKind.Ruler, FactionOf("daggerfall")));
        board.AdvanceToDay(board.NextTurnDay, None);

        Seat daggerfall = board.Board.Seat("daggerfall");
        Assert.Equal(("lord-bridwell", "king-gothryd"), (daggerfall.Holder, daggerfall.PlaysAs));
        Assert.Contains(board.LatestTrace!.Chronicle, arc => arc is { Kind: ArcKind.Succession, Phase: ArcPhase.Peak, Subjects: ["daggerfall"] });
        Assert.Contains(conditions.ActiveForRegion(board.Board.Region("daggerfall").CatalogRegion, checked((int)board.LastTurnDay)),
            token => token.Kind == DaggerfallWorldConditionKind.SuccessionCrisis && token.SubjectTag == "daggerfall");
        // While the world shows its own crisis for the vacant throne, the board's is left out, so it is told once.
        int home = board.Board.Region("daggerfall").CatalogRegion;
        conditions.RaiseSuccessionCrisis(DaggerfallOfficeLedger.FactionOfficeKey(FactionOf("daggerfall")), home, FactionOf("daggerfall"), 10);
        Assert.Single(conditions.ActiveForRegion(home, checked((int)board.LastTurnDay)), token => token.Kind == DaggerfallWorldConditionKind.SuccessionCrisis);
        conditions.ClearSuccessionCrisis(DaggerfallOfficeLedger.FactionOfficeKey(FactionOf("daggerfall")));

        world["daggerfall"] = Holding("daggerfall", cast: null, vacant: true);
        board.ObserveOfficeChanged(Changed(DaggerfallOfficeKind.Ruler, FactionOf("daggerfall")));
        board.AdvanceToDay(board.NextTurnDay, None);
        Assert.Equal(("vacant:daggerfall", "king-gothryd"), (daggerfall.Holder, daggerfall.PlaysAs));
    }

    [Fact]
    public void A_local_office_holder_killed_in_a_regions_chief_town_takes_the_seats_piece_off_that_office()
    {
        DaggerfallPoliticalBoard board = NewBoard(new DaggerfallWorldConditions());
        Region region = board.Board.Region("daggerfall");
        Assert.Equal("daggerfall", region.Offices[Office.Watch]);
        DaggerfallSiteRecord town = board.Town(region.Id)!;

        board.ObserveOfficeChanged(Changed(DaggerfallOfficeKind.WatchCaptain, null,
            TestPayload.Definitions.Locations.Records.First(site => site.Id.Region == town.Id.Region && site.Id != town.Id).Id));
        Assert.Empty(board.Board.Queued);

        board.ObserveOfficeChanged(Changed(DaggerfallOfficeKind.WatchCaptain, null, town.Id));
        board.AdvanceToDay(board.NextTurnDay, None);

        NudgeTrace removed = Assert.Single(board.LatestTrace!.Nudges);
        Assert.Equal(new OfficeHolderRemoved("daggerfall", Office.Watch), removed.Nudge);
        Assert.Contains(new BoardChange("daggerfall", "office:watch", "daggerfall", "local"), removed.Changes);
    }

    /// <summary>Patch's Q3: a finished quest for a seat's faction gives it +1 influence in the quest's region, or +1 favour where the board has none.</summary>
    [Fact]
    public void A_quest_finished_for_a_seats_faction_gives_it_influence_in_the_quests_region_or_else_favour()
    {
        DaggerfallPoliticalBoard board = NewBoard(new DaggerfallWorldConditions());
        int mages = FactionOf("mages-guild");
        Region open = board.Board.Regions.First(region => region.InfluenceOf("mages-guild") == 0);

        board.ObserveQuestSettled(mages, succeeded: false, open.CatalogRegion);
        board.ObserveQuestSettled(0, succeeded: true, open.CatalogRegion);
        Assert.Empty(board.Board.Queued);

        board.ObserveQuestSettled(mages, succeeded: true, open.CatalogRegion);
        AssertQuest(Assert.Single(board.Board.Queued), [open.Id], new CardEffect(CardEffectKind.Influence, 1));

        // A quest given by a faction under the seat's counts for the seat; one in a region off the board gives favour.
        int boardless = Enumerable.Range(0, 62).First(classic => board.Board.Regions.All(held => held.CatalogRegion != classic));
        int underMages = TestPayload.Definitions.Factions.Factions.Values.First(faction => faction.Parent == mages).Id;
        board.ObserveQuestSettled(underMages, succeeded: true, boardless);
        AssertQuest(board.Board.Queued[^1], [], new CardEffect(CardEffectKind.Gain, 1, Resource.Favour));

        board.AdvanceToDay(board.NextTurnDay, None);
        Assert.All(board.LatestTrace!.Nudges, nudge => Assert.Equal(NudgeOutcome.Applied, nudge.Outcome));
        Assert.Contains(new BoardChange(open.Id, "influence:mages-guild", "0", "1"), board.LatestTrace.Nudges[0].Changes);

        static void AssertQuest(WorldNudge nudge, string[] targets, CardEffect effect)
        {
            QuestOutcome quest = Assert.IsType<QuestOutcome>(nudge);
            Assert.Equal("mages-guild", quest.Seat);
            Assert.Equal(targets, quest.Targets);
            Assert.Equal([effect], quest.Effects);
        }
    }

    /// <summary>
    /// The canon table and the world agree on who holds each seat the world names a holder for, so no succession opens by
    /// itself; a king slain in the world reaches the board, through the ledger's office change, as his successor.
    /// </summary>
    [Fact]
    public void The_world_holds_each_seat_as_the_canon_board_does_and_a_slain_king_reaches_the_board_as_his_successor()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        // Where the world names a cast holder (the crowns), it is the board's; the rest the world leaves to the board.
        Dictionary<string, string?> cast = Canon.Setup.Seats.ToDictionary(seat => seat.Id, seat => session.OfficeLedger.ResolveBoardSeatPersona(seat.Id).HolderCastId);
        Assert.All(Canon.Setup.Seats, seat => Assert.True(cast[seat.Id] is null || cast[seat.Id] == seat.Holder, $"{seat.Id}: world {cast[seat.Id]}, board {seat.Holder}"));
        Assert.All(["daggerfall", "wayrest", "sentinel", "orsinium"], crown => Assert.NotNull(cast[crown]));

        long gothryd = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(gothryd, ActorOrigin.Cast("king-gothryd"),
            Location: ActorLocationOverride.Dead(DaggerfallWorldProfileIds.Exterior(board.Town("daggerfall")!.Id))));
        session.OfficeLedger.ReactToDeath(gothryd, currentDay: 11);

        HolderChanged change = Assert.IsType<HolderChanged>(Assert.Single(board.Board.Queued));
        Assert.Equal("daggerfall", change.Seat);
        Assert.NotEqual("king-gothryd", change.Holder);
    }

    private static string Slug(ConditionKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $"-{char.ToLowerInvariant(c)}" : char.ToLowerInvariant(c).ToString()));
}
