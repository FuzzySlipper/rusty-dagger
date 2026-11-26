namespace WorldRpg.Politics;

/// <summary>
/// What the seats remember of each other's moves. A public move bears on a seat when its targets name the seat or it
/// targets a region the seat is present in. If the seat comes out of it worse off, it was struck: its relation with
/// the mover cools by 1, unless the move already cooled it or the two are in a feud, where the blow heats the feud instead
/// (<see cref="Feuds"/>), and it holds a grudge for <see cref="GrudgeWeeks"/> weeks,
/// which it may take up as a plan to strike back (<see cref="GoalKind.Retaliate"/>). Striking back at a seat one holds
/// a grudge against is payback: it settles the grudge and opens none in return. A seat that comes out of a move better
/// off was helped, and every <see cref="HelpsToWarm"/>th help its relation with the mover warms by 1, unless the move
/// already changed it. A secret move is not remembered, since the seat struck does not know who struck it.
/// </summary>
public static class Memory
{
    public const int GrudgeWeeks = 8;
    public const int HelpsToWarm = 2;

    /// <summary>Whether <paramref name="seat"/> would remember a loss from the move <paramref name="witness"/> saw as being struck by its mover.</summary>
    public static bool Struck(Prestige.Witness witness, Seat seat) =>
        witness.Visibility == Visibility.Public && witness.Targeted.Contains(seat.Id)
        && !witness.Player.Grudges.Any(grudge => grudge.Against == seat.Id);

    /// <summary>Remembers the move <paramref name="witness"/> saw, now that it has changed the board.</summary>
    public static void Remember(Board board, Prestige.Witness witness)
    {
        if (witness.Visibility == Visibility.Secret) return;
        Seat mover = witness.Player;
        BoardChange[] changes = [.. board.ChangesSince(witness.Mark)];
        foreach ((string id, int gain) in witness.Gains(board, changes))
        {
            if (!witness.Targeted.Contains(id) || gain == 0) continue;
            Seat seat = board.Seat(id);
            (string first, string second) = Board.Pair(mover.Id, id);
            bool related = changes.Any(change => change.Subject == first && change.Field == $"relation:{second}");
            if (gain < 0 && mover.Grudges.Any(grudge => grudge.Against == id))
            {
                board.SettleGrudge(mover, seat);
            }
            else if (gain < 0)
            {
                if (!related) Feuds.Struck(board, mover, seat);
                board.HoldGrudge(seat, mover, board.Turn + GrudgeWeeks);
            }
            else if (!related && board.Helped(mover, seat) % HelpsToWarm == 0)
            {
                board.AdjustRelation(mover, seat, 1);
            }
        }
    }
}
