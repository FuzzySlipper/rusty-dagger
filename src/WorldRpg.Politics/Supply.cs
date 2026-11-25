namespace WorldRpg.Politics;

/// <summary>
/// Territorial upkeep: every Sunday each region is fed or cut off. A region feeds itself while its harvest holds,
/// at prosperity <see cref="Harvest"/> or more with no famine on it. Food comes from the next valley: a region that
/// cannot feed itself is fed if it borders one that feeds itself and passes food to it. It goes no further, so a
/// failed harvest or a blockade spreads hunger to the poorer regions around it. A region under a blockade (a war front
/// or raiders) passes nothing on, and food does not pass between crowns hostile to each other (relation
/// <see cref="Hostile"/> or below). A cut-off region pays its holder no income from it, and its order falls every
/// <see cref="CutOrderWeeks"/>th week, until the Sunday it is fed again.
/// </summary>
public static class Supply
{
    public const int Harvest = 2;
    public const int Hostile = -2;
    public const int CutOrderWeeks = 4;

    /// <summary>The regions fed this week.</summary>
    public static HashSet<Region> Fed(Board board)
    {
        HashSet<Region> feeding = [.. board.Regions.Where(FeedsItself)];
        return [.. board.Regions.Where(region => feeding.Contains(region) || region.Borders.Select(board.Region)
            .Any(from => feeding.Contains(from) && !Blockaded(from) && Friendly(board, from, region)))];
    }

    public static bool FeedsItself(Region region) =>
        region.Prosperity >= Harvest && !region.Conditions.Any(condition => condition.Kind == ConditionKind.Famine);

    public static bool Blockaded(Region region) =>
        region.Conditions.Any(condition => condition.Kind is ConditionKind.WarFront or ConditionKind.Raiders);

    private static bool Friendly(Board board, Region from, Region to) =>
        from.Allegiance is null || to.Allegiance is null || from.Allegiance == to.Allegiance
        || board.Relation(from.Allegiance, to.Allegiance) > Hostile;
}
