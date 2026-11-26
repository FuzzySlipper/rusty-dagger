using System.Globalization;

namespace WorldRpg.Politics;

/// <summary>
/// A feud between two crowns, <see cref="A"/> and <see cref="B"/>, since turn <see cref="Started"/>: hostility below
/// war, with no banners called and no land changing hands. <see cref="Heat"/> counts the blows between them towards
/// the next cooling of their relation (<see cref="Feuds"/>).
/// </summary>
public sealed record Feud(string Id, string A, string B, int Started, int Heat)
{
    /// <summary>The detail of the skirmish conditions the feud lays: its two crowns.</summary>
    public string Detail => $"{A},{B}";

    public bool Principal(string seat) => seat == A || seat == B;

    public bool Between(string a, string b) => Principal(a) && Principal(b) && a != b;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Id} since {Started}");
}

/// <summary>
/// Feuds: border scuffles, raids and vassals harassing each other, below the threshold of war.
/// <list type="bullet">
/// <item>Two crowns at <see cref="FeudRelation"/> or below whose influence meets (<see cref="Wars.Meet"/>), and not at
/// war together, are in a feud. It ends when their relation rises above that, when their influence no longer meets, or
/// when it becomes a war.</item>
/// <item>A feud holds no relation against a gesture, which lifts it and cools the feud. The seasonal drift towards 0
/// lifts it only once the feud has gone quiet, its heat died down to 0, so a feud lasts while blows are being struck.</item>
/// <item>Each blow between the two, a feud card or any public move by which one struck the other (<see cref="Memory"/>),
/// heats the feud instead of cooling their relation at once. Every <see cref="BlowsToCool"/>th blow cools it by 1, so a
/// hot feud reaches war's relation and becomes a war. Heat dies down by 1 every <see cref="CoolingWeeks"/> weeks.</item>
/// <item>Surly vassals: each week, each pair of bordering regions held by the feuding crowns skirmishes on its own,
/// with no seat moving, one week in <see cref="SkirmishChance"/>, or one in <see cref="RestlessChance"/> where either
/// region's order is <see cref="RestlessOrder"/> or less. A skirmish lays a <see cref="ConditionKind.Skirmish"/> on both
/// regions for <see cref="SkirmishWeeks"/> weeks and is a blow.</item>
/// </list>
/// </summary>
public static class Feuds
{
    public const int FeudRelation = -2;
    public const int BlowsToCool = 3;
    public const int CoolingWeeks = 4;
    public const int SkirmishWeeks = 4;
    public const int SkirmishChance = 8;
    public const int RestlessChance = 4;
    public const int RestlessOrder = 2;

    /// <summary>The feud between <paramref name="a"/> and <paramref name="b"/>, if any.</summary>
    public static Feud? Between(Board board, string a, string b) => board.Feuds.FirstOrDefault(feud => feud.Between(a, b));

    /// <summary>The week's heat dying down and vassals skirmishing, before wars open.</summary>
    public static void Weigh(Board board)
    {
        foreach (Feud feud in board.Feuds.ToArray())
        {
            if ((board.Turn - feud.Started) % CoolingWeeks == 0 && feud.Heat > 0) board.AddHeat(feud, -1);
            foreach (Region region in board.Regions.Where(region => region.Allegiance == feud.A))
            {
                foreach (Region border in region.Borders.Select(board.Region).Where(border => border.Allegiance == feud.B))
                {
                    int chance = Math.Min(region.Order, border.Order) <= RestlessOrder ? RestlessChance : SkirmishChance;
                    if (board.Random.Next(chance) != 0) continue;
                    foreach (Region skirmished in new[] { region, border })
                        board.AddCondition(skirmished, new RegionCondition(ConditionKind.Skirmish, feud.Detail, 1, null, board.Turn, board.Turn + SkirmishWeeks));
                    Blow(board, feud);
                }
            }
        }
    }

    /// <summary>Ends the feuds that have cooled or parted, then opens a feud between every two crowns that have fallen into one.</summary>
    public static void Open(Board board)
    {
        foreach (Feud feud in board.Feuds.ToArray())
        {
            string? ended = board.Relation(feud.A, feud.B) > FeudRelation ? "cooled"
                : !Wars.Meet(board, [feud.A], [feud.B]).Any() ? "parted"
                : null;
            if (ended is not null) board.EndFeud(feud, ended);
        }
        foreach ((Seat a, Seat b) in Wars.CrownPairs(board))
        {
            if (board.Relation(a.Id, b.Id) > FeudRelation || board.WarBetween(a.Id, b.Id) is not null || Between(board, a.Id, b.Id) is not null
                || !Wars.Meet(board, [a.Id], [b.Id]).Any())
                continue;
            board.OpenFeud(a, b);
        }
    }

    /// <summary>
    /// <paramref name="mover"/> has struck <paramref name="struck"/>: a blow in their feud if they have one, or else their
    /// relation cools by 1.
    /// </summary>
    public static void Struck(Board board, Seat mover, Seat struck)
    {
        if (Between(board, mover.Id, struck.Id) is { } feud) Blow(board, feud);
        else board.AdjustRelation(mover, struck, -1);
    }

    /// <summary>A blow in <paramref name="feud"/>: it heats, and every <see cref="BlowsToCool"/>th blow cools the crowns' relation by 1.</summary>
    private static void Blow(Board board, Feud feud)
    {
        feud = board.Feud(feud.Id);
        if (feud.Heat + 1 < BlowsToCool)
        {
            board.AddHeat(feud, 1);
            return;
        }
        board.AddHeat(feud, -feud.Heat);
        board.AdjustRelation(board.Seat(feud.A), board.Seat(feud.B), -1);
    }
}
