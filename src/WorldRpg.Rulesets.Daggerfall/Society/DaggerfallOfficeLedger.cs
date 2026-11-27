using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
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

/// <summary>Lifecycle transition published by <see cref="DaggerfallOfficeLedger.OfficeChanged"/>.</summary>
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
/// Typed notification published by <see cref="DaggerfallOfficeLedger.OfficeChanged"/> whenever an office opens a
/// vacancy, enters regency, seats a successor, or deposes a living holder. Each downstream domain reacts directly
/// inside its own owner without a coordinator hub.
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

/// <summary>
/// One sparse persisted office override (<c>office -&gt; holder, previous holder, cause, vacancy window</c>).
/// Untouched offices write zero save bytes and resolve deterministically from definitions and seeded slots.
/// </summary>
internal sealed record DaggerfallOfficeOverrideSave(
    string OfficeKey,
    DaggerfallOfficeKind Kind,
    int? OfficeFactionId,
    string? SeatId,
    long? HolderActorId,
    string? HolderCastId,
    int? HolderFactionId,
    string? HolderDisplayName,
    string? Title,
    long? PreviousHolderActorId,
    string? PreviousHolderName,
    DaggerfallOfficeChangeCause Cause,
    string? CauseDetail,
    int ChangedDay,
    bool Vacant = false,
    long? VacancyOpenedMinute = null,
    long? VacancyUntilMinute = null,
    bool IsRegency = false,
    int? OfficeRegion = null,
    int? OfficeSiteIndex = null,
    string? PreviousHolderCastId = null,
    string? PreviousHolderFamilyId = null)
{
    internal DaggerfallSiteId? OfficeSiteId =>
        OfficeRegion is int region && OfficeSiteIndex is int index
            ? new DaggerfallSiteId(region, index)
            : null;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OfficeKey);
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), "Saved office override names an invalid office kind.");
        if (!Enum.IsDefined(Cause))
            throw new ArgumentOutOfRangeException(nameof(Cause), "Saved office override names an invalid change cause.");
        if (OfficeFactionId is < 0)
            throw new ArgumentOutOfRangeException(nameof(OfficeFactionId), "Saved office override faction id cannot be negative.");
        if (HolderFactionId is < 0)
            throw new ArgumentOutOfRangeException(nameof(HolderFactionId), "Saved office override holder faction id cannot be negative.");
        if (HolderActorId is <= 0 or DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(HolderActorId), "Saved office override holder actor id must be a positive non-player id.");
        if (PreviousHolderActorId is <= 0 or DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(PreviousHolderActorId), "Saved office override previous actor id must be a positive non-player id.");
        if (SeatId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(SeatId);
        if (HolderCastId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(HolderCastId);
        if (!Vacant && string.IsNullOrWhiteSpace(HolderDisplayName) && string.IsNullOrWhiteSpace(HolderCastId) && HolderActorId is null)
            throw new ArgumentException("A non-vacant saved office override requires a holder identity or display name.");
        if (PreviousHolderName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(PreviousHolderName);
        if (PreviousHolderCastId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(PreviousHolderCastId);
        if (PreviousHolderFamilyId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(PreviousHolderFamilyId);
        if (CauseDetail is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(CauseDetail);
        if (VacancyOpenedMinute is < 0)
            throw new ArgumentOutOfRangeException(nameof(VacancyOpenedMinute), "Saved office vacancy start minute cannot be negative.");
        if (VacancyUntilMinute is < 0)
            throw new ArgumentOutOfRangeException(nameof(VacancyUntilMinute), "Saved office vacancy expiry minute cannot be negative.");
        if (VacancyOpenedMinute is long opened && VacancyUntilMinute is long until && until < opened)
            throw new ArgumentOutOfRangeException(nameof(VacancyUntilMinute), "Saved office vacancy cannot expire before it opened.");
        if (OfficeRegion is < 0 or > 61)
            throw new ArgumentOutOfRangeException(nameof(OfficeRegion), "Saved office region is outside classic region bounds.");
        if (OfficeSiteIndex is < 0)
            throw new ArgumentOutOfRangeException(nameof(OfficeSiteIndex), "Saved office site index cannot be negative.");
        if (OfficeSiteIndex is not null && OfficeRegion is null)
            throw new ArgumentException("Saved office site index requires an OfficeRegion.", nameof(OfficeSiteIndex));
    }
}

/// <summary>Sparse persisted office-ledger snapshot. Empty when no office has changed from its seeded baseline.</summary>
internal sealed record DaggerfallOfficeLedgerSave(DaggerfallOfficeOverrideSave[] Overrides)
{
    internal static DaggerfallOfficeLedgerSave Empty { get; } = new([]);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Overrides);
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (DaggerfallOfficeOverrideSave entry in Overrides)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Validate();
            if (!keys.Add(entry.OfficeKey))
                throw new ArgumentException($"Saved office ledger repeats office key '{entry.OfficeKey}'.");
        }
    }
}

/// <summary>
/// Resolved leader persona for a board-game seat, combining <see cref="DaggerfallOfficeLedger"/> sparse overrides
/// with <see cref="DaggerfallOfficeHolders"/> and published cast/faction defaults.
/// </summary>
internal sealed record DaggerfallSeatLeaderPersona(
    string SeatId,
    string SeatName,
    string OfficeKey,
    DaggerfallOfficeKind Kind,
    int OfficeFactionId,
    long? HolderActorId,
    string? HolderCastId,
    int? HolderFactionId,
    string DisplayName,
    string? Title,
    bool IsVacant,
    bool IsOverridden,
    string? PreviousHolderName = null,
    DaggerfallOfficeChangeCause? ChangeCause = null,
    string? CauseDetail = null,
    long? VacancyOpenedMinute = null,
    long? VacancyUntilMinute = null,
    bool IsRegency = false);

/// <summary>
/// Resolved local institutional office bound deterministically to an existing seeded palace, guildhall, temple,
/// or town-watch slot (with zero save bytes until an override is written).
/// </summary>
internal sealed record DaggerfallLocalOffice(
    string OfficeKey,
    DaggerfallOfficeKind Kind,
    string Title,
    DaggerfallSiteId SiteId,
    DaggerfallSiteBuildingId? BuildingId,
    DaggerfallWorldProfileKey ProfileKey,
    string SlotId,
    int OfficeFactionId,
    long? HolderActorId,
    string DisplayName,
    bool IsAlive,
    bool IsVacant,
    bool IsOverridden,
    string? PreviousHolderName = null,
    DaggerfallOfficeChangeCause? ChangeCause = null,
    string? CauseDetail = null,
    string? FamilyId = null,
    long? VacancyOpenedMinute = null,
    long? VacancyUntilMinute = null,
    bool IsRegency = false);

/// <summary>
/// One deterministic provincial Great House (<c>fam:house:r{region}:{index}</c>) paired with the Tier-2 regional
/// offices (palace court factors across the province) currently held by members of that house. Kept strictly separate
/// from Tier-1 <c>world/board-seats.json</c> powers.
/// </summary>
internal sealed record DaggerfallRegionalGreatHouse(
    string FamilyId,
    int Region,
    int HouseIndex,
    IReadOnlyList<DaggerfallLocalOffice> HeldOffices);

/// <summary>
/// Sparse world-scoped office ledger connecting live world actors and seeded building/town slots to the political
/// board game and quest system. Untouched offices resolve deterministically with zero save bytes; only card moves,
/// quest outcomes, deaths, or explicit successions write a sparse <see cref="DaggerfallOfficeOverrideSave"/>.
/// Seat bindings (<c>seatId -&gt; faction/region/title</c>, with no holder) come from <c>daggerfall.base/world/board-seats.json</c>;
/// <c>iliac-bay.board.json</c> owns seats, holders, and personas, and live holders reach this ledger through B2 (#11055).
/// </summary>
internal sealed class DaggerfallOfficeLedger(
    DaggerfallDefinitions definitions,
    DaggerfallOfficeHolders officeHolders,
    DaggerfallActorLedger actorLedger,
    DaggerfallNpcRegistry npcs,
    Func<ulong> worldSocialSeed,
    Func<DaggerfallNpcAppearance, int, string>? defaultName = null,
    Func<long>? currentMinute = null,
    DaggerfallSiteContext? sites = null)
{
    private const int GuildHallBuildingType = 11;
    private const int TempleBuildingType = 14;
    private const int PalaceBuildingType = 16;
    private const int SpecialGuildOrHouseBuildingType = 18;
    private const int ProvinceFactionType = 7;

    /// <summary>Minimum vacancy window for Tier 3 local offices (24 in-game hours).</summary>
    internal const long LocalVacancyMinMinutes = 24L * DaggerfallCalendar.MinutesPerHour;

    /// <summary>Maximum vacancy window for Tier 3 local offices (48 in-game hours).</summary>
    internal const long LocalVacancyMaxMinutes = 48L * DaggerfallCalendar.MinutesPerHour;

    /// <summary>Minimum vacancy window for Tier 1 board seats and Tier 2 regional offices (7 in-game days).</summary>
    internal const long RegionalVacancyMinMinutes = 7L * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour;

    /// <summary>Maximum vacancy window for Tier 1 board seats and Tier 2 regional offices (14 in-game days).</summary>
    internal const long RegionalVacancyMaxMinutes = 14L * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour;

    private readonly Dictionary<string, DaggerfallOfficeOverrideSave> _overrides = new(StringComparer.Ordinal);

    /// <summary>
    /// Fired whenever an office opens a vacancy, enters regency, seats a successor, or deposes a living holder.
    /// Downstream domains (<c>WordSpread</c>, <c>WorldConditions</c>, <c>ActorRoster</c>, <c>Introductions</c>) subscribe
    /// directly in session composition without a forwarding hub.
    /// </summary>
    internal event Action<DaggerfallOfficeChangedNotification>? OfficeChanged;

    /// <summary>Fired when an explicit sparse override is cleared back to its seeded baseline.</summary>
    internal event Action<string>? OfficeOverrideCleared;

    internal int OverrideCount => _overrides.Count;
    internal IReadOnlyCollection<DaggerfallOfficeOverrideSave> Overrides => _overrides.Values;

    /// <summary>Returns the inclusive min/max vacancy duration in game minutes for <paramref name="kind"/>.</summary>
    internal static (long MinMinutes, long MaxMinutes) VacancyBoundsFor(DaggerfallOfficeKind kind) => kind switch
    {
        DaggerfallOfficeKind.GuildHallMaster
            or DaggerfallOfficeKind.TempleHead
            or DaggerfallOfficeKind.WatchCaptain => (LocalVacancyMinMinutes, LocalVacancyMaxMinutes),
        _ => (RegionalVacancyMinMinutes, RegionalVacancyMaxMinutes),
    };

    /// <summary>
    /// Deterministically computes a vacancy window length in game minutes for <paramref name="officeKey"/>
    /// (<c>24–48h</c> for Tier 3 local offices; <c>7–14 days</c> for Tier 2 regional and Tier 1 seats).
    /// </summary>
    internal static long ComputeVacancyDurationMinutes(
        ulong socialSeed,
        string officeKey,
        DaggerfallOfficeKind kind,
        long openedMinute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(officeKey);
        (long min, long max) = VacancyBoundsFor(kind);
        ulong hash = 14695981039346656037UL ^ socialSeed;
        hash = Mix64(hash ^ unchecked((ulong)(long)kind));
        hash = Mix64(hash ^ unchecked((ulong)openedMinute));
        foreach (char c in officeKey)
            hash = Mix64(hash ^ c);
        ulong span = checked((ulong)(max - min + 1));
        return min + checked((long)(hash % span));
    }

    private static ulong Mix64(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private long ResolveMinute(int changedDay, long? explicitMinute) =>
        explicitMinute ?? currentMinute?.Invoke() ?? checked((long)Math.Max(0, changedDay) * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour);

    private static DaggerfallOfficeKind ToOfficeKind(DaggerfallBoardSeatKind kind) => kind switch
    {
        DaggerfallBoardSeatKind.Ruler => DaggerfallOfficeKind.Ruler,
        _ => DaggerfallOfficeKind.MajorSeat,
    };

    /// <summary>Canonical office key for a faction-addressed office (rulers, major seats, faction heads).</summary>
    internal static string FactionOfficeKey(int officeFactionId) =>
        string.Create(CultureInfo.InvariantCulture, $"faction:{officeFactionId}");

    /// <summary>Canonical office key for a board-game seat (aliases to its <see cref="FactionOfficeKey"/>).</summary>
    internal string SeatOfficeKey(string seatId) =>
        definitions.BoardSeats.TryGetBySeatId(seatId, out DaggerfallBoardSeatBinding? seat) && seat is not null
            ? FactionOfficeKey(seat.OfficeFactionId)
            : $"seat:{seatId}";

    /// <summary>Canonical office key for a local building office (guild hall master, temple head, court factor).</summary>
    internal static string LocalBuildingOfficeKey(DaggerfallOfficeKind kind, DaggerfallSiteId siteId, DaggerfallSiteBuildingId buildingId) =>
        string.Create(CultureInfo.InvariantCulture,
            $"local:{KindSlug(kind)}:{siteId.Region}:{siteId.Index}:{buildingId.BlockX}:{buildingId.BlockY}:{buildingId.Index}");

    /// <summary>Canonical office key for a town's local watch captain.</summary>
    internal static string WatchCaptainOfficeKey(DaggerfallSiteId siteId) =>
        string.Create(CultureInfo.InvariantCulture, $"local:watch-captain:{siteId.Region}:{siteId.Index}");

    /// <summary>Reads a sparse override by canonical <paramref name="officeKey"/>, or null when at seeded baseline.</summary>
    internal bool TryGetOverride(string officeKey, out DaggerfallOfficeOverrideSave? entry) =>
        _overrides.TryGetValue(officeKey, out entry);

    /// <summary>
    /// Lookup adapter bound to <see cref="DaggerfallOfficeHolders.BindOverrideLookup"/> so every A2 quest binding,
    /// quest text macro, and introduction check automatically sees sparse office overrides.
    /// </summary>
    internal DaggerfallOfficeOverride? TryGetFactionOverride(int officeFactionId)
    {
        if (officeFactionId <= 0)
            return null;
        if (!_overrides.TryGetValue(FactionOfficeKey(officeFactionId), out DaggerfallOfficeOverrideSave? over))
            return null;
        return new DaggerfallOfficeOverride(
            HolderActorId: over.HolderActorId,
            HolderCastId: over.HolderCastId,
            HolderFactionId: over.HolderFactionId,
            DisplayName: over.HolderDisplayName ?? string.Empty,
            Title: over.Title,
            PreviousHolderName: over.PreviousHolderName,
            PreviousHolderFate: FormatFate(over.Cause, over.CauseDetail),
            Vacant: over.Vacant);
    }

    /// <summary>
    /// Writes a sparse override seating a holder in a faction office or board-game seat (card move, quest outcome,
    /// or succession), clearing any active vacancy window and publishing <see cref="OfficeChanged"/>.
    /// Automatically captures the current living holder as <c>PreviousHolder</c> when none is explicitly supplied.
    /// </summary>
    internal DaggerfallOfficeOverrideSave SetFactionOfficeHolder(
        int officeFactionId,
        DaggerfallOfficeKind kind,
        long? holderActorId,
        string? holderCastId,
        int? holderFactionId,
        string? holderDisplayName,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        string? title = null,
        string? previousHolderName = null,
        long? previousHolderActorId = null,
        long? currentMinute = null)
    {
        if (officeFactionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(officeFactionId), "Faction office id must be positive.");

        string key = FactionOfficeKey(officeFactionId);
        definitions.BoardSeats.TryGetByFactionId(officeFactionId, out DaggerfallBoardSeatBinding? seat);
        int? officeRegion = ResolveFactionOfficeRegion(officeFactionId, seat);
        OutgoingHolderSnapshot outgoing = SnapshotOutgoingFactionHolder(
            officeFactionId, key, seat, officeRegion, previousHolderActorId, previousHolderName);

        string? resolvedName = holderDisplayName;
        if (!string.IsNullOrWhiteSpace(holderCastId))
        {
            if (definitions.People.Individuals.TryGetValue(holderCastId, out DaggerfallCastIndividualDefinition? cast))
            {
                holderActorId ??= DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
                holderFactionId ??= cast.FactionId;
                if (string.IsNullOrWhiteSpace(resolvedName))
                    resolvedName = cast.Name;
            }
            else
            {
                holderActorId ??= DaggerfallPeopleSlots.CastIndividualIdentity(holderCastId);
            }
        }
        if (holderActorId is long id && npcs.TryGet(id, out DaggerfallNpc? npc) && npc is not null)
        {
            holderFactionId ??= npc.Appearance.FactionId > 0 ? npc.Appearance.FactionId : null;
            if (string.IsNullOrWhiteSpace(resolvedName))
                resolvedName = npc.DisplayName ?? npc.Role;
        }

        string resolvedTitle = title ?? seat?.DefaultTitle ?? ResolveDefaultFactionTitle(officeFactionId) ?? "Ruler";
        DaggerfallCastHomeLocation? officeHome = outgoing.Home ?? ResolveCastHome(holderCastId, officeRegion);
        DaggerfallSiteId? officeSiteId = officeHome?.Site.Id ?? ResolveRegionCapitalSite(officeRegion)?.Id;
        long nowMinute = ResolveMinute(changedDay, currentMinute);

        DaggerfallOfficeOverrideSave saved = new(
            OfficeKey: key,
            Kind: kind,
            OfficeFactionId: officeFactionId,
            SeatId: seat?.SeatId,
            HolderActorId: holderActorId,
            HolderCastId: holderCastId,
            HolderFactionId: holderFactionId,
            HolderDisplayName: resolvedName,
            Title: title ?? seat?.DefaultTitle,
            PreviousHolderActorId: outgoing.ActorId,
            PreviousHolderName: outgoing.DisplayName,
            Cause: cause,
            CauseDetail: causeDetail,
            ChangedDay: changedDay,
            Vacant: false,
            VacancyOpenedMinute: null,
            VacancyUntilMinute: null,
            IsRegency: false,
            OfficeRegion: officeRegion ?? officeSiteId?.Region,
            OfficeSiteIndex: officeSiteId?.Index,
            PreviousHolderCastId: outgoing.CastId,
            PreviousHolderFamilyId: outgoing.FamilyId);
        saved.Validate();
        _overrides[key] = saved;

        string? newFamilyId = ResolveActorFamilyId(holderActorId, holderCastId, fallbackFamilyId: null);
        PublishChange(new DaggerfallOfficeChangedNotification(
            OfficeKey: key,
            Kind: kind,
            ChangeKind: DaggerfallOfficeChangeKind.SuccessorSeated,
            Cause: cause,
            Region: saved.OfficeRegion,
            OfficeSiteId: saved.OfficeSiteId,
            OfficeProfileKey: DaggerfallCastHomes.ResolveProfileKey(officeHome),
            OfficeSlotId: null,
            OfficeFactionId: officeFactionId,
            SeatId: seat?.SeatId,
            Title: resolvedTitle,
            PreviousHolderActorId: outgoing.ActorId,
            PreviousHolderCastId: outgoing.CastId,
            PreviousHolderName: outgoing.DisplayName,
            PreviousHolderFamilyId: outgoing.FamilyId,
            PreviousHolderIsAlive: outgoing.IsAlive,
            NewHolderActorId: holderActorId,
            NewHolderCastId: holderCastId,
            NewHolderName: resolvedName,
            NewHolderFamilyId: newFamilyId,
            ChangedDay: changedDay,
            ChangedMinute: nowMinute,
            VacancyUntilMinute: null,
            CauseDetail: causeDetail));
        return saved;
    }

    /// <summary>Writes a sparse override seating a holder in a board-game seat by its <paramref name="seatId"/>.</summary>
    internal DaggerfallOfficeOverrideSave SetBoardSeatHolder(
        string seatId,
        long? holderActorId,
        string? holderCastId,
        int? holderFactionId,
        string? holderDisplayName,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        string? title = null,
        long? currentMinute = null)
    {
        if (!definitions.BoardSeats.TryGetBySeatId(seatId, out DaggerfallBoardSeatBinding? seat) || seat is null)
            throw new ArgumentException($"Unknown board-game seat '{seatId}'.", nameof(seatId));

        return SetFactionOfficeHolder(
            seat.OfficeFactionId,
            ToOfficeKind(seat.Kind),
            holderActorId,
            holderCastId,
            holderFactionId,
            holderDisplayName,
            cause,
            changedDay,
            causeDetail,
            title ?? seat.DefaultTitle,
            currentMinute: currentMinute);
    }

    /// <summary>
    /// Marks a faction office or board-game seat vacant and opens a deterministic <see cref="DaggerfallOfficeOverrideSave.VacancyUntilMinute"/>
    /// window (<c>7–14 days</c> for Tier 1 seats / regional rulers). Publishes <see cref="DaggerfallOfficeChangeKind.HolderDeposed"/>
    /// when a living holder is removed, or <see cref="DaggerfallOfficeChangeKind.VacancyOpened"/> after death.
    /// </summary>
    internal DaggerfallOfficeOverrideSave VacateFactionOffice(
        int officeFactionId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        string? previousHolderName = null,
        long? previousHolderActorId = null,
        long? currentMinute = null,
        long? vacancyUntilMinute = null)
    {
        if (officeFactionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(officeFactionId), "Faction office id must be positive.");

        string key = FactionOfficeKey(officeFactionId);
        definitions.BoardSeats.TryGetByFactionId(officeFactionId, out DaggerfallBoardSeatBinding? seat);
        DaggerfallOfficeKind kind = seat is not null ? ToOfficeKind(seat.Kind) : DaggerfallOfficeKind.Ruler;
        int? officeRegion = ResolveFactionOfficeRegion(officeFactionId, seat);
        OutgoingHolderSnapshot outgoing = SnapshotOutgoingFactionHolder(
            officeFactionId, key, seat, officeRegion, previousHolderActorId, previousHolderName);

        long openedMinute = ResolveMinute(changedDay, currentMinute);
        long untilMinute = vacancyUntilMinute
            ?? checked(openedMinute + ComputeVacancyDurationMinutes(worldSocialSeed(), key, kind, openedMinute));
        string resolvedTitle = seat?.DefaultTitle ?? ResolveDefaultFactionTitle(officeFactionId) ?? "Ruler";
        DaggerfallSiteId? officeSiteId = outgoing.Home?.Site.Id ?? ResolveRegionCapitalSite(officeRegion)?.Id;

        DaggerfallOfficeOverrideSave saved = new(
            OfficeKey: key,
            Kind: kind,
            OfficeFactionId: officeFactionId,
            SeatId: seat?.SeatId,
            HolderActorId: null,
            HolderCastId: null,
            HolderFactionId: null,
            HolderDisplayName: null,
            Title: seat?.DefaultTitle,
            PreviousHolderActorId: outgoing.ActorId,
            PreviousHolderName: outgoing.DisplayName,
            Cause: cause,
            CauseDetail: causeDetail,
            ChangedDay: changedDay,
            Vacant: true,
            VacancyOpenedMinute: openedMinute,
            VacancyUntilMinute: untilMinute,
            IsRegency: false,
            OfficeRegion: officeRegion ?? officeSiteId?.Region,
            OfficeSiteIndex: officeSiteId?.Index,
            PreviousHolderCastId: outgoing.CastId,
            PreviousHolderFamilyId: outgoing.FamilyId);
        saved.Validate();
        _overrides[key] = saved;

        DaggerfallOfficeChangeKind changeKind = cause != DaggerfallOfficeChangeCause.Death && outgoing.IsAlive
            ? DaggerfallOfficeChangeKind.HolderDeposed
            : DaggerfallOfficeChangeKind.VacancyOpened;

        PublishChange(new DaggerfallOfficeChangedNotification(
            OfficeKey: key,
            Kind: kind,
            ChangeKind: changeKind,
            Cause: cause,
            Region: saved.OfficeRegion,
            OfficeSiteId: saved.OfficeSiteId,
            OfficeProfileKey: DaggerfallCastHomes.ResolveProfileKey(outgoing.Home),
            OfficeSlotId: null,
            OfficeFactionId: officeFactionId,
            SeatId: seat?.SeatId,
            Title: resolvedTitle,
            PreviousHolderActorId: outgoing.ActorId,
            PreviousHolderCastId: outgoing.CastId,
            PreviousHolderName: outgoing.DisplayName,
            PreviousHolderFamilyId: outgoing.FamilyId,
            PreviousHolderIsAlive: outgoing.IsAlive,
            NewHolderActorId: null,
            NewHolderCastId: null,
            NewHolderName: null,
            NewHolderFamilyId: null,
            ChangedDay: changedDay,
            ChangedMinute: openedMinute,
            VacancyUntilMinute: untilMinute,
            CauseDetail: causeDetail));
        return saved;
    }

    /// <summary>Non-lethally deposes the current living holder of a board-game seat, opening a deterministic vacancy window.</summary>
    internal DaggerfallOfficeOverrideSave DeposeBoardSeat(
        string seatId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        long? currentMinute = null)
    {
        if (!definitions.BoardSeats.TryGetBySeatId(seatId, out DaggerfallBoardSeatBinding? seat) || seat is null)
            throw new ArgumentException($"Unknown board-game seat '{seatId}'.", nameof(seatId));

        return VacateFactionOffice(
            seat.OfficeFactionId,
            cause,
            changedDay,
            causeDetail ?? "was deposed",
            currentMinute: currentMinute);
    }

    /// <summary>Non-lethally deposes the current living holder of a faction office, opening a deterministic vacancy window.</summary>
    internal DaggerfallOfficeOverrideSave DeposeFactionOffice(
        int officeFactionId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        long? currentMinute = null) =>
        VacateFactionOffice(
            officeFactionId,
            cause,
            changedDay,
            causeDetail ?? "was deposed",
            currentMinute: currentMinute);

    /// <summary>
    /// Declares an interim regent or acting steward for a faction office while succession remains unsettled
    /// (preserving the active <see cref="DaggerfallOfficeOverrideSave.VacancyUntilMinute"/> window and publishing
    /// <see cref="DaggerfallOfficeChangeKind.RegencyDeclared"/>).
    /// </summary>
    internal DaggerfallOfficeOverrideSave DeclareFactionRegency(
        int officeFactionId,
        long? regentActorId,
        string? regentCastId,
        int? regentFactionId,
        string? regentDisplayName,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        string? title = null,
        long? currentMinute = null,
        long? vacancyUntilMinute = null)
    {
        if (officeFactionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(officeFactionId), "Faction office id must be positive.");

        string key = FactionOfficeKey(officeFactionId);
        definitions.BoardSeats.TryGetByFactionId(officeFactionId, out DaggerfallBoardSeatBinding? seat);
        DaggerfallOfficeKind kind = seat is not null ? ToOfficeKind(seat.Kind) : DaggerfallOfficeKind.Ruler;
        int? officeRegion = ResolveFactionOfficeRegion(officeFactionId, seat);
        OutgoingHolderSnapshot outgoing = SnapshotOutgoingFactionHolder(officeFactionId, key, seat, officeRegion, null, null);

        string? resolvedName = regentDisplayName;
        if (!string.IsNullOrWhiteSpace(regentCastId)
            && definitions.People.Individuals.TryGetValue(regentCastId, out DaggerfallCastIndividualDefinition? cast))
        {
            regentActorId ??= DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
            regentFactionId ??= cast.FactionId;
            if (string.IsNullOrWhiteSpace(resolvedName))
                resolvedName = cast.Name;
        }
        else if (!string.IsNullOrWhiteSpace(regentCastId))
        {
            regentActorId ??= DaggerfallPeopleSlots.CastIndividualIdentity(regentCastId);
        }
        if (regentActorId is long id && npcs.TryGet(id, out DaggerfallNpc? npc) && npc is not null)
        {
            regentFactionId ??= npc.Appearance.FactionId > 0 ? npc.Appearance.FactionId : null;
            if (string.IsNullOrWhiteSpace(resolvedName))
                resolvedName = npc.DisplayName ?? npc.Role;
        }

        long nowMinute = ResolveMinute(changedDay, currentMinute);
        long openedMinute = _overrides.TryGetValue(key, out DaggerfallOfficeOverrideSave? prior) && prior.VacancyOpenedMinute is long existingOpen
            ? existingOpen
            : nowMinute;
        long untilMinute = vacancyUntilMinute
            ?? (_overrides.TryGetValue(key, out DaggerfallOfficeOverrideSave? priorUntil) ? priorUntil.VacancyUntilMinute : null)
            ?? checked(nowMinute + ComputeVacancyDurationMinutes(worldSocialSeed(), key, kind, openedMinute));

        string resolvedTitle = title ?? "Regent";
        DaggerfallSiteId? officeSiteId = outgoing.Home?.Site.Id ?? ResolveRegionCapitalSite(officeRegion)?.Id;

        DaggerfallOfficeOverrideSave saved = new(
            OfficeKey: key,
            Kind: kind,
            OfficeFactionId: officeFactionId,
            SeatId: seat?.SeatId,
            HolderActorId: regentActorId,
            HolderCastId: regentCastId,
            HolderFactionId: regentFactionId,
            HolderDisplayName: resolvedName,
            Title: resolvedTitle,
            PreviousHolderActorId: outgoing.ActorId,
            PreviousHolderName: outgoing.DisplayName,
            Cause: cause,
            CauseDetail: causeDetail ?? "holds regency",
            ChangedDay: changedDay,
            Vacant: false,
            VacancyOpenedMinute: openedMinute,
            VacancyUntilMinute: untilMinute,
            IsRegency: true,
            OfficeRegion: officeRegion ?? officeSiteId?.Region,
            OfficeSiteIndex: officeSiteId?.Index,
            PreviousHolderCastId: outgoing.CastId,
            PreviousHolderFamilyId: outgoing.FamilyId);
        saved.Validate();
        _overrides[key] = saved;

        string? regentFamilyId = ResolveActorFamilyId(regentActorId, regentCastId, fallbackFamilyId: null);
        PublishChange(new DaggerfallOfficeChangedNotification(
            OfficeKey: key,
            Kind: kind,
            ChangeKind: DaggerfallOfficeChangeKind.RegencyDeclared,
            Cause: cause,
            Region: saved.OfficeRegion,
            OfficeSiteId: saved.OfficeSiteId,
            OfficeProfileKey: DaggerfallCastHomes.ResolveProfileKey(outgoing.Home),
            OfficeSlotId: null,
            OfficeFactionId: officeFactionId,
            SeatId: seat?.SeatId,
            Title: resolvedTitle,
            PreviousHolderActorId: outgoing.ActorId,
            PreviousHolderCastId: outgoing.CastId,
            PreviousHolderName: outgoing.DisplayName,
            PreviousHolderFamilyId: outgoing.FamilyId,
            PreviousHolderIsAlive: outgoing.IsAlive,
            NewHolderActorId: regentActorId,
            NewHolderCastId: regentCastId,
            NewHolderName: resolvedName,
            NewHolderFamilyId: regentFamilyId,
            ChangedDay: changedDay,
            ChangedMinute: nowMinute,
            VacancyUntilMinute: untilMinute,
            CauseDetail: saved.CauseDetail));
        return saved;
    }

    /// <summary>Writes a sparse override for a local town/building office (guild hall master, temple head, watch captain, court factor).</summary>
    internal DaggerfallOfficeOverrideSave SetLocalOfficeHolder(
        DaggerfallLocalOffice office,
        long? holderActorId,
        string? holderDisplayName,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        int? holderFactionId = null,
        bool vacant = false,
        long? currentMinute = null,
        long? vacancyUntilMinute = null)
    {
        ArgumentNullException.ThrowIfNull(office);
        long nowMinute = ResolveMinute(changedDay, currentMinute);
        long? openedMinute = vacant ? nowMinute : null;
        long? untilMinute = vacant
            ? vacancyUntilMinute ?? checked(nowMinute + ComputeVacancyDurationMinutes(worldSocialSeed(), office.OfficeKey, office.Kind, nowMinute))
            : null;

        string? resolvedName = vacant
            ? null
            : (!string.IsNullOrWhiteSpace(holderDisplayName)
                ? holderDisplayName
                : holderActorId is long id && npcs.TryGet(id, out DaggerfallNpc? npc) && npc is not null
                    ? npc.DisplayName ?? npc.Role
                    : null);

        long? prevActorId = office.HolderActorId
            ?? (_overrides.TryGetValue(office.OfficeKey, out DaggerfallOfficeOverrideSave? prior)
                ? prior.HolderActorId ?? prior.PreviousHolderActorId
                : null);
        string? prevName = !string.IsNullOrWhiteSpace(office.DisplayName)
            ? office.DisplayName
            : office.PreviousHolderName;
        string? prevFamilyId = office.FamilyId ?? ResolveActorFamilyId(prevActorId, null, null);
        bool prevAlive = prevActorId is long pid && !officeHolders.IsActorDead(pid);

        DaggerfallOfficeOverrideSave saved = new(
            OfficeKey: office.OfficeKey,
            Kind: office.Kind,
            OfficeFactionId: office.OfficeFactionId > 0 ? office.OfficeFactionId : null,
            SeatId: null,
            HolderActorId: vacant ? null : holderActorId,
            HolderCastId: null,
            HolderFactionId: vacant ? null : (holderFactionId ?? (office.OfficeFactionId > 0 ? office.OfficeFactionId : null)),
            HolderDisplayName: resolvedName,
            Title: office.Title,
            PreviousHolderActorId: prevActorId,
            PreviousHolderName: prevName,
            Cause: cause,
            CauseDetail: causeDetail,
            ChangedDay: changedDay,
            Vacant: vacant,
            VacancyOpenedMinute: openedMinute,
            VacancyUntilMinute: untilMinute,
            IsRegency: false,
            OfficeRegion: office.SiteId.Region,
            OfficeSiteIndex: office.SiteId.Index,
            PreviousHolderCastId: null,
            PreviousHolderFamilyId: prevFamilyId);
        saved.Validate();
        _overrides[office.OfficeKey] = saved;

        DaggerfallOfficeChangeKind changeKind = vacant
            ? (cause != DaggerfallOfficeChangeCause.Death && prevAlive
                ? DaggerfallOfficeChangeKind.HolderDeposed
                : DaggerfallOfficeChangeKind.VacancyOpened)
            : DaggerfallOfficeChangeKind.SuccessorSeated;
        string? newFamilyId = vacant ? null : ResolveActorFamilyId(holderActorId, null, null);

        PublishChange(new DaggerfallOfficeChangedNotification(
            OfficeKey: office.OfficeKey,
            Kind: office.Kind,
            ChangeKind: changeKind,
            Cause: cause,
            Region: office.SiteId.Region,
            OfficeSiteId: office.SiteId,
            OfficeProfileKey: office.ProfileKey,
            OfficeSlotId: office.SlotId,
            OfficeFactionId: office.OfficeFactionId > 0 ? office.OfficeFactionId : null,
            SeatId: null,
            Title: office.Title,
            PreviousHolderActorId: prevActorId,
            PreviousHolderCastId: null,
            PreviousHolderName: prevName,
            PreviousHolderFamilyId: prevFamilyId,
            PreviousHolderIsAlive: prevAlive,
            NewHolderActorId: vacant ? null : holderActorId,
            NewHolderCastId: null,
            NewHolderName: resolvedName,
            NewHolderFamilyId: newFamilyId,
            ChangedDay: changedDay,
            ChangedMinute: nowMinute,
            VacancyUntilMinute: untilMinute,
            CauseDetail: causeDetail));
        return saved;
    }

    /// <summary>Non-lethally deposes the current living holder of a local office, opening a deterministic vacancy window.</summary>
    internal DaggerfallOfficeOverrideSave DeposeLocalOffice(
        DaggerfallLocalOffice office,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        long? currentMinute = null) =>
        SetLocalOfficeHolder(
            office,
            holderActorId: null,
            holderDisplayName: null,
            cause: cause,
            changedDay: changedDay,
            causeDetail: causeDetail ?? "was deposed",
            vacant: true,
            currentMinute: currentMinute);

    /// <summary>Removes a sparse override, returning the office to its zero-byte seeded baseline.</summary>
    internal bool ClearOverride(string officeKey)
    {
        if (!_overrides.Remove(officeKey))
            return false;
        OfficeOverrideCleared?.Invoke(officeKey);
        return true;
    }

    /// <summary>Returns all active vacant overrides whose <see cref="DaggerfallOfficeOverrideSave.VacancyUntilMinute"/> has elapsed by <paramref name="currentMinute"/>.</summary>
    internal IReadOnlyList<DaggerfallOfficeOverrideSave> DueVacancies(long currentMinute) =>
        [.. _overrides.Values
            .Where(over => over.Vacant && over.VacancyUntilMinute is long until && currentMinute >= until)
            .OrderBy(over => over.VacancyUntilMinute!.Value)
            .ThenBy(over => over.OfficeKey, StringComparer.Ordinal)];

    /// <summary>
    /// Evaluates active office vacancy windows against <paramref name="currentMinute"/> during calendar advancement,
    /// returning every vacancy whose window has matured (consumed by physical successor seating).
    /// </summary>
    internal IReadOnlyList<DaggerfallOfficeOverrideSave> AdvanceVacancies(long currentMinute, int currentDay)
    {
        _ = currentDay;
        return DueVacancies(currentMinute);
    }

    /// <summary>Resolves the capital or town <see cref="DaggerfallSiteRecord"/> from which news of <paramref name="notification"/> originates.</summary>
    internal DaggerfallSiteRecord? ResolveOfficeOriginSite(
        DaggerfallOfficeChangedNotification notification,
        DaggerfallSiteRecord? fallbackSite = null)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.OfficeSiteId is { } explicitSiteId)
        {
            if (sites is not null && sites.TryFind(explicitSiteId, out DaggerfallSiteRecord found))
                return found;
            DaggerfallSiteRecord? byId = definitions.Locations.Records.FirstOrDefault(r => r.Id == explicitSiteId);
            if (byId is not null)
                return byId;
        }
        if (ResolveCastHome(notification.PreviousHolderCastId, notification.Region) is { } prevHome)
            return prevHome.Site;
        if (ResolveCastHome(notification.NewHolderCastId, notification.Region) is { } newHome)
            return newHome.Site;
        if (notification.Region is int region && ResolveRegionCapitalSite(region) is { } capital)
            return capital;
        return fallbackSite ?? definitions.Locations.Records.FirstOrDefault();
    }

    /// <summary>Resolves the current leader persona for one of the canonical B1 board-game seats.</summary>
    internal DaggerfallSeatLeaderPersona ResolveBoardSeatPersona(string seatId)
    {
        if (!definitions.BoardSeats.TryGetBySeatId(seatId, out DaggerfallBoardSeatBinding? seat) || seat is null)
            throw new ArgumentException($"Unknown board-game seat '{seatId}'.", nameof(seatId));

        DaggerfallOfficeKind officeKind = ToOfficeKind(seat.Kind);
        string officeKey = FactionOfficeKey(seat.OfficeFactionId);
        bool overridden = _overrides.TryGetValue(officeKey, out DaggerfallOfficeOverrideSave? over);
        DaggerfallOfficeHolder? holder = officeHolders.ResolveOffice(seat.OfficeFactionId, seat.HomeRegion);

        if (over is { Vacant: true } || holder is { HasLivingHolder: false })
        {
            return new DaggerfallSeatLeaderPersona(
                SeatId: seat.SeatId,
                SeatName: seat.SeatName,
                OfficeKey: officeKey,
                Kind: officeKind,
                OfficeFactionId: seat.OfficeFactionId,
                HolderActorId: null,
                HolderCastId: null,
                HolderFactionId: null,
                DisplayName: string.Empty,
                Title: over?.Title ?? holder?.Title ?? seat.DefaultTitle,
                IsVacant: true,
                IsOverridden: overridden,
                PreviousHolderName: over?.PreviousHolderName ?? holder?.PreviousHolderName,
                ChangeCause: over?.Cause ?? (holder?.PreviousHolderName is not null ? DaggerfallOfficeChangeCause.Death : null),
                CauseDetail: over?.CauseDetail ?? holder?.PreviousHolderFate,
                VacancyOpenedMinute: over?.VacancyOpenedMinute,
                VacancyUntilMinute: over?.VacancyUntilMinute,
                IsRegency: over?.IsRegency ?? false);
        }

        if (holder is { HasLivingHolder: true })
        {
            return new DaggerfallSeatLeaderPersona(
                SeatId: seat.SeatId,
                SeatName: seat.SeatName,
                OfficeKey: officeKey,
                Kind: officeKind,
                OfficeFactionId: seat.OfficeFactionId,
                HolderActorId: holder.ActorId,
                HolderCastId: holder.CastId,
                HolderFactionId: holder.HolderFactionId,
                DisplayName: holder.DisplayName,
                Title: holder.Title ?? seat.DefaultTitle,
                IsVacant: false,
                IsOverridden: overridden,
                PreviousHolderName: over?.PreviousHolderName ?? holder.PreviousHolderName,
                ChangeCause: over?.Cause ?? (holder.PreviousHolderName is not null ? DaggerfallOfficeChangeCause.Death : null),
                CauseDetail: over?.CauseDetail ?? holder.PreviousHolderFate,
                VacancyOpenedMinute: over?.VacancyOpenedMinute,
                VacancyUntilMinute: over?.VacancyUntilMinute,
                IsRegency: over?.IsRegency ?? false);
        }

        string defaultName = definitions.Factions.Factions.TryGetValue(seat.OfficeFactionId, out DaggerfallFactionDefinition? faction)
            ? $"{seat.DefaultTitle} of {faction.Name}"
            : $"{seat.DefaultTitle} of {seat.SeatName}";
        return new DaggerfallSeatLeaderPersona(
            SeatId: seat.SeatId,
            SeatName: seat.SeatName,
            OfficeKey: officeKey,
            Kind: officeKind,
            OfficeFactionId: seat.OfficeFactionId,
            HolderActorId: null,
            HolderCastId: null,
            HolderFactionId: seat.OfficeFactionId,
            DisplayName: defaultName,
            Title: seat.DefaultTitle,
            IsVacant: false,
            IsOverridden: overridden,
            PreviousHolderName: over?.PreviousHolderName,
            ChangeCause: over?.Cause,
            CauseDetail: over?.CauseDetail);
    }

    /// <summary>Resolves all canonical B1 board-game seat personas in seat-table order.</summary>
    internal IReadOnlyList<DaggerfallSeatLeaderPersona> ResolveAllBoardSeatPersonas() =>
        [.. definitions.BoardSeats.All.Select(seat => ResolveBoardSeatPersona(seat.SeatId))];

    /// <summary>
    /// Whether <paramref name="actorId"/> currently holds an institutional office (board seat, active override,
    /// or local site office such as Watch Captain, Guild Master, Temple Head, or Court Factor).
    /// </summary>
    /// <summary>
    /// Fast O(1) office-holder check for exterior source-population walkers: checks active office overrides
    /// plus the site's exterior Watch Captain without scanning every interior building in town on every frame.
    /// </summary>
    internal bool IsExteriorOfficeHolder(long actorId, DaggerfallSiteRecord? site = null)
    {
        if (actorId <= 0) return false;
        foreach (DaggerfallOfficeOverrideSave over in _overrides.Values)
        {
            if (!over.Vacant && over.HolderActorId == actorId)
                return true;
        }
        if (site is not null && ResolveWatchCaptain(site) is { IsVacant: false, HolderActorId: long captainId } && captainId == actorId)
            return true;
        return false;
    }

    /// <summary>Collects every active (non-vacant) office holder actor ID for <paramref name="site"/> and board seats in one pass.</summary>
    internal HashSet<long> CollectOfficeHolderIds(DaggerfallSiteRecord? site = null)
    {
        HashSet<long> ids = [];
        foreach (DaggerfallOfficeOverrideSave over in _overrides.Values)
        {
            if (!over.Vacant && over.HolderActorId is long holderId && holderId > 0)
                ids.Add(holderId);
        }
        if (site is not null)
        {
            foreach (DaggerfallLocalOffice office in ResolveLocalOffices(site))
            {
                if (!office.IsVacant && office.HolderActorId is long localHolder && localHolder > 0)
                    ids.Add(localHolder);
            }
        }
        foreach (var seat in definitions.BoardSeats.All)
        {
            if (ResolveBoardSeatPersona(seat.SeatId) is { IsVacant: false, HolderActorId: long seatHolder } && seatHolder > 0)
                ids.Add(seatHolder);
        }
        return ids;
    }

    internal bool IsOfficeHolder(long actorId, DaggerfallSiteRecord? site = null)
    {
        if (actorId <= 0) return false;
        foreach (DaggerfallOfficeOverrideSave over in _overrides.Values)
        {
            if (!over.Vacant && over.HolderActorId == actorId)
                return true;
        }
        if (site is not null)
        {
            foreach (DaggerfallLocalOffice office in ResolveLocalOffices(site))
            {
                if (!office.IsVacant && office.HolderActorId == actorId)
                    return true;
            }
        }
        foreach (var seat in definitions.BoardSeats.All)
        {
            if (ResolveBoardSeatPersona(seat.SeatId) is { IsVacant: false, HolderActorId: long seatHolder }
                && seatHolder == actorId)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Deterministically resolves every local institutional office at <paramref name="site"/> (guild hall masters,
    /// temple heads, court factors, and watch captain) bound to existing seeded building/town slots.
    /// Works identically before and after first visit and writes zero save bytes until overridden.
    /// </summary>
    internal IReadOnlyList<DaggerfallLocalOffice> ResolveLocalOffices(DaggerfallSiteRecord site) =>
        ResolveLocalOffices(site, ignoreDeadActorId: null);

    private IReadOnlyList<DaggerfallLocalOffice> ResolveLocalOffices(DaggerfallSiteRecord site, long? ignoreDeadActorId)
    {
        ArgumentNullException.ThrowIfNull(site);
        List<DaggerfallLocalOffice> offices = [];

        if (site.Exterior is { Blocks.Count: > 0 } exterior)
        {
            // 1. Local Watch Captain bound deterministically to the exterior gate guard slot (dynamic/000).
            offices.Add(ResolveWatchCaptain(site, ignoreDeadActorId));

            // 2. Local building offices: Guild Hall Masters, Temple Heads, Court Factors.
            foreach (DaggerfallSiteBuildingSource building in exterior.Buildings.Values
                .OrderBy(b => b.Id.BlockY)
                .ThenBy(b => b.Id.BlockX)
                .ThenBy(b => b.Id.Index))
            {
                if (TryResolveBuildingOffice(site, building, ignoreDeadActorId) is { } localOffice)
                    offices.Add(localOffice);
            }
        }

        return offices;
    }

    /// <summary>
    /// Deterministically resolves the 2..4 seeded provincial Great Houses (<c>fam:house:r{region}:{0..N-1}</c>)
    /// for <paramref name="region"/> and associates each house with the Tier-2 regional court-factor offices
    /// across the province currently held by members of that house. Kept strictly separate from Tier-1 board seats.
    /// </summary>
    internal IReadOnlyList<DaggerfallRegionalGreatHouse> ResolveRegionalGreatHouses(int region)
    {
        if (region is < 0 or > 61)
            return [];

        ulong socialSeed = worldSocialSeed();
        int houseCount = DaggerfallHouseholds.GreatHouseCountForRegion(socialSeed, region);
        Dictionary<string, List<DaggerfallLocalOffice>> byHouse = new(StringComparer.Ordinal);
        for (int i = 0; i < houseCount; i++)
            byHouse[DaggerfallHouseholds.GreatHouseId(region, i)] = [];

        foreach (DaggerfallSiteRecord site in definitions.Locations.Records
            .Where(r => r.Id.Region == region && r.Exterior is { Buildings.Count: > 0 })
            .OrderBy(r => r.Id.Index))
        {
            foreach (DaggerfallSiteBuildingSource building in site.Exterior!.Buildings.Values
                .Where(b => b.Source.BuildingType == PalaceBuildingType)
                .OrderBy(b => b.Id.BlockY)
                .ThenBy(b => b.Id.BlockX)
                .ThenBy(b => b.Id.Index))
            {
                if (TryResolveBuildingOffice(site, building, ignoreDeadActorId: null) is { } office
                    && !string.IsNullOrWhiteSpace(office.FamilyId)
                    && byHouse.TryGetValue(office.FamilyId, out List<DaggerfallLocalOffice>? bucket))
                {
                    bucket.Add(office);
                }
            }
        }

        List<DaggerfallRegionalGreatHouse> result = new(houseCount);
        for (int i = 0; i < houseCount; i++)
        {
            string familyId = DaggerfallHouseholds.GreatHouseId(region, i);
            result.Add(new DaggerfallRegionalGreatHouse(familyId, region, i, byHouse[familyId].AsReadOnly()));
        }
        return result;
    }

    /// <summary>Resolves the town's deterministic Watch Captain office (slot <c>dynamic/000</c>).</summary>
    internal DaggerfallLocalOffice ResolveWatchCaptain(DaggerfallSiteRecord site) =>
        ResolveWatchCaptain(site, ignoreDeadActorId: null);

    private DaggerfallLocalOffice ResolveWatchCaptain(DaggerfallSiteRecord site, long? ignoreDeadActorId)
    {
        ArgumentNullException.ThrowIfNull(site);
        DaggerfallWorldProfileKey exteriorKey = DaggerfallWorldProfileIds.Exterior(site.Id);
        string slotId = DaggerfallExteriorPopulation.WatchCaptainSlotId(site);
        long defaultActorId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{exteriorKey.LogicalId}/{slotId}");
        int regionalPeopleFaction = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, site.Id.Region)?.Id ?? 0;
        string officeKey = WatchCaptainOfficeKey(site.Id);
        string title = $"Watch Captain of {site.Name}";

        return ResolveLocalSlotOffice(
            officeKey: officeKey,
            kind: DaggerfallOfficeKind.WatchCaptain,
            title: title,
            site: site,
            buildingId: null,
            profileKey: exteriorKey,
            defaultSlotId: slotId,
            defaultActorId: defaultActorId,
            officeFactionId: regionalPeopleFaction,
            fallbackCandidates: [],
            ignoreDeadActorId: ignoreDeadActorId);
    }

    /// <summary>
    /// Reacts when an actor dies in play: if <paramref name="deadActorId"/> currently holds a major seat, regional
    /// throne, or local office at <paramref name="activeSite"/>, writes ONE sparse <see cref="DaggerfallOfficeChangeCause.Death"/>
    /// vacancy override with a deterministic <see cref="DaggerfallOfficeOverrideSave.VacancyUntilMinute"/> window
    /// (<c>24–48h</c> Tier 3 local; <c>7–14 days</c> Tier 2 regional / Tier 1 seat) and publishes <see cref="OfficeChanged"/>.
    /// Ordinary non-office deaths write zero bytes.
    /// </summary>
    internal IReadOnlyList<DaggerfallOfficeOverrideSave> ReactToDeath(
        long deadActorId,
        int currentDay,
        DaggerfallSiteRecord? activeSite = null,
        long? currentMinute = null)
    {
        if (deadActorId <= 0 || deadActorId == DaggerfallActorIdentity.PlayerEntityId)
            return [];

        long nowMinute = ResolveMinute(currentDay, currentMinute);
        List<DaggerfallOfficeOverrideSave> recorded = [];

        // 1. Check all existing overridden offices first (covers any previously appointed/succeeded holder).
        // Any office present in _overrides (appointed OR explicitly vacant) is governed solely by this pass so
        // the death of a previously deposed default holder never overwrites an active appointment or vacancy.
        foreach (DaggerfallOfficeOverrideSave existing in _overrides.Values.ToArray())
        {
            long? existingActorId = existing.HolderActorId
                ?? (!string.IsNullOrWhiteSpace(existing.HolderCastId)
                    ? DaggerfallPeopleSlots.CastIndividualIdentity(existing.HolderCastId)
                    : null);
            if (existing.Vacant || existingActorId != deadActorId)
                continue;

            string? existingSlainName = !string.IsNullOrWhiteSpace(existing.HolderDisplayName)
                ? existing.HolderDisplayName
                : (!string.IsNullOrWhiteSpace(existing.HolderCastId)
                    && definitions.People.Individuals.TryGetValue(existing.HolderCastId, out DaggerfallCastIndividualDefinition? slainCast)
                    ? slainCast.Name
                    : null);

            if (existing.OfficeFactionId is int fid && existing.Kind is DaggerfallOfficeKind.Ruler or DaggerfallOfficeKind.MajorSeat)
            {
                recorded.Add(RecordFactionHolderDeath(
                    fid, existing.Kind, deadActorId, existing.HolderCastId, existingSlainName, currentDay, nowMinute));
            }
            else if (activeSite is not null
                && ResolveLocalOffices(activeSite, ignoreDeadActorId: deadActorId)
                    .FirstOrDefault(o => StringComparer.Ordinal.Equals(o.OfficeKey, existing.OfficeKey)) is { } local)
            {
                recorded.Add(RecordLocalHolderDeath(
                    activeSite,
                    local with { HolderActorId = deadActorId, DisplayName = existingSlainName ?? local.DisplayName },
                    deadActorId,
                    currentDay,
                    nowMinute));
            }
            else
            {
                long untilMinute = checked(nowMinute + ComputeVacancyDurationMinutes(worldSocialSeed(), existing.OfficeKey, existing.Kind, nowMinute));
                string? slainFamily = ResolveActorFamilyId(deadActorId, existing.HolderCastId, null);
                DaggerfallOfficeOverrideSave vacated = existing with
                {
                    HolderActorId = null,
                    HolderCastId = null,
                    HolderFactionId = null,
                    HolderDisplayName = null,
                    PreviousHolderActorId = deadActorId,
                    PreviousHolderCastId = existing.HolderCastId,
                    PreviousHolderName = existingSlainName ?? existing.PreviousHolderName,
                    PreviousHolderFamilyId = slainFamily,
                    Cause = DaggerfallOfficeChangeCause.Death,
                    CauseDetail = "was slain",
                    ChangedDay = currentDay,
                    Vacant = true,
                    VacancyOpenedMinute = nowMinute,
                    VacancyUntilMinute = untilMinute,
                    IsRegency = false,
                };
                vacated.Validate();
                _overrides[existing.OfficeKey] = vacated;
                recorded.Add(vacated);
                PublishChange(new DaggerfallOfficeChangedNotification(
                    OfficeKey: vacated.OfficeKey,
                    Kind: vacated.Kind,
                    ChangeKind: DaggerfallOfficeChangeKind.VacancyOpened,
                    Cause: DaggerfallOfficeChangeCause.Death,
                    Region: vacated.OfficeRegion,
                    OfficeSiteId: vacated.OfficeSiteId,
                    OfficeProfileKey: DaggerfallCastHomes.ResolveProfileKey(ResolveCastHome(existing.HolderCastId, vacated.OfficeRegion)),
                    OfficeSlotId: null,
                    OfficeFactionId: vacated.OfficeFactionId,
                    SeatId: vacated.SeatId,
                    Title: vacated.Title ?? "Officer",
                    PreviousHolderActorId: deadActorId,
                    PreviousHolderCastId: existing.HolderCastId,
                    PreviousHolderName: vacated.PreviousHolderName,
                    PreviousHolderFamilyId: slainFamily,
                    PreviousHolderIsAlive: false,
                    NewHolderActorId: null,
                    NewHolderCastId: null,
                    NewHolderName: null,
                    NewHolderFamilyId: null,
                    ChangedDay: currentDay,
                    ChangedMinute: nowMinute,
                    VacancyUntilMinute: untilMinute,
                    CauseDetail: "was slain"));
            }
        }

        // 2. Check untouched baseline canonical major seats and regional province rulers.
        HashSet<int> checkedFactions = [];
        foreach (DaggerfallBoardSeatBinding seat in definitions.BoardSeats.All)
        {
            if (!checkedFactions.Add(seat.OfficeFactionId) || _overrides.ContainsKey(FactionOfficeKey(seat.OfficeFactionId)))
                continue;
            if (MatchesHolderActor(seat.OfficeFactionId, seat.HomeRegion, deadActorId, out string? slainName, out string? slainCastId))
                recorded.Add(RecordFactionHolderDeath(seat.OfficeFactionId, ToOfficeKind(seat.Kind), deadActorId, slainCastId, slainName, currentDay, nowMinute));
        }

        foreach (DaggerfallFactionDefinition province in definitions.Factions.Factions.Values
            .Where(f => f.Type == ProvinceFactionType)
            .OrderBy(f => f.Id))
        {
            if (!checkedFactions.Add(province.Id) || _overrides.ContainsKey(FactionOfficeKey(province.Id)))
                continue;
            if (MatchesHolderActor(province.Id, province.Region >= 0 ? province.Region : null, deadActorId, out string? slainName, out string? slainCastId))
                recorded.Add(RecordFactionHolderDeath(province.Id, DaggerfallOfficeKind.Ruler, deadActorId, slainCastId, slainName, currentDay, nowMinute));
        }

        // 3. Check untouched baseline local offices at the active site (guildhall master, temple head, watch captain, court factor).
        // Pass ignoreDeadActorId so we evaluate who held each local office immediately before this actor's death
        // (ActorLedger.Dead is recorded by SessionPersistence right before ReactToDeath runs).
        if (activeSite is not null)
        {
            foreach (DaggerfallLocalOffice local in ResolveLocalOffices(activeSite, ignoreDeadActorId: deadActorId))
            {
                if (_overrides.ContainsKey(local.OfficeKey) || local.IsVacant)
                    continue;
                if (local.HolderActorId == deadActorId)
                {
                    recorded.Add(RecordLocalHolderDeath(activeSite, local, deadActorId, currentDay, nowMinute));
                }
            }
        }

        return recorded;
    }

    internal DaggerfallOfficeLedgerSave Capture() =>
        new([.. _overrides.Values.OrderBy(entry => entry.OfficeKey, StringComparer.Ordinal)]);

    internal void Restore(DaggerfallOfficeLedgerSave? saved)
    {
        _overrides.Clear();
        if (saved is null)
            return;
        saved.Validate();
        foreach (DaggerfallOfficeOverrideSave entry in saved.Overrides)
            _overrides[entry.OfficeKey] = entry;
    }

    private readonly record struct OutgoingHolderSnapshot(
        long? ActorId,
        string? CastId,
        string? DisplayName,
        string? FamilyId,
        bool IsAlive,
        DaggerfallCastHomeLocation? Home);

    private void PublishChange(DaggerfallOfficeChangedNotification notification) =>
        OfficeChanged?.Invoke(notification);

    private OutgoingHolderSnapshot SnapshotOutgoingFactionHolder(
        int officeFactionId,
        string officeKey,
        DaggerfallBoardSeatBinding? seat,
        int? officeRegion,
        long? explicitActorId,
        string? explicitName)
    {
        long? actorId = explicitActorId;
        string? castId = null;
        string? name = explicitName;
        string? familyId = null;
        DaggerfallCastHomeLocation? home = null;

        if (_overrides.TryGetValue(officeKey, out DaggerfallOfficeOverrideSave? prior))
        {
            if (!prior.Vacant)
            {
                actorId ??= prior.HolderActorId
                    ?? (!string.IsNullOrWhiteSpace(prior.HolderCastId)
                        ? DaggerfallPeopleSlots.CastIndividualIdentity(prior.HolderCastId)
                        : null);
                castId ??= prior.HolderCastId;
                name ??= prior.HolderDisplayName;
            }
            else
            {
                actorId ??= prior.PreviousHolderActorId;
                castId ??= prior.PreviousHolderCastId;
                name ??= prior.PreviousHolderName;
                familyId ??= prior.PreviousHolderFamilyId;
            }
        }

        if (actorId is null || name is null || castId is null)
        {
            DaggerfallOfficeHolder? current = officeHolders.ResolveOffice(officeFactionId, officeRegion ?? seat?.HomeRegion);
            if (current is { HasLivingHolder: true })
            {
                actorId ??= current.ActorId;
                castId ??= current.CastId;
                name ??= current.DisplayName;
                familyId ??= current.FamilyId;
                home ??= current.Home;
            }
        }

        if (castId is null && actorId is long aid)
        {
            foreach (DaggerfallCastIndividualDefinition candidate in definitions.People.Individuals.Values)
            {
                if (DaggerfallPeopleSlots.CastIndividualIdentity(candidate.Id) == aid)
                {
                    castId = candidate.Id;
                    name ??= candidate.Name;
                    familyId ??= candidate.FamilyId;
                    break;
                }
            }
        }

        home ??= ResolveCastHome(castId, officeRegion);
        familyId ??= ResolveActorFamilyId(actorId, castId, null);
        bool isAlive = actorId is long id && !officeHolders.IsActorDead(id);
        return new OutgoingHolderSnapshot(actorId, castId, name, familyId, isAlive, home);
    }

    private int? ResolveFactionOfficeRegion(int officeFactionId, DaggerfallBoardSeatBinding? seat)
    {
        if (seat?.HomeRegion is int seatRegion and >= 0 and <= 61)
            return seatRegion;
        if (definitions.Factions.Factions.TryGetValue(officeFactionId, out DaggerfallFactionDefinition? faction)
            && faction.Region is >= 0 and <= 61)
            return faction.Region;
        return null;
    }

    private string? ResolveDefaultFactionTitle(int officeFactionId) =>
        definitions.Factions.Factions.TryGetValue(officeFactionId, out DaggerfallFactionDefinition? faction)
            ? DaggerfallOfficeHolders.DefaultTitle(faction)
            : null;

    private DaggerfallCastHomeLocation? ResolveCastHome(string? castId, int? preferredRegion)
    {
        if (string.IsNullOrWhiteSpace(castId)
            || sites is null
            || !definitions.People.Individuals.TryGetValue(castId, out DaggerfallCastIndividualDefinition? cast))
            return null;
        return DaggerfallCastHomes.TryResolve(sites, cast, preferredRegion, out DaggerfallCastHomeLocation? home)
            ? home
            : null;
    }

    private DaggerfallSiteRecord? ResolveRegionCapitalSite(int? region)
    {
        if (region is not int r || r is < 0 or > 61)
            return null;
        return definitions.Locations.Records
            .Where(site => site.Id.Region == r && site.Exterior is { Buildings.Count: > 0 })
            .OrderByDescending(site => site.Exterior!.Buildings.Values.Any(b => b.Source.BuildingType == PalaceBuildingType) ? 1 : 0)
            .ThenByDescending(site => site.Exterior!.Buildings.Count)
            .ThenBy(site => site.Id.Index)
            .FirstOrDefault()
            ?? definitions.Locations.Records
                .Where(site => site.Id.Region == r)
                .OrderBy(site => site.Id.Index)
                .FirstOrDefault();
    }

    private bool MatchesHolderActor(int officeFactionId, int? preferredRegion, long deadActorId, out string? slainName, out string? slainCastId)
    {
        slainName = null;
        slainCastId = null;
        if (!definitions.Factions.Factions.TryGetValue(officeFactionId, out DaggerfallFactionDefinition? faction))
            return false;

        // Check default cast individuals bound to this office (remember ActorLedger may already mark deadActorId Dead!).
        List<int> candidates = [];
        if (faction.Type == 4) candidates.Add(faction.Id);
        candidates.AddRange(faction.Children);
        if (candidates.Count == 0 && faction.Parent > 0) candidates.Add(faction.Parent);

        foreach (int candidateFactionId in candidates)
        {
            if (officeHolders.TryResolveCastIndividual(candidateFactionId, preferredRegion, out DaggerfallCastIndividualDefinition? cast, out _)
                && cast is not null)
            {
                long castActorId = DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
                if (castActorId == deadActorId)
                {
                    slainCastId = cast.Id;
                    slainName = npcs.TryGet(deadActorId, out DaggerfallNpc? reg) && !string.IsNullOrWhiteSpace(reg?.DisplayName)
                        ? reg.DisplayName
                        : cast.Name;
                    return true;
                }
                // If an earlier living cast individual is still ahead in succession order, deadActorId was not the holder.
                if (!officeHolders.IsActorDead(castActorId))
                    return false;
            }
        }
        return false;
    }

    private DaggerfallOfficeOverrideSave RecordFactionHolderDeath(
        int officeFactionId,
        DaggerfallOfficeKind kind,
        long deadActorId,
        string? slainCastId,
        string? slainName,
        int currentDay,
        long currentMinute)
    {
        string key = FactionOfficeKey(officeFactionId);
        definitions.BoardSeats.TryGetByFactionId(officeFactionId, out DaggerfallBoardSeatBinding? seat);
        int? officeRegion = ResolveFactionOfficeRegion(officeFactionId, seat);
        string? resolvedCastId = slainCastId;
        if (resolvedCastId is null)
        {
            foreach (DaggerfallCastIndividualDefinition candidate in definitions.People.Individuals.Values)
            {
                if (DaggerfallPeopleSlots.CastIndividualIdentity(candidate.Id) == deadActorId)
                {
                    resolvedCastId = candidate.Id;
                    break;
                }
            }
        }
        DaggerfallCastHomeLocation? home = ResolveCastHome(resolvedCastId, officeRegion);
        DaggerfallSiteId? officeSiteId = home?.Site.Id ?? ResolveRegionCapitalSite(officeRegion)?.Id;
        string? slainFamilyId = ResolveActorFamilyId(deadActorId, resolvedCastId, null);
        long untilMinute = checked(currentMinute + ComputeVacancyDurationMinutes(worldSocialSeed(), key, kind, currentMinute));
        string resolvedTitle = seat?.DefaultTitle ?? ResolveDefaultFactionTitle(officeFactionId) ?? "Ruler";

        DaggerfallOfficeOverrideSave vacated = new(
            OfficeKey: key,
            Kind: kind,
            OfficeFactionId: officeFactionId,
            SeatId: seat?.SeatId,
            HolderActorId: null,
            HolderCastId: null,
            HolderFactionId: null,
            HolderDisplayName: null,
            Title: seat?.DefaultTitle,
            PreviousHolderActorId: deadActorId,
            PreviousHolderName: slainName,
            Cause: DaggerfallOfficeChangeCause.Death,
            CauseDetail: "was slain",
            ChangedDay: currentDay,
            Vacant: true,
            VacancyOpenedMinute: currentMinute,
            VacancyUntilMinute: untilMinute,
            IsRegency: false,
            OfficeRegion: officeRegion ?? officeSiteId?.Region,
            OfficeSiteIndex: officeSiteId?.Index,
            PreviousHolderCastId: resolvedCastId,
            PreviousHolderFamilyId: slainFamilyId);
        vacated.Validate();
        _overrides[key] = vacated;

        PublishChange(new DaggerfallOfficeChangedNotification(
            OfficeKey: key,
            Kind: kind,
            ChangeKind: DaggerfallOfficeChangeKind.VacancyOpened,
            Cause: DaggerfallOfficeChangeCause.Death,
            Region: vacated.OfficeRegion,
            OfficeSiteId: vacated.OfficeSiteId,
            OfficeProfileKey: DaggerfallCastHomes.ResolveProfileKey(home),
            OfficeSlotId: null,
            OfficeFactionId: officeFactionId,
            SeatId: seat?.SeatId,
            Title: resolvedTitle,
            PreviousHolderActorId: deadActorId,
            PreviousHolderCastId: resolvedCastId,
            PreviousHolderName: slainName,
            PreviousHolderFamilyId: slainFamilyId,
            PreviousHolderIsAlive: false,
            NewHolderActorId: null,
            NewHolderCastId: null,
            NewHolderName: null,
            NewHolderFamilyId: null,
            ChangedDay: currentDay,
            ChangedMinute: currentMinute,
            VacancyUntilMinute: untilMinute,
            CauseDetail: "was slain"));
        return vacated;
    }

    private DaggerfallOfficeOverrideSave RecordLocalHolderDeath(
        DaggerfallSiteRecord site,
        DaggerfallLocalOffice local,
        long deadActorId,
        int currentDay,
        long currentMinute)
    {
        _ = site;
        _ = deadActorId;
        // Open a deterministic VacancyUntil window (24-48h Tier 3 local; 7-14d Tier 2 CourtFactor) rather than
        // instant pop-in replacement; physical successor seating runs once VacancyUntil expires (#11106).
        return SetLocalOfficeHolder(
            local,
            holderActorId: null,
            holderDisplayName: null,
            cause: DaggerfallOfficeChangeCause.Death,
            changedDay: currentDay,
            causeDetail: "was slain",
            vacant: true,
            currentMinute: currentMinute);
    }

    private DaggerfallLocalOffice? TryResolveBuildingOffice(
        DaggerfallSiteRecord site,
        DaggerfallSiteBuildingSource building,
        long? ignoreDeadActorId)
    {
        int bType = building.Source.BuildingType;
        int bFaction = building.Source.FactionId;
        DaggerfallOfficeKind? kind = ClassifyBuildingOfficeKind(bType, bFaction);
        if (kind is null)
            return null;

        List<(string SlotId, long ActorId, string DisplayName, string? FamilyId)> candidates = EnumerateBuildingCandidates(site, building);
        if (candidates.Count == 0)
            return null;

        (string primarySlot, long primaryId, _, _) = candidates[0];
        DaggerfallWorldProfileKey profileKey = DaggerfallWorldProfileIds.Interior(site.Id, building.Id);
        string officeKey = LocalBuildingOfficeKey(kind.Value, site.Id, building.Id);
        string title = FormatLocalBuildingTitle(kind.Value, bFaction, site.Name);

        return ResolveLocalSlotOffice(
            officeKey: officeKey,
            kind: kind.Value,
            title: title,
            site: site,
            buildingId: building.Id,
            profileKey: profileKey,
            defaultSlotId: primarySlot,
            defaultActorId: primaryId,
            officeFactionId: bFaction,
            fallbackCandidates: candidates,
            ignoreDeadActorId: ignoreDeadActorId);
    }

    private bool IsDeadExcluding(long actorId, long? ignoreDeadActorId) =>
        actorId != ignoreDeadActorId && officeHolders.IsActorDead(actorId);

    private DaggerfallLocalOffice ResolveLocalSlotOffice(
        string officeKey,
        DaggerfallOfficeKind kind,
        string title,
        DaggerfallSiteRecord site,
        DaggerfallSiteBuildingId? buildingId,
        DaggerfallWorldProfileKey profileKey,
        string defaultSlotId,
        long defaultActorId,
        int officeFactionId,
        IReadOnlyList<(string SlotId, long ActorId, string DisplayName, string? FamilyId)> fallbackCandidates,
        long? ignoreDeadActorId)
    {
        if (_overrides.TryGetValue(officeKey, out DaggerfallOfficeOverrideSave? over))
        {
            if (over.Vacant)
            {
                return new DaggerfallLocalOffice(
                    OfficeKey: officeKey,
                    Kind: kind,
                    Title: over.Title ?? title,
                    SiteId: site.Id,
                    BuildingId: buildingId,
                    ProfileKey: profileKey,
                    SlotId: defaultSlotId,
                    OfficeFactionId: over.OfficeFactionId ?? officeFactionId,
                    HolderActorId: null,
                    DisplayName: string.Empty,
                    IsAlive: false,
                    IsVacant: true,
                    IsOverridden: true,
                    PreviousHolderName: over.PreviousHolderName,
                    ChangeCause: over.Cause,
                    CauseDetail: over.CauseDetail,
                    VacancyOpenedMinute: over.VacancyOpenedMinute,
                    VacancyUntilMinute: over.VacancyUntilMinute,
                    IsRegency: over.IsRegency);
            }

            bool overrideAlive = over.HolderActorId is not long oid || !IsDeadExcluding(oid, ignoreDeadActorId);
            string name = ResolveActorDisplayName(over.HolderActorId, over.HolderDisplayName, title);
            string? candidateFamily = over.HolderActorId is long oidForFamily
                ? fallbackCandidates.FirstOrDefault(c => c.ActorId == oidForFamily).FamilyId
                : null;
            string? overrideFamily = ResolveActorFamilyId(over.HolderActorId, over.HolderCastId, fallbackFamilyId: candidateFamily);
            return new DaggerfallLocalOffice(
                OfficeKey: officeKey,
                Kind: kind,
                Title: over.Title ?? title,
                SiteId: site.Id,
                BuildingId: buildingId,
                ProfileKey: profileKey,
                SlotId: defaultSlotId,
                OfficeFactionId: over.OfficeFactionId ?? officeFactionId,
                HolderActorId: overrideAlive ? over.HolderActorId : null,
                DisplayName: overrideAlive ? name : string.Empty,
                IsAlive: overrideAlive,
                IsVacant: !overrideAlive,
                IsOverridden: true,
                PreviousHolderName: overrideAlive ? over.PreviousHolderName : name,
                ChangeCause: overrideAlive ? over.Cause : DaggerfallOfficeChangeCause.Death,
                CauseDetail: overrideAlive ? over.CauseDetail : "was slain",
                FamilyId: overrideAlive ? overrideFamily : null,
                VacancyOpenedMinute: over.VacancyOpenedMinute,
                VacancyUntilMinute: over.VacancyUntilMinute,
                IsRegency: over.IsRegency);
        }

        // Untouched seeded baseline: zero save bytes, live-death aware.
        string defaultName = fallbackCandidates.Count > 0
            ? ResolveActorDisplayName(defaultActorId, fallbackCandidates[0].DisplayName, title)
            : ResolveSeededExteriorWatchName(site, defaultActorId, title);
        string? defaultFamily = fallbackCandidates.Count > 0
            ? ResolveActorFamilyId(defaultActorId, null, fallbackCandidates[0].FamilyId)
            : null;

        if (!IsDeadExcluding(defaultActorId, ignoreDeadActorId))
        {
            return new DaggerfallLocalOffice(
                OfficeKey: officeKey,
                Kind: kind,
                Title: title,
                SiteId: site.Id,
                BuildingId: buildingId,
                ProfileKey: profileKey,
                SlotId: defaultSlotId,
                OfficeFactionId: officeFactionId,
                HolderActorId: defaultActorId,
                DisplayName: defaultName,
                IsAlive: true,
                IsVacant: false,
                IsOverridden: false,
                FamilyId: defaultFamily);
        }

        // Default slot holder died without an explicit override yet: look for next living candidate in the same building.
        for (int i = 1; i < fallbackCandidates.Count; i++)
        {
            (string nextSlot, long nextId, string nextFallbackName, string? nextFallbackFamily) = fallbackCandidates[i];
            if (IsDeadExcluding(nextId, ignoreDeadActorId))
                continue;
            return new DaggerfallLocalOffice(
                OfficeKey: officeKey,
                Kind: kind,
                Title: title,
                SiteId: site.Id,
                BuildingId: buildingId,
                ProfileKey: profileKey,
                SlotId: nextSlot,
                OfficeFactionId: officeFactionId,
                HolderActorId: nextId,
                DisplayName: ResolveActorDisplayName(nextId, nextFallbackName, title),
                IsAlive: true,
                IsVacant: false,
                IsOverridden: false,
                PreviousHolderName: defaultName,
                ChangeCause: DaggerfallOfficeChangeCause.Death,
                CauseDetail: "was slain",
                FamilyId: ResolveActorFamilyId(nextId, null, nextFallbackFamily));
        }

        return new DaggerfallLocalOffice(
            OfficeKey: officeKey,
            Kind: kind,
            Title: title,
            SiteId: site.Id,
            BuildingId: buildingId,
            ProfileKey: profileKey,
            SlotId: defaultSlotId,
            OfficeFactionId: officeFactionId,
            HolderActorId: null,
            DisplayName: string.Empty,
            IsAlive: false,
            IsVacant: true,
            IsOverridden: false,
            PreviousHolderName: defaultName,
            ChangeCause: DaggerfallOfficeChangeCause.Death,
            CauseDetail: "was slain");
    }

    private string? ResolveActorFamilyId(long? actorId, string? castId, string? fallbackFamilyId)
    {
        if (actorId is long id)
        {
            if (actorLedger.TryGet(id, out ActorLedgerEntry? entry) && entry?.Social is { } social)
            {
                if (social.FamilyIdCleared)
                    return null;
                if (!string.IsNullOrWhiteSpace(social.FamilyId))
                    return social.FamilyId;
            }
            if (npcs.TryGet(id, out DaggerfallNpc? npc) && !string.IsNullOrWhiteSpace(npc.FamilyId))
                return npc.FamilyId;
        }
        if (!string.IsNullOrWhiteSpace(castId)
            && definitions.People.Individuals.TryGetValue(castId, out DaggerfallCastIndividualDefinition? cast)
            && !string.IsNullOrWhiteSpace(cast.FamilyId))
            return cast.FamilyId;
        return fallbackFamilyId;
    }

    private List<(string SlotId, long ActorId, string DisplayName, string? FamilyId)> EnumerateBuildingCandidates(
        DaggerfallSiteRecord site,
        DaggerfallSiteBuildingSource building)
    {
        List<(string SlotId, long ActorId, string DisplayName, string? FamilyId, int Priority)> scored = [];
        if (!definitions.People.TryGetRmbInteriorSlots(building.Source.Id.SourceKey, building.Source.Id.Index, out DaggerfallPeopleBuildingSlots? slots)
            || slots is null || slots.Slots.Count == 0)
        {
            return [];
        }

        DaggerfallWorldProfileKey profileKey = DaggerfallWorldProfileIds.Interior(site.Id, building.Id);
        string regionName = definitions.BuildingNames.RegionName(site.Id.Region);
        string climateName = definitions.Grids.Climate.GetCell(site.MapPixelX + 1, site.MapPixelY).Name;
        int buildingFaction = building.Source.FactionId;
        int bType = building.Source.BuildingType;
        ulong socialSeed = worldSocialSeed();
        int townBuckets = DaggerfallHouseholds.TownHouseholdBucketCount(site);

        int talkableOrdinal = 0;
        foreach (DaggerfallPeopleSlotDefinition slot in slots.Slots)
        {
            // Match DaggerfallLocationAssembly.FilterTalkableSlots so person/{ordinal} aligns 1:1 with live staticNpcs!
            if (!string.Equals(slot.Interaction, "full-dialogue", StringComparison.Ordinal)
                || string.Equals(slot.SpawnerId, "town-clutter-person", StringComparison.Ordinal))
            {
                continue;
            }

            int ordinal = talkableOrdinal++;
            if (string.Equals(slot.SpawnerId, "town-child", StringComparison.Ordinal))
                continue;

            string placementId = string.Create(CultureInfo.InvariantCulture, $"person/{ordinal}");
            long durableId;
            string fallbackName;
            string? familyId = null;
            if (!string.IsNullOrWhiteSpace(slot.CastId)
                && definitions.People.Individuals.TryGetValue(slot.CastId, out DaggerfallCastIndividualDefinition? castDef))
            {
                durableId = DaggerfallPeopleSlots.CastIndividualIdentity(castDef.Id);
                fallbackName = castDef.Name;
                familyId = castDef.FamilyId;
            }
            else
            {
                durableId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{profileKey.LogicalId}/{placementId}");
                fallbackName = ResolveSeededInteriorSlotName(site, building, slot, socialSeed, regionName, climateName, ordinal);
                if (!string.IsNullOrWhiteSpace(slot.SpawnerId)
                    && definitions.People.Spawners.TryGetValue(slot.SpawnerId, out DaggerfallPeopleSpawnerDefinition? spawner))
                {
                    string slotKey = DaggerfallPeopleSlots.InteriorBuildingSlotKey(building.Id.BlockX, building.Id.BlockY, slot.SlotId);
                    familyId = DaggerfallHouseholds.ResolveForPlacedSlot(
                        socialSeed,
                        site.Id,
                        bType,
                        building.Id.BlockX,
                        building.Id.BlockY,
                        building.Id.Index,
                        slotKey,
                        spawner.SocialGrouping,
                        townBucketCount: townBuckets);
                }
            }

            int priority = ScoreSlotPriority(bType, buildingFaction, slot);
            scored.Add((placementId, durableId, fallbackName, familyId, priority));
        }

        return
        [
            .. scored
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.SlotId, StringComparer.Ordinal)
                .Select(item => (item.SlotId, item.ActorId, item.DisplayName, item.FamilyId)),
        ];
    }

    private int ScoreSlotPriority(int buildingType, int buildingFaction, DaggerfallPeopleSlotDefinition slot)
    {
        if (buildingType == PalaceBuildingType)
        {
            if (string.Equals(slot.SpawnerId, "town-courtier", StringComparison.Ordinal)) return 30;
            if (string.Equals(slot.SpawnerId, "town-noble", StringComparison.Ordinal)) return 25;
            if (string.Equals(slot.SpawnerId, "town-palace-attendant", StringComparison.Ordinal)) return 20;
            return 10;
        }
        if (buildingType == TempleBuildingType && !IsGuildFaction(buildingFaction))
        {
            if (slot.FactionId == 240 || (buildingFaction > 0 && slot.FactionId == buildingFaction)) return 30;
            if (string.Equals(slot.SpawnerId, "town-temple-priest", StringComparison.Ordinal)) return 20;
            return 10;
        }
        // Guild hall / order hall: prefer exact building faction or questor faction, then guild member spawner.
        if (buildingFaction > 0 && slot.FactionId == buildingFaction) return 30;
        if (slot.FactionId is 63 or 851 or 804 or 807 or 846) return 25;
        if (slot.SpawnerId is "town-mage-guild-member" or "town-fighters-guild-member" or "town-knightly-order-member" or "town-dark-brotherhood-member" or "town-guild-member")
            return 20;
        return 10;
    }

    private DaggerfallOfficeKind? ClassifyBuildingOfficeKind(int buildingType, int buildingFaction)
    {
        if (buildingType == PalaceBuildingType)
            return DaggerfallOfficeKind.CourtFactor;
        if (buildingType == GuildHallBuildingType)
            return DaggerfallOfficeKind.GuildHallMaster;
        if (buildingType == TempleBuildingType)
        {
            // Some Fighters Guild blocks carry buildingType 14 with buildingFaction 41; route guilds to GuildHallMaster.
            return IsGuildFaction(buildingFaction) ? DaggerfallOfficeKind.GuildHallMaster : DaggerfallOfficeKind.TempleHead;
        }
        if (buildingType == SpecialGuildOrHouseBuildingType && IsGuildFaction(buildingFaction))
            return DaggerfallOfficeKind.GuildHallMaster;
        return null;
    }

    private bool IsGuildFaction(int factionId) =>
        factionId > 0
        && (DaggerfallConcreteGuildCatalog.All.Any(g => g.FactionId == factionId && g.Kind != DaggerfallConcreteGuildKind.Temple)
            || (definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction)
                && faction.GuildGroup is 3 or 4 or 9 or 10 or 11));

    private string FormatLocalBuildingTitle(DaggerfallOfficeKind kind, int buildingFaction, string siteName)
    {
        string? factionName = buildingFaction > 0 && definitions.Factions.Factions.TryGetValue(buildingFaction, out DaggerfallFactionDefinition? faction)
            ? faction.Name
            : null;
        return kind switch
        {
            DaggerfallOfficeKind.GuildHallMaster => factionName is not null ? $"Hall Master of {factionName} ({siteName})" : $"Guild Hall Master of {siteName}",
            DaggerfallOfficeKind.TempleHead => factionName is not null ? $"Head Priest of {factionName} ({siteName})" : $"Temple Head of {siteName}",
            DaggerfallOfficeKind.CourtFactor => $"Court Factor of {siteName}",
            DaggerfallOfficeKind.WatchCaptain => $"Watch Captain of {siteName}",
            _ => $"Officer of {siteName}",
        };
    }

    private string ResolveActorDisplayName(long? actorId, string? fallback, string titleFallback)
    {
        if (actorId is long id)
        {
            if (npcs.TryGet(id, out DaggerfallNpc? npc) && !string.IsNullOrWhiteSpace(npc?.DisplayName))
                return npc.DisplayName;
            if (actorLedger.TryGet(id, out ActorLedgerEntry? entry))
            {
                if (!string.IsNullOrWhiteSpace(entry?.Social?.DisplayNameOverride))
                    return entry.Social.DisplayNameOverride;
                if (!string.IsNullOrWhiteSpace(entry?.Referral?.DisplayNameOverride))
                    return entry.Referral.DisplayNameOverride;
            }
        }
        return !string.IsNullOrWhiteSpace(fallback) ? fallback : titleFallback;
    }

    private string ResolveSeededInteriorSlotName(
        DaggerfallSiteRecord site,
        DaggerfallSiteBuildingSource building,
        DaggerfallPeopleSlotDefinition slot,
        ulong socialSeed,
        string regionName,
        string climateName,
        int ordinal)
    {
        int nameSeed = ordinal + 1;
        string gender = "Male";
        if (!string.IsNullOrWhiteSpace(slot.SpawnerId) && definitions.People.Spawners.ContainsKey(slot.SpawnerId))
        {
            string slotKey = DaggerfallPeopleSlots.InteriorBuildingSlotKey(building.Id.BlockX, building.Id.BlockY, slot.SlotId);
            DaggerfallResolvedPeopleSlot resolved = DaggerfallPeopleSlots.ResolveSlot(
                definitions.People, definitions.Factions, socialSeed, site.Id, slotKey, slot,
                regionKey: regionName, climateKey: climateName);
            int seeded = unchecked((int)(resolved.SlotSeed & 0x7FFF_FFFFUL));
            nameSeed = seeded == 0 ? 1 : seeded;
            gender = resolved.Visual.Gender;
        }
        return ResolveDeterministicName(site.Id.Region, gender, nameSeed, slot.SlotId);
    }

    private string ResolveSeededExteriorWatchName(DaggerfallSiteRecord site, long actorId, string titleFallback)
    {
        if (npcs.TryGet(actorId, out DaggerfallNpc? npc) && !string.IsNullOrWhiteSpace(npc?.DisplayName))
            return npc.DisplayName;
        if (actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) && !string.IsNullOrWhiteSpace(entry?.Social?.DisplayNameOverride))
            return entry.Social.DisplayNameOverride;
        ulong slotSeed = DaggerfallPeopleSlots.SlotSeed(worldSocialSeed(), site.Id, DaggerfallPeopleSlots.WalkerSlotKey(0));
        ushort nameSeed = (ushort)((slotSeed & 0xFFFFUL) == 0 ? 1 : (slotSeed & 0xFFFFUL));
        return ResolveDeterministicName(site.Id.Region, "Male", nameSeed, titleFallback);
    }

    private string ResolveDeterministicName(int region, string gender, int nameSeed, string fallback)
    {
        if (defaultName is not null && definitions.BuildingNames.TryGetNameBank(region, out int bank))
        {
            string race = bank == 0 ? "breton" : "redguard";
            DaggerfallNpcAppearance appearance = new(race, gender, 0, 0, nameSeed, 0);
            return defaultName(appearance, region);
        }
        return fallback;
    }

    private static string KindSlug(DaggerfallOfficeKind kind) => kind switch
    {
        DaggerfallOfficeKind.Ruler => "ruler",
        DaggerfallOfficeKind.MajorSeat => "major-seat",
        DaggerfallOfficeKind.GuildHallMaster => "guild-hall-master",
        DaggerfallOfficeKind.TempleHead => "temple-head",
        DaggerfallOfficeKind.WatchCaptain => "watch-captain",
        DaggerfallOfficeKind.CourtFactor => "court-factor",
        _ => "office",
    };

    private static string FormatFate(DaggerfallOfficeChangeCause cause, string? detail) =>
        !string.IsNullOrWhiteSpace(detail)
            ? detail
            : cause switch
            {
                DaggerfallOfficeChangeCause.Death => "was slain",
                DaggerfallOfficeChangeCause.CardMove => "was unseated",
                DaggerfallOfficeChangeCause.Quest => "was removed",
                DaggerfallOfficeChangeCause.Succession => "stepped aside",
                _ => "departed",
            };
}
