namespace WorldRpg.Politics;

/// <summary>
/// What the seats remember of each other's moves. A public move bears on a seat when its targets name the seat or it
/// targets a region the seat is present in. If the seat comes out of it worse off, it was struck: its relation with
/// the mover cools by 1, unless the move already cooled it or the two are in a feud, and it holds a grudge for <see cref="GrudgeWeeks"/> weeks,
/// which it may take up as a plan to strike back (<see cref="GoalKind.Retaliate"/>). Striking back at a seat one holds
/// a grudge against is payback: it settles the grudge and opens none in return. A seat that comes out of a move better
/// off was helped, and every <see cref="HelpsToWarm"/>th help its relation with the mover warms by 1, unless the move
/// already changed it. A secret move is not remembered, since the seat struck does not know who struck it.
/// <para>
/// Between two crowns in a feud, a move that struck the other, or a card of open conflict aimed at it that changed more
/// than the mover's own purse, is a blow in the feud (<see cref="Feuds"/>) whatever it cost in prestige and whether or not it was payback,
/// unless the move already cooled their relation itself.
/// </para>
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
        SortedDictionary<string, int> gains = witness.Gains(board, changes);
        bool landed = changes.Any(change => change.Subject != mover.Id || change.Field is not ("coin" or "favour" or "force"));
        foreach (string id in witness.Targeted)
        {
            int gain = gains.GetValueOrDefault(id);
            bool foe = landed && witness.Foes.Contains(id);
            if (gain == 0 && !foe) continue;
            Seat seat = board.Seat(id);
            (string first, string second) = Board.Pair(mover.Id, id);
            bool related = changes.Any(change => change.Subject == first && change.Field == $"relation:{second}");
            Feud? feud = Feuds.Between(board, mover.Id, id);
            if (feud is not null && !related && (gain < 0 || foe)) Feuds.Blow(board, feud);
            if (gain < 0 && mover.Grudges.Any(grudge => grudge.Against == id))
            {
                board.SettleGrudge(mover, seat);
            }
            else if (gain < 0)
            {
                if (feud is null && !related) board.AdjustRelation(mover, seat, -1);
                board.HoldGrudge(seat, mover, board.Turn + GrudgeWeeks);
            }
            else if (gain > 0 && !foe && !related && board.Helped(mover, seat) % HelpsToWarm == 0)
            {
                board.AdjustRelation(mover, seat, 1);
            }
        }
    }
}
