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
    private readonly List<Agreement> agreements = [];
    private readonly List<War> wars = [];
    private readonly List<Truce> truces = [];
    private readonly List<Feud> feuds = [];
    private readonly List<WorldCard> worldDraw;
    private readonly List<WorldCard> worldDiscard = [];
    private readonly List<BoardChange> changes = [];
    private readonly List<Action> undo = [];
    private readonly Dictionary<string, int> pieces;
    private readonly Dictionary<string, int> held;
    private readonly IReadOnlyDictionary<string, Persona> personas;
    private readonly Dictionary<(string Helper, string Helped), int> helps = [];
    private int trialStart = -1;
    private int dealsMade;
    private int warsMade;
    private int feudsMade;

    private Board(IReadOnlyList<Seat> seats, IReadOnlyList<Region> regions, Dictionary<(string, string), int> relations,
        Dictionary<string, SeatDeck> decks, List<WorldCard> worldDraw, IReadOnlyList<Card> standingCards, BoardRandom random,
        IReadOnlyDictionary<string, Persona> personas)
    {
        this.personas = personas;
        StandingCards = standingCards;
        Seats = seats;
        Regions = regions;
        seatsById = seats.ToDictionary(seat => seat.Id, StringComparer.Ordinal);
        regionsById = regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        this.relations = relations;
        this.decks = decks;
        this.worldDraw = worldDraw;
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

    /// <summary>The seat leading the table this week, found at its start (<see cref="Balance.FindLeader"/>).</summary>
    public string? Leader { get; private set; }

    /// <summary>The slow moves under way, in the order they were played.</summary>
    public IReadOnlyList<PendingMove> Pending => pending;

    /// <summary>The trade agreements in force, in the order they were made (<see cref="Trade"/>).</summary>
    public IReadOnlyList<Agreement> Agreements => agreements;

    /// <summary>The wars in force, in the order they opened (<see cref="Politics.Wars"/>).</summary>
    public IReadOnlyList<War> Wars => wars;

    /// <summary>The feuds in force, in the order they opened (<see cref="Politics.Feuds"/>).</summary>
    public IReadOnlyList<Feud> Feuds => feuds;

    /// <summary>The truces binding crowns after their treaties.</summary>
    public IReadOnlyList<Truce> Truces => truces;

    /// <summary>
    /// The standing cards, which no seat draws: a seat in a feud or at war may play the cards of open conflict every
    /// week beside its hand, and an arbiter its countermoves.
    /// </summary>
    public IReadOnlyList<Card> StandingCards { get; }

    /// <summary>
    /// A board at its starting position, played from <paramref name="seed"/> or the setup's canon seed: each seat's
    /// deck shuffled in table order, then the World deck, then a hand dealt to each.
    /// </summary>
    public static Board Create(BoardSetup setup, CardSet cards, ulong? seed = null)
    {
        IReadOnlyList<string> problems = [.. setup.Problems(), .. cards.Problems(setup)];
        if (problems.Count > 0) throw new BoardSetupException("board setup", problems);
        Seat[] seats = [.. setup.Seats.Select(seat => new Seat(seat.Id, seat.Name, seat.Kind, seat.Holder, seat.Home, seat.Grants ?? [], seat.Agenda ?? [])
            { Coin = seat.Coin, Favour = seat.Favour, Force = seat.Force, Reserves = seat.Reserves, Arbiter = seat.Arbiter, Outsider = seat.Outsider })];
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
        Board board = new(seats, regions, relations, decks, [.. cards.WorldDeck()], [.. cards.StandingCards()], new BoardRandom(seed ?? setup.Seed),
            new Dictionary<string, Persona>(setup.Personas!, StringComparer.Ordinal));
        foreach (Seat seat in seats) board.Shuffle(decks[seat.Id].drawPile);
        board.Shuffle(board.worldDraw);
        foreach (Seat seat in seats) board.DrawHand(seat);
        return board;
    }

    public Seat Seat(string id) => seatsById[id];

    public bool HasSeat(string id) => seatsById.ContainsKey(id);

    public Region Region(string id) => regionsById[id];

    public int Relation(string a, string b) => relations.GetValueOrDefault(Pair(a, b));

    public War War(string id) => wars.Single(war => war.Id == id);

    public bool HasWar(string id) => wars.Any(war => war.Id == id);

    public Feud Feud(string id) => feuds.Single(feud => feud.Id == id);

    /// <summary>The war <paramref name="a"/> and <paramref name="b"/> fight as its principals, if any.</summary>
    public War? WarBetween(string a, string b) => wars.FirstOrDefault(war => war.Principal(a) && war.Principal(b));

    /// <summary>The persona of the cast individual holding <paramref name="seat"/> now.</summary>
    public Persona Persona(Seat seat) => personas[seat.Holder];

    /// <summary>
    /// Gives <paramref name="seat"/> to <paramref name="holder"/>, whose persona then plays it; the seat keeps its hand,
    /// pieces, plan and grudges.
    /// </summary>
    public void SetHolder(Seat seat, string holder)
    {
        if (!personas.ContainsKey(holder)) throw new ArgumentException($"holder '{holder}' has no persona on this board", nameof(holder));
        string before = seat.Holder;
        if (before == holder) return;
        seat.Holder = holder;
        Undo(() => seat.Holder = before);
        changes.Add(new BoardChange(seat.Id, "holder", before, holder));
    }

    /// <summary>Makes <paramref name="seat"/> the leader of the table, or no seat.</summary>
    public void SetLeader(Seat? seat)
    {
        if (Leader == seat?.Id) return;
        changes.Add(new BoardChange(seat?.Id ?? Leader!, "leader", Leader ?? "none", seat?.Id ?? "none"));
        Leader = seat?.Id;
    }

    /// <summary>
    /// Counts another week <paramref name="seat"/>'s reserves point is contested, or clears the count; the trace records
    /// the deliveries stopping and resuming (<see cref="Balance.ReservesCut"/>).
    /// </summary>
    public void SetContested(Seat seat, bool contested)
    {
        bool cut = Balance.ReservesCut(seat);
        seat.ContestedWeeks = contested ? seat.ContestedWeeks + 1 : 0;
        if (cut != Balance.ReservesCut(seat)) changes.Add(new BoardChange(seat.Id, "reserves", cut ? "cut" : "open", cut ? "open" : "cut"));
    }

    /// <summary>Commits <paramref name="seat"/>, which has no plan, to <paramref name="plan"/> (<see cref="Intention"/>).</summary>
    public void SetPlan(Seat seat, Plan plan)
    {
        if (seat.Plan is not null) throw new InvalidOperationException($"seat '{seat.Id}' already pursues {seat.Plan}");
        seat.Plan = plan;
        changes.Add(new BoardChange(seat.Id, "plan", "none", plan.ToString()));
    }

    /// <summary>Ends <paramref name="seat"/>'s plan for <paramref name="reason"/>.</summary>
    public void EndPlan(Seat seat, string reason)
    {
        Plan plan = seat.Plan ?? throw new InvalidOperationException($"seat '{seat.Id}' has no plan");
        seat.Plan = null;
        changes.Add(new BoardChange(seat.Id, "plan", plan.ToString(), reason));
    }

    /// <summary><paramref name="seat"/> holds a grudge against <paramref name="against"/> to the end of <paramref name="until"/>, or longer if it already did.</summary>
    public void HoldGrudge(Seat seat, Seat against, int until)
    {
        int index = seat.Grudges.FindIndex(grudge => grudge.Against == against.Id);
        Grudge? before = index < 0 ? null : seat.Grudges[index];
        if (before is not null && before.Until >= until) return;
        Grudge after = new(against.Id, until);
        if (before is null) seat.Grudges.Add(after);
        else seat.Grudges[index] = after;
        changes.Add(new BoardChange(seat.Id, $"grudge:{against.Id}", Describe(before), Describe(after)));
    }

    /// <summary><paramref name="seat"/> has struck back at <paramref name="against"/>, which settles its grudge.</summary>
    public void SettleGrudge(Seat seat, Seat against) => DropGrudge(seat, seat.Grudges.Single(grudge => grudge.Against == against.Id), "settled");

    /// <summary>Lets go every grudge whose last week has passed.</summary>
    public void ExpireGrudges()
    {
        foreach (Seat seat in Seats)
            foreach (Grudge grudge in seat.Grudges.Where(grudge => grudge.Until < Turn).ToArray()) DropGrudge(seat, grudge, "none");
    }

    /// <summary>Counts a help from <paramref name="helper"/> to <paramref name="helped"/>; returns how many it has given.</summary>
    public int Helped(Seat helper, Seat helped)
    {
        int count = helps.GetValueOrDefault((helper.Id, helped.Id)) + 1;
        helps[(helper.Id, helped.Id)] = count;
        return count;
    }

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

    /// <summary>
    /// Turns the top World card and lays its condition, with the world as its source, on a region it chooses: any
    /// region, or among those with the lowest prosperity, the highest prosperity or the lowest order, a tie settled by
    /// chance. Trouble that may fall anywhere falls twice as often on the leader's regions. An empty World draw pile takes the discards, shuffled; a table with no World cards turns nothing.
    /// </summary>
    public (WorldCard Card, Region Region)? TurnWorldCard()
    {
        if (worldDraw.Count == 0)
        {
            if (worldDiscard.Count == 0) return null;
            worldDraw.AddRange(worldDiscard);
            worldDiscard.Clear();
            Shuffle(worldDraw);
        }
        WorldCard card = worldDraw[^1];
        worldDraw.RemoveAt(worldDraw.Count - 1);
        worldDiscard.Add(card);
        Region[] candidates = card.Where switch
        {
            WorldTarget.Poorest => [.. Regions.Where(region => region.Prosperity == Regions.Min(other => other.Prosperity))],
            WorldTarget.Richest => [.. Regions.Where(region => region.Prosperity == Regions.Max(other => other.Prosperity))],
            WorldTarget.Unruliest => [.. Regions.Where(region => region.Order == Regions.Min(other => other.Order))],
            _ when Prestige.Bent(card.Condition) < 0 => [.. Regions, .. Regions.Where(region => Leader is not null && region.Allegiance == Leader)],
            _ => [.. Regions],
        };
        Region target = candidates[Random.Next(candidates.Length)];
        AddCondition(target, new RegionCondition(card.Condition, card.Detail, card.Magnitude, null, Turn, Turn + card.Weeks));
        return (card, target);
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

    /// <summary>The changes recorded since the count was <paramref name="mark"/> (<see cref="ChangeCount"/>).</summary>
    public IEnumerable<BoardChange> ChangesSince(int mark)
    {
        for (int index = mark; index < changes.Count; index++) yield return changes[index];
    }

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
    /// Adds or removes <paramref name="seat"/>'s influence pieces in <paramref name="region"/>. A placement takes no
    /// more pieces than the seat has left in its supply. A removal never takes a seat's last foothold: a body at the
    /// table is never driven off the board, so it can always act again.
    /// </summary>
    public void AdjustInfluence(Region region, Seat seat, int delta)
    {
        int before = region.InfluenceOf(seat.Id);
        int after = Math.Clamp(before + delta, 0, InfluenceLimit);
        if (after > before) after = Math.Min(after, before + PiecesLeft(seat));
        if (after < before) after = Math.Max(after, before - (Footholds(seat) - 1));
        ChangeInfluence(region, seat, before, after);
    }

    /// <summary>
    /// Moves up to <paramref name="count"/> of <paramref name="seat"/>'s pieces from <paramref name="from"/> to
    /// <paramref name="to"/>, as many as the one holds and the other has room for. A move spends no supply and keeps
    /// the seat's footholds.
    /// </summary>
    public void MoveInfluence(Region from, Region to, Seat seat, int count)
    {
        int moved = Math.Min(count, Math.Min(from.InfluenceOf(seat.Id), InfluenceLimit - to.InfluenceOf(seat.Id)));
        if (moved <= 0) return;
        ChangeInfluence(from, seat, from.InfluenceOf(seat.Id), from.InfluenceOf(seat.Id) - moved);
        ChangeInfluence(to, seat, to.InfluenceOf(seat.Id), to.InfluenceOf(seat.Id) + moved);
    }

    /// <summary>How many pieces <paramref name="seat"/> has left in its supply.</summary>
    public int PiecesLeft(Seat seat) => InfluencePieces - pieces[seat.Id];

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

    /// <summary>
    /// Moves <paramref name="seat"/>'s <paramref name="resource"/> within 0 to <see cref="ResourceLimit"/>. A seat whose
    /// reserves are cut off gains no Force (<see cref="Balance"/>).
    /// </summary>
    public void AdjustResource(Seat seat, Resource resource, int delta)
    {
        if (resource == Resource.Force && delta > 0 && Balance.ReservesCut(seat)) return;
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

    /// <summary>
    /// Moves the relation between <paramref name="a"/> and <paramref name="b"/>. Hostages hold it at 0 or above, and a war
    /// between them as principals holds it at war's relation, so no drift or gesture lifts it before the treaty.
    /// </summary>
    public void AdjustRelation(Seat a, Seat b, int delta)
    {
        if (a.Id == b.Id) throw new InvalidOperationException($"seat '{a.Id}' has no relation with itself");
        (string, string) pair = Pair(a.Id, b.Id);
        int before = relations.GetValueOrDefault(pair);
        int after = Math.Clamp(before + delta, -RelationLimit, RelationLimit);
        if (Hostages(a, b)) after = Math.Max(after, 0);
        if (WarBetween(a.Id, b.Id) is not null) after = -RelationLimit;
        if (after == before) return;
        relations[pair] = after;
        Undo(() => relations[pair] = before);
        Record(pair.Item1, $"relation:{pair.Item2}", before, after);
    }

    /// <summary>
    /// Lays <paramref name="condition"/> on <paramref name="region"/>. A condition of the same kind and detail already
    /// there is refreshed instead: it keeps the greater magnitude and the later end.
    /// </summary>
    public void AddCondition(Region region, RegionCondition condition)
    {
        int index = region.Conditions.FindIndex(held => held.Kind == condition.Kind && held.Detail == condition.Detail);
        RegionCondition? before = index < 0 ? null : region.Conditions[index];
        RegionCondition after = before is null ? condition
            : before with { Magnitude = Math.Max(before.Magnitude, condition.Magnitude), End = Math.Max(before.End, condition.End) };
        if (after == before) return;
        ReplaceCondition(region.Conditions, before, after);
        Undo(() => ReplaceCondition(region.Conditions, after, before));
        changes.Add(new BoardChange(region.Id, ConditionField(condition.Kind, condition.Detail), Describe(before), Describe(after)));
    }

    /// <summary>Lifts every condition of <paramref name="kind"/> from <paramref name="region"/>.</summary>
    public void RemoveConditions(Region region, ConditionKind kind)
    {
        foreach (RegionCondition condition in region.Conditions.Where(held => held.Kind == kind).ToArray()) Lift(region, condition);
    }

    /// <summary>Lifts the condition of <paramref name="kind"/> and <paramref name="detail"/> from <paramref name="region"/>, if it is there.</summary>
    public void RemoveCondition(Region region, ConditionKind kind, string? detail)
    {
        if (region.Conditions.Find(held => held.Kind == kind && held.Detail == detail) is { } condition) Lift(region, condition);
    }

    /// <summary>Lays <paramref name="condition"/> on <paramref name="crown"/>'s court, refreshing one of the same kind and source.</summary>
    public void AddCourtCondition(Seat crown, CourtCondition condition)
    {
        int index = crown.Court.FindIndex(held => held.Kind == condition.Kind && held.Source == condition.Source);
        CourtCondition? before = index < 0 ? null : crown.Court[index];
        CourtCondition after = before is null ? condition
            : before with { Magnitude = Math.Max(before.Magnitude, condition.Magnitude), End = Math.Max(before.End, condition.End) };
        if (after == before) return;
        ReplaceCondition(crown.Court, before, after);
        Undo(() => ReplaceCondition(crown.Court, after, before));
        changes.Add(new BoardChange(crown.Id, CourtField(condition), Describe(before), Describe(after)));
        if (condition.Kind == CourtConditionKind.HostageTreaty) AdjustRelation(crown, Seat(condition.Source), 0);
    }

    /// <summary>Marks <paramref name="region"/> fed, relieved by traded grain, or cut off from food.</summary>
    public void SetSupply(Region region, RegionSupply supply)
    {
        RegionSupply before = region.Supply;
        if (before == supply) return;
        changes.Add(new BoardChange(region.Id, "supply", Lower(before), Lower(supply)));
        region.Supply = supply;
        Undo(() => region.Supply = before);
    }

    /// <summary>Puts <paramref name="offer"/> in force under the next agreement id.</summary>
    public void AddAgreement(Agreement offer)
    {
        dealsMade++;
        Agreement deal = offer with { Id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"deal-{dealsMade}") };
        agreements.Add(deal);
        Undo(() =>
        {
            agreements.Remove(deal);
            dealsMade--;
        });
        changes.Add(new BoardChange(deal.Proposer, $"deal:{deal.Partner}", "none", deal.ToString()));
    }

    /// <summary>Records <paramref name="offer"/>'s refusal; nothing else changes.</summary>
    public void RefuseAgreement(Agreement offer) =>
        changes.Add(new BoardChange(offer.Proposer, $"deal:{offer.Partner}", "none", $"refused {offer.Gives} for {offer.Asks}"));

    /// <summary>Ends <paramref name="deal"/> for both sides, for <paramref name="reason"/>.</summary>
    public void EndAgreement(Agreement deal, string reason)
    {
        RemoveInPlace(agreements, deal);
        changes.Add(new BoardChange(deal.Proposer, $"deal:{deal.Partner}", deal.ToString(), reason));
    }

    /// <summary>Opens a war over <paramref name="aim"/> between the crowns <paramref name="a"/> and <paramref name="b"/> under the next war id.</summary>
    public void OpenWar(Seat a, Seat b, WarAim aim)
    {
        warsMade++;
        War war = new(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"war-{warsMade}"), a.Id, b.Id, aim, [], [], Turn, 0, 0);
        wars.Add(war);
        Undo(() =>
        {
            wars.Remove(war);
            warsMade--;
        });
        changes.Add(new BoardChange(a.Id, $"war:{b.Id}", "none", $"{war.Id} for {aim.ToString().ToLowerInvariant()}"));
    }

    /// <summary>Opens a feud between the crowns <paramref name="a"/> and <paramref name="b"/> under the next feud id.</summary>
    public void OpenFeud(Seat a, Seat b)
    {
        feudsMade++;
        Feud feud = new(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"feud-{feudsMade}"), a.Id, b.Id, Turn, 0);
        feuds.Add(feud);
        Undo(() =>
        {
            feuds.Remove(feud);
            feudsMade--;
        });
        changes.Add(new BoardChange(a.Id, $"feud:{b.Id}", "none", feud.Id));
    }

    /// <summary>Heats <paramref name="feud"/> by <paramref name="delta"/>, or lets it cool if negative.</summary>
    public void AddHeat(Feud feud, int delta)
    {
        Feud before = Feud(feud.Id);
        Feud after = before with { Heat = before.Heat + delta };
        int index = feuds.IndexOf(before);
        feuds[index] = after;
        Undo(() => feuds[index] = before);
        Record(before.A, $"heat:{before.Id}", before.Heat, after.Heat);
    }

    /// <summary>Ends <paramref name="feud"/> for <paramref name="reason"/>.</summary>
    public void EndFeud(Feud feud, string reason)
    {
        RemoveInPlace(feuds, Feud(feud.Id));
        changes.Add(new BoardChange(feud.A, $"feud:{feud.B}", feud.Id, reason));
    }

    /// <summary><paramref name="ally"/> joins <paramref name="war"/> on the side of the principal <paramref name="side"/>.</summary>
    public void JoinWar(War war, Seat ally, string side)
    {
        war = War(war.Id);
        ReplaceWar(war, side == war.A ? war with { AlliesOfA = [.. war.AlliesOfA, ally.Id] } : war with { AlliesOfB = [.. war.AlliesOfB, ally.Id] });
        changes.Add(new BoardChange(ally.Id, $"ally:{war.Id}", "none", side));
    }

    /// <summary>Makes the principal <paramref name="seat"/> of <paramref name="war"/> wearier by <paramref name="amount"/>.</summary>
    public void AddWeariness(War war, string seat, int amount)
    {
        war = War(war.Id);
        int before = war.Weariness(seat);
        ReplaceWar(war, seat == war.A ? war with { WearinessOfA = before + amount } : war with { WearinessOfB = before + amount });
        Record(seat, $"weariness:{war.Id}", before, before + amount);
    }

    /// <summary>Ends <paramref name="war"/> for <paramref name="reason"/>; its allies leave it with it.</summary>
    public void EndWar(War war, string reason)
    {
        RemoveInPlace(wars, War(war.Id));
        changes.Add(new BoardChange(war.A, $"war:{war.B}", war.Id, reason));
    }

    /// <summary>Binds two crowns to <paramref name="truce"/>.</summary>
    public void AddTruce(Truce truce)
    {
        truces.Add(truce);
        Undo(() => truces.Remove(truce));
        changes.Add(new BoardChange(truce.A, $"truce:{truce.B}", "none",
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"until {truce.Until}")));
    }

    /// <summary>Lets go every truce that has reached its end.</summary>
    public void ExpireTruces()
    {
        foreach (Truce truce in truces.Where(truce => truce.Until <= Turn).ToArray())
        {
            RemoveInPlace(truces, truce);
            changes.Add(new BoardChange(truce.A, $"truce:{truce.B}",
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"until {truce.Until}"), "none"));
        }
    }

    /// <summary>Takes every region and court condition that has reached its end turn off the board.</summary>
    public void ExpireConditions()
    {
        foreach (Region region in Regions)
            foreach (RegionCondition condition in region.Conditions.Where(held => held.End <= Turn).ToArray()) Lift(region, condition);
        foreach (Seat seat in Seats)
        {
            foreach (CourtCondition condition in seat.Court.Where(held => held.End <= Turn).ToArray())
            {
                RemoveInPlace(seat.Court, condition);
                changes.Add(new BoardChange(seat.Id, CourtField(condition), Describe(condition), "none"));
            }
        }
    }

    /// <summary>
    /// Whether either seat has given the other hostages: their relation is raised to 0 when the treaty is laid and
    /// cannot sink below it while the treaty lasts.
    /// </summary>
    private static bool Hostages(Seat a, Seat b) =>
        a.Court.Any(held => held.Kind == CourtConditionKind.HostageTreaty && held.Source == b.Id)
        || b.Court.Any(held => held.Kind == CourtConditionKind.HostageTreaty && held.Source == a.Id);

    private void ReplaceWar(War before, War after)
    {
        int index = wars.IndexOf(before);
        wars[index] = after;
        Undo(() => wars[index] = before);
    }

    private void Lift(Region region, RegionCondition condition)
    {
        RemoveInPlace(region.Conditions, condition);
        changes.Add(new BoardChange(region.Id, ConditionField(condition.Kind, condition.Detail), Describe(condition), "none"));
    }

    /// <summary>Removes <paramref name="condition"/>; undoing it puts the condition back where it was, since order decides how conditions weigh.</summary>
    private void RemoveInPlace<T>(List<T> held, T condition)
    {
        int index = held.IndexOf(condition);
        held.RemoveAt(index);
        Undo(() => held.Insert(index, condition));
    }

    private static void ReplaceCondition<T>(List<T> held, T? before, T? after) where T : class
    {
        int index = before is null ? -1 : held.IndexOf(before);
        if (index >= 0 && after is not null) held[index] = after;
        else if (index >= 0) held.RemoveAt(index);
        else if (after is not null) held.Add(after);
    }

    private static string ConditionField(ConditionKind kind, string? detail) =>
        $"condition:{kind.ToString().ToLowerInvariant()}{(detail is null ? "" : ":" + detail)}";

    private static string CourtField(CourtCondition condition) => $"court:{condition.Kind.ToString().ToLowerInvariant()}:{condition.Source}";

    private static string Describe(RegionCondition? condition) => condition is null ? "none"
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{condition.Magnitude} by {condition.Source ?? "world"} until {condition.End}");

    private static string Describe(CourtCondition? condition) => condition is null ? "none"
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{condition.Magnitude} until {condition.End}");

    private void ChangeInfluence(Region region, Seat seat, int before, int after)
    {
        if (after == before) return;
        SetInfluence(region, seat.Id, after);
        Undo(() => SetInfluence(region, seat.Id, before));
        Record(region.Id, $"influence:{seat.Id}", before, after);
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

    private void DropGrudge(Seat seat, Grudge grudge, string outcome)
    {
        seat.Grudges.Remove(grudge);
        changes.Add(new BoardChange(seat.Id, $"grudge:{grudge.Against}", Describe(grudge), outcome));
    }

    private static string Describe(Grudge? grudge) =>
        grudge is null ? "none" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"until {grudge.Until}");

    private void Undo(Action action)
    {
        if (trialStart >= 0) undo.Add(action);
    }

    private void Shuffle<T>(List<T> cards)
    {
        for (int index = cards.Count - 1; index > 0; index--)
        {
            int other = Random.Next(index + 1);
            (cards[index], cards[other]) = (cards[other], cards[index]);
        }
    }

    private static string Lower(RegionSupply supply) => supply.ToString().ToLowerInvariant();

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
/// A player at the table. <see cref="Holder"/> is the cast individual who holds the seat now, whose persona plays it;
/// <see cref="Home"/> is a crown's capital; <see cref="Agenda"/> holds its standing goals (<see cref="Intention"/>).
/// </summary>
public sealed record Seat(string Id, string Name, SeatKind Kind, string Holder, string? Home, IReadOnlyList<SeatGrant> Grants, IReadOnlyList<Goal> Agenda)
{
    public string Holder { get; internal set; } = Holder;

    /// <summary>The plan the seat is committed to, if any.</summary>
    public Plan? Plan { get; internal set; }

    /// <summary>The seat's grudges, in the order they were opened (<see cref="Memory"/>).</summary>
    public List<Grudge> Grudges { get; internal init; } = [];

    /// <summary>The region all its Force arrives through, if any (<see cref="Balance"/>).</summary>
    public string? Reserves { get; internal init; }

    /// <summary>Whether the seat keeps the balance of power (<see cref="Balance"/>).</summary>
    public bool Arbiter { get; internal init; }

    /// <summary>Whether the seat stands outside the Empire an arbiter keeps (<see cref="Balance"/>).</summary>
    public bool Outsider { get; internal init; }

    /// <summary>How many weeks running its reserves point has been contested.</summary>
    public int ContestedWeeks { get; internal set; }

    public int Coin { get; internal set; }

    public int Favour { get; internal set; }

    public int Force { get; internal set; }

    /// <summary>The conditions on a crown's court, in the order they were laid.</summary>
    public List<CourtCondition> Court { get; internal init; } = [];

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

    /// <summary>The conditions on the region, in the order they were laid.</summary>
    public List<RegionCondition> Conditions { get; internal init; } = [];

    /// <summary>Whether the region is fed, relieved or cut off from food this week (<see cref="Politics.Supply"/>).</summary>
    public RegionSupply Supply { get; internal set; }

    /// <summary>Whether the region is cut off this week: it pays its holder nothing.</summary>
    public bool Cut => Supply == RegionSupply.Cut;

    public int InfluenceOf(string seat) => Influence.GetValueOrDefault(seat);
}

/// <summary>How a region is fed this week.</summary>
public enum RegionSupply
{
    /// <summary>Its own harvest or the next valley's feeds it.</summary>
    Fed,

    /// <summary>It is not fed, and grain bought under a trade agreement relieves it.</summary>
    Relieved,

    /// <summary>It is cut off from food.</summary>
    Cut,
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
