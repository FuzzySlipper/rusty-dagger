namespace WorldRpg.Politics;

/// <summary>
/// Territorial upkeep: every Sunday each region is fed, relieved or cut off.
/// <list type="bullet">
/// <item>A region feeds itself while its harvest holds: prosperity <see cref="Harvest"/> or more, or
/// <see cref="WinterHarvest"/> in winter (the last <see cref="WinterWeeks"/> weeks of the year), with no famine on it.</item>
/// <item>Food comes from the next valley: a region that cannot feed itself is fed if it borders one that passes food
/// on. It goes no further, so a failed harvest or a blockade spreads hunger to the poorer regions around it. A region
/// passes food on when it feeds itself, and in winter only when it has grain to spare (<see cref="Surplus"/>).</item>
/// <item>A region under a blockade (a war front or raiders) passes nothing on, and food does not pass between crowns
/// hostile to each other (relation <see cref="Hostile"/> or below).</item>
/// <item>Grain bought under a trade agreement can relieve a region that is not fed (<see cref="Trade"/>).</item>
/// </list>
/// A cut-off region pays its holder no income from it, and its order and prosperity fall every <see cref="CutWeeks"/>th
/// week, until the Sunday it is fed or relieved again. Land that is fed recovers with the seasons (<see cref="BoardTurn"/>).
/// </summary>
public static class Supply
{
    public const int Harvest = 2;
    public const int WinterHarvest = 3;
    public const int WinterWeeks = 13;

    /// <summary>A region that feeds itself at this prosperity has grain to spare: one ration a week (<see cref="Trade"/>).</summary>
    public const int SurplusProsperity = 4;

    public const int Hostile = -2;
    public const int CutWeeks = 4;

    /// <summary>The regions fed this week.</summary>
    public static HashSet<Region> Fed(Board board)
    {
        HashSet<Region> feeding = [.. board.Regions.Where(region => FeedsItself(region, board.Turn))];
        bool winter = Winter(board.Turn);
        return [.. board.Regions.Where(region => feeding.Contains(region) || region.Borders.Select(board.Region).Any(from =>
            feeding.Contains(from) && (!winter || from.Prosperity >= SurplusProsperity) && !Blockaded(from) && Friendly(board, from, region)))];
    }

    public static bool FeedsItself(Region region, int turn) =>
        region.Prosperity >= (Winter(turn) ? WinterHarvest : Harvest) && !region.Conditions.Any(condition => condition.Kind == ConditionKind.Famine);

    /// <summary>Whether <paramref name="region"/> has grain to spare this week: it feeds itself at prosperity <see cref="SurplusProsperity"/> or more.</summary>
    public static bool Surplus(Region region, int turn) => FeedsItself(region, turn) && region.Prosperity >= SurplusProsperity;

    /// <summary>Whether <paramref name="turn"/> falls in the last <see cref="WinterWeeks"/> weeks of its year.</summary>
    public static bool Winter(int turn) => TurnTrace.DayOf(turn) > TurnTrace.DaysPerYear - WinterWeeks * TurnTrace.DaysPerWeek;

    public static bool Blockaded(Region region) =>
        region.Conditions.Any(condition => condition.Kind is ConditionKind.WarFront or ConditionKind.Raiders);

    private static bool Friendly(Board board, Region from, Region to) =>
        from.Allegiance is null || to.Allegiance is null || from.Allegiance == to.Allegiance
        || board.Relation(from.Allegiance, to.Allegiance) > Hostile;
}
