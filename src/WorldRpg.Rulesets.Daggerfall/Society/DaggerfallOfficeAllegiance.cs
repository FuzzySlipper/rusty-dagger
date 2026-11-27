using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How the player stands with the seat a town's office answers to.</summary>
internal enum DaggerfallOfficeStanding
{
    None = 0,

    /// <summary>The player belongs to the seat: a guild membership under its office faction.</summary>
    Member = 1,

    /// <summary>The player belongs to a seat hostile to it.</summary>
    Enemy = 2,
}

/// <summary>
/// A local office of a town that answers to a board seat: the office, the seat and its name, its office faction, and
/// whether a secret card took it, so the world sees only the symptom.
/// </summary>
internal sealed record DaggerfallOfficeAllegianceView(DaggerfallOfficeKind Kind, string SeatId, string SeatName, int? SeatFactionId, bool Covert);

/// <summary>
/// Whom a town's local offices answer to (#11127, settled with Patch on the design thread). A board office is the
/// watch captain, court factor, chief guild hall master or temple head of a board region's chief town
/// (<see cref="DaggerfallPoliticalBoard.Town"/>). A seat that takes one on the board turns its incumbent, who stays in
/// post and answers to the seat. The board saves who holds each office and whether it was taken covertly, so this
/// keeps nothing of its own.
/// <para>
/// In that town, a member of the holding seat is received better by the office's people, and a member of a seat
/// hostile to it worse (<see cref="DaggerfallPoliticsTuning"/>). The factor's people are the town's shopkeepers; the
/// hall's and the temple's are those of the office's own guild or temple. The watch passes a member through its
/// scrutiny as if vouched for, and holds a hostile seat's member to heightened scrutiny.
/// </para>
/// </summary>
internal sealed class DaggerfallOfficeAllegiance(
    DaggerfallPoliticalBoard board,
    DaggerfallSocialState social,
    DaggerfallBoardSeatSet seats,
    DaggerfallOfficeLedger officeLedger,
    DaggerfallPoliticsTuning tuning)
{
    /// <summary>
    /// The local offices of <paramref name="site"/> that answer to a seat: none unless it is a board region's chief town.
    /// An office held by the crown that rules the region is the town's ordinary order, not a seat's hold, and is left out.
    /// </summary>
    internal IReadOnlyList<DaggerfallOfficeAllegianceView> All(DaggerfallSiteRecord site)
    {
        ArgumentNullException.ThrowIfNull(site);
        Region? region = board.Board.Regions.FirstOrDefault(held => held.CatalogRegion == site.Id.Region && board.Town(held.Id)?.Id == site.Id);
        if (region is null) return [];
        return [.. region.Offices.Where(held => held.Value != region.Allegiance).Select(held => new DaggerfallOfficeAllegianceView(Kind(held.Key), held.Value, board.Board.Seat(held.Value).Name,
            seats.TryGetBySeatId(held.Value, out DaggerfallBoardSeatBinding? binding) ? binding!.OfficeFactionId : null, region.Covert.Contains(held.Key)))];
    }

    /// <summary>Whom <paramref name="site"/>'s office of <paramref name="kind"/> answers to, if a seat.</summary>
    internal DaggerfallOfficeAllegianceView? Of(DaggerfallSiteRecord site, DaggerfallOfficeKind kind) =>
        All(site).FirstOrDefault(held => held.Kind == kind);

    /// <summary>How the player stands with the seat <paramref name="held"/> answers to.</summary>
    internal DaggerfallOfficeStanding Standing(DaggerfallOfficeAllegianceView held)
    {
        ArgumentNullException.ThrowIfNull(held);
        IReadOnlyList<string> mine = PlayerSeats();
        if (mine.Contains(held.SeatId, StringComparer.Ordinal)) return DaggerfallOfficeStanding.Member;
        return mine.Any(seat => board.Board.Relation(seat, held.SeatId) <= Supply.Hostile) ? DaggerfallOfficeStanding.Enemy : DaggerfallOfficeStanding.None;
    }

    /// <summary>How <paramref name="site"/>'s watch takes the player: as a member of its seat, an enemy of it, or neither.</summary>
    internal DaggerfallOfficeStanding Watch(DaggerfallSiteRecord? site) =>
        site is not null && Of(site, DaggerfallOfficeKind.WatchCaptain) is { } watch ? Standing(watch) : DaggerfallOfficeStanding.None;

    /// <summary>
    /// Each office-allegiance reaction shift <paramref name="npc"/> applies to the player, with its player-facing label (#11158).
    /// Covert offices describe only the visible influence so secret board holds are never leaked in player text.
    /// </summary>
    internal IReadOnlyList<DaggerfallReactionShiftCause> DescribeReactionShifts(DaggerfallNpc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (board.Board.Regions.FirstOrDefault(held => held.CatalogRegion == npc.Site.Region) is not { } region
            || board.Town(region.Id) is not { } town || !string.Equals(town.Name, npc.Site.Location, StringComparison.Ordinal))
            return [];
        IReadOnlyList<DaggerfallLocalOffice> offices = officeLedger.ResolveLocalOffices(town);
        List<DaggerfallReactionShiftCause> shifts = [];
        foreach (DaggerfallOfficeAllegianceView held in All(town).Where(held => held.Kind != DaggerfallOfficeKind.WatchCaptain))
        {
            bool ofThisOffice = held.Kind == DaggerfallOfficeKind.CourtFactor
                ? npc.Services.Contains("merchant", StringComparer.Ordinal)
                : offices.FirstOrDefault(office => office.Kind == held.Kind) is { } office
                    && social.Liege.Lineage(npc.Appearance.FactionId).Any(faction => faction.Id == office.OfficeFactionId);
            if (!ofThisOffice) continue;
            DaggerfallOfficeStanding standing = Standing(held);
            int delta = standing switch
            {
                DaggerfallOfficeStanding.Member => tuning.OfficeMemberReaction,
                DaggerfallOfficeStanding.Enemy => -tuning.OfficeEnemyReaction,
                _ => 0,
            };
            if (delta == 0) continue;
            string officeTitle = OfficeLabel(held.Kind);
            string label = held.Covert
                ? (standing == DaggerfallOfficeStanding.Member ? $"{officeTitle} favor" : $"{officeTitle} hostility")
                : (standing == DaggerfallOfficeStanding.Member
                    ? $"{officeTitle} — {held.SeatName} (member)"
                    : $"{officeTitle} — {held.SeatName} (enemy)");
            shifts.Add(new(label, delta));
        }
        return shifts;
    }

    /// <summary>
    /// The reaction shift <paramref name="npc"/> gives the player, as one of the people of an office of their town that
    /// answers to a seat: positive for the seat's members, negative for its enemies, else 0.
    /// </summary>
    internal int ReactionShift(DaggerfallNpc npc)
    {
        int shift = 0;
        foreach (DaggerfallReactionShiftCause cause in DescribeReactionShifts(npc))
            shift += cause.Delta;
        return shift;
    }

    private static string OfficeLabel(DaggerfallOfficeKind kind) => kind switch
    {
        DaggerfallOfficeKind.CourtFactor => "Court Factor",
        DaggerfallOfficeKind.GuildHallMaster => "Guild Hall Master",
        DaggerfallOfficeKind.TempleHead => "Temple Head",
        _ => "Watch Captain",
    };

    /// <summary>The seats the player belongs to: those whose office faction one of their guild memberships comes under.</summary>
    private IReadOnlyList<string> PlayerSeats() =>
        [.. seats.All.Where(seat => board.Board.HasSeat(seat.SeatId) && social.ReadAffiliations()
            .Any(membership => social.Liege.Lineage(membership.FactionId).Any(faction => faction.Id == seat.OfficeFactionId)))
            .Select(seat => seat.SeatId)];

    /// <summary>The local office a board office is in its region's chief town.</summary>
    internal static DaggerfallOfficeKind Kind(Office office) => office switch
    {
        Office.Watch => DaggerfallOfficeKind.WatchCaptain,
        Office.Factor => DaggerfallOfficeKind.CourtFactor,
        Office.Hall => DaggerfallOfficeKind.GuildHallMaster,
        _ => DaggerfallOfficeKind.TempleHead,
    };
}
