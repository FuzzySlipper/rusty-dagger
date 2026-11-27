namespace WorldRpg.Politics;

/// <summary>
/// A region under a liege that cannot hold it breaks free (Patch, Den #11129 Q5). Without it allegiance only ever moves
/// from one crown to another, and the independent regions drain away over the decades.
/// <para>
/// A crown-held region strains while, past its consolidation, its order and control are both low, its liege has no
/// influence left in it to call on, and something presses on it: it is cut off from food, or carries unrest, a war
/// front, famine, raiders or a crime wave. Low tracks alone are the ordinary lot of held land, so they never start a
/// revolt by themselves.
/// </para>
/// <para>
/// A region that has strained <see cref="StrainWeeks"/> weeks running throws its liege off. It is independent again,
/// the crown's influence there is gone as a conquest clears it, and its own lords hold it uncontested for
/// <see cref="FreedWeeks"/> weeks. The chronicle names it a revolt (<see cref="ArcKind.Revolt"/>). A capital never revolts.
/// </para>
/// <para>
/// Tuned over fifty canon years: a revolt every three to eight years, and the independent regions settle at about a
/// half to three quarters of the table instead of draining.
/// </para>
/// </summary>
public static class Revolts
{
    /// <summary>Order at or below which a held region strains.</summary>
    public const int StrainOrder = 1;

    /// <summary>Control at or below which a held region strains.</summary>
    public const int StrainControl = 1;

    /// <summary>Weeks running a region strains before it breaks free.</summary>
    public const int StrainWeeks = 16;

    /// <summary>Weeks a freed region's own lords hold it uncontested, and the revolt's arc runs.</summary>
    public const int FreedWeeks = 26;

    /// <summary>Whether <paramref name="region"/> strains against its liege this week.</summary>
    public static bool Strains(Board board, Region region) =>
        region.Allegiance is { } liege && !region.Capital && board.Turn >= region.ContestableFrom
        && region.Order <= StrainOrder && region.Control <= StrainControl && region.InfluenceOf(liege) == 0
        && (region.Cut || region.Conditions.Any(condition => condition.Kind is ConditionKind.Unrest or ConditionKind.WarFront
            or ConditionKind.Famine or ConditionKind.Raiders or ConditionKind.CrimeWave));

    /// <summary>Counts each region's strain and frees those that have strained long enough. Runs at the end of upkeep.</summary>
    public static void Check(Board board)
    {
        foreach (Region region in board.Regions)
        {
            board.CountStrain(region, Strains(board, region));
            if (region.StrainWeeks >= StrainWeeks) Free(board, region);
        }
    }

    private static void Free(Board board, Region region)
    {
        Seat liege = board.Seat(region.Allegiance!);
        board.SetAllegiance(region, null);
        board.AdjustInfluence(region, liege, -region.InfluenceOf(liege.Id));
        board.Consolidate(region, board.Turn + FreedWeeks);
        board.CountStrain(region, false);
        board.FollowArc(new OpenArc(ArcKind.Revolt, $"{region.Id}:{board.Turn}", [region.Id, liege.Id], board.Turn, false, board.Turn + FreedWeeks));
    }
}
