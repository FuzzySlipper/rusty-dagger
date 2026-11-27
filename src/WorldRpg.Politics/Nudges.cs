using System.Text.Json.Serialization;

namespace WorldRpg.Politics;

/// <summary>
/// Something the world did that bears on the board, queued with <see cref="Board.Queue"/> and applied at the start of the
/// next Sunday, before upkeep (<see cref="Nudges"/>). The world decides what its events mean; the board only applies
/// these records.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(QuestOutcome), "questOutcome")]
[JsonDerivedType(typeof(HolderChanged), "holderChanged")]
[JsonDerivedType(typeof(OfficeHolderRemoved), "officeHolderRemoved")]
[JsonDerivedType(typeof(OfficeTurned), "officeTurned")]
[JsonDerivedType(typeof(MoveDelivered), "moveDelivered")]
[JsonDerivedType(typeof(MoveDelayed), "moveDelayed")]
[JsonDerivedType(typeof(MoveIntercepted), "moveIntercepted")]
[JsonDerivedType(typeof(LairCleared), "lairCleared")]
[JsonDerivedType(typeof(ConditionEased), "conditionEased")]
[JsonDerivedType(typeof(MainQuestBoon), "mainQuestBoon")]
public abstract record WorldNudge;

/// <summary>
/// A quest ended in <paramref name="Seat"/>'s favour or against it: <paramref name="Effects"/>, in the cards'
/// vocabulary, applied for the seat on <paramref name="Targets"/>: nothing, a region, or a region and another seat.
/// </summary>
public sealed record QuestOutcome(string Seat, IReadOnlyList<string> Targets, IReadOnlyList<CardEffect> Effects) : WorldNudge;

/// <summary>
/// The cast individual holding <paramref name="Seat"/> has changed to <paramref name="Holder"/>. A holder with no persona
/// keeps the seat playing as its predecessor did, as a regency (<see cref="Board.SetHolder"/>).
/// </summary>
public sealed record HolderChanged(string Seat, string Holder) : WorldNudge;

/// <summary>The seat's holder of <paramref name="Office"/> in <paramref name="Region"/> is gone: local people hold it again.</summary>
public sealed record OfficeHolderRemoved(string Region, Office Office) : WorldNudge;

/// <summary>
/// The incumbent of <paramref name="Region"/>'s <paramref name="Office"/> was won over for <paramref name="Seat"/>: the seat
/// takes the office covertly and the holder stays in post, as a secret challenge takes one, without the contest. Stale
/// where the seat may not take it (<see cref="Card.MayTakeOffice"/>).
/// </summary>
public sealed record OfficeTurned(string Seat, string Region, Office Office) : WorldNudge;

/// <summary>The slow move <paramref name="MoveId"/> arrives this Sunday, sooner than it would have.</summary>
public sealed record MoveDelivered(string MoveId) : WorldNudge;

/// <summary>The slow move <paramref name="MoveId"/> arrives <paramref name="Weeks"/> weeks later than it would have.</summary>
public sealed record MoveDelayed(string MoveId, int Weeks) : WorldNudge;

/// <summary>The slow move <paramref name="MoveId"/> was intercepted on its way: it never arrives.</summary>
public sealed record MoveIntercepted(string MoveId) : WorldNudge;

/// <summary>A lair in <paramref name="Region"/> was cleared: its monster surge lifts, of <paramref name="Family"/> or of every family.</summary>
public sealed record LairCleared(string Region, string? Family = null) : WorldNudge;

/// <summary>
/// The player wore down a condition in <paramref name="Region"/>: its magnitude falls by <paramref name="Amount"/>, and
/// it lifts at 0.
/// </summary>
public sealed record ConditionEased(string Region, ConditionKind Condition, int Amount, string? Detail = null) : WorldNudge;

/// <summary>
/// The main quest has granted <paramref name="Seat"/> its boon, such as the Numidium: <paramref name="Boon"/> is
/// recorded on the seat and <paramref name="Effects"/> are applied as a quest outcome's are. The main quest never ends
/// the board; a boon is what upsets its balance.
/// </summary>
public sealed record MainQuestBoon(string Seat, string Boon, IReadOnlyList<string> Targets, IReadOnlyList<CardEffect> Effects) : WorldNudge;

/// <summary>A nudge from the world scheduled for the Sunday of <paramref name="Turn"/>: one line of a CLI nudges file.</summary>
public sealed record ScheduledNudge(int Turn, WorldNudge Nudge)
{
    /// <summary>The nudges in a file of one JSON object per line; a malformed line is refused, naming its line.</summary>
    public static IReadOnlyList<ScheduledNudge> ReadLines(string path)
    {
        List<ScheduledNudge> nudges = [];
        string[] lines = File.ReadAllLines(path);
        for (int index = 0; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            try
            {
                nudges.Add(System.Text.Json.JsonSerializer.Deserialize(lines[index], PoliticsJson.Default.ScheduledNudge)
                    ?? throw new BoardSetupException(path, [$"line {index + 1} holds no nudge"]));
            }
            catch (System.Text.Json.JsonException exception)
            {
                throw new BoardSetupException(path, [$"line {index + 1}: {exception.Message}"]);
            }
        }
        return nudges;
    }
}

/// <summary>
/// How a nudge was taken: applied, or stale because the move it named had already arrived or been taken off, or the office
/// it turned could no longer be taken.
/// </summary>
public enum NudgeOutcome
{
    Applied,
    Stale,
}

/// <summary>A nudge applied at the start of a Sunday: what it was, how it was taken and what it changed.</summary>
public sealed record NudgeTrace(WorldNudge Nudge, NudgeOutcome Outcome, IReadOnlyList<BoardChange> Changes);

/// <summary>Checks and applies the world's nudges.</summary>
public static class Nudges
{
    /// <summary>How many weeks a succession crisis runs after a holder changes, before the chronicle calls it settled.</summary>
    public const int SuccessionWeeks = 13;

    /// <summary>What is wrong with <paramref name="nudge"/> on <paramref name="board"/>, or null if it can be applied.</summary>
    public static string? Problem(Board board, WorldNudge nudge) => nudge switch
    {
        QuestOutcome quest => SeatProblem(board, quest.Seat) ?? EffectsProblem(board, quest.Seat, quest.Targets, quest.Effects),
        MainQuestBoon boon => SeatProblem(board, boon.Seat) ?? (string.IsNullOrWhiteSpace(boon.Boon) ? "names no boon" : null)
            ?? EffectsProblem(board, boon.Seat, boon.Targets, boon.Effects),
        HolderChanged change => SeatProblem(board, change.Seat) ?? (string.IsNullOrWhiteSpace(change.Holder) ? "names no holder" : null),
        OfficeHolderRemoved office => RegionProblem(board, office.Region),
        OfficeTurned turned => SeatProblem(board, turned.Seat) ?? RegionProblem(board, turned.Region),
        MoveDelivered or MoveIntercepted => null,
        MoveDelayed delayed => delayed.Weeks < 1 ? $"delays move '{delayed.MoveId}' by {delayed.Weeks} weeks" : null,
        LairCleared lair => RegionProblem(board, lair.Region),
        ConditionEased eased => RegionProblem(board, eased.Region) ?? (eased.Amount < 1 ? $"eases a condition by {eased.Amount}" : null),
        _ => $"is an unknown nudge {nudge.GetType().Name}",
    };

    /// <summary>Applies <paramref name="nudge"/>; returns whether it applied, or was stale.</summary>
    public static NudgeOutcome Apply(Board board, WorldNudge nudge)
    {
        switch (nudge)
        {
            case QuestOutcome quest:
                Outcome(quest.Targets, quest.Effects).Apply(board, board.Seat(quest.Seat), quest.Targets);
                return NudgeOutcome.Applied;
            case MainQuestBoon boon:
                board.GrantBoon(board.Seat(boon.Seat), boon.Boon);
                Outcome(boon.Targets, boon.Effects).Apply(board, board.Seat(boon.Seat), boon.Targets);
                return NudgeOutcome.Applied;
            case HolderChanged change:
                board.SetHolder(board.Seat(change.Seat), change.Holder);
                // A succession already under way, such as a vacancy whose successor is now named, runs on as one.
                if (!board.Arcs.Any(arc => arc.Kind == ArcKind.Succession && arc.Subjects[0] == change.Seat))
                    board.OpenSuccession(board.Seat(change.Seat), board.Turn + SuccessionWeeks);
                return NudgeOutcome.Applied;
            case OfficeHolderRemoved office:
                board.SetOffice(board.Region(office.Region), office.Office, null);
                return NudgeOutcome.Applied;
            case OfficeTurned turned:
                Seat taker = board.Seat(turned.Seat);
                Region held = board.Region(turned.Region);
                if (!Card.MayTakeOffice(board, taker, held, turned.Office)) return NudgeOutcome.Stale;
                Card.TakeOffice(board, taker, held, turned.Office, covert: true);
                return NudgeOutcome.Applied;
            case MoveDelivered delivered:
                return board.Reschedule(delivered.MoveId, _ => board.Turn) ? NudgeOutcome.Applied : NudgeOutcome.Stale;
            case MoveDelayed delayed:
                return board.Reschedule(delayed.MoveId, arrives => arrives + delayed.Weeks) ? NudgeOutcome.Applied : NudgeOutcome.Stale;
            case MoveIntercepted intercepted:
                return board.Intercept(intercepted.MoveId) ? NudgeOutcome.Applied : NudgeOutcome.Stale;
            case LairCleared lair:
                Region region = board.Region(lair.Region);
                foreach (RegionCondition surge in region.Conditions.Where(held => held.Kind == ConditionKind.MonsterSurge && (lair.Family is null || held.Detail == lair.Family)).ToArray())
                    board.RemoveCondition(region, surge.Kind, surge.Detail);
                return NudgeOutcome.Applied;
            case ConditionEased eased:
                board.EaseCondition(board.Region(eased.Region), eased.Condition, eased.Detail, eased.Amount);
                return NudgeOutcome.Applied;
            default:
                throw new ArgumentOutOfRangeException(nameof(nudge), nudge, null);
        }
    }

    /// <summary>
    /// The card a quest outcome or boon is applied as: no cost, and a target inferred from its targets, nothing, a region,
    /// or a region and another seat.
    /// </summary>
    private static Card Outcome(IReadOnlyList<string> targets, IReadOnlyList<CardEffect> effects) =>
        new("world-outcome", "The world's outcome", CardFamily.Court, 0, 0, 0, Target(targets), Visibility.Public, effects);

    private static CardTarget Target(IReadOnlyList<string> targets) => targets.Count switch
    {
        0 => CardTarget.None,
        1 => CardTarget.ReachableRegion,
        _ => CardTarget.RegionRival,
    };

    private static string? EffectsProblem(Board board, string seat, IReadOnlyList<string> targets, IReadOnlyList<CardEffect> effects)
    {
        if (effects.Count == 0) return "changes nothing";
        if (targets.Count > 2) return $"names {targets.Count} targets";
        if (targets.Count >= 1 && !board.HasRegion(targets[0])) return $"targets unknown region '{targets[0]}'";
        if (targets.Count == 2 && (!board.HasSeat(targets[1]) || targets[1] == seat)) return $"targets '{targets[1]}', which is no other seat";
        Card outcome = Outcome(targets, effects);
        return effects.Select(effect => CardSet.EffectProblem(outcome, effect)).FirstOrDefault(problem => problem is not null);
    }

    private static string? SeatProblem(Board board, string seat) => board.HasSeat(seat) ? null : $"names unknown seat '{seat}'";

    private static string? RegionProblem(Board board, string region) => board.HasRegion(region) ? null : $"names unknown region '{region}'";
}
