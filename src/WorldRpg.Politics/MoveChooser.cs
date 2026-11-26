namespace WorldRpg.Politics;

/// <summary>A card and its targets, or gathering, as a seat might play it.</summary>
public sealed record MoveOption(string Card, IReadOnlyList<string> Targets, int Score);

/// <summary>
/// How a seat chooses its move, one move deep and steered by its holder's persona and its plan (<see cref="Intention"/>).
/// It plays each legal move in a trial on the board and scores it:
/// <list type="bullet">
/// <item>prestige (<see cref="Prestige"/>): the player's gain counted <see cref="SelfWeight"/> times, minus the gains of
/// seats it is not friendly with (twice for a rival, once for a neutral), so resources spent and relations moved
/// count;</item>
/// <item>plan progress: how far the move takes the plan's goal (<see cref="Intention.Measure"/>, or what the target seat
/// loses for a goal against a seat), times <see cref="PlanWeight"/> and the holder's weight for that kind of goal.
/// Gathering towards a card that would further the plan, which the seat cannot pay for yet, earns that card's progress
/// shared over what it still lacks;</item>
/// <item>leaning: <see cref="LeaningWeight"/> per point the holder leans towards the card's family, or away from it;</item>
/// <item>risk: for each seat that would remember being struck (<see cref="Memory"/>), <see cref="RiskWeight"/> per
/// point the holder's risk appetite falls short of <see cref="Persona.RiskLimit"/>.</item>
/// </list>
/// The seeded chance breaks only exact ties.
/// </summary>
public static class MoveChooser
{
    /// <summary>How much the player's own gain counts.</summary>
    public const int SelfWeight = 4;

    public const int PlanWeight = 1;
    public const int LeaningWeight = 2;
    public const int RiskWeight = 6;

    /// <summary>
    /// Every legal play from <paramref name="seat"/>'s hand, and from the war cards if it fights a war, that would change
    /// something, then gathering, then withdrawing
    /// from each of its trade agreements, best first; moves of equal score keep hand, target and that order. A slow card
    /// is weighed as if it arrived now. Gathering is always among them, so a seat always has a move.
    /// </summary>
    public static IReadOnlyList<MoveOption> Options(Board board, Seat seat)
    {
        List<MoveOption> options = [];
        Weighing weighing = new(board, seat);
        IEnumerable<Card> war = board.Wars.Any(war => war.Fights(seat.Id)) ? board.WarCards : [];
        foreach (Card card in board.Deck(seat).Hand.Distinct().Concat(war))
        {
            int leaning = LeaningWeight * weighing.Persona.Leaning(card.Family);
            foreach (IReadOnlyList<string> targets in card.Candidates(board, seat).ToArray())
            {
                if (!card.Playable(board, seat, targets)) continue;
                Prestige.Witness witness = weighing.Witness(card, targets);
                int planBefore = weighing.PlanMeasure(witness);
                int? score = board.Trial(() => card.Play(board, seat, targets) ? weighing.Score(witness, planBefore) + leaning : (int?)null);
                if (score is not null) options.Add(new MoveOption(card.Id, targets, score.Value));
            }
        }
        Resource lacking = Gathering.Lacking(board, seat);
        Prestige.Witness still = new(board, seat, weighing.Before, [], [], Visibility.Public);
        int toward = weighing.Toward(lacking);
        int stillBefore = weighing.PlanMeasure(still);
        options.Add(new MoveOption(Gathering.Id, [], board.Trial(() =>
        {
            board.AdjustResource(seat, lacking, 1);
            return weighing.Score(still, stillBefore) + toward;
        })));
        foreach (Agreement deal in board.Agreements.Where(deal => deal.Binds(seat.Id)).ToArray())
        {
            Prestige.Witness witness = Prestige.Witness.OfWithdrawal(board, seat, weighing.Before, deal);
            int dealBefore = weighing.PlanMeasure(witness);
            options.Add(new MoveOption(Trade.WithdrawId, [deal.Id], board.Trial(() =>
            {
                Trade.Withdraw(board, seat, deal);
                return weighing.Score(witness, dealBefore);
            })));
        }
        return [.. options.OrderByDescending(option => option.Score)];
    }

    /// <summary>The move <paramref name="seat"/> plays: the best scored, an exact tie settled by the board's chance.</summary>
    public static MoveOption Choose(Board board, Seat seat, IReadOnlyList<MoveOption> options)
    {
        MoveOption[] best = [.. options.TakeWhile(option => option.Score == options[0].Score)];
        return best.Length == 1 ? best[0] : best[board.Random.Next(best.Length)];
    }

    /// <summary>How much a seat's gain counts against the player, by their relation: twice for a rival, once for a neutral, not for a friend.</summary>
    internal static int RivalWeight(int relation) => relation < 0 ? 2 : relation == 0 ? 1 : 0;

    /// <summary>The board as it stood before any trial, as <see cref="Player"/> weighs its moves.</summary>
    private sealed class Weighing(Board board, Seat player)
    {
        public Seat Player { get; } = player;

        public Persona Persona { get; } = board.Persona(player);

        public Prestige.Before Before { get; } = new(board);

        /// <summary>Each seat's weight to the player.</summary>
        public Dictionary<string, int> Weight { get; } = board.Seats.ToDictionary(seat => seat.Id,
            seat => seat == player ? SelfWeight : -RivalWeight(board.Relation(player.Id, seat.Id)), StringComparer.Ordinal);

        public Prestige.Witness Witness(Card card, IReadOnlyList<string> targets) => Prestige.Witness.OfCard(board, Player, Before, card, targets);

        /// <summary>The plan's measure over the witnessed move's regions before it is tried.</summary>
        public int PlanMeasure(Prestige.Witness witness) => Player.Plan is { } plan ? Intention.Measure(board, Player, plan.Goal, witness.Regions) : 0;

        /// <summary>The trial under way scored for the player: prestige, plan progress and risk.</summary>
        public int Score(Prestige.Witness witness, int planBefore)
        {
            SortedDictionary<string, int> gains = witness.Gains(board, [.. board.TrialChanges]);
            int score = 0;
            foreach ((string id, int gain) in gains)
            {
                score += Weight[id] * gain;
                if (gain < 0 && id != Player.Id && Memory.Struck(witness, board.Seat(id))) score -= RiskWeight * (Persona.RiskLimit - Persona.Risk);
            }
            return score + Progress(witness, planBefore, gains);
        }

        /// <summary>
        /// What gathering <paramref name="lacking"/> earns towards the plan: the best progress a card the player is saving
        /// for would make, shared over the resources that card still lacks, if it lacks <paramref name="lacking"/>.
        /// </summary>
        public int Toward(Resource lacking)
        {
            if (Player.Plan is null) return 0;
            int best = 0;
            foreach (Card card in Gathering.Saving(board, Player).Distinct())
            {
                if (card.Cost(lacking) <= Player.Amount(lacking)) continue;
                int shortfall = Enum.GetValues<Resource>().Sum(resource => Math.Max(0, card.Cost(resource) - Player.Amount(resource)));
                foreach (IReadOnlyList<string> targets in card.Candidates(board, Player).ToArray())
                {
                    if (!card.Fits(board, Player, targets)) continue;
                    Prestige.Witness witness = Witness(card, targets);
                    int planBefore = PlanMeasure(witness);
                    int progress = board.Trial(() =>
                        card.Apply(board, Player, targets) ? Progress(witness, planBefore, witness.Gains(board, [.. board.TrialChanges])) : 0);
                    best = Math.Max(best, progress / shortfall);
                }
            }
            return best;
        }

        private int Progress(Prestige.Witness witness, int planBefore, SortedDictionary<string, int> gains)
        {
            if (Player.Plan is not { } plan) return 0;
            int progress = plan.Goal.Kind is GoalKind.Counter or GoalKind.Retaliate
                ? -gains.GetValueOrDefault(plan.Goal.Target)
                : Intention.Measure(board, Player, plan.Goal, witness.Regions) - planBefore;
            return PlanWeight * Persona.Weight(plan.Goal.Kind) * progress;
        }
    }
}
