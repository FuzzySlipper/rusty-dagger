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

    private static DaggerfallPoliticalBoard NewBoard(DaggerfallWorldConditions conditions, long day = Wednesday, DaggerfallPoliticalBoardSave? saved = null) =>
        new(Canon, TestPayload.Definitions.BoardSeats, conditions, TestPayload.Definitions.Locations.Records, day, saved);

    private static IReadOnlyList<DaggerfallConditionCounterplayRecord> None() => [];

    [Fact]
    public void The_base_pack_carries_the_canon_board_with_a_binding_for_every_seat()
    {
        DaggerfallPoliticalCanon canon = Canon;
        Assert.Equal(13, canon.Setup.Seats.Count);
        Assert.All(canon.Setup.Seats, seat => Assert.True(TestPayload.Definitions.BoardSeats.TryGetBySeatId(seat.Id, out _)));
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

    private static string Slug(ConditionKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $"-{char.ToLowerInvariant(c)}" : char.ToLowerInvariant(c).ToString()));
}
