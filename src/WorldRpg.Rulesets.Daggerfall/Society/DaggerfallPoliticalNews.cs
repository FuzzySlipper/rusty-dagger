using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One piece of the board's news, spreading from <see cref="Origin"/> from the Sunday it happened.</summary>
internal sealed record DaggerfallPoliticalNewsItem(string EventId, string Headline, string Body, DaggerfallSiteRecord Origin, long Minute, int? FactionId);

/// <summary>
/// What of a board turn is news the towns hear: wars declared, the chronicle's arcs as they peak and settle, and the
/// public moves that change who holds a region or an office. Ordinary weekly moves are not news; the world sees them
/// through the condition tokens. Secret moves never are: their tokens show the symptom only. A war's end is told by
/// its arc, not by the move that ended it. Each piece spreads from the largest town of its region.
/// </summary>
internal sealed class DaggerfallPoliticalNews
{
    private readonly Board _board;
    private readonly IReadOnlyDictionary<string, Card> _cards;
    private readonly Func<string, int?> _faction;
    private readonly Func<string, DaggerfallSiteRecord?> _town;

    /// <summary>News of <paramref name="board"/>; <paramref name="town"/> is the town a board region's news leaves from, <paramref name="faction"/> a seat's office faction.</summary>
    internal DaggerfallPoliticalNews(Board board, CardSet cards, Func<string, DaggerfallSiteRecord?> town, Func<string, int?> faction)
    {
        _board = board;
        _cards = cards.Cards.ToDictionary(card => card.Id, StringComparer.Ordinal);
        _town = town;
        _faction = faction;
    }

    /// <summary>The news of the turn <paramref name="trace"/> recorded, played on minute <paramref name="minute"/>; <paramref name="warsBefore"/> are the wars in force before it.</summary>
    internal IReadOnlyList<DaggerfallPoliticalNewsItem> Of(TurnTrace trace, IReadOnlySet<string> warsBefore, long minute)
    {
        List<DaggerfallPoliticalNewsItem> news = [];
        void Add(string key, string headline, string body, string? region, string? seat)
        {
            if (region is not null && _town(region) is { } origin)
                news.Add(new($"politics:{trace.Turn}:{key}", headline, body, origin, minute, seat is null ? null : _faction(seat)));
        }

        foreach (War war in _board.Wars.Where(war => !warsBefore.Contains(war.Id)))
            Add($"war:{war.Id}", $"War Declared: {Name(war.A)} against {Name(war.B)}",
                war.Aim == WarAim.Conquest
                    ? $"{Name(war.A)} has gone to war with {Name(war.B)} over the lands along their border."
                    : $"{Name(war.A)} has gone to war with {Name(war.B)} over the provinces where both reach for power.",
                Home(war.A) ?? Home(war.B), war.A);

        foreach (ArcRecord arc in trace.Chronicle)
        {
            string key = $"arc:{arc.Key}:{arc.Phase}";
            switch (arc.Kind, arc.Phase)
            {
                case (ArcKind.War, ArcPhase.Peak):
                    Add(key, $"First Province Falls in the War of {Name(arc.Subjects[0])} and {Name(arc.Subjects[1])}",
                        $"The war between {Name(arc.Subjects[0])} and {Name(arc.Subjects[1])} has cost its first province. Neither court speaks of peace.",
                        Home(arc.Subjects[0]) ?? Home(arc.Subjects[1]), arc.Subjects[0]);
                    break;
                case (ArcKind.War, ArcPhase.Settled):
                    Add(key, $"Peace Between {Name(arc.Subjects[0])} and {Name(arc.Subjects[1])}",
                        $"A treaty ends the war between {Name(arc.Subjects[0])} and {Name(arc.Subjects[1])}. Soldiers are going home.",
                        Home(arc.Subjects[0]) ?? Home(arc.Subjects[1]), arc.Subjects[0]);
                    break;
                case (ArcKind.Blockade, ArcPhase.Peak):
                    Add(key, $"{RegionName(arc.Subjects[0])} Cut Off",
                        $"No grain has reached {RegionName(arc.Subjects[0])} for a month. Hunger stalks its towns, and whoever holds it draws no coin from it.",
                        arc.Subjects[0], null);
                    break;
                case (ArcKind.Blockade, ArcPhase.Settled):
                    Add(key, $"Grain Reaches {RegionName(arc.Subjects[0])} Again",
                        $"The wagons are rolling into {RegionName(arc.Subjects[0])} once more, and its markets fill.",
                        arc.Subjects[0], null);
                    break;
                case (ArcKind.Succession, ArcPhase.Peak):
                    Add(key, $"{Name(arc.Subjects[0])} Passes to a New Hand",
                        $"{Name(arc.Subjects[0])} has a new head, and its court is unsettled while the new hand finds its grip.",
                        Home(arc.Subjects[0]), arc.Subjects[0]);
                    break;
                case (ArcKind.Succession, ArcPhase.Settled):
                    Add(key, $"{Name(arc.Subjects[0])} Settles Under Its New Head",
                        $"The court of {Name(arc.Subjects[0])} has settled after its change of head.",
                        Home(arc.Subjects[0]), arc.Subjects[0]);
                    break;
                case (ArcKind.Revolt, ArcPhase.Peak):
                    Add(key, $"{RegionName(arc.Subjects[0])} Throws Off {Name(arc.Subjects[1])}",
                        $"{RegionName(arc.Subjects[0])} has risen against {Name(arc.Subjects[1])}. Its own lords hold its towns, and the crown's banners are down.",
                        arc.Subjects[0], null);
                    break;
                case (ArcKind.Revolt, ArcPhase.Settled):
                    Add(key, $"{RegionName(arc.Subjects[0])} Stands on Its Own",
                        $"A year after throwing off {Name(arc.Subjects[1])}, {RegionName(arc.Subjects[0])} answers to no crown.",
                        arc.Subjects[0], null);
                    break;
            }
        }

        foreach ((string id, string seat, string cardId, IReadOnlyList<string> targets) in Resolved(trace))
        {
            if (!_cards.TryGetValue(cardId, out Card? card) || card.Visibility != Visibility.Public || targets.Count == 0 || !_board.HasRegion(targets[0])) continue;
            string region = targets[0];
            foreach (CardEffect effect in card.Effects)
            {
                switch (effect.Kind)
                {
                    case CardEffectKind.Annex:
                        Add($"move:{id}", $"{RegionName(region)} Bows to {Name(seat)}",
                            $"{Name(seat)} has annexed {RegionName(region)}. Its banners fly over the province's towns.", region, seat);
                        break;
                    case CardEffectKind.Conquer:
                        Add($"move:{id}", $"{RegionName(region)} Taken by {Name(seat)}",
                            $"{Name(seat)} has taken {RegionName(region)} by force of arms.", region, seat);
                        break;
                    case CardEffectKind.ChallengeOffice when effect.Office is { } office:
                        Add($"move:{id}", $"New {Title(office)} in {RegionName(region)}",
                            $"A {Title(office).ToLowerInvariant()} answering to {Name(seat)} now holds office in {RegionName(region)}.", region, seat);
                        break;
                }
            }
        }
        return news;
    }

    /// <summary>The moves that took effect this turn: those played and resolved at once, and those that arrived and resolved.</summary>
    private static IEnumerable<(string Id, string Seat, string Card, IReadOnlyList<string> Targets)> Resolved(TurnTrace trace) =>
        trace.Arrivals.Where(arrival => arrival.Outcome == MoveOutcome.Resolved).Select(arrival => (arrival.Id, arrival.Seat, arrival.Card, arrival.Targets))
            .Concat(trace.Moves.Where(move => move.Arrives is null && move.Changes.Count > 0).Select(move => (move.Id, move.Seat, move.Card, move.Targets)));

    private string Name(string seat) => _board.Seat(seat).Name;

    private string RegionName(string region) => _board.Region(region).Name;

    private string? Home(string seat) => _board.Seat(seat).Home;

    private static string Title(Office office) => office switch
    {
        Office.Watch => "Watch Captain",
        Office.Factor => "Court Factor",
        Office.Temple => "Temple Head",
        _ => "Guild Master",
    };
}
