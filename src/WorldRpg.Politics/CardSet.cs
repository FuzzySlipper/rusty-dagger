using System.Text.Json;

namespace WorldRpg.Politics;

/// <summary>
/// The cards a table plays with: the shared families and every seat's signature cards, and the World deck no seat
/// plays. The canon set is <c>canon/iliac-bay.cards.json</c> beside this library. A seat's deck is every shared card of
/// its families and its own signature cards, each as many times as the card's copies; the World deck is every World
/// card, as many times as its copies. A set with no World cards turns none.
/// </summary>
public sealed record CardSet(IReadOnlyList<Card> Cards, IReadOnlyList<WorldCard>? World = null)
{
    /// <summary>The canon card set's file name in the build output.</summary>
    public const string CanonFile = "canon/iliac-bay.cards.json";

    public static CardSet Canon() => Load(Path.Combine(AppContext.BaseDirectory, CanonFile));

    /// <summary>Reads a card file and checks the cards on their own; <see cref="Problems"/> checks them against a table.</summary>
    public static CardSet Load(string path)
    {
        CardSet? set;
        try
        {
            set = JsonSerializer.Deserialize(File.ReadAllBytes(path), PoliticsJson.Default.CardSet);
        }
        catch (JsonException exception)
        {
            throw new BoardSetupException(path, [exception.Message]);
        }
        if (set is null) throw new BoardSetupException(path, ["the file holds no cards"]);
        IReadOnlyList<string> problems = set.Problems(null);
        if (problems.Count > 0) throw new BoardSetupException(path, problems);
        return set;
    }

    public Card ById(string id) => Cards.Single(card => card.Id == id);

    /// <summary>
    /// The standing cards no seat draws: the cards of open conflict (<see cref="CardFamily.War"/>) and the arbiter's
    /// countermoves (<see cref="CardFamily.Arbitration"/>).
    /// </summary>
    public IEnumerable<Card> StandingCards() => Cards.Where(Standing);

    private static bool Standing(Card card) => card.Family is CardFamily.War or CardFamily.Arbitration;

    /// <summary>The World deck before its shuffle, in card set order.</summary>
    public IEnumerable<WorldCard> WorldDeck() => (World ?? []).SelectMany(card => Enumerable.Repeat(card, card.Copies));

    /// <summary>The deck <paramref name="seat"/> starts with, in card set order before its shuffle. No seat draws the standing cards.</summary>
    public IEnumerable<Card> Deck(SeatSetup seat) => Cards
        .Where(card => !Standing(card) && (card.Seat is null ? seat.Families.Contains(card.Family) : card.Seat == seat.Id))
        .SelectMany(card => Enumerable.Repeat(card, card.Copies));

    /// <summary>
    /// Everything wrong with these cards, and, given a <paramref name="table"/>, with the decks they make for it: each
    /// signature card's seat must be at the table and each seat's deck must fill a hand.
    /// </summary>
    public IReadOnlyList<string> Problems(BoardSetup? table)
    {
        List<string> problems = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (Card card in Cards)
        {
            if (!ids.Add(card.Id)) problems.Add($"card '{card.Id}' is listed twice");
            foreach ((string name, int value) in new[] { ("coin", card.Coin), ("favour", card.Favour), ("force", card.Force) })
                if (value is < 0 or > Board.ResourceLimit) problems.Add($"card '{card.Id}' costs {value} {name}, outside 0..{Board.ResourceLimit}");
            if (card.Weeks is < 0 or > Card.ConsolidationWeeks) problems.Add($"card '{card.Id}' takes {card.Weeks} weeks, outside 0..{Card.ConsolidationWeeks}");
            if (card.Copies < 1) problems.Add($"card '{card.Id}' has {card.Copies} copies");
            if (card.Effects.Count == 0) problems.Add($"card '{card.Id}' does nothing");
            if (card.Effects.Count(effect => effect.Kind == CardEffectKind.Deal) > 1) problems.Add($"card '{card.Id}' offers more than one deal");
            if (Standing(card) && card.Seat is not null) problems.Add($"card '{card.Id}' is a standing {card.Family} card and no seat's signature");
            foreach (CardEffect effect in card.Effects)
                if (EffectProblem(card, effect) is { } problem) problems.Add($"card '{card.Id}' {effect.Kind}: {problem}");
        }
        HashSet<string> worldIds = new(StringComparer.Ordinal);
        foreach (WorldCard card in World ?? [])
        {
            if (!worldIds.Add(card.Id)) problems.Add($"world card '{card.Id}' is listed twice");
            if (card.Magnitude is < 1 or > Conditions.MaxMagnitude) problems.Add($"world card '{card.Id}' magnitude {card.Magnitude} is outside 1..{Conditions.MaxMagnitude}");
            if (card.Weeks < 1) problems.Add($"world card '{card.Id}' lasts no weeks");
            if (card.Copies < 1) problems.Add($"world card '{card.Id}' has {card.Copies} copies");
            if (ConditionDetailProblem(card.Condition, card.Detail) is { } detail) problems.Add($"world card '{card.Id}': {detail}");
        }
        if (table is null) return problems;
        HashSet<string> seats = [.. table.Seats.Select(seat => seat.Id)];
        foreach (Card card in Cards)
            foreach (CardEffect effect in card.Effects.Where(effect => effect.Condition == ConditionKind.Persecution && effect.Detail is not null && !seats.Contains(effect.Detail)))
                problems.Add($"card '{card.Id}' persecutes unknown seat '{effect.Detail}'");
        foreach (WorldCard card in (World ?? []).Where(card => card.Condition == ConditionKind.Persecution && card.Detail is not null && !seats.Contains(card.Detail)))
            problems.Add($"world card '{card.Id}' persecutes unknown seat '{card.Detail}'");
        foreach (Card card in Cards)
            if (card.Seat is not null && table.Seats.All(seat => seat.Id != card.Seat))
                problems.Add($"card '{card.Id}' is the signature of unknown seat '{card.Seat}'");
        foreach (SeatSetup seat in table.Seats)
        {
            int size = Deck(seat).Count();
            if (size < Board.HandSize) problems.Add($"seat '{seat.Id}' draws from {size} cards, fewer than a hand of {Board.HandSize}");
        }
        return problems;
    }

    /// <summary>
    /// What is wrong with a condition's detail: price surges, monster surges and persecutions name what they concern;
    /// a war front's or a skirmish's seats are named when a war or feud lays it; the rest name nothing. A persecuted seat is checked against
    /// the table.
    /// </summary>
    internal static string? ConditionDetailProblem(ConditionKind kind, string? detail)
    {
        bool needs = kind is ConditionKind.PriceSurge or ConditionKind.MonsterSurge or ConditionKind.Persecution;
        return needs && string.IsNullOrWhiteSpace(detail) ? $"a {kind} names no detail"
            : !needs && kind is not (ConditionKind.WarFront or ConditionKind.Skirmish) && detail is not null ? $"a {kind} names a detail it does not use"
            : null;
    }

    /// <summary>What is wrong with a menu of deal terms: it offers none, or one is not a term a card can offer.</summary>
    private static string? TermsProblem(IReadOnlyList<string>? menu)
    {
        if (menu is null || menu.Count == 0) return "offers an empty menu of terms";
        foreach (string text in menu)
        {
            TradeTerm? term = TradeTerm.Parse(text);
            if (term is null || term.Source is not null) return $"term '{text}' is not grain or resource:amount/weeks";
            if (term.Amount is < 1 or > Board.ResourceLimit) return $"term '{text}' moves an amount outside 1..{Board.ResourceLimit}";
            if (term.EveryWeeks is < 1 or > Trade.Horizon) return $"term '{text}' falls due outside every 1..{Trade.Horizon} weeks";
        }
        return null;
    }

    private static string? EffectProblem(Card card, CardEffect effect)
    {
        bool region = card.Target is CardTarget.OwnRegion or CardTarget.ReachableRegion or CardTarget.ContestedRegion or CardTarget.RegionRival
            or CardTarget.EnemyFront or CardTarget.OwnFront or CardTarget.FeudRival;
        bool needsResource = effect.Kind is CardEffectKind.Gain or CardEffectKind.Take or CardEffectKind.Fund;
        bool needsOffice = effect.Kind == CardEffectKind.ChallengeOffice;
        bool needsCondition = effect.Kind is CardEffectKind.AddCondition or CardEffectKind.RemoveCondition;
        bool needsCourt = effect.Kind == CardEffectKind.AddCourtCondition;
        bool lasts = effect.Kind is CardEffectKind.AddCondition or CardEffectKind.AddCourtCondition;
        bool deal = effect.Kind == CardEffectKind.Deal;
        if (deal != (card.Target == CardTarget.Deal)) return deal ? "needs a deal target" : "cannot be played on a deal";
        if (deal != (effect.Gives is not null || effect.Asks is not null)) return deal ? "names no terms" : "names terms it does not use";
        if (deal && (TermsProblem(effect.Gives) ?? TermsProblem(effect.Asks)) is { } terms) return terms;
        if (needsResource != effect.Resource.HasValue) return needsResource ? "names no resource" : "names a resource it does not use";
        if (needsOffice != effect.Office.HasValue) return needsOffice ? "names no office" : "names an office it does not use";
        if (needsCondition != effect.Condition.HasValue) return needsCondition ? "names no condition" : "names a condition it does not use";
        if (needsCourt != effect.Court.HasValue) return needsCourt ? "names no court condition" : "names a court condition it does not use";
        if (lasts && effect.Weeks < 1) return "lasts no weeks";
        if (!lasts && effect.Weeks != 0) return "names weeks it does not use";
        if (lasts && effect.Amount is < 1 or > Conditions.MaxMagnitude) return $"magnitude {effect.Amount} is outside 1..{Conditions.MaxMagnitude}";
        if (effect.Kind != CardEffectKind.AddCondition && effect.Detail is not null) return "names a detail it does not use";
        if (effect.Kind == CardEffectKind.AddCondition && ConditionDetailProblem(effect.Condition!.Value, effect.Detail) is { } detail) return detail;
        return effect.Kind switch
        {
            CardEffectKind.Control or CardEffectKind.Prosperity or CardEffectKind.Order when !region && card.Target != CardTarget.RegionPair =>
                "needs a region target",
            CardEffectKind.Influence or CardEffectKind.HolderInfluence or CardEffectKind.ChallengeOffice when !region => "needs a single region target",
            CardEffectKind.MoveInfluence when card.Target != CardTarget.RegionPair => "needs a region pair",
            CardEffectKind.RivalInfluence when card.Target is not (CardTarget.RegionRival or CardTarget.FeudRival) => "needs a rival in a region",
            CardEffectKind.Relation when card.Target is CardTarget.None or CardTarget.RegionPair or CardTarget.War or CardTarget.LeaderFriend or CardTarget.Underdog => "needs a seat or a single region target",
            CardEffectKind.Take when card.Target is not (CardTarget.OtherSeat or CardTarget.FeudRival or CardTarget.Leader) => "needs a seat target",
            CardEffectKind.Annex when card.Target != CardTarget.ContestedRegion => "needs a contested region",
            CardEffectKind.Conquer when card.Target != CardTarget.EnemyFront => "needs an enemy front",
            CardEffectKind.Peace or CardEffectKind.Broker when card.Target != CardTarget.War => "needs a war target",
            CardEffectKind.Estrange when card.Target != CardTarget.LeaderFriend => "needs a friend of the leader",
            CardEffectKind.Fund when card.Target is not (CardTarget.Underdog or CardTarget.OtherSeat) => "needs a seat target",
            CardEffectKind.Estrange or CardEffectKind.Fund when effect.Amount < 1 => "moves nothing",
            CardEffectKind.AddCondition or CardEffectKind.RemoveCondition when !region => "needs a single region target",
            CardEffectKind.AddCourtCondition when card.Target != CardTarget.OtherSeat => "needs a seat target",
            CardEffectKind.MoveInfluence or CardEffectKind.Take when effect.Amount < 1 => "moves nothing",
            CardEffectKind.ChallengeOffice when effect.Amount < 0 => "has a negative strength",
            _ => null,
        };
    }
}
