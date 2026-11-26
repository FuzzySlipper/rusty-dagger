namespace WorldRpg.Politics;

/// <summary>
/// The balance of the table, which has no winner.
/// <list type="bullet">
/// <item><b>The leader</b> is the seat, other than an arbiter, whose prestige (<see cref="Prestige.Total"/>) runs at
/// least <see cref="LeadMargin"/> ahead of every other's, found at the start of each week; a table where no seat has
/// pulled ahead has none. Dampers work against it: the trailing seat moves first (<see cref="Order"/>), harmful
/// World cards fall twice as often on its regions, and every relation with it drifts towards <see cref="LeaderDrift"/>
/// each season instead of 0, so coalitions form against it.</item>
/// <item><b>An arbiter</b>, the Empire in the canon table, keeps the balance of power. Its prestige counts
/// <see cref="BufferWeight"/> per point of control in each independent region, the buffer baronies, and loses
/// <see cref="WarCost"/> for each war in force; it weighs the leader's gains as a rival's whatever their relation; and it
/// may broker a truce with any principal it is not hostile to. Its countermoves are cards that cost Force.</item>
/// <item><b>Reserves.</b> A seat may name a reserves point, a region all its Force arrives through: its grants, its
/// offices' pay, what it gathers and what it is paid or takes. Once the point has been contested for
/// <see cref="ReservesBite"/> weeks running, the seat gains no Force until the contest lifts, so its countermoves dry
/// up. A point is
/// contested while a blockade or skirmish lies on it, or while a seat hostile to the receiver (<see cref="Supply.Hostile"/>
/// or below) has more influence there than it.</item>
/// </list>
/// The caps on influence (<see cref="Board.InfluenceLimit"/> a region, <see cref="Board.InfluencePieces"/> pieces) and on
/// hands (<see cref="Board.HandSize"/>) hold every seat's reach in check.
/// </summary>
public static class Balance
{
    public const int ReservesBite = 4;
    public const int BufferWeight = 2;
    public const int WarCost = 6;
    public const int LeaderDrift = -1;
    public const int LeadMargin = 30;

    /// <summary>The seat leading the table now, if one has pulled <see cref="LeadMargin"/> ahead of every other seat that is not an arbiter.</summary>
    public static Seat? FindLeader(Board board)
    {
        (Seat Seat, int Prestige)[] ranked = [.. board.Seats.Where(seat => !seat.Arbiter).Select(seat => (seat, Prestige.Total(board, seat)))
            .OrderByDescending(entry => entry.Item2)];
        return ranked.Length > 0 && (ranked.Length == 1 || ranked[0].Prestige - ranked[1].Prestige >= LeadMargin) ? ranked[0].Seat : null;
    }

    /// <summary>
    /// The order the seats move in this week: the trailing seat by prestige first. Seats of equal prestige keep the
    /// table's order, turned by one seat each week.
    /// </summary>
    public static IReadOnlyList<Seat> Order(Board board)
    {
        int seats = board.Seats.Count;
        return [.. Enumerable.Range(0, seats).Select(index => board.Seats[(board.Turn - 1 + index) % seats])
            .Select((seat, index) => (seat, index, prestige: Prestige.Total(board, seat)))
            .OrderBy(entry => entry.prestige).ThenBy(entry => entry.index).Select(entry => entry.seat)];
    }

    /// <summary>The relation each season's drift moves the relation between <paramref name="a"/> and <paramref name="b"/> towards.</summary>
    public static int DriftTarget(Board board, Seat a, Seat b) => board.Leader == a.Id || board.Leader == b.Id ? LeaderDrift : 0;

    /// <summary>Whether <paramref name="seat"/>'s reserves point is contested this week.</summary>
    public static bool Contested(Board board, Seat seat)
    {
        if (seat.Reserves is not { } point) return false;
        Region region = board.Region(point);
        int own = region.InfluenceOf(seat.Id);
        return region.Conditions.Any(condition => condition.Kind is ConditionKind.WarFront or ConditionKind.Raiders or ConditionKind.Skirmish)
            || region.Influence.Any(entry => entry.Key != seat.Id && entry.Value > own && board.Relation(seat.Id, entry.Key) <= Supply.Hostile);
    }

    /// <summary>Whether <paramref name="seat"/>'s Force has stopped arriving: its reserves point has been contested <see cref="ReservesBite"/> weeks running.</summary>
    public static bool ReservesCut(Seat seat) => seat.ContestedWeeks >= ReservesBite;

    /// <summary>
    /// The arbiter's part of its bearing: <see cref="BufferWeight"/> per point of control in each independent region,
    /// less <see cref="WarCost"/> for each war in force.
    /// </summary>
    public static int Arbitration(Board board, Seat seat) => !seat.Arbiter ? 0
        : BufferWeight * board.Regions.Where(region => region.Allegiance is null).Sum(region => region.Control) - WarCost * board.Wars.Count;
}
