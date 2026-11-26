namespace WorldRpg.Politics;

/// <summary>What a standing goal or a plan aims at. Its target is a region, an office or a seat, as each kind says.</summary>
public enum GoalKind
{
    /// <summary>A crown takes the target region: it builds influence there, breaks its control and annexes it.</summary>
    Take,

    /// <summary>A crown keeps the target region; a plan only while the region is threatened (<see cref="Intention.Threatened"/>).</summary>
    Hold,

    /// <summary>The seat takes one more of the target office (Watch, Factor, Temple or Hall) anywhere on the board.</summary>
    Office,

    /// <summary>The seat builds a footing in the target region: <see cref="Intention.FootingPieces"/> influence and offices.</summary>
    Footing,

    /// <summary>The seat warms its relation with the target seat to <see cref="Intention.RecognisedRelation"/>.</summary>
    Recognition,

    /// <summary>The seat weakens the target seat wherever it can.</summary>
    Counter,

    /// <summary>The seat strikes back at the target seat, which struck it (<see cref="Memory"/>); never authored.</summary>
    Retaliate,
}

/// <summary>A standing goal or a plan's aim: its kind and its target's id (a region, an office's name or a seat).</summary>
public sealed record Goal(GoalKind Kind, string Target)
{
    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()} {Target.ToLowerInvariant()}";
}

/// <summary>
/// A holder's character at the table: how much each kind of goal matters to it (0 to <see cref="WeightLimit"/>), how it
/// leans towards or away from each card family (-<see cref="LeaningLimit"/> to <see cref="LeaningLimit"/>), and its
/// appetite for risk, from 0 (wary of every reprisal) to <see cref="RiskLimit"/> (heedless of them). The seat plays;
/// its holder's persona weighs the moves.
/// </summary>
public sealed record Persona(IReadOnlyDictionary<GoalKind, int> Goals, IReadOnlyDictionary<CardFamily, int>? Families = null, int Risk = 0)
{
    public const int WeightLimit = 3;
    public const int LeaningLimit = 2;
    public const int RiskLimit = 3;

    public int Weight(GoalKind kind) => Goals.GetValueOrDefault(kind);

    public int Leaning(CardFamily family) => Families?.GetValueOrDefault(family) ?? 0;
}

/// <summary>
/// A seat's committed plan: its goal, the week it took it up, and the goal's measure then (<see cref="Intention.Measure"/>),
/// against which taking one more office counts as done.
/// </summary>
public sealed record Plan(Goal Goal, int Since, int Baseline)
{
    public override string ToString() => Goal.ToString();
}

/// <summary>A seat's grudge against another that struck it, held until the end of week <paramref name="Until"/>.</summary>
public sealed record Grudge(string Against, int Until);

/// <summary>
/// How a seat keeps a plan. Before it moves each week, a seat reviews its plan: it keeps the plan until it is no longer
/// possible or done, or a threat that matters as much to the holder pre-empts it and becomes the plan, unless the plan
/// is itself a threat. A threat is a grudge (<see cref="GoalKind.Retaliate"/>), done once the grudge is settled or
/// let go, or a region the seat holds and is threatened in (<see cref="GoalKind.Hold"/>). A seat with no plan takes up
/// the goal that matters most to its holder among its grudges and its standing goals that are possible and not yet
/// met, the first listed winning a tie. Plans steer choice through <see cref="MoveChooser"/>.
/// </summary>
public static class Intention
{
    public const int RecognisedRelation = 2;
    public const int FootingPieces = 3;

    /// <summary>A held region is threatened at this control or below.</summary>
    public const int ThreatControl = 1;

    public static void Review(Board board, Seat seat)
    {
        Persona persona = board.Persona(seat);
        Goal? next = null;
        if (seat.Plan is { } plan)
        {
            string? ended = !Possible(board, seat, plan.Goal) ? "impossible"
                : Done(board, seat, plan) ? "done"
                : null;
            if (ended is null && !IsThreat(board, seat, plan.Goal))
            {
                next = Threats(board, seat).FirstOrDefault(threat => persona.Weight(threat.Kind) >= persona.Weight(plan.Goal.Kind));
                if (next is not null) ended = $"pre-empted by {next}";
            }
            if (ended is not null) board.EndPlan(seat, ended);
        }
        if (seat.Plan is null && (next ?? Choose(board, seat, persona)) is { } goal)
            board.SetPlan(seat, new Plan(goal, board.Turn, Measure(board, seat, goal, board.Regions)));
    }

    /// <summary>Whether <paramref name="seat"/> holds <paramref name="region"/> and a rival crown is closing on it, or its control is all but broken.</summary>
    public static bool Threatened(Board board, Seat seat, Region region)
    {
        if (region.Allegiance != seat.Id || region.Capital) return false;
        int own = region.InfluenceOf(seat.Id);
        return region.Control <= ThreatControl
            || region.Influence.Any(entry => entry.Key != seat.Id && entry.Value >= own && board.Seat(entry.Key).Kind == SeatKind.Crown);
    }

    /// <summary>
    /// How far <paramref name="seat"/> stands towards <paramref name="goal"/>, in the half points prestige is counted in
    /// (4 per piece, office or relation point). Offices are counted in <paramref name="regions"/> only, so a trial can
    /// weigh the regions it changed; a goal against a seat has no measure of its own and is weighed by what the seat
    /// loses (<see cref="MoveChooser"/>).
    /// </summary>
    public static int Measure(Board board, Seat seat, Goal goal, IReadOnlyList<Region> regions)
    {
        switch (goal.Kind)
        {
            case GoalKind.Take:
            {
                Region region = board.Region(goal.Target);
                if (region.Allegiance == seat.Id) return 20 + 4 * Board.InfluenceLimit;
                int holder = region.Allegiance is null ? 0 : region.InfluenceOf(region.Allegiance);
                return 4 * region.InfluenceOf(seat.Id) - 2 * holder - 3 * region.Control;
            }
            case GoalKind.Hold:
            {
                Region region = board.Region(goal.Target);
                if (region.Allegiance != seat.Id) return 0;
                int rival = region.Influence.Where(entry => entry.Key != seat.Id && board.Seat(entry.Key).Kind == SeatKind.Crown)
                    .Select(entry => entry.Value).DefaultIfEmpty().Max();
                return 20 + 3 * region.Control + 2 * region.Order + 4 * region.InfluenceOf(seat.Id) - 4 * rival;
            }
            case GoalKind.Office:
            {
                Office office = Enum.Parse<Office>(goal.Target);
                return 4 * regions.Count(region => region.Offices.GetValueOrDefault(office) == seat.Id);
            }
            case GoalKind.Footing:
                return 4 * Footing(board.Region(goal.Target), seat);
            case GoalKind.Recognition:
                return 4 * board.Relation(seat.Id, goal.Target);
            default:
                return 0;
        }
    }

    private static bool Done(Board board, Seat seat, Plan plan) => plan.Goal.Kind switch
    {
        GoalKind.Office => Measure(board, seat, plan.Goal, board.Regions) > plan.Baseline,
        GoalKind.Retaliate => !seat.Grudges.Any(grudge => grudge.Against == plan.Goal.Target),
        _ => Met(board, seat, plan.Goal),
    };

    /// <summary>Whether a standing goal is met for now; an office or a counter is never met, only pursued again.</summary>
    private static bool Met(Board board, Seat seat, Goal goal) => goal.Kind switch
    {
        GoalKind.Take => board.Region(goal.Target).Allegiance == seat.Id,
        GoalKind.Hold => !Threatened(board, seat, board.Region(goal.Target)),
        GoalKind.Footing => Footing(board.Region(goal.Target), seat) >= FootingPieces,
        GoalKind.Recognition => board.Relation(seat.Id, goal.Target) >= RecognisedRelation,
        _ => false,
    };

    private static bool Possible(Board board, Seat seat, Goal goal) => goal.Kind switch
    {
        GoalKind.Take => seat.Kind == SeatKind.Crown && !board.Region(goal.Target).Capital && board.Reaches(seat, board.Region(goal.Target)),
        GoalKind.Hold => board.Region(goal.Target).Allegiance == seat.Id,
        GoalKind.Footing => board.Reaches(seat, board.Region(goal.Target)),
        _ => true,
    };

    private static bool IsThreat(Board board, Seat seat, Goal goal) =>
        goal.Kind == GoalKind.Retaliate || (goal.Kind == GoalKind.Hold && Threatened(board, seat, board.Region(goal.Target)));

    /// <summary>The seat's threats, most pressing first: its grudges in the order they were opened, then its threatened holdings.</summary>
    private static IEnumerable<Goal> Threats(Board board, Seat seat) =>
        Candidates(board, seat).Where(goal => IsThreat(board, seat, goal)).OrderByDescending(goal => board.Persona(seat).Weight(goal.Kind));

    private static IEnumerable<Goal> Candidates(Board board, Seat seat) =>
        seat.Grudges.Select(grudge => new Goal(GoalKind.Retaliate, grudge.Against))
            .Concat(seat.Agenda.Where(goal => Possible(board, seat, goal) && !Met(board, seat, goal)));

    private static Goal? Choose(Board board, Seat seat, Persona persona)
    {
        Goal[] open = [.. Candidates(board, seat).Where(goal => persona.Weight(goal.Kind) > 0)];
        return open.Length == 0 ? null : open.MaxBy(goal => persona.Weight(goal.Kind));
    }

    private static int Footing(Region region, Seat seat) =>
        region.InfluenceOf(seat.Id) + region.Offices.Values.Count(holder => holder == seat.Id);
}
