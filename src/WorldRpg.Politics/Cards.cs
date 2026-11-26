namespace WorldRpg.Politics;

/// <summary>The shared families seats draw their decks from. A signature card belongs to one too.</summary>
public enum CardFamily
{
    Trade,
    Court,
    Force,
    Shadow,
    Arcane,
    Faith,

    /// <summary>
    /// The cards of open conflict, which no seat draws: every seat in a feud or fighting a war may play them each week
    /// beside its hand, and each fits only the feud or war it is for (<see cref="Feuds"/>, <see cref="Wars"/>).
    /// </summary>
    War,

    /// <summary>
    /// An arbiter's countermoves, which no seat draws: every arbiter may play them each week beside its hand, so its
    /// Force, not its draw, limits how often it intervenes (<see cref="Balance"/>).
    /// </summary>
    Arbitration,
}

/// <summary>What a card is played on. Its targets are given in this order.</summary>
public enum CardTarget
{
    /// <summary>Played without a target.</summary>
    None,

    /// <summary>A region owing allegiance to the player.</summary>
    OwnRegion,

    /// <summary>A region the player reaches: one it is present in, or one bordering such a region.</summary>
    ReachableRegion,

    /// <summary>
    /// A region the player reaches that is not a capital, does not owe it allegiance and is not consolidated after an
    /// annexation.
    /// </summary>
    ContestedRegion,

    /// <summary>Another seat.</summary>
    OtherSeat,

    /// <summary>Two regions: one the player is present in, then one bordering it.</summary>
    RegionPair,

    /// <summary>A region the player reaches, then another seat with influence there: a rival on its own ground.</summary>
    RegionRival,

    /// <summary>
    /// A region of an enemy's in a war the player fights, bordering land held by the player's side, that the player reaches.
    /// </summary>
    EnemyFront,

    /// <summary>A region owing allegiance to the player that borders land held by the enemy's side in a war it fights.</summary>
    OwnFront,

    /// <summary>A war in force.</summary>
    War,

    /// <summary>A region the player reaches, then a foe of the player's present there: a crown it feuds with, or an enemy in a war.</summary>
    FeudRival,

    /// <summary>A seat friendly with the leader, other than the player: a member of the coalition gathering round it (<see cref="Balance.LeadersFriends"/>).</summary>
    LeaderFriend,

    /// <summary>A seat an arbiter backs (<see cref="Balance.Underdogs"/>).</summary>
    Underdog,

    /// <summary>The seat leading the table (<see cref="Board.Leader"/>), if it is not the player.</summary>
    Leader,

    /// <summary>
    /// Another seat, then the term the player gives and the term it asks in return, each from the card's menu
    /// (<see cref="TradeTerm"/>): a trade agreement offered to a partner.
    /// </summary>
    Deal,
}

/// <summary>
/// Whether the world sees who made a move. A secret move's record still keeps its true seat; which surfaces show a
/// cover story, and who counts as an insider, is the world's to decide.
/// </summary>
public enum Visibility
{
    Public,
    Secret,
}

/// <summary>A local office in a region, held by a seat or by local people with no seat. Each pays its holder one resource.</summary>
public enum Office
{
    /// <summary>The watch captain; pays force.</summary>
    Watch,

    /// <summary>The court factor, who keeps the region's purse; pays coin.</summary>
    Factor,

    /// <summary>The head of the region's temple; pays favour.</summary>
    Temple,

    /// <summary>The master of the region's chief guild hall; pays coin.</summary>
    Hall,
}

/// <summary>The fixed set of things a card can do.</summary>
public enum CardEffectKind
{
    /// <summary>Each target region's control.</summary>
    Control,

    /// <summary>Each target region's prosperity.</summary>
    Prosperity,

    /// <summary>Each target region's order.</summary>
    Order,

    /// <summary>The player's influence in the target region; placing a piece needs one left in its supply.</summary>
    Influence,

    /// <summary>The influence of the target region's crown in that region.</summary>
    HolderInfluence,

    /// <summary>The influence of the targeted rival in the target region.</summary>
    RivalInfluence,

    /// <summary>Moves up to the amount of the player's pieces from the first region of a pair to the second.</summary>
    MoveInfluence,

    /// <summary>
    /// Challenges the target region's office. The challenge carries the player's influence there plus the amount; the
    /// office is defended by 1, plus its holder's influence there if a seat holds it, plus 1 where the region's order
    /// is <see cref="Card.WatchfulOrder"/> or more. A stronger challenge takes the office, and the seat that lost it
    /// thinks 1 worse of the player. Taking an office puts one of the player's pieces on it, so a challenge needs a piece
    /// left in the supply. An office that is its holder's last foothold on the board cannot be taken. Cast holders are
    /// never challenged: offices are local people.
    /// </summary>
    ChallengeOffice,

    /// <summary>
    /// The player's relation with the target seat or rival, or else with the crown the target region owes allegiance to.
    /// </summary>
    Relation,

    /// <summary>The player gains the resource.</summary>
    Gain,

    /// <summary>The player takes up to the amount of the resource from the target seat, or from the foe it strikes.</summary>
    Take,

    /// <summary>
    /// Lays <see cref="CardEffect.Condition"/> on the target region at magnitude <see cref="CardEffect.Amount"/> for
    /// <see cref="CardEffect.Weeks"/> weeks, with <see cref="CardEffect.Detail"/> and the player as its source.
    /// </summary>
    AddCondition,

    /// <summary>
    /// Lifts every <see cref="CardEffect.Condition"/> from the target region. A card that lifts conditions fits only a
    /// region with at least one of them.
    /// </summary>
    RemoveCondition,

    /// <summary>
    /// Lays <see cref="CardEffect.Court"/> on the target crown's court at magnitude <see cref="CardEffect.Amount"/> for
    /// <see cref="CardEffect.Weeks"/> weeks, with the player as its source. Only a crown has a court, and a plot needs the
    /// player to reach the crown's capital.
    /// </summary>
    AddCourtCondition,

    /// <summary>
    /// The target region changes allegiance to the player at control <see cref="Card.AnnexedControl"/> and is
    /// consolidated for <see cref="Card.ConsolidationWeeks"/> weeks, during which no seat may contest it. Its former
    /// seat's factors are expelled (its influence there goes to 0) and its relation with the player falls by 2.
    /// Only a crown may annex, and only where the region's control is 0 and the player holds more influence
    /// there than its current seat.
    /// </summary>
    Annex,

    /// <summary>
    /// A crown takes the target region if its control is 0 and it is not a capital, as an annexation takes one, with no
    /// further cost in relation: the two are at war already.
    /// </summary>
    Conquer,

    /// <summary>The target seat's relation with the leader cools by the amount: a coalition fracturing.</summary>
    Estrange,

    /// <summary>The target seat gains the amount of the resource, paid by no one: an arbiter's quiet funding.</summary>
    Fund,

    /// <summary>The player, a principal of the target war, sues for peace (<see cref="Wars.SueForPeace"/>).</summary>
    Peace,

    /// <summary>The player, a third seat, brokers a truce ending the target war (<see cref="Wars.Broker"/>).</summary>
    Broker,

    /// <summary>
    /// Offers the target seat a standing trade agreement on the targeted terms, chosen from <see cref="CardEffect.Gives"/>
    /// and <see cref="CardEffect.Asks"/>; the partner accepts or refuses it (<see cref="Trade"/>).
    /// </summary>
    Deal,
}

/// <summary>
/// One effect of a card. <see cref="Resource"/> is set for gaining and taking, <see cref="Office"/> for a challenge,
/// <see cref="Condition"/> or <see cref="Court"/> with <see cref="Weeks"/> and any <see cref="Detail"/> for a condition,
/// and the menus of terms <see cref="Gives"/> and <see cref="Asks"/> for a deal.
/// </summary>
public sealed record CardEffect(
    CardEffectKind Kind,
    int Amount = 0,
    Resource? Resource = null,
    Office? Office = null,
    ConditionKind? Condition = null,
    CourtConditionKind? Court = null,
    int Weeks = 0,
    string? Detail = null,
    IReadOnlyList<string>? Gives = null,
    IReadOnlyList<string>? Asks = null);

/// <summary>
/// A card: its family, what it costs, what it is played on, who sees it and what it does. A card of
/// <see cref="Weeks"/> above 0 is slow: it is paid when played and resolves that many Sundays later. A signature card
/// names its <see cref="Seat"/>, the only seat that draws it. A deck holds <see cref="Copies"/> of the card.
/// </summary>
public sealed record Card(
    string Id,
    string Name,
    CardFamily Family,
    int Coin,
    int Favour,
    int Force,
    CardTarget Target,
    Visibility Visibility,
    IReadOnlyList<CardEffect> Effects,
    int Weeks = 0,
    string? Seat = null,
    int Copies = 1)
{
    public const int AnnexInfluence = 1;
    public const int AnnexedControl = 2;
    public const int ConsolidationWeeks = 52;
    public const int WatchfulOrder = 4;

    public bool Affordable(Seat seat) => seat.Coin >= Coin && seat.Favour >= Favour && seat.Force >= Force;

    public int Cost(Resource resource) => resource switch
    {
        Resource.Coin => Coin,
        Resource.Favour => Favour,
        Resource.Force => Force,
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
    };

    /// <summary>
    /// The targets worth checking for <paramref name="seat"/>, in board order: every seat or region for a single
    /// target, for a pair each region the seat is present in with each of its borders, and for a deal every other seat
    /// with each pair of terms from the menus, grain from the region it would come from. <see cref="Fits"/> decides.
    /// </summary>
    public IEnumerable<IReadOnlyList<string>> Candidates(Board board, Seat seat) => Target switch
    {
        CardTarget.None => [[]],
        CardTarget.OtherSeat => board.Seats.Select(other => (IReadOnlyList<string>)[other.Id]),
        CardTarget.Leader => board.Leader is { } leader ? [[leader]] : [],
        CardTarget.LeaderFriend => Balance.LeadersFriends(board).Where(friend => friend != seat).Select(friend => (IReadOnlyList<string>)[friend.Id]),
        CardTarget.Underdog => seat.Arbiter ? Balance.Underdogs(board, seat).Select(underdog => (IReadOnlyList<string>)[underdog.Id]) : [],
        CardTarget.RegionPair => board.Regions.Where(region => board.Present(seat, region))
            .SelectMany(region => region.Borders.Select(border => (IReadOnlyList<string>)[region.Id, border])),
        CardTarget.RegionRival => board.Regions
            .SelectMany(region => region.Influence.Keys.Where(rival => rival != seat.Id).Select(rival => (IReadOnlyList<string>)[region.Id, rival])),
        CardTarget.FeudRival => board.Regions.SelectMany(region => board.Seats
            .Where(foe => Wars.Foe(board, seat.Id, foe.Id) && board.Present(foe, region)).Select(foe => (IReadOnlyList<string>)[region.Id, foe.Id])),
        CardTarget.Deal => board.Seats.Where(partner => partner != seat).SelectMany(partner => Offers(board, seat, partner)),
        CardTarget.War => board.Wars.Select(war => (IReadOnlyList<string>)[war.Id]),
        _ => board.Regions.Select(region => (IReadOnlyList<string>)[region.Id]),
    };

    /// <summary>
    /// What is wrong with the shape of <paramref name="targets"/> for this card, whatever the board's state: how many
    /// there are and whether each names a region (<paramref name="isRegion"/>) or seat (<paramref name="isSeat"/>) of the
    /// table, or a trade term, as its target kind says. A war is named by id and may have ended, which only makes the
    /// move fail on arrival.
    /// </summary>
    public string? TargetShapeProblem(IReadOnlyList<string> targets, Func<string, bool> isRegion, Func<string, bool> isSeat)
    {
        Func<string, bool>[] shape = Target switch
        {
            CardTarget.None => [],
            CardTarget.OtherSeat or CardTarget.Leader or CardTarget.LeaderFriend or CardTarget.Underdog => [isSeat],
            CardTarget.RegionPair => [isRegion, isRegion],
            CardTarget.RegionRival or CardTarget.FeudRival => [isRegion, isSeat],
            CardTarget.Deal => [isSeat, term => TradeTerm.Parse(term) is not null, term => TradeTerm.Parse(term) is not null],
            CardTarget.War => [_ => true],
            _ => [isRegion],
        };
        if (targets.Count != shape.Length) return $"has {targets.Count} targets where {Target} takes {shape.Length}";
        int bad = Enumerable.Range(0, shape.Length).FirstOrDefault(index => !shape[index](targets[index]), -1);
        return bad < 0 ? null : $"targets '{targets[bad]}', which is no {(Target == CardTarget.Deal && bad > 0 ? "trade term" : shape[bad] == isSeat ? "seat" : "region")} of the table";
    }

    /// <summary>Whether <paramref name="seat"/> can afford this card and play it on <paramref name="targets"/> now.</summary>
    public bool Playable(Board board, Seat seat, IReadOnlyList<string> targets) => Affordable(seat) && Fits(board, seat, targets);

    /// <summary>
    /// Whether <paramref name="targets"/> are a legal target for <paramref name="seat"/> now, cost aside: checked when a
    /// card is played and again when a slow card arrives.
    /// </summary>
    public bool Fits(Board board, Seat seat, IReadOnlyList<string> targets)
    {
        bool fits = Target switch
        {
            CardTarget.None => targets.Count == 0,
            CardTarget.OwnRegion => targets.Count == 1 && board.Region(targets[0]).Allegiance == seat.Id,
            CardTarget.ReachableRegion => targets.Count == 1 && board.Reaches(seat, board.Region(targets[0])),
            CardTarget.ContestedRegion => targets.Count == 1 && Contestable(board, seat, board.Region(targets[0])),
            CardTarget.OtherSeat => targets.Count == 1 && targets[0] != seat.Id,
            CardTarget.RegionPair => targets.Count == 2 && board.Present(seat, board.Region(targets[0]))
                && board.Region(targets[0]).Borders.Contains(targets[1]),
            CardTarget.RegionRival => targets.Count == 2 && targets[1] != seat.Id && board.Reaches(seat, board.Region(targets[0]))
                && board.Region(targets[0]).InfluenceOf(targets[1]) > 0,
            CardTarget.Deal => targets.Count == 3 && board.HasSeat(targets[0])
                && TradeTerm.Parse(targets[1]) is { } gives && TradeTerm.Parse(targets[2]) is { } asks
                && Trade.Fits(board, seat, board.Seat(targets[0]), gives, asks),
            CardTarget.EnemyFront => targets.Count == 1 && Wars.Against(board, seat, board.Region(targets[0])) is not null,
            CardTarget.OwnFront => targets.Count == 1 && Wars.OwnFront(board, seat, board.Region(targets[0])),
            CardTarget.War => targets.Count == 1 && board.HasWar(targets[0]),
            CardTarget.LeaderFriend => targets.Count == 1 && targets[0] != seat.Id && Balance.LeadersFriends(board).Any(friend => friend.Id == targets[0]),
            CardTarget.Underdog => targets.Count == 1 && seat.Arbiter && Balance.Underdogs(board, seat).Any(underdog => underdog.Id == targets[0]),
            CardTarget.Leader => targets.Count == 1 && targets[0] == board.Leader && targets[0] != seat.Id,
            CardTarget.FeudRival => targets.Count == 2 && board.HasSeat(targets[1]) && Wars.Foe(board, seat.Id, targets[1])
                && board.Reaches(seat, board.Region(targets[0])) && board.Present(board.Seat(targets[1]), board.Region(targets[0])),
            _ => false,
        };
        if (!fits) return false;
        CardEffect[] lifts = [.. Effects.Where(effect => effect.Kind == CardEffectKind.RemoveCondition)];
        if (lifts.Length > 0 && !board.Region(targets[0]).Conditions.Any(held => lifts.Any(lift => lift.Condition == held.Kind))) return false;
        foreach (CardEffect effect in Effects)
        {
            switch (effect.Kind)
            {
                case CardEffectKind.Influence when effect.Amount > 0 && board.PiecesLeft(seat) == 0:
                case CardEffectKind.ChallengeOffice when board.PiecesLeft(seat) == 0:
                case CardEffectKind.AddCourtCondition when board.Seat(targets[0]) is not { Kind: SeatKind.Crown, Home: { } home }
                    || !board.Reaches(seat, board.Region(home)):
                    return false;
                case CardEffectKind.Peace when !Wars.MaySue(board.War(targets[0]), seat):
                case CardEffectKind.Broker when !Wars.MayBroker(board, board.War(targets[0]), seat):
                    return false;
                case CardEffectKind.Annex:
                    if (seat.Kind != SeatKind.Crown) return false;
                    Region region = board.Region(targets[0]);
                    int own = region.InfluenceOf(seat.Id);
                    int holder = region.Allegiance is null ? 0 : region.InfluenceOf(region.Allegiance);
                    if (region.Control > 0 || own < AnnexInfluence || own <= holder) return false;
                    break;
            }
        }
        return true;
    }

    public void Pay(Board board, Seat seat)
    {
        board.AdjustResource(seat, Resource.Coin, -Coin);
        board.AdjustResource(seat, Resource.Favour, -Favour);
        board.AdjustResource(seat, Resource.Force, -Force);
    }

    /// <summary>Pays for the card and applies it at once, slow or not; returns whether its effects changed anything.</summary>
    public bool Play(Board board, Seat seat, IReadOnlyList<string> targets)
    {
        Pay(board, seat);
        return Apply(board, seat, targets);
    }

    /// <summary>
    /// Applies the card's effects; the caller has checked <see cref="Fits"/>. Returns whether it changed anything: a
    /// garrison in a region already at full control, or a challenge that falls short, does not.
    /// </summary>
    public bool Apply(Board board, Seat seat, IReadOnlyList<string> targets)
    {
        int before = board.ChangeCount;
        IReadOnlyList<Region> regions = TargetRegions(board, targets);
        War? front = Target == CardTarget.EnemyFront ? Wars.Against(board, seat, regions[0]) : null;
        string? quarrel = Target == CardTarget.FeudRival ? Wars.Quarrel(board, seat.Id, targets[1]) : front?.Detail;
        string? holder = regions.Count > 0 ? regions[0].Allegiance : null;
        foreach (CardEffect effect in Effects)
        {
            switch (effect.Kind)
            {
                case CardEffectKind.Control: foreach (Region region in regions) board.AdjustControl(region, effect.Amount); break;
                case CardEffectKind.Prosperity: foreach (Region region in regions) board.AdjustProsperity(region, effect.Amount); break;
                case CardEffectKind.Order: foreach (Region region in regions) board.AdjustOrder(region, effect.Amount); break;
                case CardEffectKind.Influence: board.AdjustInfluence(regions[0], seat, effect.Amount); break;
                case CardEffectKind.HolderInfluence:
                    if (regions[0].Allegiance is not null) board.AdjustInfluence(regions[0], board.Seat(regions[0].Allegiance!), effect.Amount);
                    break;
                case CardEffectKind.RivalInfluence: board.AdjustInfluence(regions[0], board.Seat(targets[1]), effect.Amount); break;
                case CardEffectKind.MoveInfluence:
                    board.MoveInfluence(regions[0], regions[1], seat, effect.Amount);
                    break;
                case CardEffectKind.ChallengeOffice: Challenge(board, seat, regions[0], effect.Office!.Value, effect.Amount); break;
                case CardEffectKind.Relation:
                    string? other = Target switch
                    {
                        CardTarget.OtherSeat or CardTarget.Leader => targets[0],
                        CardTarget.RegionRival or CardTarget.FeudRival => targets[1],
                        _ => regions[0].Allegiance,
                    };
                    if (other is not null && other != seat.Id) board.AdjustRelation(seat, board.Seat(other), effect.Amount);
                    break;
                case CardEffectKind.Gain: board.AdjustResource(seat, effect.Resource!.Value, effect.Amount); break;
                case CardEffectKind.Take:
                    Seat victim = board.Seat(Target == CardTarget.FeudRival ? targets[1] : targets[0]);
                    int taken = Math.Min(effect.Amount, victim.Amount(effect.Resource!.Value));
                    board.AdjustResource(victim, effect.Resource.Value, -taken);
                    board.AdjustResource(seat, effect.Resource.Value, taken);
                    break;
                case CardEffectKind.AddCondition:
                    string? detail = effect.Condition is ConditionKind.WarFront or ConditionKind.Skirmish ? quarrel ?? effect.Detail : effect.Detail;
                    board.AddCondition(regions[0], new RegionCondition(effect.Condition!.Value, detail, effect.Amount, seat.Id, board.Turn, board.Turn + effect.Weeks));
                    break;
                case CardEffectKind.RemoveCondition: board.RemoveConditions(regions[0], effect.Condition!.Value); break;
                case CardEffectKind.AddCourtCondition:
                    board.AddCourtCondition(board.Seat(targets[0]), new CourtCondition(effect.Court!.Value, effect.Amount, seat.Id, board.Turn, board.Turn + effect.Weeks));
                    break;
                case CardEffectKind.Annex:
                    if (TakeRegion(board, seat, regions[0]) is { } former) board.AdjustRelation(seat, former, -2);
                    break;
                case CardEffectKind.Conquer:
                    if (front?.Aim == WarAim.Conquest && seat.Kind == SeatKind.Crown && regions[0].Control == 0 && !regions[0].Capital)
                        TakeRegion(board, seat, regions[0]);
                    break;
                case CardEffectKind.Estrange: board.AdjustRelation(board.Seat(targets[0]), board.Seat(board.Leader!), -effect.Amount); break;
                case CardEffectKind.Fund: board.AdjustResource(board.Seat(targets[0]), effect.Resource!.Value, effect.Amount); break;
                case CardEffectKind.Peace: Wars.SueForPeace(board, board.War(targets[0]), seat); break;
                case CardEffectKind.Broker: Wars.Broker(board, board.War(targets[0]), seat); break;
                case CardEffectKind.Deal:
                    Trade.Propose(board, seat, board.Seat(targets[0]), TradeTerm.Parse(targets[1])!, TradeTerm.Parse(targets[2])!);
                    break;
                default: throw new InvalidOperationException($"card '{Id}' has unknown effect {effect.Kind}");
            }
        }
        bool changed = board.ChangeCount > before;
        if (changed && front is not null) Wars.Hurt(board, front, seat, holder, regions[0]);
        return changed;
    }

    /// <summary>
    /// <paramref name="seat"/> takes <paramref name="region"/>: it changes allegiance at control <see cref="AnnexedControl"/>
    /// and is consolidated for <see cref="ConsolidationWeeks"/> weeks, and its former seat's factors are expelled.
    /// Returns the former seat, if any.
    /// </summary>
    internal static Seat? TakeRegion(Board board, Seat seat, Region region)
    {
        Seat? former = region.Allegiance is null ? null : board.Seat(region.Allegiance);
        board.SetAllegiance(region, seat);
        board.AdjustControl(region, AnnexedControl - region.Control);
        board.Consolidate(region, board.Turn + ConsolidationWeeks);
        if (former is not null) board.AdjustInfluence(region, former, -region.InfluenceOf(former.Id));
        return former;
    }

    /// <summary>
    /// The only regions a card's effects change: the regions among <paramref name="targets"/>, or for a war, those a
    /// treaty ending it may change (<see cref="Wars.TreatyRegions"/>).
    /// </summary>
    public IReadOnlyList<Region> TargetRegions(Board board, IReadOnlyList<string> targets) => Target switch
    {
        CardTarget.None or CardTarget.OtherSeat or CardTarget.Deal or CardTarget.Leader or CardTarget.LeaderFriend or CardTarget.Underdog => [],
        CardTarget.War => [.. Wars.TreatyRegions(board, board.War(targets[0]))],
        CardTarget.RegionRival or CardTarget.FeudRival => [board.Region(targets[0])],
        _ => [.. targets.Select(board.Region)],
    };

    private IEnumerable<IReadOnlyList<string>> Offers(Board board, Seat seat, Seat partner)
    {
        CardEffect deal = Effects.Single(effect => effect.Kind == CardEffectKind.Deal);
        foreach (string give in deal.Gives!)
        {
            if (Trade.Resolve(board, TradeTerm.Parse(give)!, seat, partner) is not { } gives) continue;
            foreach (string ask in deal.Asks!)
            {
                if (Trade.Resolve(board, TradeTerm.Parse(ask)!, partner, seat) is { } asks && gives.Resource != asks.Resource)
                    yield return [partner.Id, gives.ToString(), asks.ToString()];
            }
        }
    }

    private static bool Contestable(Board board, Seat seat, Region region) =>
        !region.Capital && region.Allegiance != seat.Id && board.Turn >= region.ContestableFrom && board.Reaches(seat, region);

    private static void Challenge(Board board, Seat seat, Region region, Office office, int strength)
    {
        string? holder = region.Offices.GetValueOrDefault(office);
        if (holder == seat.Id || board.PiecesLeft(seat) == 0 || (holder is not null && board.Footholds(board.Seat(holder)) == 1)) return;
        int defence = 1 + (holder is null ? 0 : region.InfluenceOf(holder)) + (region.Order >= WatchfulOrder ? 1 : 0);
        if (region.InfluenceOf(seat.Id) + strength <= defence) return;
        board.SetOffice(region, office, seat);
        if (holder is not null) board.AdjustRelation(seat, board.Seat(holder), -1);
    }
}
