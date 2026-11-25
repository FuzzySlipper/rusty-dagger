namespace WorldRpg.Politics;

/// <summary>What a card may be played on.</summary>
public enum CardTarget
{
    /// <summary>Played without a target.</summary>
    None,

    /// <summary>A region owing allegiance to the player.</summary>
    OwnRegion,

    /// <summary>A region the player holds or borders.</summary>
    ReachableRegion,

    /// <summary>
    /// A region the player borders that is not a capital, does not owe it allegiance and is not consolidated after an
    /// annexation.
    /// </summary>
    ContestedRegion,

    /// <summary>Another seat.</summary>
    OtherSeat,
}

/// <summary>The fixed set of things a card can do. Amounts apply to the target region or to the player.</summary>
public enum CardEffectKind
{
    /// <summary>The target region's control.</summary>
    Control,

    /// <summary>The target region's prosperity.</summary>
    Prosperity,

    /// <summary>The target region's order.</summary>
    Order,

    /// <summary>The player's influence in the target region.</summary>
    Influence,

    /// <summary>The influence of the target region's seat in that region.</summary>
    HolderInfluence,

    /// <summary>The player's relation with the target seat, or with the seat the target region owes allegiance to.</summary>
    Relation,

    /// <summary>The player's coin.</summary>
    Coin,

    /// <summary>The player's favour.</summary>
    Favour,

    /// <summary>The player's force.</summary>
    Force,

    /// <summary>The player's scarcest resource.</summary>
    Bank,

    /// <summary>
    /// The target region changes allegiance to the player at control <see cref="Card.AnnexedControl"/> and is
    /// consolidated for <see cref="Card.ConsolidationWeeks"/> weeks, during which no seat may contest it. Its former
    /// seat's factors are expelled (its influence there goes to 0) and its relation with the player falls by 2.
    /// Only playable where the region's control is 0 and the player holds at least 2 influence
    /// there and more than its current seat.
    /// </summary>
    Annex,
}

public sealed record CardEffect(CardEffectKind Kind, int Amount = 0);

/// <summary>A card: what it costs, what it is played on and what it does.</summary>
public sealed record Card(string Id, string Name, int Coin, int Favour, int Force, CardTarget Target, IReadOnlyList<CardEffect> Effects)
{
    public const int AnnexInfluence = 2;
    public const int AnnexedControl = 2;
    public const int ConsolidationWeeks = 52;

    public bool Affordable(Seat seat) => seat.Coin >= Coin && seat.Favour >= Favour && seat.Force >= Force;

    /// <summary>Whether <paramref name="seat"/> can play this card on <paramref name="target"/> now.</summary>
    public bool Playable(Board board, Seat seat, string? target)
    {
        if (!Affordable(seat)) return false;
        bool fits = Target switch
        {
            CardTarget.None => target is null,
            CardTarget.OwnRegion => target is not null && board.Region(target).Allegiance == seat.Id,
            CardTarget.ReachableRegion => target is not null && board.Reaches(seat, board.Region(target)),
            CardTarget.ContestedRegion => target is not null && !board.Region(target).Capital && board.Region(target).Allegiance != seat.Id
                && board.Turn >= board.Region(target).ContestableFrom && board.Reaches(seat, board.Region(target)),
            CardTarget.OtherSeat => target is not null && target != seat.Id,
            _ => false,
        };
        if (!fits) return false;
        if (Effects.Any(effect => effect.Kind == CardEffectKind.Annex))
        {
            Region region = board.Region(target!);
            int own = region.InfluenceOf(seat.Id);
            int holder = region.Allegiance is null ? 0 : region.InfluenceOf(region.Allegiance);
            if (region.Control > 0 || own < AnnexInfluence || own <= holder) return false;
        }
        return true;
    }

    /// <summary>
    /// Pays for and applies the card; the caller has checked <see cref="Playable"/>. Returns whether it changed
    /// anything beyond its payment: a garrison in a region already at full control does not.
    /// </summary>
    public bool Play(Board board, Seat seat, string? target)
    {
        board.AdjustResource(seat, Resource.Coin, -Coin);
        board.AdjustResource(seat, Resource.Favour, -Favour);
        board.AdjustResource(seat, Resource.Force, -Force);
        int paid = board.ChangeCount;
        Region? region = Target is CardTarget.OwnRegion or CardTarget.ReachableRegion or CardTarget.ContestedRegion ? board.Region(target!) : null;
        foreach (CardEffect effect in Effects)
        {
            switch (effect.Kind)
            {
                case CardEffectKind.Control: board.AdjustControl(region!, effect.Amount); break;
                case CardEffectKind.Prosperity: board.AdjustProsperity(region!, effect.Amount); break;
                case CardEffectKind.Order: board.AdjustOrder(region!, effect.Amount); break;
                case CardEffectKind.Influence: board.AdjustInfluence(region!, seat, effect.Amount); break;
                case CardEffectKind.HolderInfluence:
                    if (region!.Allegiance is not null) board.AdjustInfluence(region, board.Seat(region.Allegiance), effect.Amount);
                    break;
                case CardEffectKind.Relation:
                    string? other = region is null ? target : region.Allegiance;
                    if (other is not null && other != seat.Id) board.AdjustRelation(seat, board.Seat(other), effect.Amount);
                    break;
                case CardEffectKind.Coin: board.AdjustResource(seat, Resource.Coin, effect.Amount); break;
                case CardEffectKind.Favour: board.AdjustResource(seat, Resource.Favour, effect.Amount); break;
                case CardEffectKind.Force: board.AdjustResource(seat, Resource.Force, effect.Amount); break;
                case CardEffectKind.Bank:
                    Resource scarcest = Enum.GetValues<Resource>().MinBy(seat.Amount);
                    board.AdjustResource(seat, scarcest, 1);
                    break;
                case CardEffectKind.Annex:
                    Seat? former = region!.Allegiance is null ? null : board.Seat(region.Allegiance);
                    board.SetAllegiance(region, seat);
                    board.AdjustControl(region, AnnexedControl - region.Control);
                    board.Consolidate(region, board.Turn + ConsolidationWeeks);
                    if (former is not null)
                    {
                        board.AdjustInfluence(region, former, -region.InfluenceOf(former.Id));
                        board.AdjustRelation(seat, former, -2);
                    }
                    break;
                default: throw new InvalidOperationException($"card '{Id}' has unknown effect {effect.Kind}");
            }
        }
        return board.ChangeCount > paid;
    }
}

/// <summary>
/// The starting card set every seat may play each week. Decks, hands and seat-specific cards come later (#11058),
/// when cards become content.
/// </summary>
public static class StarterCards
{
    public static readonly Card Bank = new("bank", "Bank", 0, 0, 0, CardTarget.None, [new(CardEffectKind.Bank)]);

    public static readonly IReadOnlyList<Card> All =
    [
        new("garrison", "Garrison", 0, 0, 2, CardTarget.OwnRegion, [new(CardEffectKind.Control, 1)]),
        new("keep-the-peace", "Keep the Peace", 1, 0, 0, CardTarget.OwnRegion, [new(CardEffectKind.Order, 1)]),
        new("trade-charter", "Trade Charter", 2, 0, 0, CardTarget.OwnRegion, [new(CardEffectKind.Prosperity, 1)]),
        new("levy", "Levy", 0, 0, 0, CardTarget.OwnRegion,
            [new(CardEffectKind.Coin, 2), new(CardEffectKind.Force, 1), new(CardEffectKind.Order, -1)]),
        new("install-factor", "Install a Factor", 0, 2, 0, CardTarget.ReachableRegion, [new(CardEffectKind.Influence, 1)]),
        new("discredit", "Discredit", 0, 2, 0, CardTarget.ContestedRegion,
            [new(CardEffectKind.HolderInfluence, -1), new(CardEffectKind.Order, -1), new(CardEffectKind.Relation, -1)]),
        new("press-claim", "Press a Claim", 0, 1, 1, CardTarget.ContestedRegion,
            [new(CardEffectKind.Control, -1), new(CardEffectKind.Relation, -1)]),
        new("annex", "Annex", 0, 0, 4, CardTarget.ContestedRegion, [new(CardEffectKind.Annex)]),
        new("envoy", "Envoy", 0, 1, 0, CardTarget.OtherSeat, [new(CardEffectKind.Relation, 1)]),
        Bank,
    ];
}
