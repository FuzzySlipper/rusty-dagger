namespace WorldRpg.Politics;

/// <summary>A condition on a region, as the world reads it: where, what, how strong, who laid it and for which weeks.</summary>
public sealed record ConditionFact(string Region, int CatalogRegion, ConditionKind Kind, string? Detail, int Magnitude, string? Source, int Start, int End);

/// <summary>A region's standing this week: who holds it, its tracks, whether it is fed, and who holds its offices.</summary>
public sealed record RegionFact(
    string Region,
    int CatalogRegion,
    string? Allegiance,
    int Control,
    int Prosperity,
    int Order,
    RegionSupply Supply,
    IReadOnlyDictionary<Office, string> Offices);

/// <summary>A war a seat fights: its id, the enemy principal, its aim, and the principal whose side the seat is on.</summary>
public sealed record WarFact(string War, string Enemy, WarAim Aim, string Side);

/// <summary>
/// A seat's standing this week: its holder (and under a regency, whose persona plays it), its relations with every seat it is not neutral with, the wars it fights,
/// the crowns it feuds with, the truces binding it (other seat to the week the truce ends), whether it leads the table,
/// whether its reserves are cut off, and the main quest's boons it holds.
/// </summary>
public sealed record SeatFact(
    string Seat,
    string Holder,
    string? PlaysAs,
    IReadOnlyDictionary<string, int> Relations,
    IReadOnlyList<WarFact> Wars,
    IReadOnlyList<string> Feuds,
    IReadOnlyDictionary<string, int> Truces,
    bool Leader,
    bool ReservesCut,
    IReadOnlyList<string> Boons);

/// <summary>How a move stands this week, as the world reads it.</summary>
public enum MoveStatus
{
    /// <summary>It took effect.</summary>
    Resolved,

    /// <summary>It was played or arrived and changed nothing, or no longer fitted its targets.</summary>
    Failed,

    /// <summary>A slow move under way, arriving at <see cref="MoveRecord.PendingUntil"/>.</summary>
    Pending,

    /// <summary>A slow move the world intercepted on its way.</summary>
    Intercepted,
}

/// <summary>
/// A move the world can tell of: its id (the turn and seat it was played on), the turn it was played, the true seat,
/// the card and targets, the region it set out from, who may see it, how it stands, the key its headline is looked up
/// by (<c>politics.move.&lt;card&gt;</c>), the week a slow move arrives and, for a challenge for a local office, the key
/// of the office's piece in the world (<c>office:&lt;region&gt;:&lt;office&gt;</c>). The world decides cover stories and
/// who counts as an insider.
/// </summary>
public sealed record MoveRecord(
    string MoveId,
    int Turn,
    string Seat,
    string Card,
    IReadOnlyList<string> Targets,
    string? OriginRegion,
    Visibility Visibility,
    MoveStatus Outcome,
    string HeadlineKey,
    int? PendingUntil,
    string? OfficeKey);

/// <summary>
/// What the board publishes after each Sunday for the world: every condition in force, every region's and seat's
/// standing, the moves made, arrived or intercepted this week, and the arcs the chronicle named. Courier friction is the
/// world's: it reads each condition's kind and region.
/// </summary>
public sealed record BoardOutputs(
    int Turn,
    int Year,
    int Day,
    IReadOnlyList<ConditionFact> Conditions,
    IReadOnlyList<RegionFact> Regions,
    IReadOnlyList<SeatFact> Seats,
    IReadOnlyList<MoveRecord> Moves,
    IReadOnlyList<ArcRecord> Chronicle)
{
    /// <summary>The outputs of the week <paramref name="trace"/> recorded, read from <paramref name="board"/> as that week left it.</summary>
    public static BoardOutputs Of(Board board, CardSet cards, TurnTrace trace)
    {
        Dictionary<string, Card> byId = cards.Cards.ToDictionary(card => card.Id, StringComparer.Ordinal);
        List<MoveRecord> moves = [];
        foreach (PendingMove move in board.InterceptedThisWeek)
            moves.Add(Record(move.Id, move.Played, move.Seat, move.Card, move.Card.Id, move.Targets, move.Origin, move.Card.Visibility, MoveStatus.Intercepted, null));
        foreach (ArrivalTrace arrival in trace.Arrivals)
            moves.Add(Record(arrival.Id, Played(arrival.Id), arrival.Seat, byId.GetValueOrDefault(arrival.Card), arrival.Card, arrival.Targets, arrival.Origin,
                arrival.Visibility, arrival.Outcome == MoveOutcome.Resolved ? MoveStatus.Resolved : MoveStatus.Failed, null));
        foreach (MoveTrace move in trace.Moves.Where(move => move.Card != Gathering.Id))
        {
            MoveStatus status = move.Arrives is not null ? MoveStatus.Pending : move.Changes.Count > 0 ? MoveStatus.Resolved : MoveStatus.Failed;
            moves.Add(Record(move.Id, trace.Turn, move.Seat, byId.GetValueOrDefault(move.Card), move.Card, move.Targets, move.Origin, move.Visibility, status, move.Arrives));
        }
        return new BoardOutputs(trace.Turn, trace.Year, trace.Day,
            [.. board.Regions.SelectMany(region => region.Conditions.Select(condition =>
                new ConditionFact(region.Id, region.CatalogRegion, condition.Kind, condition.Detail, condition.Magnitude, condition.Source, condition.Start, condition.End)))],
            [.. board.Regions.Select(region => new RegionFact(region.Id, region.CatalogRegion, region.Allegiance, region.Control, region.Prosperity,
                region.Order, region.Supply, new SortedDictionary<Office, string>(region.Offices)))],
            [.. board.Seats.Select(seat => Fact(board, seat))],
            moves,
            trace.Chronicle);
    }

    private static SeatFact Fact(Board board, Seat seat) => new(
        seat.Id,
        seat.Holder,
        seat.PlaysAs,
        new SortedDictionary<string, int>(board.Seats.Where(other => other != seat && board.Relation(seat.Id, other.Id) != 0)
            .ToDictionary(other => other.Id, other => board.Relation(seat.Id, other.Id)), StringComparer.Ordinal),
        [.. board.Wars.Where(war => war.Fights(seat.Id)).Select(war => new WarFact(war.Id, war.Enemy(war.Side(seat.Id)!), war.Aim, war.Side(seat.Id)!))],
        [.. board.Feuds.Where(feud => feud.Principal(seat.Id)).Select(feud => feud.A == seat.Id ? feud.B : feud.A)],
        new SortedDictionary<string, int>(board.Truces.Where(truce => truce.A == seat.Id || truce.B == seat.Id)
            .ToDictionary(truce => truce.A == seat.Id ? truce.B : truce.A, truce => truce.Until), StringComparer.Ordinal),
        board.Leader == seat.Id,
        Balance.ReservesCut(seat),
        [.. seat.Boons]);

    private static MoveRecord Record(string id, int turn, string seat, Card? card, string cardId, IReadOnlyList<string> targets, string? origin,
        Visibility visibility, MoveStatus status, int? pendingUntil)
    {
        string? office = card?.Effects.FirstOrDefault(effect => effect.Kind == CardEffectKind.ChallengeOffice)?.Office is { } challenged
            ? $"office:{targets[0]}:{challenged.ToString().ToLowerInvariant()}"
            : null;
        return new MoveRecord(id, turn, seat, cardId, targets, origin, visibility, status, $"politics.move.{cardId}", pendingUntil, office);
    }

    /// <summary>The turn a move was played on, from its id (<c>turn-seat</c>).</summary>
    private static int Played(string moveId) => int.Parse(moveId[..moveId.IndexOf('-', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture);
}
