namespace WorldRpg.Politics;

/// <summary>A card and its target, as a seat might play it.</summary>
public sealed record MoveOption(string Card, string? Target, int Score);

/// <summary>
/// The first chooser: greedy, one move deep. It plays each legal move on a copy of the board and scores the
/// resulting prestige: the player's gain counted four times, minus the gains of seats it is not friendly with
/// (twice for a rival, once for a neutral). The seeded chance breaks only exact ties. Intention, with plans that
/// span weeks, replaces it in #11060.
/// </summary>
public static class MoveChooser
{
    /// <summary>
    /// Every legal move for <paramref name="seat"/> that would change something, best first; moves of equal score keep
    /// card and target order. Banking is always among them, so a seat always has a move.
    /// </summary>
    public static IReadOnlyList<MoveOption> Options(Board board, Seat seat)
    {
        List<MoveOption> options = [];
        Dictionary<string, int> before = board.Seats.ToDictionary(other => other.Id, other => Prestige(board, other), StringComparer.Ordinal);
        foreach (Card card in StarterCards.All)
        {
            foreach (string? target in Targets(board, card))
            {
                if (!card.Playable(board, seat, target)) continue;
                Board trial = board.Clone();
                if (!card.Play(trial, trial.Seat(seat.Id), target) && card != StarterCards.Bank) continue;
                int score = 0;
                foreach (Seat other in board.Seats)
                {
                    int gain = Prestige(trial, trial.Seat(other.Id)) - before[other.Id];
                    score += other.Id == seat.Id ? 4 * gain : -RivalWeight(board.Relation(seat.Id, other.Id)) * gain;
                }
                options.Add(new MoveOption(card.Id, target, score));
            }
        }
        return [.. options.OrderByDescending(option => option.Score)];
    }

    /// <summary>The move <paramref name="seat"/> plays: the best scored, an exact tie settled by the board's chance.</summary>
    public static MoveOption Choose(Board board, Seat seat, IReadOnlyList<MoveOption> options)
    {
        MoveOption[] best = [.. options.TakeWhile(option => option.Score == options[0].Score)];
        return best.Length == 1 ? best[0] : best[board.Random.Next(best.Length)];
    }

    /// <summary>
    /// A seat's standing on the board, in half points: 20 per region held; 4 per point of control and prosperity
    /// and 2 per point of order in those regions; 6 per influence piece anywhere; 2 per point of relation; and 1 per
    /// point of each resource up to 6, beyond which a hoard is worth nothing more. Influence leading a contested
    /// region's own seat adds its lead times the region's missing control, so pressing a claim where the seat is
    /// ahead is worth something before the annexation it leads to.
    /// </summary>
    public static int Prestige(Board board, Seat seat)
    {
        int prestige = 0;
        foreach (Region region in board.Regions)
        {
            int influence = region.InfluenceOf(seat.Id);
            prestige += 6 * influence;
            if (region.Allegiance == seat.Id)
            {
                prestige += 20 + 4 * region.Control + 4 * region.Prosperity + 2 * region.Order;
            }
            else if (!region.Capital)
            {
                int lead = influence - (region.Allegiance is null ? 0 : region.InfluenceOf(region.Allegiance));
                if (lead > 0) prestige += 2 * lead * (Board.TrackLimit - region.Control);
            }
        }
        foreach (Seat other in board.Seats)
            if (other.Id != seat.Id) prestige += 2 * board.Relation(seat.Id, other.Id);
        foreach (Resource resource in Enum.GetValues<Resource>())
            prestige += Math.Min(seat.Amount(resource), 6);
        return prestige;
    }

    private static int RivalWeight(int relation) => relation < 0 ? 2 : relation == 0 ? 1 : 0;

    private static IEnumerable<string?> Targets(Board board, Card card) => card.Target switch
    {
        CardTarget.None => [(string?)null],
        CardTarget.OtherSeat => board.Seats.Select(string? (seat) => seat.Id),
        _ => board.Regions.Select(string? (region) => region.Id),
    };
}
