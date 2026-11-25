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

    /// <summary>The player takes up to the amount of the resource from the target seat.</summary>
    Take,

    /// <summary>
    /// The target region changes allegiance to the player at control <see cref="Card.AnnexedControl"/> and is
    /// consolidated for <see cref="Card.ConsolidationWeeks"/> weeks, during which no seat may contest it. Its former
    /// seat's factors are expelled (its influence there goes to 0) and its relation with the player falls by 2.
    /// Only a crown may annex, and only where the region's control is 0 and the player holds at least 2 influence
    /// there and more than its current seat.
    /// </summary>
    Annex,
}

/// <summary>One effect of a card. <see cref="Resource"/> is set for gaining and taking, <see cref="Office"/> for a challenge.</summary>
public sealed record CardEffect(CardEffectKind Kind, int Amount = 0, Resource? Resource = null, Office? Office = null);

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
    public const int AnnexInfluence = 2;
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
    /// target, and for a pair each region the seat is present in with each of its borders. <see cref="Fits"/> decides.
    /// </summary>
    public IEnumerable<IReadOnlyList<string>> Candidates(Board board, Seat seat) => Target switch
    {
        CardTarget.None => [[]],
        CardTarget.OtherSeat => board.Seats.Select(other => (IReadOnlyList<string>)[other.Id]),
        CardTarget.RegionPair => board.Regions.Where(region => board.Present(seat, region))
            .SelectMany(region => region.Borders.Select(border => (IReadOnlyList<string>)[region.Id, border])),
        CardTarget.RegionRival => board.Regions
            .SelectMany(region => region.Influence.Keys.Where(rival => rival != seat.Id).Select(rival => (IReadOnlyList<string>)[region.Id, rival])),
        _ => board.Regions.Select(region => (IReadOnlyList<string>)[region.Id]),
    };

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
            _ => false,
        };
        if (!fits) return false;
        foreach (CardEffect effect in Effects)
        {
            switch (effect.Kind)
            {
                case CardEffectKind.Influence when effect.Amount > 0 && board.PiecesPlaced(seat) >= Board.InfluencePieces:
                case CardEffectKind.ChallengeOffice when board.PiecesPlaced(seat) >= Board.InfluencePieces:
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
                    int moved = Math.Min(effect.Amount, Math.Min(regions[0].InfluenceOf(seat.Id), Board.InfluenceLimit - regions[1].InfluenceOf(seat.Id)));
                    board.AdjustInfluence(regions[1], seat, moved);
                    board.AdjustInfluence(regions[0], seat, -moved);
                    break;
                case CardEffectKind.ChallengeOffice: Challenge(board, seat, regions[0], effect.Office!.Value, effect.Amount); break;
                case CardEffectKind.Relation:
                    string? other = Target switch
                    {
                        CardTarget.OtherSeat => targets[0],
                        CardTarget.RegionRival => targets[1],
                        _ => regions[0].Allegiance,
                    };
                    if (other is not null && other != seat.Id) board.AdjustRelation(seat, board.Seat(other), effect.Amount);
                    break;
                case CardEffectKind.Gain: board.AdjustResource(seat, effect.Resource!.Value, effect.Amount); break;
                case CardEffectKind.Take:
                    Seat victim = board.Seat(targets[0]);
                    int taken = Math.Min(effect.Amount, victim.Amount(effect.Resource!.Value));
                    board.AdjustResource(victim, effect.Resource.Value, -taken);
                    board.AdjustResource(seat, effect.Resource.Value, taken);
                    break;
                case CardEffectKind.Annex:
                    Region annexed = regions[0];
                    Seat? former = annexed.Allegiance is null ? null : board.Seat(annexed.Allegiance);
                    board.SetAllegiance(annexed, seat);
                    board.AdjustControl(annexed, AnnexedControl - annexed.Control);
                    board.Consolidate(annexed, board.Turn + ConsolidationWeeks);
                    if (former is not null)
                    {
                        board.AdjustInfluence(annexed, former, -annexed.InfluenceOf(former.Id));
                        board.AdjustRelation(seat, former, -2);
                    }
                    break;
                default: throw new InvalidOperationException($"card '{Id}' has unknown effect {effect.Kind}");
            }
        }
        return board.ChangeCount > before;
    }

    /// <summary>The regions among <paramref name="targets"/>: the only regions a card's effects change.</summary>
    public IReadOnlyList<Region> TargetRegions(Board board, IReadOnlyList<string> targets) => Target switch
    {
        CardTarget.None or CardTarget.OtherSeat => [],
        CardTarget.RegionRival => [board.Region(targets[0])],
        _ => [.. targets.Select(board.Region)],
    };

    private static bool Contestable(Board board, Seat seat, Region region) =>
        !region.Capital && region.Allegiance != seat.Id && board.Turn >= region.ContestableFrom && board.Reaches(seat, region);

    private static void Challenge(Board board, Seat seat, Region region, Office office, int strength)
    {
        string? holder = region.Offices.GetValueOrDefault(office);
        if (holder == seat.Id || (holder is not null && board.Footholds(board.Seat(holder)) == 1)) return;
        int defence = 1 + (holder is null ? 0 : region.InfluenceOf(holder)) + (region.Order >= WatchfulOrder ? 1 : 0);
        if (region.InfluenceOf(seat.Id) + strength <= defence) return;
        board.SetOffice(region, office, seat);
        if (holder is not null) board.AdjustRelation(seat, board.Seat(holder), -1);
    }
}
