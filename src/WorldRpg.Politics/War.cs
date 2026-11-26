using System.Globalization;

namespace WorldRpg.Politics;

/// <summary>
/// A war in force between two crowns, <see cref="A"/> and <see cref="B"/>, the war's principals, each with the allies
/// fighting on its side, since turn <see cref="Started"/>. Each principal's weariness counts towards the treaty that
/// ends it (<see cref="Wars"/>). Allies grow no weary; they fight until their principals make peace.
/// </summary>
public sealed record War(string Id, string A, string B, IReadOnlyList<string> AlliesOfA, IReadOnlyList<string> AlliesOfB, int Started,
    int WearinessOfA, int WearinessOfB)
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
/// <item>Two crowns at war's relation (-<see cref="Board.RelationLimit"/>) whose lands border each other, not already
/// at war together and bound by no truce, go to war. Only crowns hold land, so only crowns open wars.</item>
/// <item>A seat at <see cref="AllianceRelation"/> with one principal of a war and not with the other is pulled in on
/// that one's side, and its relation with the other principal falls to <see cref="AllyEnmity"/>.</item>
/// </list>
/// A war's fronts are the regions held by either side that border a region held by the other, so they move as regions
/// change hands. Its two principals' relation is held at war's relation while it lasts. War cards (<see cref="CardFamily.War"/>)
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
        Seat[] crowns = [.. board.Seats.Where(seat => seat.Kind == SeatKind.Crown)];
        foreach (Seat a in crowns)
        {
            foreach (Seat b in crowns.SkipWhile(other => other != a).Skip(1))
            {
                if (board.Relation(a.Id, b.Id) > -Board.RelationLimit || board.WarBetween(a.Id, b.Id) is not null
                    || board.Truces.Any(truce => truce.Binds(a.Id, b.Id)) || !Border(board, [a.Id], [b.Id]))
                    continue;
                board.OpenWar(a, b);
            }
        }
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
    /// The war in which <paramref name="region"/> is an enemy front for <paramref name="seat"/>: its holder fights on the
    /// other side from the seat, it borders land held by the seat's side, and the seat reaches it. A region consolidated
    /// after it was taken is no one's to strike until its consolidation ends, so a conquest holds.
    /// </summary>
    public static War? Against(Board board, Seat seat, Region region)
    {
        if (region.Allegiance is not { } holder || board.Turn < region.ContestableFrom || !board.Reaches(seat, region)) return null;
        foreach (War war in board.Wars)
        {
            if (war.Side(seat.Id) is not { } side || war.Side(holder) is not { } other || other == side) continue;
            if (Borders(board, region, war.SideOf(side))) return war;
        }
        return null;
    }

    /// <summary>Whether <paramref name="region"/> is one of <paramref name="seat"/>'s own front regions in a war it fights.</summary>
    public static bool OwnFront(Board board, Seat seat, Region region) =>
        region.Allegiance == seat.Id
        && board.Wars.Any(war => war.Side(seat.Id) is { } side && Borders(board, region, war.SideOf(war.Enemy(side))));

    /// <summary>The war's front regions, in board order: those held by either side that border a region held by the other.</summary>
    public static IEnumerable<Region> Fronts(Board board, War war) => board.Regions.Where(region =>
        region.Allegiance is { } holder && war.Side(holder) is { } side && Borders(board, region, war.SideOf(war.Enemy(side))));

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
    /// What a war card that changed <paramref name="region"/>, which <paramref name="holder"/> held, costs the
    /// principal of the holder's side: <see cref="HurtWeariness"/>, and <see cref="LossWeariness"/> more if the region was taken.
    /// </summary>
    public static void Hurt(Board board, War war, string holder, Region region)
    {
        string principal = war.Side(holder)!;
        board.AddWeariness(war, principal, HurtWeariness + (region.Allegiance == holder ? 0 : LossWeariness));
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
    /// Whether <paramref name="broker"/> may broker a truce in <paramref name="war"/>: it does not fight it, stands at
    /// <see cref="BrokerRelation"/> or better with both principals, and both are weary enough.
    /// </summary>
    public static bool MayBroker(Board board, War war, Seat broker) =>
        !war.Fights(broker.Id) && war.WearinessOfA >= BrokerWeariness && war.WearinessOfB >= BrokerWeariness
        && board.Relation(broker.Id, war.A) >= BrokerRelation && board.Relation(broker.Id, war.B) >= BrokerRelation;

    /// <summary>
    /// Ends <paramref name="war"/> in a treaty for <paramref name="reason"/>. <paramref name="cedes"/>, if a principal,
    /// gives up the front region where the other has the most influence, the lowest control breaking a tie; a capital is
    /// never ceded. Then the principals' relation is set to <see cref="TreatyRelation"/>, the war's fronts are lifted and
    /// a truce binds them.
    /// </summary>
    private static void Treaty(Board board, War war, string? cedes, string reason)
    {
        if (cedes is not null)
        {
            Seat winner = board.Seat(war.Enemy(cedes));
            Region? ceded = board.Regions
                .Where(region => region.Allegiance == cedes && !region.Capital && Borders(board, region, war.SideOf(winner.Id)))
                .OrderByDescending(region => region.InfluenceOf(winner.Id)).ThenBy(region => region.Control).FirstOrDefault();
            if (ceded is not null) Card.TakeRegion(board, winner, ceded);
        }
        board.EndWar(war, reason);
        Seat a = board.Seat(war.A);
        Seat b = board.Seat(war.B);
        board.AdjustRelation(a, b, TreatyRelation - board.Relation(a.Id, b.Id));
        foreach (Region region in board.Regions) board.RemoveCondition(region, ConditionKind.WarFront, war.Detail);
        board.AddTruce(new Truce(war.A, war.B, board.Turn + TruceWeeks));
    }

    private static bool Borders(Board board, Region region, IEnumerable<string> seats) =>
        region.Borders.Any(border => board.Region(border).Allegiance is { } holder && seats.Contains(holder));

    private static bool Border(Board board, IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        board.Regions.Any(region => region.Allegiance is { } holder && a.Contains(holder) && Borders(board, region, b));
}
