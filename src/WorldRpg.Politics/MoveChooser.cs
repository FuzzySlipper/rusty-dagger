namespace WorldRpg.Politics;

/// <summary>A card and its targets, or gathering, as a seat might play it.</summary>
public sealed record MoveOption(string Card, IReadOnlyList<string> Targets, int Score);

/// <summary>
/// The first chooser: greedy, one move deep. It plays each legal move in a trial on the board and scores the
/// resulting prestige: the player's gain counted four times, minus the gains of seats it is not friendly with
/// (twice for a rival, once for a neutral). The seeded chance breaks only exact ties. Intention, with plans that
/// span weeks, replaces it in #11060.
/// </summary>
public static class MoveChooser
{
    /// <summary>
    /// Every legal play from <paramref name="seat"/>'s hand that would change something, then gathering, best first;
    /// moves of equal score keep hand, target and that order. A slow card is weighed as if it arrived now. Gathering
    /// is always among them, so a seat always has a move.
    /// </summary>
    public static IReadOnlyList<MoveOption> Options(Board board, Seat seat)
    {
        List<MoveOption> options = [];
        Before before = new(board, seat);
        foreach (Card card in board.Deck(seat).Hand.Distinct())
        {
            foreach (IReadOnlyList<string> targets in card.Candidates(board, seat).ToArray())
            {
                if (!card.Playable(board, seat, targets)) continue;
                IReadOnlyList<Region> regions = card.TargetRegions(board, targets);
                Dictionary<string, int> standing = board.Seats.ToDictionary(other => other.Id, other => Standing(other, regions), StringComparer.Ordinal);
                int? score = board.Trial(() => card.Play(board, seat, targets) ? Score(board, seat, before, regions, standing) : (int?)null);
                if (score is not null) options.Add(new MoveOption(card.Id, targets, score.Value));
            }
        }
        Resource lacking = Gathering.Lacking(board, seat);
        options.Add(new MoveOption(Gathering.Id, [], board.Trial(() =>
        {
            board.AdjustResource(seat, lacking, 1);
            return Score(board, seat, before, [], null);
        })));
        return [.. options.OrderByDescending(option => option.Score)];
    }

    /// <summary>The move <paramref name="seat"/> plays: the best scored, an exact tie settled by the board's chance.</summary>
    public static MoveOption Choose(Board board, Seat seat, IReadOnlyList<MoveOption> options)
    {
        MoveOption[] best = [.. options.TakeWhile(option => option.Score == options[0].Score)];
        return best.Length == 1 ? best[0] : best[board.Random.Next(best.Length)];
    }

    /// <summary>
    /// The trial under way scored for <paramref name="seat"/>: each touched seat's gain in prestige, weighted as the seat
    /// stood with the player before it. A card changes only its target <paramref name="regions"/>, so a seat's standing
    /// elsewhere is unchanged; <paramref name="standing"/> holds each seat's standing in them before the trial.
    /// </summary>
    private static int Score(Board board, Seat seat, Before before, IReadOnlyList<Region> regions, Dictionary<string, int>? standing)
    {
        int score = 0;
        foreach (string id in Touched(board, seat))
        {
            Seat other = board.Seat(id);
            int gain = Bearing(board, other) - before.Bearing[id] + (standing is null ? 0 : Standing(other, regions) - standing[id]);
            score += before.Weight[id] * gain;
        }
        return score;
    }

    private static int Standing(Seat seat, IReadOnlyList<Region> regions)
    {
        int standing = 0;
        foreach (Region region in regions) standing += Standing(seat, region);
        return standing;
    }

    /// <summary>
    /// A seat's prestige, in half points, is its standing in every region and its bearing. Its standing in a region:
    /// 20 if it holds it, with 4 per point of control and prosperity and 2 per point of order there; 6 per influence
    /// piece and per office. A crown's influence leading a contested region's own seat adds its lead times the region's
    /// missing control, so pressing a claim where the crown is ahead is worth something before the annexation it leads
    /// to.
    /// </summary>
    private static int Standing(Seat seat, Region region)
    {
        int influence = region.InfluenceOf(seat.Id);
        int standing = 6 * influence;
        foreach (string holder in region.Offices.Values)
            if (holder == seat.Id) standing += 6;
        if (region.Allegiance == seat.Id)
        {
            standing += 20 + 4 * region.Control + 4 * region.Prosperity + 2 * region.Order;
        }
        else if (seat.Kind == SeatKind.Crown && !region.Capital)
        {
            int lead = influence - (region.Allegiance is null ? 0 : region.InfluenceOf(region.Allegiance));
            if (lead > 0) standing += 2 * lead * (Board.TrackLimit - region.Control);
        }
        return standing;
    }

    /// <summary>
    /// A seat's bearing: 2 per point of relation, and 1 per point of each resource up to 6, beyond which a hoard is worth
    /// nothing more.
    /// </summary>
    private static int Bearing(Board board, Seat seat)
    {
        int bearing = 0;
        foreach (Seat other in board.Seats)
            if (other.Id != seat.Id) bearing += 2 * board.Relation(seat.Id, other.Id);
        foreach (Resource resource in Enum.GetValues<Resource>())
            bearing += Math.Min(seat.Amount(resource), 6);
        return bearing;
    }

    /// <summary>
    /// The seats whose prestige the trial under way can have changed: the player, the seats it changed directly, and
    /// every seat present before or after in a region it changed. Present before and gone now, a seat shows in the
    /// change that removed it. Any other seat's gain is exactly 0, so it is not weighed.
    /// </summary>
    private static SortedSet<string> Touched(Board board, Seat player)
    {
        SortedSet<string> touched = new(StringComparer.Ordinal) { player.Id };
        foreach (BoardChange change in board.TrialChanges)
        {
            string field = change.Field;
            if (field is "coin" or "favour" or "force")
            {
                touched.Add(change.Subject);
            }
            else if (field.StartsWith("relation:", StringComparison.Ordinal))
            {
                touched.Add(change.Subject);
                touched.Add(field["relation:".Length..]);
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

    private static int RivalWeight(int relation) => relation < 0 ? 2 : relation == 0 ? 1 : 0;

    /// <summary>The board as it stood before any trial: each seat's weight to the player and its relations and resources.</summary>
    private sealed class Before(Board board, Seat player)
    {
        public Dictionary<string, int> Weight { get; } = board.Seats.ToDictionary(seat => seat.Id,
            seat => seat == player ? 4 : -RivalWeight(board.Relation(player.Id, seat.Id)), StringComparer.Ordinal);

        public Dictionary<string, int> Bearing { get; } = board.Seats.ToDictionary(seat => seat.Id, seat => MoveChooser.Bearing(board, seat), StringComparer.Ordinal);
    }
}
