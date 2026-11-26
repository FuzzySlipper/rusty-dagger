namespace WorldRpg.Politics;

/// <summary>
/// The move a seat makes instead of playing a card: it gathers. It keeps the cards it is saving for, those it cannot
/// pay for yet but could play now if it could, and sets aside the rest: cards with no legal target, and cards it could
/// have played and passed over. So a hand never clogs, and the next draw refills it. It gains 1 of the resource the
/// kept cards lack most, by total shortfall, or, saving for nothing, its scarcest resource. A seat whose reserves are
/// cut off gathers no Force: its Force comes through its reserves point (<see cref="Balance"/>).
/// </summary>
public static class Gathering
{
    public const string Id = "gather";

    public static void Gather(Board board, Seat seat)
    {
        Card[] saving = Saving(board, seat);
        foreach (Card card in board.Deck(seat).Hand.Where(card => !saving.Contains(card)).ToArray()) board.Discard(seat, card);
        board.AdjustResource(seat, Lacking(seat, saving), 1);
    }

    /// <summary>The resource gathering gains for <paramref name="seat"/> now.</summary>
    public static Resource Lacking(Board board, Seat seat) => Lacking(seat, Saving(board, seat));

    /// <summary>The resources <paramref name="seat"/> can gather now.</summary>
    private static Resource[] Gatherable(Seat seat) =>
        [.. Enum.GetValues<Resource>().Where(resource => resource != Resource.Force || !Balance.ReservesCut(seat))];

    /// <summary>The cards in <paramref name="seat"/>'s hand it is saving for: those it cannot pay for yet but could play now if it could.</summary>
    internal static Card[] Saving(Board board, Seat seat) =>
        [.. board.Deck(seat).Hand.Where(card => !card.Affordable(seat) && card.Candidates(board, seat).Any(targets => card.Fits(board, seat, targets)))];

    private static Resource Lacking(Seat seat, Card[] saving)
    {
        Resource[] resources = Gatherable(seat);
        Dictionary<Resource, int> shortfall = resources.ToDictionary(resource => resource,
            resource => saving.Sum(card => Math.Max(0, card.Cost(resource) - seat.Amount(resource))));
        return shortfall.Values.Any(value => value > 0) ? resources.MaxBy(resource => shortfall[resource]) : resources.MinBy(seat.Amount);
    }
}
