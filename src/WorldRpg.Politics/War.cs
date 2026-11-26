using System.Globalization;

namespace WorldRpg.Politics;

/// <summary>What a war is fought over, fixed when it opens (<see cref="Wars"/>).</summary>
public enum WarAim
{
    /// <summary>Land: the principals' lands border, its fronts are the land where they meet and a treaty may cede a region.</summary>
    Conquest,

    /// <summary>
    /// A grievance where the principals' influence meets: its fronts are the regions where they meet, no region is
    /// taken, and a treaty takes the loser's factors and hostages instead of land.
    /// </summary>
    Grievance,
}

/// <summary>
/// A war in force between two crowns, <see cref="A"/> and <see cref="B"/>, the war's principals, over <see cref="Aim"/>,
/// each with the allies fighting on its side, since turn <see cref="Started"/>. Each principal's weariness counts
/// towards the treaty that ends it (<see cref="Wars"/>). Allies grow no weary; they fight until their principals make peace.
/// </summary>
public sealed record War(string Id, string A, string B, WarAim Aim, IReadOnlyList<string> AlliesOfA, IReadOnlyList<string> AlliesOfB,
    int Started, int WearinessOfA, int WearinessOfB)
{
    /// <summary>The detail of the war front conditions the war lays: its principals.</summary>
    public string Detail => $"{A},{B}";

    public bool Principal(string seat) => seat == A || seat == B;

    /// <summary>The principal <paramref name="seat"/> fights for, itself or its ally, or null if it is not in the war.</summary>
    public string? Side(string seat) =>
        seat == A || AlliesOfA.Contains(seat) ? A
        : seat == B || AlliesOfB.Contains(seat) ? B
        : null;

    public bool Fights(string seat) => Side(seat) is not null;

    /// <summary>The other principal.</summary>
    public string Enemy(string principal) => principal == A ? B : A;

    public int Weariness(string principal) => principal == A ? WearinessOfA : WearinessOfB;

    /// <summary>The principal and the allies on its side.</summary>
    public IEnumerable<string> SideOf(string principal) => principal == A ? [A, .. AlliesOfA] : [B, .. AlliesOfB];

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Id} since {Started}");
}

/// <summary>The truce after a treaty between two crowns: no war opens between them before turn <see cref="Until"/>.</summary>
public sealed record Truce(string A, string B, int Until)
{
    public bool Binds(string a, string b) => (A == a && B == b) || (A == b && B == a);
}

/// <summary>
/// Abstract war, with no battles. Each week, once every seat has moved:
/// <list type="number">
/// <item>Truces past their end are let go. Each principal grows 1 wearier every <see cref="WearinessWeeks"/>th week of
/// its war, and a war in which a principal has reached <see cref="Exhausted"/> ends in a treaty: the wearier side
/// cedes a front region unless the other is itself past <see cref="PeaceWeariness"/>.</item>
/// <item>Feuds cool and their vassals skirmish (<see cref="Feuds.Weigh"/>).</item>
/// <item>Two crowns at war's relation (-<see cref="Board.RelationLimit"/>) whose lands border each other, or whose
/// influence meets (<see cref="Meet"/>), not already at war together and bound by no truce, go to war: over conquest
/// where their lands border, else over a grievance. A feud between them becomes the war. Only crowns open wars.</item>
/// <item>Feuds that have cooled or parted end, and new ones open (<see cref="Feuds.Open"/>).</item>
/// <item>A seat at <see cref="AllianceRelation"/> with one principal of a war and not with the other is pulled in on
/// that one's side, and its relation with the other principal falls to <see cref="AllyEnmity"/>.</item>
/// </list>
/// A conquest's fronts are the regions held by either side that border a region held by the other, so they move as
/// regions change hands; a grievance's are where the two sides' influence meets. Its two principals' relation is held at war's relation while it lasts. War cards (<see cref="CardFamily.War"/>)
/// hurt the enemy's front regions, and each one that changes the enemy's land makes its side's principal
/// <see cref="HurtWeariness"/> wearier, and <see cref="LossWeariness"/> more if it takes the region. A treaty, made by
/// exhaustion, by a principal suing for peace or by a third seat brokering a truce, sets the principals' relation to
/// <see cref="TreatyRelation"/>, lifts the war's fronts and binds them to a truce of <see cref="TruceWeeks"/> weeks.
/// </summary>
public static class Wars
{
    public const int AllianceRelation = 3;
    public const int AllyEnmity = -2;
    public const int WearinessWeeks = 4;
    public const int HurtWeariness = 1;
    public const int LossWeariness = 3;

    /// <summary>A principal this weary may sue for peace, and one not yet this weary makes the other side cede a region.</summary>
    public const int PeaceWeariness = 10;

    /// <summary>A third seat may broker a truce once both principals are this weary.</summary>
    public const int BrokerWeariness = 6;

    /// <summary>A broker needs at least this relation with both principals.</summary>
    public const int BrokerRelation = 1;

    public const int Exhausted = 20;
    public const int TreatyRelation = -1;
    public const int TruceWeeks = 52;

    /// <summary>Runs the week's war checks, once every seat has moved.</summary>
    public static void Check(Board board)
    {
        board.ExpireTruces();
        foreach (War war in board.Wars.ToArray())
        {
            if ((board.Turn - war.Started) % WearinessWeeks != 0) continue;
            board.AddWeariness(war, war.A, 1);
            board.AddWeariness(war, war.B, 1);
        }
        foreach (War war in board.Wars.ToArray())
        {
            if (war.WearinessOfA < Exhausted && war.WearinessOfB < Exhausted) continue;
            string weary = war.WearinessOfA >= war.WearinessOfB ? war.A : war.B;
            Treaty(board, war, war.Weariness(war.Enemy(weary)) < PeaceWeariness ? weary : null, "exhausted");
        }
        Feuds.Weigh(board);
        foreach ((Seat a, Seat b) in CrownPairs(board))
        {
            if (board.Relation(a.Id, b.Id) > -Board.RelationLimit || board.WarBetween(a.Id, b.Id) is not null
                || board.Truces.Any(truce => truce.Binds(a.Id, b.Id)))
                continue;
            WarAim? aim = Border(board, [a.Id], [b.Id]) ? WarAim.Conquest : Meet(board, [a.Id], [b.Id]).Any() ? WarAim.Grievance : null;
            if (aim is null) continue;
            if (Feuds.Between(board, a.Id, b.Id) is { } feud) board.EndFeud(feud, "war");
            board.OpenWar(a, b, aim.Value);
        }
        Feuds.Open(board);
        foreach (War opened in board.Wars.ToArray())
        {
            foreach (Seat seat in board.Seats)
            {
                War war = board.War(opened.Id);
                if (war.Fights(seat.Id)) continue;
                bool withA = board.Relation(seat.Id, war.A) >= AllianceRelation;
                bool withB = board.Relation(seat.Id, war.B) >= AllianceRelation;
                if (withA == withB) continue;
                string side = withA ? war.A : war.B;
                Seat enemy = board.Seat(war.Enemy(side));
                board.JoinWar(war, seat, side);
                int relation = board.Relation(seat.Id, enemy.Id);
                if (relation > AllyEnmity) board.AdjustRelation(seat, enemy, AllyEnmity - relation);
            }
        }
    }

    /// <summary>
    /// The war in which <paramref name="region"/> is an enemy front for <paramref name="seat"/>: one of the war's fronts,
    /// held by the other side in a conquest or where the other side is present in a grievance, that the seat reaches. A
    /// region consolidated after it was taken is no one's to strike until its consolidation ends, so a conquest holds.
    /// </summary>
    public static War? Against(Board board, Seat seat, Region region)
    {
        if (board.Turn < region.ContestableFrom || !board.Reaches(seat, region)) return null;
        foreach (War war in board.Wars)
        {
            if (war.Side(seat.Id) is not { } side) continue;
            IEnumerable<string> enemies = war.SideOf(war.Enemy(side));
            bool enemyThere = war.Aim == WarAim.Conquest
                ? region.Allegiance is { } holder && enemies.Contains(holder)
                : enemies.Any(enemy => board.Present(board.Seat(enemy), region));
            if (enemyThere && Fronts(board, war).Contains(region)) return war;
        }
        return null;
    }

    /// <summary>Whether <paramref name="region"/> is one of <paramref name="seat"/>'s own front regions in a war it fights.</summary>
    public static bool OwnFront(Board board, Seat seat, Region region) =>
        region.Allegiance == seat.Id && board.Wars.Any(war => war.Fights(seat.Id) && Fronts(board, war).Contains(region));

    /// <summary>
    /// The war's front regions, in board order: for a conquest, those held by either side that border a region held by
    /// the other; for a grievance, where the two sides' influence meets.
    /// </summary>
    public static IEnumerable<Region> Fronts(Board board, War war) => war.Aim == WarAim.Conquest
        ? board.Regions.Where(region =>
            region.Allegiance is { } holder && war.Side(holder) is { } side && Borders(board, region, war.SideOf(war.Enemy(side))))
        : Meet(board, [.. war.SideOf(war.A)], [.. war.SideOf(war.B)]);

    /// <summary>
    /// Where the influence of seats <paramref name="a"/> and <paramref name="b"/> meets, in board order: the regions
    /// where one side is present (<see cref="Board.Present"/>) and the other is present there or in a bordering region.
    /// </summary>
    public static IEnumerable<Region> Meet(Board board, IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        bool Present(IReadOnlyList<string> side, Region region) => side.Any(seat => board.Present(board.Seat(seat), region));
        bool Near(IReadOnlyList<string> side, Region region) =>
            Present(side, region) || region.Borders.Any(border => Present(side, board.Region(border)));
        return board.Regions.Where(region => (Present(a, region) && Near(b, region)) || (Present(b, region) && Near(a, region)));
    }

    /// <summary>
    /// Whether <paramref name="other"/> is <paramref name="seat"/>'s open foe: a crown it feuds with, or a seat on the
    /// other side of a war it fights.
    /// </summary>
    public static bool Foe(Board board, string seat, string other) => Quarrel(board, seat, other) is not null;

    /// <summary>The detail of the conditions <paramref name="seat"/>'s blows against <paramref name="other"/> lay: their feud's or their war's.</summary>
    public static string? Quarrel(Board board, string seat, string other) =>
        Feuds.Between(board, seat, other)?.Detail
        ?? board.Wars.FirstOrDefault(war => war.Side(seat) is { } side && war.Side(other) is { } theirs && side != theirs)?.Detail;

    /// <summary>Whether <paramref name="seat"/> is in a feud or fights a war, and so may play the cards of open conflict.</summary>
    public static bool InConflict(Board board, Seat seat) =>
        board.Wars.Any(war => war.Fights(seat.Id)) || board.Feuds.Any(feud => feud.Principal(seat.Id));

    /// <summary>Every pair of crowns, in table order.</summary>
    internal static IEnumerable<(Seat A, Seat B)> CrownPairs(Board board)
    {
        Seat[] crowns = [.. board.Seats.Where(seat => seat.Kind == SeatKind.Crown)];
        for (int first = 0; first < crowns.Length; first++)
            for (int second = first + 1; second < crowns.Length; second++) yield return (crowns[first], crowns[second]);
    }

    /// <summary>
    /// The regions a treaty ending <paramref name="war"/> may change, in board order: its fronts, where a region may be
    /// ceded, and every region its war fronts lie on.
    /// </summary>
    public static IEnumerable<Region> TreatyRegions(Board board, War war)
    {
        HashSet<Region> fronts = [.. Fronts(board, war)];
        return board.Regions.Where(region => fronts.Contains(region)
            || region.Conditions.Any(condition => condition.Kind == ConditionKind.WarFront && condition.Detail == war.Detail));
    }

    /// <summary>
    /// What a war card <paramref name="mover"/> played that changed <paramref name="region"/>, which
    /// <paramref name="holder"/> held, costs the enemy's principal: <see cref="HurtWeariness"/>, and
    /// <see cref="LossWeariness"/> more if the enemy held the region and it was taken.
    /// </summary>
    public static void Hurt(Board board, War war, Seat mover, string? holder, Region region)
    {
        string enemy = war.Enemy(war.Side(mover.Id)!);
        bool lost = holder is not null && war.Side(holder) == enemy && region.Allegiance != holder;
        board.AddWeariness(war, enemy, HurtWeariness + (lost ? LossWeariness : 0));
    }

    /// <summary><paramref name="seat"/>, a principal, sues for peace: it cedes a front region unless the enemy is weary too.</summary>
    public static void SueForPeace(Board board, War war, Seat seat) =>
        Treaty(board, war, war.Weariness(war.Enemy(seat.Id)) < PeaceWeariness ? seat.Id : null, $"peace sued by {seat.Id}");

    /// <summary><paramref name="broker"/>, a third seat, brokers a truce: no region is ceded, and both principals think 1 better of it.</summary>
    public static void Broker(Board board, War war, Seat broker)
    {
        Treaty(board, war, null, $"truce brokered by {broker.Id}");
        board.AdjustRelation(broker, board.Seat(war.A), 1);
        board.AdjustRelation(broker, board.Seat(war.B), 1);
    }

    /// <summary>Whether <paramref name="seat"/> may sue for peace in <paramref name="war"/>: it is a principal and weary enough.</summary>
    public static bool MaySue(War war, Seat seat) => war.Principal(seat.Id) && war.Weariness(seat.Id) >= PeaceWeariness;

    /// <summary>
    /// Whether <paramref name="broker"/> may broker a truce in <paramref name="war"/>: it does not fight it, both
    /// principals are weary enough, and it stands at <see cref="BrokerRelation"/> or better with both, or, as an arbiter
    /// (<see cref="Balance"/>), is hostile to neither.
    /// </summary>
    public static bool MayBroker(Board board, War war, Seat broker)
    {
        bool Trusted(string principal) => broker.Arbiter
            ? board.Relation(broker.Id, principal) > Supply.Hostile
            : board.Relation(broker.Id, principal) >= BrokerRelation;
        return !war.Fights(broker.Id) && war.WearinessOfA >= BrokerWeariness && war.WearinessOfB >= BrokerWeariness && Trusted(war.A) && Trusted(war.B);
    }

    /// <summary>
    /// Ends <paramref name="war"/> in a treaty for <paramref name="reason"/>. Then the principals' relation is set to
    /// <see cref="TreatyRelation"/>, the war's fronts are lifted and a truce binds them. <paramref name="cedes"/>, if a
    /// principal, pays the other:
    /// <list type="bullet">
    /// <item>after a conquest, the front region it holds where the other has the most influence, the lowest control
    /// breaking a tie; a capital is never ceded;</item>
    /// <item>after a grievance, it withdraws its factors from the front region where the other has the most influence
    /// among those it has influence in, and gives the other hostages (<see cref="CourtConditionKind.HostageTreaty"/>)
    /// for the truce, which keeps their relation from falling below 0 while it lasts.</item>
    /// </list>
    /// </summary>
    private static void Treaty(Board board, War war, string? cedes, string reason)
    {
        Region[] fronts = [.. Fronts(board, war)];
        board.EndWar(war, reason);
        Seat a = board.Seat(war.A);
        Seat b = board.Seat(war.B);
        board.AdjustRelation(a, b, TreatyRelation - board.Relation(a.Id, b.Id));
        foreach (Region region in board.Regions) board.RemoveCondition(region, ConditionKind.WarFront, war.Detail);
        board.AddTruce(new Truce(war.A, war.B, board.Turn + TruceWeeks));
        if (cedes is null) return;
        Seat loser = board.Seat(cedes);
        Seat winner = board.Seat(war.Enemy(cedes));
        if (war.Aim == WarAim.Conquest)
        {
            Region? ceded = fronts.Where(region => region.Allegiance == cedes && !region.Capital)
                .OrderByDescending(region => region.InfluenceOf(winner.Id)).ThenBy(region => region.Control).FirstOrDefault();
            if (ceded is not null) Card.TakeRegion(board, winner, ceded);
            return;
        }
        Region? withdrawn = fronts.Where(region => region.InfluenceOf(cedes) > 0).OrderByDescending(region => region.InfluenceOf(winner.Id)).FirstOrDefault();
        if (withdrawn is not null) board.AdjustInfluence(withdrawn, loser, -withdrawn.InfluenceOf(cedes));
        board.AddCourtCondition(loser, new CourtCondition(CourtConditionKind.HostageTreaty, 1, winner.Id, board.Turn, board.Turn + TruceWeeks));
    }

    private static bool Borders(Board board, Region region, IEnumerable<string> seats) =>
        region.Borders.Any(border => board.Region(border).Allegiance is { } holder && seats.Contains(holder));

    private static bool Border(Board board, IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        board.Regions.Any(region => region.Allegiance is { } holder && a.Contains(holder) && Borders(board, region, b));
}
