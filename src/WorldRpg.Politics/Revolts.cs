namespace WorldRpg.Politics;

/// <summary>
/// When a held region strains against its liege and breaks free (<see cref="Revolts"/>): order and control at or below
/// which it strains, the weeks running it strains before it breaks free, and the weeks its own lords then hold it
/// uncontested. A board file may set it (<see cref="BoardSetup.Revolts"/>); the defaults are the canon's, tuned over
/// fifty canon years to a revolt every three to eight years, with the independent regions settling at about a half to
/// three quarters of the table instead of draining.
/// </summary>
public sealed record RevoltTuning(int StrainOrder = 1, int StrainControl = 1, int StrainWeeks = 16, int FreedWeeks = 52)
{
    public static RevoltTuning Default { get; } = new();

    /// <summary>What is wrong with the tuning, each named; empty when it can be played.</summary>
    public IReadOnlyList<string> Problems()
    {
        List<string> problems = [];
        if (StrainOrder is < 0 or > Board.TrackLimit) problems.Add($"revolts strain at order {StrainOrder}, outside 0..{Board.TrackLimit}");
        if (StrainControl is < 0 or > Board.TrackLimit) problems.Add($"revolts strain at control {StrainControl}, outside 0..{Board.TrackLimit}");
        if (StrainWeeks < 1) problems.Add($"revolts need {StrainWeeks} weeks of strain, fewer than 1");
        if (FreedWeeks < 0) problems.Add($"a freed region is held {FreedWeeks} weeks, fewer than 0");
        return problems;
    }
}

/// <summary>
/// A region under a liege that cannot hold it breaks free (Patch, Den #11129 Q5). Without it allegiance only ever moves
/// from one crown to another, and the independent regions drain away over the decades.
/// <para>
/// A crown-held region strains while, past its consolidation, its order and control are both at or below the board's
/// <see cref="RevoltTuning"/>, its liege has no influence left in it to call on, and something presses on it: it is cut
/// off from food, or carries unrest, a war front, famine, raiders or a crime wave. Low tracks alone are the ordinary lot
/// of held land, so they never start a revolt by themselves.
/// </para>
/// <para>
/// A region that has strained <see cref="RevoltTuning.StrainWeeks"/> weeks running throws its liege off. It is
/// independent again, the crown's influence and offices there are gone, and its own lords hold it uncontested for
/// <see cref="RevoltTuning.FreedWeeks"/> weeks. The chronicle names it a revolt (<see cref="ArcKind.Revolt"/>). A capital
/// never revolts.
/// </para>
/// </summary>
public static class Revolts
{
    /// <summary>Whether <paramref name="region"/> strains against its liege this week.</summary>
    public static bool Strains(Board board, Region region) =>
        region.Allegiance is { } liege && !region.Capital && board.Turn >= region.ContestableFrom
        && region.Order <= board.Revolts.StrainOrder && region.Control <= board.Revolts.StrainControl && region.InfluenceOf(liege) == 0
        && (region.Cut || region.Conditions.Any(condition => condition.Kind is ConditionKind.Unrest or ConditionKind.WarFront
            or ConditionKind.Famine or ConditionKind.Raiders or ConditionKind.CrimeWave));

    /// <summary>Counts each region's strain and frees those that have strained long enough. Runs at the end of upkeep.</summary>
    public static void Check(Board board)
    {
        foreach (Region region in board.Regions)
        {
            board.CountStrain(region, Strains(board, region));
            if (region.StrainWeeks >= board.Revolts.StrainWeeks) Free(board, region);
        }
    }

    private static void Free(Board board, Region region)
    {
        Seat liege = board.Seat(region.Allegiance!);
        board.SetAllegiance(region, null);
        board.AdjustInfluence(region, liege, -region.InfluenceOf(liege.Id));
        foreach (Office office in region.Offices.Where(held => held.Value == liege.Id).Select(held => held.Key).ToArray())
            board.SetOffice(region, office, null);
        board.Consolidate(region, board.Turn + board.Revolts.FreedWeeks);
        board.CountStrain(region, false);
        board.FollowArc(new OpenArc(ArcKind.Revolt, $"{region.Id}:{board.Turn}", [region.Id, liege.Id], board.Turn, false, board.Turn + board.Revolts.FreedWeeks));
    }
}
