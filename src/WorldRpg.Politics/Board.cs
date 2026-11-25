namespace WorldRpg.Politics;

/// <summary>
/// The board in play: seats with their resources, regions with their tracks and influence, and the relations
/// between seats. Every change goes through a method here, which clamps it to its range and records it as a
/// <see cref="BoardChange"/> for the trace.
/// </summary>
public sealed class Board
{
    public const int TrackLimit = 5;
    public const int InfluenceLimit = 3;
    public const int ResourceLimit = 12;
    public const int RelationLimit = 3;

    private readonly Dictionary<string, Seat> seatsById;
    private readonly Dictionary<string, Region> regionsById;
    private readonly Dictionary<(string, string), int> relations;
    private readonly List<BoardChange> changes = [];

    private Board(IReadOnlyList<Seat> seats, IReadOnlyList<Region> regions, Dictionary<(string, string), int> relations, BoardRandom random, int turn)
    {
        Seats = seats;
        Regions = regions;
        seatsById = seats.ToDictionary(seat => seat.Id, StringComparer.Ordinal);
        regionsById = regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        this.relations = relations;
        Random = random;
        Turn = turn;
    }

    /// <summary>Seats in table order.</summary>
    public IReadOnlyList<Seat> Seats { get; }

    /// <summary>Regions in board order.</summary>
    public IReadOnlyList<Region> Regions { get; }

    public BoardRandom Random { get; }

    /// <summary>How many weekly turns have been played.</summary>
    public int Turn { get; internal set; }

    /// <summary>A board at its starting position, played from <paramref name="seed"/> or the setup's canon seed.</summary>
    public static Board Create(BoardSetup setup, ulong? seed = null)
    {
        IReadOnlyList<string> problems = setup.Problems();
        if (problems.Count > 0) throw new BoardSetupException("board setup", problems);
        Seat[] seats = [.. setup.Seats.Select(seat => new Seat(seat.Id, seat.Name, seat.Holder, seat.Home)
            { Coin = seat.Coin, Favour = seat.Favour, Force = seat.Force })];
        Region[] regions = [.. setup.Regions.Select(region => new Region(region.Id, region.Name, region.CatalogRegion, region.Capital, [.. region.Borders])
        {
            Allegiance = region.Allegiance,
            Control = region.Control,
            Prosperity = region.Prosperity,
            Order = region.Order,
            Influence = new SortedDictionary<string, int>(region.Influence.Where(entry => entry.Value > 0)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        })];
        Dictionary<(string, string), int> relations = setup.Relations.ToDictionary(relation => Pair(relation.A, relation.B), relation => relation.Value);
        return new Board(seats, regions, relations, new BoardRandom(seed ?? setup.Seed), 0);
    }

    public Seat Seat(string id) => seatsById[id];

    public Region Region(string id) => regionsById[id];

    public int Relation(string a, string b) => relations.GetValueOrDefault(Pair(a, b));

    /// <summary>Whether <paramref name="seat"/> holds <paramref name="region"/> or a region bordering it.</summary>
    public bool Reaches(Seat seat, Region region) =>
        region.Allegiance == seat.Id || region.Borders.Any(border => regionsById[border].Allegiance == seat.Id);

    /// <summary>An independent copy for weighing a move; it shares nothing with this board, its chance included.</summary>
    public Board Clone() => new(
        [.. Seats.Select(seat => seat with { })],
        [.. Regions.Select(region => region with { Influence = new SortedDictionary<string, int>(region.Influence, StringComparer.Ordinal) })],
        new Dictionary<(string, string), int>(relations),
        new BoardRandom(Random.State),
        Turn);

    /// <summary>How many changes are recorded and not yet taken.</summary>
    public int ChangeCount => changes.Count;

    /// <summary>The changes recorded since the last call, in the order they happened.</summary>
    public IReadOnlyList<BoardChange> TakeChanges()
    {
        BoardChange[] taken = [.. changes];
        changes.Clear();
        return taken;
    }

    public void AdjustControl(Region region, int delta) => region.Control = Track(region.Id, "control", region.Control, delta);

    public void AdjustProsperity(Region region, int delta) => region.Prosperity = Track(region.Id, "prosperity", region.Prosperity, delta);

    public void AdjustOrder(Region region, int delta) => region.Order = Track(region.Id, "order", region.Order, delta);

    public void AdjustInfluence(Region region, Seat seat, int delta)
    {
        int before = region.InfluenceOf(seat.Id);
        int after = Math.Clamp(before + delta, 0, InfluenceLimit);
        if (after == before) return;
        if (after == 0) region.Influence.Remove(seat.Id);
        else region.Influence[seat.Id] = after;
        Record(region.Id, $"influence:{seat.Id}", before, after);
    }

    public void SetAllegiance(Region region, Seat? seat)
    {
        if (region.Capital) throw new InvalidOperationException($"capital '{region.Id}' cannot change allegiance");
        if (region.Allegiance == seat?.Id) return;
        changes.Add(new BoardChange(region.Id, "allegiance", region.Allegiance ?? "independent", seat?.Id ?? "independent"));
        region.Allegiance = seat?.Id;
    }

    /// <summary>Holds <paramref name="region"/> uncontested until turn <paramref name="turn"/>.</summary>
    public void Consolidate(Region region, int turn)
    {
        if (region.ContestableFrom == turn) return;
        Record(region.Id, "contestable-from", region.ContestableFrom, turn);
        region.ContestableFrom = turn;
    }

    public void AdjustResource(Seat seat, Resource resource, int delta)
    {
        switch (resource)
        {
            case Resource.Coin: seat.Coin = Clamped(seat.Id, "coin", seat.Coin, delta, 0, ResourceLimit); break;
            case Resource.Favour: seat.Favour = Clamped(seat.Id, "favour", seat.Favour, delta, 0, ResourceLimit); break;
            case Resource.Force: seat.Force = Clamped(seat.Id, "force", seat.Force, delta, 0, ResourceLimit); break;
            default: throw new ArgumentOutOfRangeException(nameof(resource), resource, null);
        }
    }

    public void AdjustRelation(Seat a, Seat b, int delta)
    {
        if (a.Id == b.Id) throw new InvalidOperationException($"seat '{a.Id}' has no relation with itself");
        (string, string) pair = Pair(a.Id, b.Id);
        int before = relations.GetValueOrDefault(pair);
        int after = Math.Clamp(before + delta, -RelationLimit, RelationLimit);
        if (after == before) return;
        relations[pair] = after;
        Record(pair.Item1, $"relation:{pair.Item2}", before, after);
    }

    internal static (string, string) Pair(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    private int Track(string subject, string field, int value, int delta) => Clamped(subject, field, value, delta, 0, TrackLimit);

    private int Clamped(string subject, string field, int value, int delta, int minimum, int maximum)
    {
        int after = Math.Clamp(value + delta, minimum, maximum);
        if (after != value) Record(subject, field, value, after);
        return after;
    }

    private void Record(string subject, string field, int before, int after) =>
        changes.Add(new BoardChange(subject, field, before.ToString(System.Globalization.CultureInfo.InvariantCulture),
            after.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}

/// <summary>A seat's three resources.</summary>
public enum Resource
{
    Coin,
    Favour,
    Force,
}

/// <summary>A player at the table. <see cref="Holder"/> is the cast individual who holds the seat now.</summary>
public sealed record Seat(string Id, string Name, string Holder, string Home)
{
    public int Coin { get; internal set; }

    public int Favour { get; internal set; }

    public int Force { get; internal set; }

    public int Amount(Resource resource) => resource switch
    {
        Resource.Coin => Coin,
        Resource.Favour => Favour,
        Resource.Force => Force,
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
    };
}

/// <summary>
/// A region on the board. A capital never changes allegiance. Influence holds each seat's pieces (1 to 3); a seat
/// with none is absent.
/// </summary>
public sealed record Region(string Id, string Name, int CatalogRegion, bool Capital, IReadOnlyList<string> Borders)
{
    public string? Allegiance { get; internal set; }

    public int Control { get; internal set; }

    public int Prosperity { get; internal set; }

    public int Order { get; internal set; }

    /// <summary>The first turn a seat may contest the region again after an annexation consolidated it.</summary>
    public int ContestableFrom { get; internal set; }

    public SortedDictionary<string, int> Influence { get; internal init; } = new(StringComparer.Ordinal);

    public int InfluenceOf(string seat) => Influence.GetValueOrDefault(seat);
}

/// <summary>One recorded change: a region's or seat's field going from one value to another.</summary>
public sealed record BoardChange(string Subject, string Field, string From, string To);
