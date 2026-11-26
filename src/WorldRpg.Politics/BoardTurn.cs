namespace WorldRpg.Politics;

/// <summary>
/// One weekly turn, resolved on Sunday:
/// <list type="number">
/// <item>The table's leader is found (<see cref="Balance"/>).</item>
/// <item>Upkeep:
/// <list type="bullet">
/// <item>Conditions that have reached their end are lifted, grudges past their last week are let go, and each region
/// is found fed or cut off from food (<see cref="Supply"/>). Trade agreements that have broken end, and the grain rations due relieve regions not fed
/// (<see cref="Trade"/>).</item>
/// <item>A seat with a reserves point counts another week it is contested, or clears the count (<see cref="Balance"/>).
/// Each seat is paid from the regions it holds that are fed or relieved: coin of a quarter of their prosperity (a
/// famine empties a region's granaries, and a price surge adds its magnitude) and force of a quarter of their control,
/// and favour of a third of its influence pieces, each rounded down. A seat's grants fall due in the weeks their
/// schedules divide, and every <see cref="OfficeWeeks"/>th week each office pays its holder 1 of its resource. Then the
/// resource terms of trade agreements falling due are paid.</item>
/// <item>Each region and court condition weighs on its region or crown as <see cref="Conditions.Weigh(Board, Region)"/>
/// says. A region still cut off loses order and prosperity every <see cref="Supply.CutWeeks"/>th week. A region with
/// order 1 or less loses a point of control to unrest. Every fourth week an independent region below control 3 regains
/// one, as its own lords reassert themselves.</item>
/// <item>Every <see cref="SeasonWeeks"/>th week ground left untended drifts: each region's control, prosperity and
/// order above <see cref="SettledTrack"/> falls by 1, a region not cut off below it regains 1 prosperity, and each
/// relation moves 1 towards 0, or towards <see cref="Balance.LeaderDrift"/> with the leader, except between seats at
/// war or in a heated feud, so what a seat has built needs keeping up, fed land recovers and the leader's friends fall
/// away.</item>
/// <item>Each seat draws its hand back up to <see cref="Board.HandSize"/>.</item>
/// </list>
/// </item>
/// <item>The World deck turns one card, which lays its condition on a region.</item>
/// <item>The slow moves due this week arrive, in the order they were played. One that no longer fits its targets, or
/// changes nothing, fails.</item>
/// <item>Each seat reviews its plan (<see cref="Intention"/>), then plays a card from its hand, gathers or withdraws from
/// a trade agreement, the trailing seat by prestige first (<see cref="Balance.Order"/>). A slow card is paid now and arrives its weeks
/// later. Each seat a move bears on remembers it once it has changed the board (<see cref="Memory"/>). A seat at war
/// or in a feud may play the cards of open conflict as well as its hand.</item>
/// <item>Wars and feuds are checked (<see cref="Wars"/>): weariness grows and exhausted wars end in treaties, feuds
/// cool and their vassals skirmish, crowns at war's relation go to war, feuds open and end, and allies are pulled in.</item>
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
        board.SetLeader(Balance.FindLeader(board));
        Upkeep(board);
        IReadOnlyList<BoardChange> upkeep = board.TakeChanges();

        WorldTrace? world = board.TurnWorldCard() is (WorldCard turned, Region region)
            ? new WorldTrace(turned.Id, region.Id, turned.Actor, board.TakeChanges())
            : null;

        List<ArrivalTrace> arrivals = [];
        foreach (PendingMove move in board.TakeArrivals())
        {
            Seat seat = board.Seat(move.Seat);
            Prestige.Witness witness = Prestige.Witness.OfCard(board, seat, new Prestige.Before(board), move.Card, move.Targets);
            bool resolved = move.Card.Fits(board, seat, move.Targets) && move.Card.Apply(board, seat, move.Targets);
            if (resolved) Memory.Remember(board, witness);
            arrivals.Add(new ArrivalTrace(move.Id, move.Seat, move.Card.Id, move.Targets, move.Card.Visibility,
                resolved ? MoveOutcome.Resolved : MoveOutcome.Failed, board.TakeChanges()));
        }

        List<MoveTrace> moves = [];
        foreach (Seat seat in Balance.Order(board))
        {
            Intention.Review(board, seat);
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
            else if (chosen.Card == Trade.WithdrawId)
            {
                Agreement deal = board.Agreements.Single(deal => deal.Id == chosen.Targets[0]);
                Prestige.Witness witness = Prestige.Witness.OfWithdrawal(board, seat, new Prestige.Before(board), deal);
                Trade.Withdraw(board, seat, deal);
                Memory.Remember(board, witness);
            }
            else
            {
                Card? held = board.Deck(seat).Hand.FirstOrDefault(held => held.Id == chosen.Card);
                Card card = held ?? board.StandingCards.First(standing => standing.Id == chosen.Card);
                if (held is not null) board.Discard(seat, card);
                visibility = card.Visibility;
                if (card.Weeks == 0)
                {
                    Prestige.Witness witness = Prestige.Witness.OfCard(board, seat, new Prestige.Before(board), card, chosen.Targets);
                    card.Play(board, seat, chosen.Targets);
                    Memory.Remember(board, witness);
                }
                else
                {
                    card.Pay(board, seat);
                    origin = Origin(board, seat, card, chosen.Targets);
                    arrives = board.Turn + card.Weeks;
                    board.Schedule(new PendingMove(id, seat.Id, card, chosen.Targets, origin, board.Turn, arrives.Value));
                }
            }
            moves.Add(new MoveTrace(id, seat.Id, seat.Plan?.ToString(), chosen.Card, chosen.Targets, chosen.Score, visibility, origin, arrives,
                [.. options.Where(option => !ReferenceEquals(option, chosen)).Take(MoveTrace.AlternativesShown)], board.TakeChanges()));
        }
        Wars.Check(board);
        return new TurnTrace(board.Turn, upkeep, world, arrivals, moves, board.TakeChanges());
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
        if (card.Target is not (CardTarget.None or CardTarget.OtherSeat or CardTarget.Deal or CardTarget.War or CardTarget.Leader))
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
        board.ExpireConditions();
        board.ExpireGrudges();
        HashSet<Region> fed = Supply.Fed(board);
        HashSet<Region> relieved = Trade.Relieve(board, fed);
        foreach (Region region in board.Regions)
            board.SetSupply(region, fed.Contains(region) ? RegionSupply.Fed : relieved.Contains(region) ? RegionSupply.Relieved : RegionSupply.Cut);
        foreach (Seat seat in board.Seats.Where(seat => seat.Reserves is not null)) board.SetContested(seat, Balance.Contested(board, seat));
        foreach (Seat seat in board.Seats)
        {
            Region[] held = [.. board.Regions.Where(region => region.Allegiance == seat.Id && !region.Cut)];
            board.AdjustResource(seat, Resource.Coin, held.Sum(Conditions.ProsperityIncome) / 4);
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
        Trade.Settle(board);
        foreach (Region region in board.Regions) Conditions.Weigh(board, region);
        foreach (Seat seat in board.Seats) Conditions.Weigh(board, seat);
        foreach (Region region in board.Regions)
        {
            if (region.Cut && board.Turn % Supply.CutWeeks == 0)
            {
                board.AdjustOrder(region, -1);
                board.AdjustProsperity(region, -1);
            }
            if (region.Order <= UnrestOrder) board.AdjustControl(region, -1);
            if (region.Allegiance is null && board.Turn % IndependentRecoveryWeeks == 0 && region.Control < IndependentRecoveryControl)
                board.AdjustControl(region, 1);
            if (board.Turn % SeasonWeeks == 0)
            {
                if (region.Control > SettledTrack) board.AdjustControl(region, -1);
                if (region.Prosperity > SettledTrack) board.AdjustProsperity(region, -1);
                else if (region.Prosperity < SettledTrack && !region.Cut) board.AdjustProsperity(region, 1);
                if (region.Order > SettledTrack) board.AdjustOrder(region, -1);
            }
        }
        if (board.Turn % SeasonWeeks == 0)
        {
            foreach (Seat seat in board.Seats)
                foreach (Seat other in board.Seats.Where(other => string.CompareOrdinal(seat.Id, other.Id) < 0))
                    if (Feuds.Between(board, seat.Id, other.Id) is not { Heat: > 0 })
                        board.AdjustRelation(seat, other, Math.Sign(Balance.DriftTarget(board, seat, other) - board.Relation(seat.Id, other.Id)));
        }
        foreach (Seat seat in board.Seats) board.DrawHand(seat);
    }
}
