namespace WorldRpg.Politics;

/// <summary>
/// A seat's prestige, the derived score the seats weigh moves by and balance is measured in, and what a move changes
/// of it. A seat's agreements in force count at what each is worth to it (<see cref="Trade.Worth"/>), as it valued them
/// before the move.
/// </summary>
public static class Prestige
{
    /// <summary>
    /// A seat's standing in a region, in half points: 20 if it holds it, with 4 per point of control and prosperity and 2
    /// per point of order there; 6 per influence piece and per office. A crown's influence leading a contested region's own
    /// seat adds its lead times the region's missing control, so pressing a claim where the crown is ahead is worth
    /// something before the annexation it leads to. Conditions count for the region's holder before they weigh: 2 per point
    /// of magnitude, gained for a good one and lost for a harmful one; a persecution counts against the seat it persecutes.
    /// </summary>
    public static int Standing(Seat seat, Region region)
    {
        int influence = region.InfluenceOf(seat.Id);
        int standing = 6 * influence;
        foreach (RegionCondition condition in region.Conditions)
            if (condition.Kind == ConditionKind.Persecution && condition.Detail == seat.Id) standing -= 2 * condition.Magnitude;
        foreach (string holder in region.Offices.Values)
            if (holder == seat.Id) standing += 6;
        if (region.Allegiance == seat.Id)
        {
            standing += 20 + 4 * region.Control + 4 * region.Prosperity + 2 * region.Order;
            foreach (RegionCondition condition in region.Conditions) standing += 2 * Bent(condition.Kind) * condition.Magnitude;
        }
        else if (seat.Kind == SeatKind.Crown && !region.Capital)
        {
            int lead = influence - (region.Allegiance is null ? 0 : region.InfluenceOf(region.Allegiance));
            if (lead > 0) standing += 2 * lead * (Board.TrackLimit - region.Control);
        }
        return standing;
    }

    public static int Standing(Seat seat, IReadOnlyList<Region> regions)
    {
        int standing = 0;
        foreach (Region region in regions) standing += Standing(seat, region);
        return standing;
    }

    /// <summary>
    /// A seat's bearing: 2 per point of relation, 1 per point of each resource up to 6, beyond which a hoard is worth
    /// nothing more, and 3 lost per point of each condition troubling its court.
    /// </summary>
    public static int Bearing(Board board, Seat seat)
    {
        int bearing = 0;
        foreach (CourtCondition condition in seat.Court) bearing -= 3 * condition.Magnitude;
        foreach (Seat other in board.Seats)
            if (other.Id != seat.Id) bearing += 2 * board.Relation(seat.Id, other.Id);
        foreach (Resource resource in Enum.GetValues<Resource>())
            bearing += Math.Min(seat.Amount(resource), 6);
        return bearing;
    }

    /// <summary>Whether a condition is good (1), harmful (-1) or mixed (0) for the region's holder.</summary>
    private static int Bent(ConditionKind kind) => kind switch
    {
        ConditionKind.Prosperity or ConditionKind.Festival or ConditionKind.Recruitment or ConditionKind.PriceSurge => 1,
        ConditionKind.Crackdown or ConditionKind.WitchHunt or ConditionKind.Persecution => 0,
        _ => -1,
    };

    /// <summary>What <paramref name="seat"/>'s agreements in force are worth to it: as it valued them before, or a new one now.</summary>
    private static int Deals(Board board, Seat seat, Before? before)
    {
        int worth = 0;
        foreach (Agreement deal in board.Agreements)
        {
            if (!deal.Binds(seat.Id)) continue;
            worth += before is not null && before.Worth.TryGetValue((deal.Id, seat.Id), out int valued) ? valued : Trade.Worth(board, seat, deal);
        }
        return worth;
    }

    /// <summary>
    /// The seats whose prestige <paramref name="changes"/> by <paramref name="player"/> can have changed: the player, the
    /// seats changed directly, and every seat present before or after in a region changed. Present before and gone now, a
    /// seat shows in the change that removed it. Any other seat's gain is exactly 0.
    /// </summary>
    private static SortedSet<string> Touched(Board board, Seat player, IEnumerable<BoardChange> changes)
    {
        SortedSet<string> touched = new(StringComparer.Ordinal) { player.Id };
        foreach (BoardChange change in changes)
        {
            string field = change.Field;
            if (field is "coin" or "favour" or "force" || field.StartsWith("court:", StringComparison.Ordinal)
                || field.StartsWith("weariness:", StringComparison.Ordinal) || field.StartsWith("ally:", StringComparison.Ordinal))
            {
                touched.Add(change.Subject);
            }
            else if (field.StartsWith("relation:", StringComparison.Ordinal) || field.StartsWith("deal:", StringComparison.Ordinal)
                || field.StartsWith("war:", StringComparison.Ordinal) || field.StartsWith("truce:", StringComparison.Ordinal))
            {
                touched.Add(change.Subject);
                touched.Add(field[(field.IndexOf(':') + 1)..]);
            }
            else
            {
                Region region = board.Region(change.Subject);
                if (region.Allegiance is not null) touched.Add(region.Allegiance);
                touched.UnionWith(region.Influence.Keys);
                touched.UnionWith(region.Offices.Values);
                if (field.StartsWith("influence:", StringComparison.Ordinal)) touched.Add(field["influence:".Length..]);
                if (field == "allegiance" || field.StartsWith("office:", StringComparison.Ordinal))
                    touched.UnionWith(new[] { change.From, change.To }.Where(board.HasSeat));
            }
        }
        return touched;
    }

    /// <summary>Each seat's bearing and agreements as the board stood before any move was weighed.</summary>
    public sealed class Before(Board board)
    {
        public Dictionary<string, int> Bearing { get; } = board.Seats.ToDictionary(seat => seat.Id, seat => Prestige.Bearing(board, seat), StringComparer.Ordinal);

        /// <summary>What each agreement in force is worth to each of its two seats.</summary>
        public Dictionary<(string Deal, string Seat), int> Worth { get; } = board.Agreements
            .SelectMany(deal => new[] { deal.Proposer, deal.Partner }.Select(seat => (deal, seat)))
            .ToDictionary(pair => (pair.deal.Id, pair.seat), pair => Trade.Worth(board, board.Seat(pair.seat), pair.deal));

        public Dictionary<string, int> Deals { get; } = board.Seats.ToDictionary(seat => seat.Id, seat => Prestige.Deals(board, seat, null), StringComparer.Ordinal);
    }

    /// <summary>
    /// The board as a move by <see cref="Player"/> found it: each seat's standing in the move's target regions and the
    /// seats it bears on, named in its targets or present in a region it targets. A move changes only its target
    /// regions, so a seat's standing elsewhere is unchanged by it.
    /// </summary>
    public sealed class Witness
    {
        private readonly Before before;
        private readonly Dictionary<string, int> standing;

        public Witness(Board board, Seat player, Before before, IReadOnlyList<Region> regions, IEnumerable<string> named, Visibility visibility)
        {
            this.before = before;
            Player = player;
            Regions = regions;
            Visibility = visibility;
            standing = board.Seats.ToDictionary(seat => seat.Id, seat => Standing(seat, regions), StringComparer.Ordinal);
            Targeted = new SortedSet<string>(named.Concat(board.Seats.Where(seat => regions.Any(region => board.Present(seat, region))).Select(seat => seat.Id)),
                StringComparer.Ordinal);
            Targeted.Remove(player.Id);
            Mark = board.ChangeCount;
        }

        /// <summary>The move of <paramref name="card"/> on <paramref name="targets"/> by <paramref name="player"/>, about to be made.</summary>
        public static Witness OfCard(Board board, Seat player, Before before, Card card, IReadOnlyList<string> targets) =>
            new(board, player, before, card.TargetRegions(board, targets), Named(board, card, targets), card.Visibility);

        /// <summary>The seats a card's targets name: a seat, a rival on its own ground, a deal's partner or a war's principals.</summary>
        public static IReadOnlyList<string> Named(Board board, Card card, IReadOnlyList<string> targets) => card.Target switch
        {
            CardTarget.OtherSeat or CardTarget.Deal => [targets[0]],
            CardTarget.RegionRival => [targets[1]],
            CardTarget.War => [board.War(targets[0]).A, board.War(targets[0]).B],
            _ => [],
        };

        /// <summary><paramref name="player"/> withdrawing from <paramref name="deal"/>, which bears on its partner.</summary>
        public static Witness OfWithdrawal(Board board, Seat player, Before before, Agreement deal) =>
            new(board, player, before, [], [deal.Proposer == player.Id ? deal.Partner : deal.Proposer], Visibility.Public);

        public Seat Player { get; }

        public IReadOnlyList<Region> Regions { get; }

        public Visibility Visibility { get; }

        /// <summary>The other seats the move bears on: named in its targets, or present in a region it targets.</summary>
        public SortedSet<string> Targeted { get; }

        /// <summary>The board's change count when the move was witnessed: the move's own changes follow it.</summary>
        public int Mark { get; }

        /// <summary>Each touched seat's gain in prestige from <paramref name="changes"/>, the move's changes since it was witnessed.</summary>
        public SortedDictionary<string, int> Gains(Board board, IReadOnlyList<BoardChange> changes)
        {
            SortedDictionary<string, int> gains = new(StringComparer.Ordinal);
            foreach (string id in Touched(board, Player, changes))
            {
                Seat seat = board.Seat(id);
                gains[id] = Prestige.Bearing(board, seat) - before.Bearing[id] + Standing(seat, Regions) - standing[id]
                    + Deals(board, seat, before) - before.Deals[id];
            }
            return gains;
        }
    }
}
