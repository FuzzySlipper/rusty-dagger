using Xunit;

namespace WorldRpg.Politics.Tests;

/// <summary>A region under a liege that cannot hold it breaks free (Den #11146, Patch's Q5 on #11129).</summary>
public sealed class RevoltTests
{
    /// <summary>West's march at order 1 and control 1, with West holding no influence there.</summary>
    private static Board Strained() =>
        TestBoards.Create(TestBoards.TwoCrowns(marchControl: 1, marchOrder: 1));

    private static RegionCondition Unrest(Board board) => new(ConditionKind.Unrest, null, 1, null, board.Turn, board.Turn + 100);

    /// <summary>Plays the revolt check for <paramref name="weeks"/> weeks without the rest of the turn.</summary>
    private static void Weeks(Board board, int weeks)
    {
        for (int week = 0; week < weeks; week++)
        {
            board.Turn++;
            Revolts.Check(board);
        }
    }

    [Fact]
    public void A_held_region_under_unrest_that_its_liege_cannot_hold_breaks_free_after_its_weeks_of_strain()
    {
        Board board = Strained();
        Region march = board.Region("march");
        board.AddCondition(march, Unrest(board));

        Weeks(board, RevoltTuning.Default.StrainWeeks - 1);
        Assert.Equal("west", march.Allegiance);
        Assert.Equal(RevoltTuning.Default.StrainWeeks - 1, march.StrainWeeks);

        board.TakeChanges();
        Weeks(board, 1);
        Assert.Null(march.Allegiance);
        Assert.Equal(0, march.StrainWeeks);
        Assert.Equal(board.Turn + RevoltTuning.Default.FreedWeeks, march.ContestableFrom);
        Assert.Contains(new BoardChange("march", "allegiance", "west", "independent"), board.TakeChanges());

        // The chronicle names the revolt the week it breaks free and settles once its own lords hold it.
        ArcRecord peak = Assert.Single(Chronicle.Check(board));
        Assert.Equal((ArcKind.Revolt, ArcPhase.Peak), (peak.Kind, peak.Phase));
        Assert.Equal(["march", "west"], peak.Subjects);
        Assert.Equal("politics.arc.revolt.peak", peak.HeadlineKey);
        board.Turn += RevoltTuning.Default.FreedWeeks;
        Assert.Equal((ArcKind.Revolt, ArcPhase.Settled), Assert.Single(Chronicle.Check(board)) is { } settled ? (settled.Kind, settled.Phase) : default);
    }

    [Fact]
    public void Low_tracks_alone_a_liege_with_friends_there_a_consolidating_hold_or_a_break_in_the_strain_keep_the_region()
    {
        Board quiet = Strained();
        Weeks(quiet, RevoltTuning.Default.StrainWeeks * 2);
        Assert.Equal("west", quiet.Region("march").Allegiance);

        Board befriended = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 1, marchOrder: 1, marchInfluence: new Dictionary<string, int> { ["west"] = 1 }));
        befriended.AddCondition(befriended.Region("march"), Unrest(befriended));
        Weeks(befriended, RevoltTuning.Default.StrainWeeks * 2);
        Assert.Equal("west", befriended.Region("march").Allegiance);

        Board consolidating = Strained();
        consolidating.Consolidate(consolidating.Region("march"), consolidating.Turn + RevoltTuning.Default.StrainWeeks * 2);
        consolidating.AddCondition(consolidating.Region("march"), Unrest(consolidating));
        Weeks(consolidating, RevoltTuning.Default.StrainWeeks * 2);
        Assert.Equal("west", consolidating.Region("march").Allegiance);

        // A week of order restored resets the count.
        Board eased = Strained();
        Region march = eased.Region("march");
        eased.AddCondition(march, Unrest(eased));
        Weeks(eased, RevoltTuning.Default.StrainWeeks - 1);
        eased.AdjustOrder(march, 1);
        Weeks(eased, 1);
        Assert.Equal(0, march.StrainWeeks);
        eased.AdjustOrder(march, -1);
        Weeks(eased, RevoltTuning.Default.StrainWeeks - 1);
        Assert.Equal("west", march.Allegiance);
    }

    /// <summary>A region that breaks free clears out its former liege: its influence and the offices it held there (R11146-1).</summary>
    [Fact]
    public void A_freed_region_turns_its_former_lieges_office_holders_out()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 1, marchOrder: 1,
            marchOffices: new Dictionary<Office, string> { [Office.Watch] = "west", [Office.Factor] = "west" }));
        Region march = board.Region("march");
        board.AddCondition(march, Unrest(board));
        int pieces = board.PiecesLeft(board.Seat("west"));

        Weeks(board, RevoltTuning.Default.StrainWeeks - 1);
        board.TakeChanges();
        Weeks(board, 1);

        Assert.Null(march.Allegiance);
        Assert.Empty(march.Offices);
        Assert.False(board.Present(board.Seat("west"), march));
        Assert.Equal(pieces + 2, board.PiecesLeft(board.Seat("west")));
        IReadOnlyList<BoardChange> changes = board.TakeChanges();
        Assert.Contains(new BoardChange("march", "office:watch", "west", "local"), changes);
        Assert.Contains(new BoardChange("march", "office:factor", "west", "local"), changes);
    }

    /// <summary>A board file tunes the rule (R11146-2): a shorter strain frees a region sooner, a save resumes under it, and a tuning that can't be played is refused.</summary>
    [Fact]
    public void A_board_files_revolt_tuning_sets_the_rule_and_a_resumed_save_plays_by_it()
    {
        BoardSetup quick = TestBoards.TwoCrowns(marchControl: 1, marchOrder: 1) with { Revolts = new RevoltTuning(StrainWeeks: 4, FreedWeeks: 8) };
        Board board = TestBoards.Create(quick);
        board.AddCondition(board.Region("march"), Unrest(board));
        Weeks(board, 2);

        Board resumed = Board.Load(quick, TestBoards.CardsFor(quick), BoardSave.FromJson(board.Save().ToJson(), "quick save"));
        Assert.Equal(quick.Revolts, resumed.Revolts);
        Weeks(resumed, 1);
        Assert.Equal("west", resumed.Region("march").Allegiance);
        Weeks(resumed, 1);
        Assert.Null(resumed.Region("march").Allegiance);
        Assert.Equal(resumed.Turn + 8, resumed.Region("march").ContestableFrom);

        // Only order 0 strains under a stricter file, so the march at order 1 holds.
        Board strict = TestBoards.Create(TestBoards.TwoCrowns(marchControl: 1, marchOrder: 1) with { Revolts = new RevoltTuning(StrainOrder: 0) });
        strict.AddCondition(strict.Region("march"), Unrest(strict));
        Weeks(strict, RevoltTuning.Default.StrainWeeks * 2);
        Assert.Equal("west", strict.Region("march").Allegiance);

        BoardSetupException refused = Assert.Throws<BoardSetupException>(() => TestBoards.Create(quick with { Revolts = new RevoltTuning(StrainWeeks: 0, StrainOrder: 9) }));
        Assert.Contains("revolts need 0 weeks of strain, fewer than 1", refused.Problems);
        Assert.Contains("revolts strain at order 9, outside 0..5", refused.Problems);
    }

    [Fact]
    public void A_region_part_way_to_revolt_resumes_its_count_from_a_save()
    {
        Board board = Strained();
        board.AddCondition(board.Region("march"), Unrest(board));
        Weeks(board, 5);

        Board resumed = Board.Load(TestBoards.TwoCrowns(marchControl: 1, marchOrder: 1), TestBoards.CardsFor(TestBoards.TwoCrowns()),
            BoardSave.FromJson(board.Save().ToJson(), "strained save"));
        Assert.Equal(5, resumed.Region("march").StrainWeeks);
    }
}
