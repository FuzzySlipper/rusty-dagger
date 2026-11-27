namespace WorldRpg.Politics;

/// <summary>What kind of multi-week arc the chronicle names.</summary>
public enum ArcKind
{
    /// <summary>A war: it peaks when its first region falls and settles at its treaty.</summary>
    War,

    /// <summary>A region cut off from food: it peaks after <see cref="Chronicle.BlockadeWeeks"/> weeks and settles when the region is fed.</summary>
    Blockade,

    /// <summary>A seat whose holder changed: it peaks at the change and settles <see cref="Nudges.SuccessionWeeks"/> weeks later.</summary>
    Succession,

    /// <summary>
    /// A region breaking free of its liege: it peaks the week it breaks free and settles <see cref="Revolts.FreedWeeks"/>
    /// weeks later, once its own lords hold it. Its subjects are the region and the crown it threw off.
    /// </summary>
    Revolt,
}

/// <summary>Whether a chronicle entry marks an arc's peak or its settling.</summary>
public enum ArcPhase
{
    Peak,
    Settled,
}

/// <summary>
/// An arc the chronicle is following: its kind, its key (a war's id, or the region or seat and the week it began), the
/// seats or regions it concerns, the week it began, whether it has peaked, and for a succession the week it settles.
/// </summary>
public sealed record OpenArc(ArcKind Kind, string Key, IReadOnlyList<string> Subjects, int Started, bool Peaked, int? Settles = null);

/// <summary>
/// One chronicle entry: an arc named in the week <see cref="Turn"/> it peaked or settled, with the key the world's
/// headlines are looked up by (<c>politics.arc.&lt;kind&gt;.&lt;phase&gt;</c>).
/// </summary>
public sealed record ArcRecord(ArcKind Kind, string Key, ArcPhase Phase, IReadOnlyList<string> Subjects, int Started, int Turn)
{
    public string HeadlineKey => $"politics.arc.{Kind.ToString().ToLowerInvariant()}.{Phase.ToString().ToLowerInvariant()}";
}

/// <summary>
/// Names the board's multi-week arcs when they peak or settle, so fifty years read as chapters: wars, blockades,
/// successions and revolts. It runs once every seat has moved and the wars are checked, reading the board as it stands.
/// </summary>
public static class Chronicle
{
    public const int BlockadeWeeks = 4;

    /// <summary>Follows the week's arcs and returns the entries named this week.</summary>
    public static IReadOnlyList<ArcRecord> Check(Board board)
    {
        List<ArcRecord> named = [];
        foreach (War war in board.Wars)
        {
            if (board.Arc(war.Id) is null) board.FollowArc(new OpenArc(ArcKind.War, war.Id, [war.A, war.B], war.Started, false));
            if (war.FirstLoss is not null && board.Arc(war.Id) is { Peaked: false } arc) Peak(board, arc, named);
        }
        foreach (Region region in board.Regions)
        {
            OpenArc? arc = board.Arcs.FirstOrDefault(open => open.Kind == ArcKind.Blockade && open.Subjects[0] == region.Id);
            if (arc is null && region.CutWeeks >= BlockadeWeeks)
            {
                arc = new OpenArc(ArcKind.Blockade, $"{region.Id}:{board.Turn - region.CutWeeks + 1}", [region.Id], board.Turn - region.CutWeeks + 1, false);
                board.FollowArc(arc);
                Peak(board, arc, named);
            }
        }
        foreach (OpenArc arc in board.Arcs.ToArray())
        {
            if (arc.Kind is ArcKind.Succession or ArcKind.Revolt && !arc.Peaked) Peak(board, arc, named);
            bool settled = arc.Kind switch
            {
                ArcKind.War => !board.HasWar(arc.Key),
                ArcKind.Blockade => !board.Region(arc.Subjects[0]).Cut,
                _ => board.Turn >= arc.Settles,
            };
            if (!settled) continue;
            board.DropArc(arc);
            named.Add(new ArcRecord(arc.Kind, arc.Key, ArcPhase.Settled, arc.Subjects, arc.Started, board.Turn));
        }
        return named;
    }

    private static void Peak(Board board, OpenArc arc, List<ArcRecord> named)
    {
        board.DropArc(arc);
        board.FollowArc(arc with { Peaked = true });
        named.Add(new ArcRecord(arc.Kind, arc.Key, ArcPhase.Peak, arc.Subjects, arc.Started, board.Turn));
    }
}
