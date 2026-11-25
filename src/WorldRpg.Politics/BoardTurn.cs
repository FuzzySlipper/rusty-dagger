namespace WorldRpg.Politics;

/// <summary>
/// One weekly turn, resolved on Sunday:
/// <list type="number">
/// <item>Upkeep. Each seat is paid: coin of a quarter of the prosperity of the regions it holds; favour of a third of
/// its influence pieces; force of a quarter of its regions' control, each rounded down. A seat's grants
/// fall due in the weeks their schedules divide, and every <see cref="OfficeWeeks"/>th week each office pays its holder
/// 1 of its resource. A region with order 1 or less loses a point of control to unrest. Every fourth week an
/// independent region below control 3 regains one, as its own lords reassert themselves. Every
/// <see cref="SeasonWeeks"/>th week ground left untended drifts: each region's control, prosperity and order above
/// <see cref="SettledTrack"/> falls by 1, and each relation moves 1 towards 0, so what a seat has built needs keeping
/// up. Each seat draws its hand back up to <see cref="Board.HandSize"/>.</item>
/// <item>The slow moves due this week arrive, in the order they were played. One that no longer fits its targets, or
/// changes nothing, fails.</item>
/// <item>Each seat plays a card from its hand or gathers, the table's order turning by one seat each week. A slow card
/// is paid now and arrives its weeks later.</item>
/// </list>
/// </summary>
public static class BoardTurn
{
    public const int UnrestOrder = 1;
    public const int IndependentRecoveryWeeks = 4;
    public const int IndependentRecoveryControl = 3;
    public const int OfficeWeeks = 8;
    public const int SeasonWeeks = 13;
    public const int SettledTrack = 3;

    public static TurnTrace Play(Board board)
    {
        board.Turn++;
        board.TakeChanges();
        Upkeep(board);
        IReadOnlyList<BoardChange> upkeep = board.TakeChanges();

        List<ArrivalTrace> arrivals = [];
        foreach (PendingMove move in board.TakeArrivals())
        {
            Seat seat = board.Seat(move.Seat);
            bool resolved = move.Card.Fits(board, seat, move.Targets) && move.Card.Apply(board, seat, move.Targets);
            arrivals.Add(new ArrivalTrace(move.Id, move.Seat, move.Card.Id, move.Targets, move.Card.Visibility,
                resolved ? MoveOutcome.Resolved : MoveOutcome.Failed, board.TakeChanges()));
        }

        List<MoveTrace> moves = [];
        int seats = board.Seats.Count;
        for (int index = 0; index < seats; index++)
        {
            Seat seat = board.Seats[(board.Turn - 1 + index) % seats];
            IReadOnlyList<MoveOption> options = MoveChooser.Options(board, seat);
            MoveOption chosen = MoveChooser.Choose(board, seat, options);
            string id = $"{board.Turn}-{seat.Id}";
            Visibility visibility = Visibility.Public;
            string? origin = null;
            int? arrives = null;
            if (chosen.Card == Gathering.Id)
            {
                Gathering.Gather(board, seat);
            }
            else
            {
                Card card = board.Deck(seat).Hand.First(held => held.Id == chosen.Card);
                board.Discard(seat, card);
                visibility = card.Visibility;
                if (card.Weeks == 0)
                {
                    card.Play(board, seat, chosen.Targets);
                }
                else
                {
                    card.Pay(board, seat);
                    origin = Origin(board, seat, card, chosen.Targets);
                    arrives = board.Turn + card.Weeks;
                    board.Schedule(new PendingMove(id, seat.Id, card, chosen.Targets, origin, board.Turn, arrives.Value));
                }
            }
            moves.Add(new MoveTrace(id, seat.Id, chosen.Card, chosen.Targets, chosen.Score, visibility, origin, arrives,
                [.. options.Where(option => !ReferenceEquals(option, chosen)).Take(MoveTrace.AlternativesShown)], board.TakeChanges()));
        }
        return new TurnTrace(board.Turn, upkeep, arrivals, moves);
    }

    /// <summary>The resource an office pays its holder.</summary>
    public static Resource Pays(Office office) => office switch
    {
        Office.Watch => Resource.Force,
        Office.Factor or Office.Hall => Resource.Coin,
        Office.Temple => Resource.Favour,
        _ => throw new ArgumentOutOfRangeException(nameof(office), office, null),
    };

    /// <summary>
    /// Where a slow move sets out from: its first target region if the seat is present there, or else the first of that
    /// region's borders where it is; for a move on a seat or on nothing, a crown's capital or the region where a power
    /// has the most influence. A power with no influence sends from nowhere.
    /// </summary>
    public static string? Origin(Board board, Seat seat, Card card, IReadOnlyList<string> targets)
    {
        if (card.Target is not (CardTarget.None or CardTarget.OtherSeat))
        {
            Region target = board.Region(targets[0]);
            return board.Present(seat, target) ? target.Id : target.Borders.FirstOrDefault(border => board.Present(seat, board.Region(border)));
        }
        if (seat.Home is not null) return seat.Home;
        Region? strongest = board.Regions.Where(region => region.InfluenceOf(seat.Id) > 0).MaxBy(region => region.InfluenceOf(seat.Id));
        return strongest?.Id;
    }

    private static void Upkeep(Board board)
    {
        foreach (Seat seat in board.Seats)
        {
            Region[] held = [.. board.Regions.Where(region => region.Allegiance == seat.Id)];
            board.AdjustResource(seat, Resource.Coin, held.Sum(region => region.Prosperity) / 4);
            board.AdjustResource(seat, Resource.Favour, board.Regions.Sum(region => region.InfluenceOf(seat.Id)) / 3);
            board.AdjustResource(seat, Resource.Force, held.Sum(region => region.Control) / 4);
            foreach (SeatGrant grant in seat.Grants)
                if (board.Turn % grant.EveryWeeks == 0) board.AdjustResource(seat, grant.Resource, grant.Amount);
            if (board.Turn % OfficeWeeks == 0)
            {
                foreach (Resource resource in Enum.GetValues<Resource>())
                {
                    int offices = board.Regions.Sum(region => region.Offices.Count(office => office.Value == seat.Id && Pays(office.Key) == resource));
                    board.AdjustResource(seat, resource, offices);
                }
            }
        }
        foreach (Region region in board.Regions)
        {
            if (region.Order <= UnrestOrder) board.AdjustControl(region, -1);
            if (region.Allegiance is null && board.Turn % IndependentRecoveryWeeks == 0 && region.Control < IndependentRecoveryControl)
                board.AdjustControl(region, 1);
            if (board.Turn % SeasonWeeks == 0)
            {
                if (region.Control > SettledTrack) board.AdjustControl(region, -1);
                if (region.Prosperity > SettledTrack) board.AdjustProsperity(region, -1);
                if (region.Order > SettledTrack) board.AdjustOrder(region, -1);
            }
        }
        if (board.Turn % SeasonWeeks == 0)
        {
            foreach (Seat seat in board.Seats)
                foreach (Seat other in board.Seats.Where(other => string.CompareOrdinal(seat.Id, other.Id) < 0))
                    board.AdjustRelation(seat, other, -Math.Sign(board.Relation(seat.Id, other.Id)));
        }
        foreach (Seat seat in board.Seats) board.DrawHand(seat);
    }
}
