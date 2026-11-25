namespace WorldRpg.Politics;

/// <summary>
/// The board in play: seats with their resources and decks, regions with their tracks, influence and offices, the
/// relations between seats and the slow moves under way. Every change to a track, resource, piece, office or
/// relation goes through a method here, which clamps it to its range and records it as a <see cref="BoardChange"/>
/// for the trace. Hands and decks are hidden and record nothing.
/// </summary>
public sealed class Board
{
    public const int TrackLimit = 5;
    public const int InfluenceLimit = 3;

    /// <summary>
    /// Each seat's supply of pieces, for influence and for the offices it holds. A seat with every piece on the board must
    /// take one back, or lose one, before it can place another, so its reach spreads only as far as it can hold it.
    /// </summary>
    public const int InfluencePieces = 10;
    public const int ResourceLimit = 12;
    public const int HandSize = 3;
    public const int RelationLimit = 3;

    private readonly Dictionary<string, Seat> seatsById;
    private readonly Dictionary<string, Region> regionsById;
    private readonly Dictionary<(string, string), int> relations;
    private readonly Dictionary<string, SeatDeck> decks;
    private readonly List<PendingMove> pending = [];
    private readonly List<BoardChange> changes = [];
    private readonly List<Action> undo = [];
    private readonly Dictionary<string, int> pieces;
    private readonly Dictionary<string, int> held;
    private int trialStart = -1;

    private Board(IReadOnlyList<Seat> seats, IReadOnlyList<Region> regions, Dictionary<(string, string), int> relations,
        Dictionary<string, SeatDeck> decks, BoardRandom random)
    {
        Seats = seats;
        Regions = regions;
        seatsById = seats.ToDictionary(seat => seat.Id, StringComparer.Ordinal);
        regionsById = regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        this.relations = relations;
        this.decks = decks;
        pieces = seats.ToDictionary(seat => seat.Id,
            seat => regions.Sum(region => region.InfluenceOf(seat.Id) + region.Offices.Values.Count(holder => holder == seat.Id)), StringComparer.Ordinal);
        held = seats.ToDictionary(seat => seat.Id, seat => regions.Count(region => region.Allegiance == seat.Id), StringComparer.Ordinal);
        Random = random;
    }

    /// <summary>Seats in table order.</summary>
    public IReadOnlyList<Seat> Seats { get; }

    /// <summary>Regions in board order.</summary>
    public IReadOnlyList<Region> Regions { get; }

    public BoardRandom Random { get; }

    /// <summary>How many weekly turns have been played.</summary>
    public int Turn { get; internal set; }

    /// <summary>The slow moves under way, in the order they were played.</summary>
    public IReadOnlyList<PendingMove> Pending => pending;

    /// <summary>
    /// A board at its starting position, played from <paramref name="seed"/> or the setup's canon seed: each seat's
    /// deck shuffled in table order, then a hand dealt to each.
    /// </summary>
    public static Board Create(BoardSetup setup, CardSet cards, ulong? seed = null)
    {
        IReadOnlyList<string> problems = [.. setup.Problems(), .. cards.Problems(setup)];
        if (problems.Count > 0) throw new BoardSetupException("board setup", problems);
        Seat[] seats = [.. setup.Seats.Select(seat => new Seat(seat.Id, seat.Name, seat.Kind, seat.Holder, seat.Home, seat.Grants ?? [])
            { Coin = seat.Coin, Favour = seat.Favour, Force = seat.Force })];
        Region[] regions = [.. setup.Regions.Select(region => new Region(region.Id, region.Name, region.CatalogRegion, region.Capital, [.. region.Borders])
        {
            Allegiance = region.Allegiance,
            Control = region.Control,
            Prosperity = region.Prosperity,
            Order = region.Order,
            Influence = new SortedDictionary<string, int>(region.Influence.Where(entry => entry.Value > 0)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal), StringComparer.Ordinal),
            Offices = new SortedDictionary<Office, string>((region.Offices ?? new Dictionary<Office, string>()).ToDictionary()),
        })];
        Dictionary<(string, string), int> relations = setup.Relations.ToDictionary(relation => Pair(relation.A, relation.B), relation => relation.Value);
        Dictionary<string, SeatDeck> decks = setup.Seats.ToDictionary(seat => seat.Id, seat => new SeatDeck([.. cards.Deck(seat)], [], []), StringComparer.Ordinal);
        Board board = new(seats, regions, relations, decks, new BoardRandom(seed ?? setup.Seed));
        foreach (Seat seat in seats) board.Shuffle(decks[seat.Id].drawPile);
        foreach (Seat seat in seats) board.DrawHand(seat);
        return board;
    }

    public Seat Seat(string id) => seatsById[id];

    public bool HasSeat(string id) => seatsById.ContainsKey(id);

    public Region Region(string id) => regionsById[id];

    public int Relation(string a, string b) => relations.GetValueOrDefault(Pair(a, b));

    /// <summary>
    /// Whether <paramref name="seat"/> can act in <paramref name="region"/>: it is present there or in a region bordering
    /// it. Influence and offices are how a power without territory reaches the board.
    /// </summary>
    public bool Reaches(Seat seat, Region region) =>
        Present(seat, region) || region.Borders.Any(border => Present(seat, regionsById[border]));

    /// <summary>Whether <paramref name="seat"/> holds <paramref name="region"/>, has influence there or holds one of its offices.</summary>
    public bool Present(Seat seat, Region region) =>
        region.Allegiance == seat.Id || region.InfluenceOf(seat.Id) > 0 || region.Offices.ContainsValue(seat.Id);

    /// <summary>How many of <paramref name="seat"/>'s pieces are on the board: its influence and the offices it holds.</summary>
    public int PiecesPlaced(Seat seat) => pieces[seat.Id];

    /// <summary>
    /// How many footholds <paramref name="seat"/> has on the board: the regions it holds, its influence pieces and its
    /// offices. Cards never take the last one.
    /// </summary>
    public int Footholds(Seat seat) => held[seat.Id] + pieces[seat.Id];

    /// <summary><paramref name="seat"/>'s hand, draw pile and discard pile.</summary>
    public SeatDeck Deck(Seat seat) => decks[seat.Id];

    /// <summary>
    /// Fills <paramref name="seat"/>'s hand up to <see cref="HandSize"/> from the top of its draw pile. An empty draw
    /// pile takes the discard pile, shuffled; a seat with every card in hand draws nothing.
    /// </summary>
    public void DrawHand(Seat seat)
    {
        SeatDeck deck = decks[seat.Id];
        while (deck.hand.Count < HandSize)
        {
            if (deck.drawPile.Count == 0)
            {
                if (deck.discard.Count == 0) return;
                deck.drawPile.AddRange(deck.discard);
                deck.discard.Clear();
                Shuffle(deck.drawPile);
            }
            deck.hand.Add(deck.drawPile[^1]);
            deck.drawPile.RemoveAt(deck.drawPile.Count - 1);
        }
    }

    /// <summary>Moves <paramref name="card"/> from <paramref name="seat"/>'s hand to its discard pile.</summary>
    public void Discard(Seat seat, Card card)
    {
        SeatDeck deck = decks[seat.Id];
        if (!deck.hand.Remove(card)) throw new InvalidOperationException($"seat '{seat.Id}' holds no '{card.Id}' to discard");
        deck.discard.Add(card);
    }

    /// <summary>Puts a slow move under way.</summary>
    public void Schedule(PendingMove move) => pending.Add(move);

    /// <summary>The slow moves arriving this turn, in the order they were played, taken off the board.</summary>
    public IReadOnlyList<PendingMove> TakeArrivals()
    {
        PendingMove[] arriving = [.. pending.Where(move => move.Arrives <= Turn)];
        pending.RemoveAll(move => move.Arrives <= Turn);
        return arriving;
    }

    /// <summary>
    /// Weighs a move: runs <paramref name="weigh"/> on this board, which may change tracks, pieces, offices, allegiance,
    /// resources and relations and read them back through <see cref="TrialChanges"/>, then undoes every change and
    /// returns its result. Hands, decks, slow moves and chance must not change inside it.
    /// </summary>
    public T Trial<T>(Func<T> weigh)
    {
        if (trialStart >= 0) throw new InvalidOperationException("a trial is already under way");
        trialStart = changes.Count;
        try
        {
            return weigh();
        }
        finally
        {
            for (int index = undo.Count - 1; index >= 0; index--) undo[index]();
            undo.Clear();
            changes.RemoveRange(trialStart, changes.Count - trialStart);
            trialStart = -1;
        }
    }

    /// <summary>The changes made so far in the trial under way.</summary>
    public IEnumerable<BoardChange> TrialChanges
    {
        get
        {
            for (int index = trialStart < 0 ? changes.Count : trialStart; index < changes.Count; index++) yield return changes[index];
        }
    }

    /// <summary>How many changes are recorded and not yet taken.</summary>
    public int ChangeCount => changes.Count;

    /// <summary>The changes recorded since the last call, in the order they happened.</summary>
    public IReadOnlyList<BoardChange> TakeChanges()
    {
        BoardChange[] taken = [.. changes];
        changes.Clear();
        return taken;
    }

    public void AdjustControl(Region region, int delta)
    {
        int before = region.Control;
        region.Control = Track(region.Id, "control", before, delta);
        Undo(() => region.Control = before);
    }

    public void AdjustProsperity(Region region, int delta)
    {
        int before = region.Prosperity;
        region.Prosperity = Track(region.Id, "prosperity", before, delta);
        Undo(() => region.Prosperity = before);
    }

    public void AdjustOrder(Region region, int delta)
    {
        int before = region.Order;
        region.Order = Track(region.Id, "order", before, delta);
        Undo(() => region.Order = before);
    }

    /// <summary>
    /// Adds or removes <paramref name="seat"/>'s influence pieces in <paramref name="region"/>. A removal never takes a
    /// seat's last foothold: a body at the table is never driven off the board, so it can always act again.
    /// </summary>
    public void AdjustInfluence(Region region, Seat seat, int delta)
    {
        int before = region.InfluenceOf(seat.Id);
        int after = Math.Clamp(before + delta, 0, InfluenceLimit);
        if (after < before) after = Math.Max(after, before - (Footholds(seat) - 1));
        if (after == before) return;
        SetInfluence(region, seat.Id, after);
        Undo(() => SetInfluence(region, seat.Id, before));
        Record(region.Id, $"influence:{seat.Id}", before, after);
    }

    public void SetAllegiance(Region region, Seat? seat)
    {
        if (region.Capital) throw new InvalidOperationException($"capital '{region.Id}' cannot change allegiance");
        if (region.Allegiance == seat?.Id) return;
        string? before = region.Allegiance;
        changes.Add(new BoardChange(region.Id, "allegiance", before ?? "independent", seat?.Id ?? "independent"));
        SetAllegianceTo(region, seat?.Id);
        Undo(() => SetAllegianceTo(region, before));
    }

    /// <summary>Gives <paramref name="region"/>'s <paramref name="office"/> to <paramref name="seat"/>, or to local people with no seat.</summary>
    public void SetOffice(Region region, Office office, Seat? seat)
    {
        string? before = region.Offices.GetValueOrDefault(office);
        if (before == seat?.Id) return;
        SetOfficeHolder(region, office, seat?.Id);
        Undo(() => SetOfficeHolder(region, office, before));
        changes.Add(new BoardChange(region.Id, $"office:{office.ToString().ToLowerInvariant()}", before ?? "local", seat?.Id ?? "local"));
    }

    /// <summary>Holds <paramref name="region"/> uncontested until turn <paramref name="turn"/>.</summary>
    public void Consolidate(Region region, int turn)
    {
        if (region.ContestableFrom == turn) return;
        int before = region.ContestableFrom;
        Record(region.Id, "contestable-from", before, turn);
        region.ContestableFrom = turn;
        Undo(() => region.ContestableFrom = before);
    }

    public void AdjustResource(Seat seat, Resource resource, int delta)
    {
        (int coin, int favour, int force) = (seat.Coin, seat.Favour, seat.Force);
        Undo(() => (seat.Coin, seat.Favour, seat.Force) = (coin, favour, force));
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
        Undo(() => relations[pair] = before);
        Record(pair.Item1, $"relation:{pair.Item2}", before, after);
    }

    private void SetInfluence(Region region, string seat, int count)
    {
        pieces[seat] += count - region.InfluenceOf(seat);
        if (count == 0) region.Influence.Remove(seat);
        else region.Influence[seat] = count;
    }

    private void SetOfficeHolder(Region region, Office office, string? seat)
    {
        if (region.Offices.TryGetValue(office, out string? before)) pieces[before]--;
        if (seat is null) region.Offices.Remove(office);
        else region.Offices[office] = seat;
        if (seat is not null) pieces[seat]++;
    }

    private void SetAllegianceTo(Region region, string? seat)
    {
        if (region.Allegiance is not null) held[region.Allegiance]--;
        region.Allegiance = seat;
        if (seat is not null) held[seat]++;
    }

    private void Undo(Action action)
    {
        if (trialStart >= 0) undo.Add(action);
    }

    private void Shuffle(List<Card> cards)
    {
        for (int index = cards.Count - 1; index > 0; index--)
        {
            int other = Random.Next(index + 1);
            (cards[index], cards[other]) = (cards[other], cards[index]);
        }
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

/// <summary>
/// What a seat is at the table. A crown holds a capital and regions by allegiance; a power holds no territory and
/// acts through influence.
/// </summary>
public enum SeatKind
{
    Crown,
    Power,
}

/// <summary>
/// A player at the table. <see cref="Holder"/> is the cast individual who holds the seat now, if the head of the body
/// is cast; <see cref="Home"/> is a crown's capital.
/// </summary>
public sealed record Seat(string Id, string Name, SeatKind Kind, string? Holder, string? Home, IReadOnlyList<SeatGrant> Grants)
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

    /// <summary>The offices a seat holds; an office missing here is held by local people with no seat.</summary>
    public SortedDictionary<Office, string> Offices { get; internal init; } = [];

    public int InfluenceOf(string seat) => Influence.GetValueOrDefault(seat);
}

/// <summary>A seat's cards: the hand it plays from, the draw pile it fills the hand from and the discard pile.</summary>
public sealed class SeatDeck
{
    internal readonly List<Card> drawPile;
    internal readonly List<Card> hand;
    internal readonly List<Card> discard;

    internal SeatDeck(List<Card> drawPile, List<Card> hand, List<Card> discard)
    {
        this.drawPile = drawPile;
        this.hand = hand;
        this.discard = discard;
    }

    /// <summary>The draw pile; its last card is the top.</summary>
    public IReadOnlyList<Card> DrawPile => drawPile;

    public IReadOnlyList<Card> Hand => hand;

    public IReadOnlyList<Card> Discard => discard;
}

/// <summary>
/// A slow move under way: paid for on turn <see cref="Played"/>, resolving on turn <see cref="Arrives"/>. Its
/// <see cref="Id"/> is the turn and seat it was played on. <see cref="Origin"/> is the region it sets out from, where
/// the world may carry, hurry, delay or intercept it.
/// </summary>
public sealed record PendingMove(string Id, string Seat, Card Card, IReadOnlyList<string> Targets, string? Origin, int Played, int Arrives);

/// <summary>One recorded change: a region's or seat's field going from one value to another.</summary>
public sealed record BoardChange(string Subject, string Field, string From, string To);
