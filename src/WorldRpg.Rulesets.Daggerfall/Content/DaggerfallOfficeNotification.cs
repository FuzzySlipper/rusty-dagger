using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Category of political or institutional office tracked by <see cref="DaggerfallOfficeLedger"/>.</summary>
internal enum DaggerfallOfficeKind
{
    /// <summary>Regional throne or province ruler (Crown seat or regional province office).</summary>
    Ruler = 0,

    /// <summary>Major non-crown board-game seat head (Empire/Blades, Guilds, Temples, Necromancers, Underking, Direnni).</summary>
    MajorSeat = 1,

    /// <summary>Local guild hall or knightly order hall master bound to a seeded guildhall interior slot.</summary>
    GuildHallMaster = 2,

    /// <summary>Local temple head bound to a seeded temple interior slot.</summary>
    TempleHead = 3,

    /// <summary>Local town watch captain bound to the town's deterministic exterior gate-guard slot.</summary>
    WatchCaptain = 4,

    /// <summary>Local palace/court factor or steward bound to a seeded palace interior or royal court slot.</summary>
    CourtFactor = 5,
}

/// <summary>What caused a sparse office override in <see cref="DaggerfallOfficeLedger"/>.</summary>
internal enum DaggerfallOfficeChangeCause
{
    /// <summary>The previous holder died (slain or expired in play).</summary>
    Death = 0,

    /// <summary>A political board-game card move deposed, suborned, exiled, or appointed a holder.</summary>
    CardMove = 1,

    /// <summary>A quest outcome unseated, exposed, or installed an office holder.</summary>
    Quest = 2,

    /// <summary>Institutional succession or formal appointment.</summary>
    Succession = 3,
}

/// <summary>Lifecycle transition published when an office changes state.</summary>
internal enum DaggerfallOfficeChangeKind
{
    /// <summary>An office entered a vacancy window after its holder died or the seat was vacated.</summary>
    VacancyOpened = 0,

    /// <summary>An interim regent or acting steward was named while succession remains unsettled.</summary>
    RegencyDeclared = 1,

    /// <summary>A lawful successor or appointee was seated, closing any active vacancy window.</summary>
    SuccessorSeated = 2,

    /// <summary>A living holder was non-lethally deposed from the office, opening a vacancy window.</summary>
    HolderDeposed = 3,
}

/// <summary>
/// Shared typed notification vocabulary published whenever an office opens a vacancy, enters regency, seats a
/// successor, or deposes a living holder. Lives in <c>Content/</c> so <c>Content</c>, <c>Actors</c>, and <c>Society</c>
/// owners each react directly inside their own domain without a coordinator hub or upward folder references.
/// </summary>
internal sealed record DaggerfallOfficeChangedNotification(
    string OfficeKey,
    DaggerfallOfficeKind Kind,
    DaggerfallOfficeChangeKind ChangeKind,
    DaggerfallOfficeChangeCause Cause,
    int? Region,
    DaggerfallSiteId? OfficeSiteId,
    DaggerfallWorldProfileKey? OfficeProfileKey,
    string? OfficeSlotId,
    int? OfficeFactionId,
    string? SeatId,
    string Title,
    long? PreviousHolderActorId,
    string? PreviousHolderCastId,
    string? PreviousHolderName,
    string? PreviousHolderFamilyId,
    bool PreviousHolderIsAlive,
    long? NewHolderActorId,
    string? NewHolderCastId,
    string? NewHolderName,
    string? NewHolderFamilyId,
    int ChangedDay,
    long ChangedMinute,
    long? VacancyUntilMinute,
    string? CauseDetail);
