namespace WorldRpg.Politics;

/// <summary>Where a World card falls: any region, or among the poorest, richest or most unruly, a tie settled by chance.</summary>
public enum WorldTarget
{
    AnyRegion,
    Poorest,
    Richest,
    Unruliest,
}

/// <summary>
/// A card of the World deck, which no seat plays: one is turned each Sunday and lays its condition, at its magnitude
/// for its weeks, on a region chosen by <see cref="Where"/>, with the world as its source. <see cref="Actor"/> names a
/// body with no seat at the table, such as a knightly order or a minor court, that the event is told through.
/// </summary>
public sealed record WorldCard(
    string Id,
    string Name,
    ConditionKind Condition,
    int Magnitude,
    int Weeks,
    WorldTarget Where,
    string? Detail = null,
    string? Actor = null,
    int Copies = 1);

/// <summary>The World card turned in a week: where it fell and what it changed.</summary>
public sealed record WorldTrace(string Card, string Region, string? Actor, IReadOnlyList<BoardChange> Changes);
