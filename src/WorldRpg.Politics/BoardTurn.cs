namespace WorldRpg.Politics;

/// <summary>
/// One weekly turn, resolved on Sunday:
/// <list type="number">
/// <item>Upkeep. Each seat is paid: coin of a quarter of the prosperity of the regions it holds; favour of 1 plus a
/// third of its influence pieces; force of 1 plus a fifth of its regions' control, each rounded down. A region with order 1 or less loses a point of
/// control to unrest. Every fourth week an independent region below control 3 regains one, as its own lords
/// reassert themselves.</item>
/// <item>Each seat plays one move, the table's order turning by one seat each week.</item>
/// </list>
/// </summary>
public static class BoardTurn
{
    public const int UnrestOrder = 1;
    public const int IndependentRecoveryWeeks = 4;
    public const int IndependentRecoveryControl = 3;

    public static TurnTrace Play(Board board)
    {
        board.Turn++;
        board.TakeChanges();
        Upkeep(board);
        IReadOnlyList<BoardChange> upkeep = board.TakeChanges();

        List<MoveTrace> moves = [];
        int seats = board.Seats.Count;
        for (int index = 0; index < seats; index++)
        {
            Seat seat = board.Seats[(board.Turn - 1 + index) % seats];
            IReadOnlyList<MoveOption> options = MoveChooser.Options(board, seat);
            MoveOption chosen = MoveChooser.Choose(board, seat, options);
            StarterCards.All.Single(card => card.Id == chosen.Card).Play(board, seat, chosen.Target);
            moves.Add(new MoveTrace(seat.Id, chosen.Card, chosen.Target, chosen.Score,
                [.. options.Where(option => option != chosen).Take(MoveTrace.AlternativesShown)], board.TakeChanges()));
        }
        return new TurnTrace(board.Turn, upkeep, moves);
    }

    private static void Upkeep(Board board)
    {
        foreach (Seat seat in board.Seats)
        {
            Region[] held = [.. board.Regions.Where(region => region.Allegiance == seat.Id)];
            board.AdjustResource(seat, Resource.Coin, held.Sum(region => region.Prosperity) / 4);
            board.AdjustResource(seat, Resource.Favour, 1 + board.Regions.Sum(region => region.InfluenceOf(seat.Id)) / 3);
            board.AdjustResource(seat, Resource.Force, 1 + held.Sum(region => region.Control) / 5);
        }
        foreach (Region region in board.Regions)
        {
            if (region.Order <= UnrestOrder) board.AdjustControl(region, -1);
            if (region.Allegiance is null && board.Turn % IndependentRecoveryWeeks == 0 && region.Control < IndependentRecoveryControl)
                board.AdjustControl(region, 1);
        }
    }
}
