using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

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
    string? PreviousHolderFamilyId = null,
    DaggerfallWorldProfileKeySave? OfficeProfile = null,
    string? OfficeSlotId = null)
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
        OfficeProfile?.Validate();
        if (OfficeSlotId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(OfficeSlotId);
    }
}

/// <summary>
/// One sparse persisted exile record for a living office holder displaced to a deterministic refuge interior.
/// Tracks the lost office, the refuge profile where the exile stands, and the origin anchor needed to restore them.
/// </summary>
internal sealed record DaggerfallOfficeExileSave(
    long ActorId,
    string OfficeKey,
    DaggerfallOfficeKind Kind,
    string Title,
    DaggerfallWorldProfileKeySave RefugeProfile,
    DaggerfallOfficeChangeCause Cause,
    int ExiledDay,
    string? CastId = null,
    int? OfficeFactionId = null,
    string? SeatId = null,
    int? OfficeRegion = null,
    int? OfficeSiteIndex = null,
    string? DisplayName = null,
    string? CauseDetail = null,
    DaggerfallWorldProfileKeySave? OriginProfile = null,
    string? OriginSlotId = null)
{
    internal DaggerfallSiteId? OfficeSiteId =>
        OfficeRegion is int region && OfficeSiteIndex is int index
            ? new DaggerfallSiteId(region, index)
            : null;

    internal void Validate()
    {
        if (ActorId is <= 0 or DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(ActorId), "Saved exiled office holder actor id must be a positive non-player id.");
        ArgumentException.ThrowIfNullOrWhiteSpace(OfficeKey);
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), "Saved exiled office holder names an invalid office kind.");
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        ArgumentNullException.ThrowIfNull(RefugeProfile);
        RefugeProfile.Validate();
        if (!Enum.IsDefined(Cause))
            throw new ArgumentOutOfRangeException(nameof(Cause), "Saved exiled office holder names an invalid change cause.");
        if (CastId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(CastId);
        if (OfficeFactionId is < 0)
            throw new ArgumentOutOfRangeException(nameof(OfficeFactionId), "Saved exiled office holder faction id cannot be negative.");
        if (SeatId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(SeatId);
        if (OfficeRegion is < 0 or > 61)
            throw new ArgumentOutOfRangeException(nameof(OfficeRegion), "Saved exiled office holder region is outside classic region bounds.");
        if (OfficeSiteIndex is < 0)
            throw new ArgumentOutOfRangeException(nameof(OfficeSiteIndex), "Saved exiled office holder site index cannot be negative.");
        if (OfficeSiteIndex is not null && OfficeRegion is null)
            throw new ArgumentException("Saved exiled office holder site index requires an OfficeRegion.", nameof(OfficeSiteIndex));
        if (DisplayName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        if (CauseDetail is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(CauseDetail);
        OriginProfile?.Validate();
        if (OriginSlotId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(OriginSlotId);
    }
}

/// <summary>Sparse persisted office-ledger snapshot. Empty when no office has changed from its seeded baseline.</summary>
internal sealed record DaggerfallOfficeLedgerSave(
    DaggerfallOfficeOverrideSave[] Overrides,
    DaggerfallOfficeExileSave[]? Exiles = null)
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
        if (Exiles is not null)
        {
            HashSet<long> actorIds = [];
            foreach (DaggerfallOfficeExileSave exile in Exiles)
            {
                ArgumentNullException.ThrowIfNull(exile);
                exile.Validate();
                if (!actorIds.Add(exile.ActorId))
                    throw new ArgumentException($"Saved office ledger repeats exiled actor {exile.ActorId}.");
            }
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
    private const int TavernBuildingType = 15;
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
    private readonly Dictionary<long, DaggerfallOfficeExileSave> _exiles = [];

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
    internal int ExileCount => _exiles.Count;
    internal IReadOnlyCollection<DaggerfallOfficeExileSave> Exiles => _exiles.Values;

    internal bool TryGetExile(long actorId, out DaggerfallOfficeExileSave? exile)
    {
        if (actorId > 0 && _exiles.TryGetValue(actorId, out DaggerfallOfficeExileSave? found))
        {
            exile = found;
            return true;
        }
        exile = null;
        return false;
    }

    internal DaggerfallOfficeExileSave? TryGetExile(long actorId) =>
        actorId > 0 && _exiles.TryGetValue(actorId, out DaggerfallOfficeExileSave? found) ? found : null;

    internal IReadOnlyList<DaggerfallOfficeExileSave> ExilesAtProfile(DaggerfallWorldProfileKey profile) =>
        [.. _exiles.Values
            .Where(e => e.RefugeProfile.Require() == profile)
            .OrderBy(e => e.ActorId)];

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

    /// <summary>Returns the active sparse override for <paramref name="officeKey"/>, or <c>null</c> when untouched.</summary>
    internal DaggerfallOfficeOverrideSave? TryGetOverride(string officeKey) =>
        !string.IsNullOrWhiteSpace(officeKey) && _overrides.TryGetValue(officeKey, out DaggerfallOfficeOverrideSave? entry)
            ? entry
            : null;

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
        long? currentMinute = null,
        DaggerfallWorldProfileKey? newHolderHomeProfile = null,
        string? newHolderHomeSlotId = null,
        DaggerfallOfficeActorBlueprint? newHolderBlueprint = null)
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
        (DaggerfallWorldProfileKey? officeProfileKey, string? officeSlotId, DaggerfallSiteId? officeSiteId) =
            ResolveCanonicalFactionOfficeAnchor(officeFactionId, key, officeRegion, outgoing, holderCastId);
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
            PreviousHolderFamilyId: outgoing.FamilyId,
            OfficeProfile: officeProfileKey is { } opk ? DaggerfallWorldProfileKeySave.Capture(opk) : null,
            OfficeSlotId: officeSlotId);
        saved.Validate();
        _overrides[key] = saved;

        string? newFamilyId = ResolveActorFamilyId(holderActorId, holderCastId, fallbackFamilyId: null);
        DaggerfallWorldProfileKey? resolvedNewHome = newHolderHomeProfile
            ?? DaggerfallCastHomes.ResolveProfileKey(ResolveCastHome(holderCastId, officeRegion));
        bool displacesLivingIncumbent = outgoing.WasActiveIncumbent
            && outgoing.IsAlive
            && outgoing.ActorId is long prevAid
            && prevAid != holderActorId;
        DaggerfallWorldProfileKey? exileProfile = displacesLivingIncumbent
            ? ResolveExileDestination(worldSocialSeed(), key, kind, saved.OfficeRegion, saved.OfficeSiteId, officeProfileKey)
            : null;
        DaggerfallOfficeActorBlueprint? prevBlueprint = displacesLivingIncumbent
            ? BuildActorBlueprint(outgoing.ActorId, outgoing.CastId, outgoing.DisplayName, outgoing.FamilyId)
            : null;
        DaggerfallOfficeActorBlueprint? resolvedNewBlueprint = newHolderBlueprint
            ?? BuildActorBlueprint(holderActorId, holderCastId, resolvedName, newFamilyId);
        if (holderActorId is long seatedId)
            _exiles.Remove(seatedId);
        if (displacesLivingIncumbent && outgoing.ActorId is long exiledId && exileProfile is { } refuge)
        {
            RecordExile(
                exiledId, key, kind, resolvedTitle, refuge, cause, changedDay,
                castId: outgoing.CastId, officeFactionId: officeFactionId, seatId: seat?.SeatId,
                officeRegion: saved.OfficeRegion, officeSiteIndex: saved.OfficeSiteIndex,
                displayName: outgoing.DisplayName, causeDetail: causeDetail,
                originProfile: officeProfileKey, originSlotId: officeSlotId);
        }

        PublishChange(new DaggerfallOfficeChangedNotification(
            OfficeKey: key,
            Kind: kind,
            ChangeKind: DaggerfallOfficeChangeKind.SuccessorSeated,
            Cause: cause,
            Region: saved.OfficeRegion,
            OfficeSiteId: saved.OfficeSiteId,
            OfficeProfileKey: officeProfileKey,
            OfficeSlotId: officeSlotId,
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
            CauseDetail: causeDetail,
            NewHolderHomeProfile: resolvedNewHome,
            NewHolderHomeSlotId: newHolderHomeSlotId,
            NewHolderBlueprint: resolvedNewBlueprint,
            ExileProfileKey: exileProfile,
            PreviousHolderBlueprint: prevBlueprint,
            DisplacesLivingIncumbent: displacesLivingIncumbent));
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
        (DaggerfallWorldProfileKey? officeProfileKey, string? officeSlotId, DaggerfallSiteId? officeSiteId) =
            ResolveCanonicalFactionOfficeAnchor(officeFactionId, key, officeRegion, outgoing, null);

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
            PreviousHolderFamilyId: outgoing.FamilyId,
            OfficeProfile: officeProfileKey is { } opk ? DaggerfallWorldProfileKeySave.Capture(opk) : null,
            OfficeSlotId: officeSlotId);
        saved.Validate();
        _overrides[key] = saved;

        bool displacesLivingIncumbent = cause != DaggerfallOfficeChangeCause.Death
            && outgoing.WasActiveIncumbent
            && outgoing.IsAlive
            && outgoing.ActorId is not null;
        DaggerfallOfficeChangeKind changeKind = displacesLivingIncumbent
            ? DaggerfallOfficeChangeKind.HolderDeposed
            : DaggerfallOfficeChangeKind.VacancyOpened;
        DaggerfallWorldProfileKey? exileProfile = displacesLivingIncumbent
            ? ResolveExileDestination(worldSocialSeed(), key, kind, saved.OfficeRegion, saved.OfficeSiteId, officeProfileKey)
            : null;
        DaggerfallOfficeActorBlueprint? prevBlueprint = displacesLivingIncumbent
            ? BuildActorBlueprint(outgoing.ActorId, outgoing.CastId, outgoing.DisplayName, outgoing.FamilyId)
            : null;
        if (displacesLivingIncumbent && outgoing.ActorId is long exiledId && exileProfile is { } refuge)
        {
            RecordExile(
                exiledId, key, kind, resolvedTitle, refuge, cause, changedDay,
                castId: outgoing.CastId, officeFactionId: officeFactionId, seatId: seat?.SeatId,
                officeRegion: saved.OfficeRegion, officeSiteIndex: saved.OfficeSiteIndex,
                displayName: outgoing.DisplayName, causeDetail: causeDetail,
                originProfile: officeProfileKey, originSlotId: officeSlotId);
        }

        PublishChange(new DaggerfallOfficeChangedNotification(
            OfficeKey: key,
            Kind: kind,
            ChangeKind: changeKind,
            Cause: cause,
            Region: saved.OfficeRegion,
            OfficeSiteId: saved.OfficeSiteId,
            OfficeProfileKey: officeProfileKey,
            OfficeSlotId: officeSlotId,
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
            CauseDetail: causeDetail,
            ExileProfileKey: exileProfile,
            PreviousHolderBlueprint: prevBlueprint,
            DisplacesLivingIncumbent: displacesLivingIncumbent));
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
    /// When <paramref name="exileIncumbent"/> is true and a living incumbent is displaced, relocates them to their
    /// deterministic refuge and records them in <see cref="Exiles"/>.
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
        long? vacancyUntilMinute = null,
        bool exileIncumbent = false,
        DaggerfallWorldProfileKey? regentHomeProfile = null,
        string? regentHomeSlotId = null,
        DaggerfallOfficeActorBlueprint? regentBlueprint = null)
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
        (DaggerfallWorldProfileKey? officeProfileKey, string? officeSlotId, DaggerfallSiteId? officeSiteId) =
            ResolveCanonicalFactionOfficeAnchor(officeFactionId, key, officeRegion, outgoing, regentCastId);

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
            PreviousHolderFamilyId: outgoing.FamilyId,
            OfficeProfile: officeProfileKey is { } opk ? DaggerfallWorldProfileKeySave.Capture(opk) : prior?.OfficeProfile,
            OfficeSlotId: officeSlotId ?? prior?.OfficeSlotId);
        saved.Validate();
        _overrides[key] = saved;

        string? regentFamilyId = ResolveActorFamilyId(regentActorId, regentCastId, fallbackFamilyId: null);
        DaggerfallWorldProfileKey? resolvedRegentHome = regentHomeProfile
            ?? DaggerfallCastHomes.ResolveProfileKey(ResolveCastHome(regentCastId, officeRegion));
        DaggerfallOfficeActorBlueprint? resolvedRegentBlueprint = regentBlueprint
            ?? BuildActorBlueprint(regentActorId, regentCastId, resolvedName, regentFamilyId);

        bool displacesLivingIncumbent = exileIncumbent
            && outgoing.WasActiveIncumbent
            && outgoing.IsAlive
            && outgoing.ActorId is long prevAid
            && prevAid != regentActorId;
        DaggerfallWorldProfileKey? exileProfile = displacesLivingIncumbent
            ? ResolveExileDestination(worldSocialSeed(), key, kind, saved.OfficeRegion, saved.OfficeSiteId, officeProfileKey)
            : null;
        DaggerfallOfficeActorBlueprint? prevBlueprint = displacesLivingIncumbent
            ? BuildActorBlueprint(outgoing.ActorId, outgoing.CastId, outgoing.DisplayName, outgoing.FamilyId)
            : null;
        if (regentActorId is long seatedRegentId)
            _exiles.Remove(seatedRegentId);
        if (displacesLivingIncumbent && outgoing.ActorId is long exiledId && exileProfile is { } refuge)
        {
            string lostTitle = seat?.DefaultTitle ?? ResolveDefaultFactionTitle(officeFactionId) ?? "Ruler";
            RecordExile(
                exiledId, key, kind, lostTitle, refuge, cause, changedDay,
                castId: outgoing.CastId, officeFactionId: officeFactionId, seatId: seat?.SeatId,
                officeRegion: saved.OfficeRegion, officeSiteIndex: saved.OfficeSiteIndex,
                displayName: outgoing.DisplayName, causeDetail: saved.CauseDetail,
                originProfile: officeProfileKey, originSlotId: officeSlotId);
        }

        PublishChange(new DaggerfallOfficeChangedNotification(
            OfficeKey: key,
            Kind: kind,
            ChangeKind: DaggerfallOfficeChangeKind.RegencyDeclared,
            Cause: cause,
            Region: saved.OfficeRegion,
            OfficeSiteId: saved.OfficeSiteId,
            OfficeProfileKey: officeProfileKey,
            OfficeSlotId: officeSlotId,
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
            CauseDetail: saved.CauseDetail,
            NewHolderHomeProfile: resolvedRegentHome,
            NewHolderHomeSlotId: regentHomeSlotId,
            NewHolderBlueprint: resolvedRegentBlueprint,
            ExileProfileKey: exileProfile,
            PreviousHolderBlueprint: prevBlueprint,
            DisplacesLivingIncumbent: displacesLivingIncumbent));
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
        long? vacancyUntilMinute = null,
        DaggerfallWorldProfileKey? newHolderHomeProfile = null,
        string? newHolderHomeSlotId = null,
        DaggerfallOfficeActorBlueprint? newHolderBlueprint = null,
        bool suppressExile = false)
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

        bool hasPrior = _overrides.TryGetValue(office.OfficeKey, out DaggerfallOfficeOverrideSave? prior);
        bool wasVacant = hasPrior ? prior!.Vacant : office.IsVacant;
        long? prevActorId = office.HolderActorId
            ?? (hasPrior ? prior!.HolderActorId ?? prior.PreviousHolderActorId : null);
        string? prevName = !string.IsNullOrWhiteSpace(office.DisplayName)
            ? office.DisplayName
            : office.PreviousHolderName ?? (hasPrior ? prior!.PreviousHolderName : null);
        string? prevFamilyId = office.FamilyId
            ?? (hasPrior ? prior!.PreviousHolderFamilyId : null)
            ?? ResolveActorFamilyId(prevActorId, null, null);
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
            PreviousHolderFamilyId: prevFamilyId,
            OfficeProfile: DaggerfallWorldProfileKeySave.Capture(office.ProfileKey),
            OfficeSlotId: office.SlotId);
        saved.Validate();
        _overrides[office.OfficeKey] = saved;

        bool displacesLivingIncumbent = !suppressExile
            && !wasVacant
            && prevAlive
            && prevActorId is long prevId
            && (vacant ? cause != DaggerfallOfficeChangeCause.Death : prevId != holderActorId);
        DaggerfallOfficeChangeKind changeKind = vacant
            ? (displacesLivingIncumbent
                ? DaggerfallOfficeChangeKind.HolderDeposed
                : DaggerfallOfficeChangeKind.VacancyOpened)
            : DaggerfallOfficeChangeKind.SuccessorSeated;
        string? newFamilyId = vacant ? null : ResolveActorFamilyId(holderActorId, null, null);
        DaggerfallWorldProfileKey? exileProfile = displacesLivingIncumbent
            ? ResolveExileDestination(worldSocialSeed(), office.OfficeKey, office.Kind, office.SiteId.Region, office.SiteId, office.ProfileKey)
            : null;
        DaggerfallOfficeActorBlueprint? prevBlueprint = displacesLivingIncumbent
            ? BuildLocalOfficeHolderBlueprint(office, prevActorId, prevName, prevFamilyId)
            : null;
        DaggerfallOfficeActorBlueprint? resolvedNewBlueprint = !vacant
            ? (newHolderBlueprint ?? BuildActorBlueprint(holderActorId, null, resolvedName, newFamilyId))
            : null;
        if (!vacant && holderActorId is long seatedLocalId)
            _exiles.Remove(seatedLocalId);
        if (displacesLivingIncumbent && prevActorId is long exiledLocalId && exileProfile is { } refuge)
        {
            RecordExile(
                exiledLocalId, office.OfficeKey, office.Kind, office.Title, refuge, cause, changedDay,
                castId: null, officeFactionId: office.OfficeFactionId > 0 ? office.OfficeFactionId : null,
                seatId: null, officeRegion: office.SiteId.Region, officeSiteIndex: office.SiteId.Index,
                displayName: prevName, causeDetail: causeDetail,
                originProfile: office.ProfileKey, originSlotId: office.SlotId);
        }

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
            CauseDetail: causeDetail,
            NewHolderHomeProfile: !vacant ? newHolderHomeProfile : null,
            NewHolderHomeSlotId: !vacant ? newHolderHomeSlotId : null,
            NewHolderBlueprint: resolvedNewBlueprint,
            ExileProfileKey: exileProfile,
            PreviousHolderBlueprint: prevBlueprint,
            DisplacesLivingIncumbent: displacesLivingIncumbent));
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

    /// <summary>Returns all active vacant or unsettled-regency overrides whose <see cref="DaggerfallOfficeOverrideSave.VacancyUntilMinute"/> has elapsed by <paramref name="currentMinute"/>.</summary>
    internal IReadOnlyList<DaggerfallOfficeOverrideSave> DueVacancies(long currentMinute) =>
        [.. _overrides.Values
            .Where(over => (over.Vacant || over.IsRegency) && over.VacancyUntilMinute is long until && currentMinute >= until)
            .OrderBy(over => over.VacancyUntilMinute!.Value)
            .ThenBy(over => over.OfficeKey, StringComparer.Ordinal)];

    /// <summary>
    /// Evaluates active office vacancy windows against <paramref name="currentMinute"/> during calendar advancement,
    /// deterministically selecting and seating successors across Tier 1, Tier 2, and Tier 3 offices whose vacancy
    /// windows have matured. Returns every newly seated office override.
    /// </summary>
    internal IReadOnlyList<DaggerfallOfficeOverrideSave> AdvanceVacancies(long currentMinute, int currentDay)
    {
        IReadOnlyList<DaggerfallOfficeOverrideSave> due = DueVacancies(currentMinute);
        if (due.Count == 0)
            return [];

        List<DaggerfallOfficeOverrideSave> seated = [];
        foreach (DaggerfallOfficeOverrideSave vacancy in due)
        {
            if (TrySeatDeterministicSuccessor(vacancy, currentDay, currentMinute) is { } appointed)
                seated.Add(appointed);
        }
        return seated;
    }

    /// <summary>
    /// Deterministically derives an exile interior profile (tavern or rival court in a nearby friendly town)
    /// from <c>(worldSocialSeed, officeKey, kind)</c> so non-lethally deposed living holders relocate visibly
    /// rather than disappearing off-stage.
    /// </summary>
    internal DaggerfallWorldProfileKey? ResolveExileDestination(
        ulong socialSeed,
        string officeKey,
        DaggerfallOfficeKind kind,
        int? originRegion,
        DaggerfallSiteId? originSiteId,
        DaggerfallWorldProfileKey? originProfileKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(officeKey);
        bool courtOffice = kind is DaggerfallOfficeKind.Ruler or DaggerfallOfficeKind.MajorSeat or DaggerfallOfficeKind.CourtFactor;

        DaggerfallSiteRecord? originSite = originSiteId is { } sid
            ? FindSiteById(sid)
            : ResolveRegionCapitalSite(originRegion);

        List<(int Priority, double DistSq, int Region, int SiteIndex, int BlockY, int BlockX, int BuildingIndex, DaggerfallWorldProfileKey Profile)> candidates = [];

        foreach (DaggerfallSiteRecord site in definitions.Locations.Records)
        {
            if (site.Exterior is not { Buildings.Count: > 0 } exterior)
                continue;

            bool sameSite = originSiteId is { } origId && site.Id == origId;
            bool sameRegion = originRegion is int origReg && site.Id.Region == origReg;
            double distSq = originSite is not null
                ? Math.Pow(site.MapPixelX - originSite.MapPixelX, 2) + Math.Pow(site.MapPixelY - originSite.MapPixelY, 2)
                : 0d;

            foreach (DaggerfallSiteBuildingSource building in exterior.Buildings.Values)
            {
                int bType = building.Source.BuildingType;
                if (bType is not (TavernBuildingType or PalaceBuildingType))
                    continue;
                if (!courtOffice && bType != TavernBuildingType)
                    continue;

                DaggerfallWorldProfileKey profile = DaggerfallWorldProfileIds.Interior(site.Id, building.Id);
                if (originProfileKey is { } origProfile && profile == origProfile)
                    continue;

                int priority;
                if (courtOffice)
                {
                    // Prefer a rival court or tavern in a nearby friendly town (different site; rival palace or tavern).
                    priority = !sameSite && !sameRegion && bType == PalaceBuildingType ? 0
                        : !sameSite && !sameRegion && bType == TavernBuildingType ? 1
                        : !sameSite && sameRegion && bType == TavernBuildingType ? 2
                        : !sameSite && sameRegion && bType == PalaceBuildingType ? 3
                        : 4;
                }
                else
                {
                    // Local deposed officers relocate to a tavern in a nearby town (same region first, else nearby town, else local tavern).
                    priority = !sameSite && sameRegion ? 0
                        : !sameSite ? 1
                        : 2;
                }

                candidates.Add((priority, distSq, site.Id.Region, site.Id.Index, building.Id.BlockY, building.Id.BlockX, building.Id.Index, profile));
            }
        }

        if (candidates.Count == 0)
            return null;

        int bestPriority = candidates.Min(c => c.Priority);
        List<DaggerfallWorldProfileKey> pool =
        [
            .. candidates
                .Where(c => c.Priority == bestPriority)
                .OrderBy(c => c.DistSq)
                .ThenBy(c => c.Region)
                .ThenBy(c => c.SiteIndex)
                .ThenBy(c => c.BlockY)
                .ThenBy(c => c.BlockX)
                .ThenBy(c => c.BuildingIndex)
                .Take(16)
                .Select(c => c.Profile),
        ];

        ulong hash = 14695981039346656037UL ^ socialSeed;
        hash = Mix64(hash ^ 0x4558494C45UL);
        foreach (char c in officeKey)
            hash = Mix64(hash ^ c);
        int picked = checked((int)(hash % (ulong)pool.Count));
        return pool[picked];
    }

    /// <summary>Resolves the deterministic exile destination for <paramref name="officeKey"/> using the current world social seed.</summary>
    internal DaggerfallWorldProfileKey? ResolveExileDestinationFor(
        string officeKey,
        DaggerfallOfficeKind kind,
        int? originRegion,
        DaggerfallSiteId? originSiteId,
        DaggerfallWorldProfileKey? originProfileKey) =>
        ResolveExileDestination(worldSocialSeed(), officeKey, kind, originRegion, originSiteId, originProfileKey);

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

        _exiles.Remove(deadActorId);
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
        new(
            [.. _overrides.Values.OrderBy(entry => entry.OfficeKey, StringComparer.Ordinal)],
            _exiles.Count == 0 ? null : [.. _exiles.Values.OrderBy(entry => entry.ActorId)]);

    internal void Restore(DaggerfallOfficeLedgerSave? saved)
    {
        _overrides.Clear();
        _exiles.Clear();
        if (saved is null)
            return;
        saved.Validate();
        foreach (DaggerfallOfficeOverrideSave entry in saved.Overrides)
            _overrides[entry.OfficeKey] = entry;
        if (saved.Exiles is not null)
        {
            foreach (DaggerfallOfficeExileSave exile in saved.Exiles)
                _exiles[exile.ActorId] = exile;
        }
    }

    /// <summary>
    /// Restores a living exiled former office holder back to their lost office through <see cref="OfficeChanged"/>,
    /// physically returning them from their refuge to their office slot, displacing any living usurper, and clearing
    /// their exile record. Returns null if the actor is not currently exiled or is dead.
    /// </summary>
    internal DaggerfallOfficeOverrideSave? RestoreExile(
        long exiledActorId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        long? currentMinute = null)
    {
        if (exiledActorId <= 0 || !_exiles.TryGetValue(exiledActorId, out DaggerfallOfficeExileSave? exile))
            return null;
        if (officeHolders.IsActorDead(exiledActorId))
        {
            _exiles.Remove(exiledActorId);
            return null;
        }

        string detail = !string.IsNullOrWhiteSpace(causeDetail) ? causeDetail : "was restored to office";
        DaggerfallWorldProfileKey? originProfile = exile.OriginProfile?.Require();
        if (exile.Kind is DaggerfallOfficeKind.Ruler or DaggerfallOfficeKind.MajorSeat && exile.OfficeFactionId is int factionId and > 0)
        {
            int? holderFactionId = exile.CastId is { Length: > 0 } cid
                && definitions.People.Individuals.TryGetValue(cid, out DaggerfallCastIndividualDefinition? cast)
                    ? cast.FactionId
                    : factionId;
            return SetFactionOfficeHolder(
                factionId,
                exile.Kind,
                holderActorId: exiledActorId,
                holderCastId: exile.CastId,
                holderFactionId: holderFactionId,
                holderDisplayName: exile.DisplayName,
                cause: cause,
                changedDay: changedDay,
                causeDetail: detail,
                title: exile.Title,
                currentMinute: currentMinute,
                newHolderHomeProfile: originProfile,
                newHolderHomeSlotId: exile.OriginSlotId);
        }

        if (exile.OfficeSiteId is { } siteId && FindSiteById(siteId) is { } site)
        {
            DaggerfallLocalOffice? local = ResolveLocalOffices(site)
                .FirstOrDefault(o => StringComparer.Ordinal.Equals(o.OfficeKey, exile.OfficeKey));
            if (local is not null)
            {
                return SetLocalOfficeHolder(
                    local,
                    holderActorId: exiledActorId,
                    holderDisplayName: exile.DisplayName,
                    cause: cause,
                    changedDay: changedDay,
                    causeDetail: detail,
                    holderFactionId: exile.OfficeFactionId,
                    vacant: false,
                    currentMinute: currentMinute,
                    newHolderHomeProfile: originProfile,
                    newHolderHomeSlotId: exile.OriginSlotId);
            }
        }

        return null;
    }

    /// <summary>
    /// Shared deposition caller for a regional ruler by classic region index (used by board conquest/cession in
    /// <c>#11132</c> and <c>#11143</c> as well as quests). Resolves the province faction and deposes its living ruler
    /// into exile through <see cref="OfficeChanged"/>. Returns null if the region has no province faction.
    /// </summary>
    internal DaggerfallOfficeOverrideSave? DeposeRegionalRuler(
        int region,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        long? currentMinute = null)
    {
        if (region is < 0 or > 61)
            return null;
        DaggerfallFactionDefinition? province = definitions.Factions.ProvinceOf(region);
        if (province is null)
            return null;
        return DeposeFactionOffice(
            province.Id,
            cause,
            changedDay,
            causeDetail ?? $"was deposed from {province.Name}",
            currentMinute: currentMinute);
    }

    /// <summary>
    /// Non-lethally deposes every institutional office (overridden office, board seat, regional ruler, or local office
    /// at <paramref name="activeSite"/>) currently held by <paramref name="actorId"/> through <see cref="OfficeChanged"/>.
    /// </summary>
    internal IReadOnlyList<DaggerfallOfficeOverrideSave> DeposeActorOffices(
        long actorId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        DaggerfallSiteRecord? activeSite = null,
        long? currentMinute = null)
    {
        if (actorId <= 0 || actorId == DaggerfallActorIdentity.PlayerEntityId)
            return [];

        List<DaggerfallOfficeOverrideSave> deposed = [];
        HashSet<string> handledKeys = new(StringComparer.Ordinal);

        // 1. Overridden offices currently held by this actor.
        foreach (DaggerfallOfficeOverrideSave existing in _overrides.Values.ToArray())
        {
            if (existing.Vacant || existing.HolderActorId != actorId || !handledKeys.Add(existing.OfficeKey))
                continue;
            if (existing.OfficeFactionId is int factionId and > 0
                && existing.Kind is DaggerfallOfficeKind.Ruler or DaggerfallOfficeKind.MajorSeat)
            {
                deposed.Add(DeposeFactionOffice(factionId, cause, changedDay, causeDetail, currentMinute));
            }
            else if (existing.OfficeSiteId is { } sid && FindSiteById(sid) is { } site)
            {
                DaggerfallLocalOffice? local = ResolveLocalOffices(site)
                    .FirstOrDefault(o => StringComparer.Ordinal.Equals(o.OfficeKey, existing.OfficeKey));
                if (local is not null)
                    deposed.Add(DeposeLocalOffice(local, cause, changedDay, causeDetail, currentMinute));
            }
        }

        // 2. Baseline canonical board seats and province rulers.
        HashSet<int> checkedFactions = [];
        foreach (DaggerfallBoardSeatBinding seat in definitions.BoardSeats.All)
        {
            string key = FactionOfficeKey(seat.OfficeFactionId);
            if (!checkedFactions.Add(seat.OfficeFactionId) || handledKeys.Contains(key) || _overrides.ContainsKey(key))
                continue;
            if (MatchesHolderActor(seat.OfficeFactionId, seat.HomeRegion, actorId, out _, out _))
            {
                handledKeys.Add(key);
                deposed.Add(DeposeFactionOffice(seat.OfficeFactionId, cause, changedDay, causeDetail, currentMinute));
            }
        }

        foreach (DaggerfallFactionDefinition province in definitions.Factions.Factions.Values
            .Where(f => f.Type == ProvinceFactionType)
            .OrderBy(f => f.Id))
        {
            string key = FactionOfficeKey(province.Id);
            if (!checkedFactions.Add(province.Id) || handledKeys.Contains(key) || _overrides.ContainsKey(key))
                continue;
            if (MatchesHolderActor(province.Id, province.Region >= 0 ? province.Region : null, actorId, out _, out _))
            {
                handledKeys.Add(key);
                deposed.Add(DeposeFactionOffice(province.Id, cause, changedDay, causeDetail, currentMinute));
            }
        }

        // 3. Local offices at the active site (or the actor's known site).
        DaggerfallSiteRecord? localSite = activeSite;
        if (localSite is null && npcs.TryGet(actorId, out DaggerfallNpc? npc) && npc?.Profile is { } npcProfile)
            localSite = FindSiteById(npcProfile.Site);
        if (localSite is not null)
        {
            foreach (DaggerfallLocalOffice local in ResolveLocalOffices(localSite))
            {
                if (handledKeys.Contains(local.OfficeKey) || local.IsVacant || local.HolderActorId != actorId)
                    continue;
                handledKeys.Add(local.OfficeKey);
                deposed.Add(DeposeLocalOffice(local, cause, changedDay, causeDetail, currentMinute));
            }
        }

        return deposed;
    }

    /// <summary>
    /// Deposes an office identified by a holder's actor ID and/or faction ID (used by quest outcome actions).
    /// Tries the actor's held offices first, then falls back to any board seat or province office owned by <paramref name="factionId"/>.
    /// </summary>
    internal IReadOnlyList<DaggerfallOfficeOverrideSave> DeposeActorOrFactionOffice(
        long? actorId,
        int? factionId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        DaggerfallSiteRecord? activeSite = null,
        long? currentMinute = null)
    {
        if (actorId is long aid && aid > 0)
        {
            IReadOnlyList<DaggerfallOfficeOverrideSave> byActor = DeposeActorOffices(aid, cause, changedDay, causeDetail, activeSite, currentMinute);
            if (byActor.Count > 0)
                return byActor;
        }
        if (factionId is int fid && fid > 0 && TryResolveOfficeFactionId(fid) is int officeFaction)
            return [DeposeFactionOffice(officeFaction, cause, changedDay, causeDetail, currentMinute)];
        return [];
    }

    /// <summary>
    /// Places a faction office under interim regency, deterministically selecting a living court/dynasty/deputy regent
    /// when none is explicitly supplied. Publishes <see cref="DaggerfallOfficeChangeKind.RegencyDeclared"/>.
    /// </summary>
    internal DaggerfallOfficeOverrideSave DeclareRegencyForFaction(
        int officeFactionId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        long? regentActorId = null,
        string? regentCastId = null,
        int? regentFactionId = null,
        string? regentDisplayName = null,
        string? causeDetail = null,
        long? currentMinute = null,
        long? vacancyUntilMinute = null,
        bool exileIncumbent = false)
    {
        if (officeFactionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(officeFactionId), "Faction office id must be positive.");

        DaggerfallWorldProfileKey? regentHomeProfile = null;
        string? regentHomeSlotId = null;
        DaggerfallOfficeActorBlueprint? regentBlueprint = null;
        if (regentActorId is null && string.IsNullOrWhiteSpace(regentCastId) && string.IsNullOrWhiteSpace(regentDisplayName))
        {
            string key = FactionOfficeKey(officeFactionId);
            definitions.BoardSeats.TryGetByFactionId(officeFactionId, out DaggerfallBoardSeatBinding? seat);
            DaggerfallOfficeKind kind = seat is not null ? ToOfficeKind(seat.Kind) : DaggerfallOfficeKind.Ruler;
            int? officeRegion = ResolveFactionOfficeRegion(officeFactionId, seat);
            OutgoingHolderSnapshot outgoing = SnapshotOutgoingFactionHolder(officeFactionId, key, seat, officeRegion, null, null);
            long nowMinute = ResolveMinute(changedDay, currentMinute);
            DaggerfallOfficeOverrideSave probe = new(
                OfficeKey: key, Kind: kind, OfficeFactionId: officeFactionId, SeatId: seat?.SeatId,
                HolderActorId: null, HolderCastId: null, HolderFactionId: null, HolderDisplayName: null,
                Title: seat?.DefaultTitle ?? ResolveDefaultFactionTitle(officeFactionId),
                PreviousHolderActorId: outgoing.ActorId, PreviousHolderName: outgoing.DisplayName,
                Cause: cause, CauseDetail: causeDetail, ChangedDay: changedDay, Vacant: true,
                VacancyOpenedMinute: nowMinute, VacancyUntilMinute: nowMinute, IsRegency: false,
                OfficeRegion: officeRegion, OfficeSiteIndex: outgoing.Home?.Site.Id.Index,
                PreviousHolderCastId: outgoing.CastId, PreviousHolderFamilyId: outgoing.FamilyId);
            if (TrySelectFactionSuccessor(probe, officeFactionId) is { } picked)
            {
                regentActorId = picked.ActorId;
                regentCastId = picked.CastId;
                regentFactionId = picked.HolderFactionId;
                regentDisplayName = picked.DisplayName;
                regentHomeProfile = picked.HomeProfile;
                regentHomeSlotId = picked.HomeSlotId;
                regentBlueprint = picked.Blueprint;
            }
            else
            {
                string realm = officeRegion is int r ? definitions.BuildingNames.RegionName(r)
                    : definitions.Factions.Factions.TryGetValue(officeFactionId, out DaggerfallFactionDefinition? f) ? f.Name : "the Realm";
                regentDisplayName = $"Council of {realm}";
                regentFactionId = officeFactionId;
            }
        }

        return DeclareFactionRegency(
            officeFactionId,
            regentActorId,
            regentCastId,
            regentFactionId,
            regentDisplayName,
            cause,
            changedDay,
            causeDetail: causeDetail ?? "holds interim regency",
            title: "Regent",
            currentMinute: currentMinute,
            vacancyUntilMinute: vacancyUntilMinute,
            exileIncumbent: exileIncumbent,
            regentHomeProfile: regentHomeProfile,
            regentHomeSlotId: regentHomeSlotId,
            regentBlueprint: regentBlueprint);
    }

    /// <summary>Places a board-game seat under interim regency by its <paramref name="seatId"/>.</summary>
    internal DaggerfallOfficeOverrideSave DeclareRegencyForBoardSeat(
        string seatId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        long? currentMinute = null,
        long? vacancyUntilMinute = null,
        bool exileIncumbent = false)
    {
        if (!definitions.BoardSeats.TryGetBySeatId(seatId, out DaggerfallBoardSeatBinding? seat) || seat is null)
            throw new ArgumentException($"Unknown board-game seat '{seatId}'.", nameof(seatId));
        return DeclareRegencyForFaction(
            seat.OfficeFactionId,
            cause,
            changedDay,
            causeDetail: causeDetail,
            currentMinute: currentMinute,
            vacancyUntilMinute: vacancyUntilMinute,
            exileIncumbent: exileIncumbent);
    }

    /// <summary>Places an actor's or faction's office under regency (used by quest outcome actions).</summary>
    internal DaggerfallOfficeOverrideSave? DeclareRegencyForActorOrFaction(
        long? actorId,
        int? factionId,
        long? regentActorId,
        int? regentFactionId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        long? currentMinute = null,
        bool exileIncumbent = false)
    {
        int? officeFactionId = ResolveOfficeFactionForActor(actorId) ?? (factionId is int fid ? TryResolveOfficeFactionId(fid) : null);
        if (officeFactionId is not int resolved || resolved <= 0)
            return null;

        string? regentCastId = null;
        string? regentName = null;
        if (regentActorId is long rid && rid > 0)
        {
            regentName = ResolveActorDisplayName(rid, null, "Regent");
            foreach (DaggerfallCastIndividualDefinition c in definitions.People.Individuals.Values)
            {
                if (DaggerfallPeopleSlots.CastIndividualIdentity(c.Id) == rid)
                {
                    regentCastId = c.Id;
                    regentName = c.Name;
                    regentFactionId ??= c.FactionId;
                    break;
                }
            }
        }
        else if (regentFactionId is int rfid && rfid > 0
            && definitions.People.TryGetIndividualByFaction(rfid, out DaggerfallCastIndividualDefinition? regentCast)
            && regentCast is not null)
        {
            regentCastId = regentCast.Id;
            regentActorId = DaggerfallPeopleSlots.CastIndividualIdentity(regentCast.Id);
            regentName = regentCast.Name;
        }

        return DeclareRegencyForFaction(
            resolved,
            cause,
            changedDay,
            regentActorId: regentActorId,
            regentCastId: regentCastId,
            regentFactionId: regentFactionId,
            regentDisplayName: regentName,
            causeDetail: causeDetail,
            currentMinute: currentMinute,
            exileIncumbent: exileIncumbent);
    }

    /// <summary>
    /// Reacts when an office holder is convicted of a crime or banished from a province by the law: deposes every
    /// office held by <paramref name="actorId"/> with <see cref="DaggerfallOfficeChangeCause.Law"/>, exiling them
    /// to their refuge and firing <see cref="OfficeChanged"/>.
    /// </summary>
    internal IReadOnlyList<DaggerfallOfficeOverrideSave> ReactToHolderCrimeOrBanishment(
        long actorId,
        int region,
        Crime.DaggerfallCrimeKind? crimeKind,
        bool banished,
        int currentDay,
        string? detail = null,
        DaggerfallSiteRecord? activeSite = null,
        long? currentMinute = null)
    {
        if (actorId <= 0 || actorId == DaggerfallActorIdentity.PlayerEntityId)
            return [];

        string regionName = region is >= 0 and <= 61 ? definitions.BuildingNames.RegionName(region) : "the province";
        string causeDetail = !string.IsNullOrWhiteSpace(detail)
            ? detail
            : banished
                ? (crimeKind is { } ck
                    ? $"was banished from {regionName} for {Crime.DaggerfallLaw.CrimeLabel(ck).ToLowerInvariant()}"
                    : $"was banished from {regionName}")
                : (crimeKind is { } convicted
                    ? $"was convicted of {Crime.DaggerfallLaw.CrimeLabel(convicted).ToLowerInvariant()}"
                    : "was removed by judgment of the court");

        return DeposeActorOffices(
            actorId,
            DaggerfallOfficeChangeCause.Law,
            currentDay,
            causeDetail,
            activeSite,
            currentMinute);
    }

    private void RecordExile(
        long actorId,
        string officeKey,
        DaggerfallOfficeKind kind,
        string title,
        DaggerfallWorldProfileKey refugeProfile,
        DaggerfallOfficeChangeCause cause,
        int exiledDay,
        string? castId,
        int? officeFactionId,
        string? seatId,
        int? officeRegion,
        int? officeSiteIndex,
        string? displayName,
        string? causeDetail,
        DaggerfallWorldProfileKey? originProfile,
        string? originSlotId)
    {
        if (actorId <= 0 || actorId == DaggerfallActorIdentity.PlayerEntityId)
            return;
        DaggerfallOfficeExileSave record = new(
            ActorId: actorId,
            OfficeKey: officeKey,
            Kind: kind,
            Title: !string.IsNullOrWhiteSpace(title) ? title : "Officer",
            RefugeProfile: DaggerfallWorldProfileKeySave.Capture(refugeProfile),
            Cause: cause,
            ExiledDay: exiledDay,
            CastId: string.IsNullOrWhiteSpace(castId) ? null : castId,
            OfficeFactionId: officeFactionId is > 0 ? officeFactionId : null,
            SeatId: string.IsNullOrWhiteSpace(seatId) ? null : seatId,
            OfficeRegion: officeRegion,
            OfficeSiteIndex: officeSiteIndex,
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            CauseDetail: string.IsNullOrWhiteSpace(causeDetail) ? null : causeDetail,
            OriginProfile: originProfile is { } op ? DaggerfallWorldProfileKeySave.Capture(op) : null,
            OriginSlotId: string.IsNullOrWhiteSpace(originSlotId) ? null : originSlotId);
        record.Validate();
        _exiles[actorId] = record;
    }

    private int? ResolveOfficeFactionForActor(long? actorId)
    {
        if (actorId is not long id || id <= 0)
            return null;
        foreach (DaggerfallOfficeOverrideSave over in _overrides.Values)
        {
            if (!over.Vacant && over.HolderActorId == id && over.OfficeFactionId is int ofid and > 0
                && over.Kind is DaggerfallOfficeKind.Ruler or DaggerfallOfficeKind.MajorSeat)
                return ofid;
        }
        foreach (DaggerfallBoardSeatBinding seat in definitions.BoardSeats.All)
        {
            if (MatchesHolderActor(seat.OfficeFactionId, seat.HomeRegion, id, out _, out _))
                return seat.OfficeFactionId;
        }
        foreach (DaggerfallFactionDefinition province in definitions.Factions.Factions.Values
            .Where(f => f.Type == ProvinceFactionType)
            .OrderBy(f => f.Id))
        {
            if (MatchesHolderActor(province.Id, province.Region >= 0 ? province.Region : null, id, out _, out _))
                return province.Id;
        }
        return null;
    }

    private int? TryResolveOfficeFactionId(int factionId)
    {
        if (factionId <= 0)
            return null;
        if (definitions.BoardSeats.TryGetByFactionId(factionId, out _))
            return factionId;
        int current = factionId;
        for (int depth = 0; depth < 6 && current > 0; depth++)
        {
            if (!definitions.Factions.Factions.TryGetValue(current, out DaggerfallFactionDefinition? def))
                break;
            if (definitions.BoardSeats.TryGetByFactionId(def.Id, out _) || def.Type == ProvinceFactionType)
                return def.Id;
            if (def.Parent == current)
                break;
            current = def.Parent;
        }
        return null;
    }

    private readonly record struct OutgoingHolderSnapshot(
        long? ActorId,
        string? CastId,
        string? DisplayName,
        string? FamilyId,
        bool IsAlive,
        bool WasActiveIncumbent,
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
        bool hasPrior = _overrides.TryGetValue(officeKey, out DaggerfallOfficeOverrideSave? prior);
        bool wasActiveIncumbent = false;

        if (hasPrior)
        {
            if (!prior!.Vacant && !prior.IsRegency)
            {
                wasActiveIncumbent = true;
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

        if (!hasPrior || actorId is null || name is null || castId is null)
        {
            DaggerfallOfficeHolder? current = officeHolders.ResolveOffice(officeFactionId, officeRegion ?? seat?.HomeRegion);
            if (current is { HasLivingHolder: true })
            {
                if (!hasPrior)
                    wasActiveIncumbent = true;
                actorId ??= current.ActorId;
                castId ??= current.CastId;
                name ??= current.DisplayName;
                familyId ??= current.FamilyId;
                home ??= current.Home;
            }
            else if (!hasPrior && explicitActorId is not null)
            {
                wasActiveIncumbent = true;
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
        return new OutgoingHolderSnapshot(actorId, castId, name, familyId, isAlive, wasActiveIncumbent, home);
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
        OutgoingHolderSnapshot outgoing = new(deadActorId, resolvedCastId, slainName, ResolveActorFamilyId(deadActorId, resolvedCastId, null), false, true, home);
        (DaggerfallWorldProfileKey? officeProfileKey, string? officeSlotId, DaggerfallSiteId? officeSiteId) =
            ResolveCanonicalFactionOfficeAnchor(officeFactionId, key, officeRegion, outgoing, null);
        string? slainFamilyId = outgoing.FamilyId;
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
            PreviousHolderFamilyId: slainFamilyId,
            OfficeProfile: officeProfileKey is { } opk ? DaggerfallWorldProfileKeySave.Capture(opk) : null,
            OfficeSlotId: officeSlotId);
        vacated.Validate();
        _overrides[key] = vacated;

        PublishChange(new DaggerfallOfficeChangedNotification(
            OfficeKey: key,
            Kind: kind,
            ChangeKind: DaggerfallOfficeChangeKind.VacancyOpened,
            Cause: DaggerfallOfficeChangeCause.Death,
            Region: vacated.OfficeRegion,
            OfficeSiteId: vacated.OfficeSiteId,
            OfficeProfileKey: officeProfileKey,
            OfficeSlotId: officeSlotId,
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

    private sealed record SuccessorCandidate(
        long ActorId,
        string? CastId,
        int? HolderFactionId,
        string DisplayName,
        string? Title,
        string CauseDetail,
        DaggerfallWorldProfileKey? HomeProfile,
        string? HomeSlotId,
        DaggerfallOfficeActorBlueprint? Blueprint,
        DaggerfallLocalOffice? VacatedLocalOffice = null);

    private DaggerfallOfficeOverrideSave? TrySeatDeterministicSuccessor(
        DaggerfallOfficeOverrideSave vacancy,
        int currentDay,
        long currentMinute)
    {
        if (vacancy.Kind is DaggerfallOfficeKind.Ruler or DaggerfallOfficeKind.MajorSeat)
        {
            if (vacancy.OfficeFactionId is not int officeFactionId || officeFactionId <= 0)
                return null;
            if (TrySelectFactionSuccessor(vacancy, officeFactionId) is not { } picked)
                return null;
            if (picked.VacatedLocalOffice is { } priorLocal)
            {
                SetLocalOfficeHolder(
                    priorLocal,
                    holderActorId: null,
                    holderDisplayName: null,
                    cause: DaggerfallOfficeChangeCause.Succession,
                    changedDay: currentDay,
                    causeDetail: "elevated to higher office",
                    vacant: true,
                    currentMinute: currentMinute,
                    suppressExile: true);
            }
            return SetFactionOfficeHolder(
                officeFactionId,
                vacancy.Kind,
                holderActorId: picked.ActorId,
                holderCastId: picked.CastId,
                holderFactionId: picked.HolderFactionId,
                holderDisplayName: picked.DisplayName,
                cause: DaggerfallOfficeChangeCause.Succession,
                changedDay: currentDay,
                causeDetail: picked.CauseDetail,
                title: picked.Title ?? vacancy.Title,
                previousHolderName: vacancy.PreviousHolderName,
                previousHolderActorId: vacancy.PreviousHolderActorId,
                currentMinute: currentMinute,
                newHolderHomeProfile: picked.HomeProfile,
                newHolderHomeSlotId: picked.HomeSlotId,
                newHolderBlueprint: picked.Blueprint);
        }

        return TrySeatLocalSuccessor(vacancy, currentDay, currentMinute);
    }

    private SuccessorCandidate? TrySelectFactionSuccessor(DaggerfallOfficeOverrideSave vacancy, int officeFactionId)
    {
        definitions.BoardSeats.TryGetByFactionId(officeFactionId, out DaggerfallBoardSeatBinding? seat);
        definitions.Factions.Factions.TryGetValue(officeFactionId, out DaggerfallFactionDefinition? faction);
        int? officeRegion = vacancy.OfficeRegion ?? ResolveFactionOfficeRegion(officeFactionId, seat);
        HashSet<long> occupiedTier1 = CollectActiveTier1HoldersExcluding(vacancy.OfficeKey);

        bool IsEligibleCast(DaggerfallCastIndividualDefinition cast, out long actorId)
        {
            actorId = DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
            return actorId != vacancy.PreviousHolderActorId
                && !officeHolders.IsActorDead(actorId)
                && !occupiedTier1.Contains(actorId);
        }

        // Priority 1: Dynastic succession — eligible living cast kin matching #11084 FamilyId.
        string? dynastyFamilyId = ResolveSeatDynastyFamilyId(vacancy, officeFactionId, officeRegion);
        if (!string.IsNullOrWhiteSpace(dynastyFamilyId))
        {
            Dictionary<int, int> childOrder = [];
            if (faction is not null)
            {
                for (int i = 0; i < faction.Children.Count; i++)
                    childOrder.TryAdd(faction.Children[i], i);
            }

            DaggerfallCastIndividualDefinition? dynasticHeir = definitions.People.Individuals.Values
                .Where(c => string.Equals(c.FamilyId, dynastyFamilyId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => childOrder.TryGetValue(c.FactionId, out int idx) ? idx : int.MaxValue)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .FirstOrDefault(c => IsEligibleCast(c, out _));
            if (dynasticHeir is not null)
            {
                long heirId = DaggerfallPeopleSlots.CastIndividualIdentity(dynasticHeir.Id);
                DaggerfallCastHomeLocation? heirHome = ResolveCastHome(dynasticHeir.Id, officeRegion);
                return new SuccessorCandidate(
                    ActorId: heirId,
                    CastId: dynasticHeir.Id,
                    HolderFactionId: dynasticHeir.FactionId,
                    DisplayName: ResolveActorDisplayName(heirId, dynasticHeir.Name, dynasticHeir.Name),
                    Title: ResolveGenderedRulerTitle(vacancy.Kind, dynasticHeir.Gender, seat?.DefaultTitle ?? vacancy.Title),
                    CauseDetail: "succeeded by dynastic right",
                    HomeProfile: DaggerfallCastHomes.ResolveProfileKey(heirHome),
                    HomeSlotId: null,
                    Blueprint: BuildActorBlueprint(heirId, dynasticHeir.Id, dynasticHeir.Name, dynasticHeir.FamilyId));
            }
        }

        // Priority 2: Other eligible living published cast children in the office faction's succession list.
        if (faction is not null)
        {
            List<int> factionCandidates = [];
            if (faction.Type == 4) factionCandidates.Add(faction.Id);
            factionCandidates.AddRange(faction.Children);
            if (factionCandidates.Count == 0 && faction.Parent > 0) factionCandidates.Add(faction.Parent);

            foreach (int candidateFactionId in factionCandidates)
            {
                if (definitions.People.TryGetIndividualByFaction(candidateFactionId, out DaggerfallCastIndividualDefinition? cast)
                    && cast is not null && IsEligibleCast(cast, out long castId))
                {
                    DaggerfallCastHomeLocation? castHome = ResolveCastHome(cast.Id, officeRegion);
                    return new SuccessorCandidate(
                        ActorId: castId,
                        CastId: cast.Id,
                        HolderFactionId: cast.FactionId,
                        DisplayName: ResolveActorDisplayName(castId, cast.Name, cast.Name),
                        Title: ResolveGenderedRulerTitle(vacancy.Kind, cast.Gender, seat?.DefaultTitle ?? vacancy.Title),
                        CauseDetail: "acclaimed by the court",
                        HomeProfile: DaggerfallCastHomes.ResolveProfileKey(castHome),
                        HomeSlotId: null,
                        Blueprint: BuildActorBlueprint(castId, cast.Id, cast.Name, cast.FamilyId));
                }
            }
        }

        // Priority 3 (Ruler): Tier 2 provincial Great House court factor in the same province.
        if (vacancy.Kind == DaggerfallOfficeKind.Ruler && officeRegion is int rulerRegion)
        {
            List<DaggerfallLocalOffice> greatHouseHolders =
            [
                .. ResolveRegionalGreatHouses(rulerRegion)
                    .SelectMany(h => h.HeldOffices)
                    .Where(o => !o.IsVacant && o.IsAlive && o.HolderActorId is long hid
                        && hid != vacancy.PreviousHolderActorId
                        && !officeHolders.IsActorDead(hid)
                        && !occupiedTier1.Contains(hid))
                    .OrderBy(o => o.FamilyId, StringComparer.Ordinal)
                    .ThenBy(o => o.OfficeKey, StringComparer.Ordinal),
            ];
            if (greatHouseHolders.Count > 0)
            {
                int idx = DeterministicIndex(worldSocialSeed(), vacancy.OfficeKey, vacancy.VacancyOpenedMinute ?? 0L, greatHouseHolders.Count);
                DaggerfallLocalOffice chosen = greatHouseHolders[idx];
                DaggerfallOfficeActorBlueprint? bp = BuildLocalOfficeHolderBlueprint(chosen, chosen.HolderActorId, chosen.DisplayName, chosen.FamilyId);
                return new SuccessorCandidate(
                    ActorId: chosen.HolderActorId!.Value,
                    CastId: null,
                    HolderFactionId: officeFactionId,
                    DisplayName: chosen.DisplayName,
                    Title: ResolveGenderedRulerTitle(vacancy.Kind, bp?.Gender, seat?.DefaultTitle ?? vacancy.Title),
                    CauseDetail: "elevated from the provincial Great Houses",
                    HomeProfile: chosen.ProfileKey,
                    HomeSlotId: chosen.SlotId,
                    Blueprint: bp,
                    VacatedLocalOffice: chosen);
            }
        }

        // Priority 4: Promoted town-* SeededSlot deputy matching the office archetype.
        return TrySelectSeededDeputyForOffice(
            vacancy.OfficeKey,
            vacancy.Kind,
            officeFactionId,
            officeRegion,
            vacancy.OfficeSiteId,
            vacancy.PreviousHolderActorId,
            vacancy.VacancyOpenedMinute ?? 0L,
            occupiedTier1,
            seat?.DefaultTitle ?? vacancy.Title);
    }

    private DaggerfallOfficeOverrideSave? TrySeatLocalSuccessor(
        DaggerfallOfficeOverrideSave vacancy,
        int currentDay,
        long currentMinute)
    {
        DaggerfallSiteRecord? site = vacancy.OfficeSiteId is { } sid ? FindSiteById(sid) : null;
        if (site is null)
            return null;

        DaggerfallLocalOffice? local = ResolveLocalOffices(site)
            .FirstOrDefault(o => StringComparer.Ordinal.Equals(o.OfficeKey, vacancy.OfficeKey));
        if (local is null)
            return null;

        HashSet<long> activeHolders = CollectOfficeHolderIds(site);
        SuccessorCandidate? picked = local.Kind == DaggerfallOfficeKind.WatchCaptain
            ? TrySelectWatchCaptainDeputy(site, local, vacancy, activeHolders)
            : TrySelectBuildingOfficeSuccessor(site, local, vacancy, activeHolders);
        if (picked is null)
            return null;

        return SetLocalOfficeHolder(
            local,
            holderActorId: picked.ActorId,
            holderDisplayName: picked.DisplayName,
            cause: DaggerfallOfficeChangeCause.Succession,
            changedDay: currentDay,
            causeDetail: picked.CauseDetail,
            holderFactionId: picked.HolderFactionId ?? (local.OfficeFactionId > 0 ? local.OfficeFactionId : null),
            vacant: false,
            currentMinute: currentMinute,
            newHolderHomeProfile: picked.HomeProfile,
            newHolderHomeSlotId: picked.HomeSlotId,
            newHolderBlueprint: picked.Blueprint);
    }

    private SuccessorCandidate? TrySelectBuildingOfficeSuccessor(
        DaggerfallSiteRecord site,
        DaggerfallLocalOffice local,
        DaggerfallOfficeOverrideSave vacancy,
        HashSet<long> activeHolders)
    {
        if (local.BuildingId is { } bId && site.Exterior is not null
            && site.Exterior.Buildings.TryGetValue(bId, out DaggerfallSiteBuildingSource? building))
        {
            List<BuildingSlotCandidate> sameBuilding = EnumerateBuildingCandidateDetails(site, building);
            // Tier 2 CourtFactor: prefer living kin in the same provincial Great House first!
            if (local.Kind == DaggerfallOfficeKind.CourtFactor && !string.IsNullOrWhiteSpace(vacancy.PreviousHolderFamilyId))
            {
                BuildingSlotCandidate? houseKin = sameBuilding.FirstOrDefault(c =>
                    c.ActorId != vacancy.PreviousHolderActorId
                    && !officeHolders.IsActorDead(c.ActorId)
                    && !activeHolders.Contains(c.ActorId)
                    && string.Equals(c.FamilyId, vacancy.PreviousHolderFamilyId, StringComparison.OrdinalIgnoreCase));
                if (houseKin is not null)
                {
                    return new SuccessorCandidate(
                        houseKin.ActorId, null, local.OfficeFactionId > 0 ? local.OfficeFactionId : null,
                        houseKin.DisplayName, local.Title, "succeeded within the house",
                        local.ProfileKey, houseKin.SlotId, houseKin.Blueprint);
                }
            }

            // Next living deputy inside the same building (preferred spawner priority order).
            BuildingSlotCandidate? inBuildingDeputy = sameBuilding.FirstOrDefault(c =>
                c.ActorId != vacancy.PreviousHolderActorId
                && !officeHolders.IsActorDead(c.ActorId)
                && !activeHolders.Contains(c.ActorId));
            if (inBuildingDeputy is not null)
            {
                return new SuccessorCandidate(
                    inBuildingDeputy.ActorId, null, local.OfficeFactionId > 0 ? local.OfficeFactionId : null,
                    inBuildingDeputy.DisplayName, local.Title, "promoted from within the hall",
                    local.ProfileKey, inBuildingDeputy.SlotId, inBuildingDeputy.Blueprint);
            }
        }

        // Cross-building/regional fallback deputy when every member of the original hall is dead/unavailable.
        return TrySelectSeededDeputyForOffice(
            vacancy.OfficeKey,
            local.Kind,
            local.OfficeFactionId,
            site.Id.Region,
            site.Id,
            vacancy.PreviousHolderActorId,
            vacancy.VacancyOpenedMinute ?? 0L,
            activeHolders,
            local.Title,
            preferredFamilyId: local.Kind == DaggerfallOfficeKind.CourtFactor ? vacancy.PreviousHolderFamilyId : null);
    }

    private SuccessorCandidate? TrySelectWatchCaptainDeputy(
        DaggerfallSiteRecord site,
        DaggerfallLocalOffice local,
        DaggerfallOfficeOverrideSave vacancy,
        HashSet<long> activeHolders)
    {
        int slotCount = Math.Max(24, DaggerfallExteriorPopulation.EstimatedSlotCount(site));
        DaggerfallWorldProfileKey exteriorKey = DaggerfallWorldProfileIds.Exterior(site.Id);
        int regionalPeopleFaction = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, site.Id.Region)?.Id ?? 0;

        // Prefer secondary city-watch guard slots (32, 64, ...) before promoting a town militia deputy (1..23).
        IEnumerable<int> indices = Enumerable.Range(1, slotCount - 1)
            .OrderBy(i => i % 32 == 0 ? 0 : 1)
            .ThenBy(i => i);

        foreach (int index in indices)
        {
            string slotId = DaggerfallExteriorPopulation.ExteriorSlotId(site, index);
            long candidateId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{exteriorKey.LogicalId}/{slotId}");
            if (candidateId == vacancy.PreviousHolderActorId
                || officeHolders.IsActorDead(candidateId)
                || activeHolders.Contains(candidateId))
                continue;

            DaggerfallOfficeActorBlueprint bp = BuildExteriorSlotBlueprint(site, exteriorKey, slotId, index, regionalPeopleFaction);
            string displayName = ResolveActorDisplayName(candidateId, bp.DisplayName, local.Title);
            return new SuccessorCandidate(
                ActorId: candidateId,
                CastId: null,
                HolderFactionId: regionalPeopleFaction > 0 ? regionalPeopleFaction : null,
                DisplayName: displayName,
                Title: local.Title,
                CauseDetail: index % 32 == 0 ? "promoted from the city watch" : "commissioned to lead the town watch",
                HomeProfile: exteriorKey,
                HomeSlotId: slotId,
                Blueprint: bp with { DisplayName = displayName });
        }

        return null;
    }

    private SuccessorCandidate? TrySelectSeededDeputyForOffice(
        string officeKey,
        DaggerfallOfficeKind kind,
        int officeFactionId,
        int? preferredRegion,
        DaggerfallSiteId? preferredSiteId,
        long? excludedActorId,
        long openedMinute,
        HashSet<long> occupiedHolders,
        string? defaultTitle,
        string? preferredFamilyId = null)
    {
        HashSet<string> preferredSpawners = ExpectedDeputySpawnersFor(kind, officeFactionId);
        IEnumerable<DaggerfallSiteRecord> orderedSites = definitions.Locations.Records
            .Where(r => r.Exterior is { Buildings.Count: > 0 })
            .OrderBy(r => preferredSiteId is { } ps && r.Id == ps ? 0 : 1)
            .ThenBy(r => preferredRegion is int pr && r.Id.Region == pr ? 0 : 1)
            .ThenByDescending(r => r.Exterior!.Buildings.Count)
            .ThenBy(r => r.Id.Region)
            .ThenBy(r => r.Id.Index)
            .Take(24);

        List<(int Score, DaggerfallWorldProfileKey Profile, BuildingSlotCandidate Candidate)> matches = [];
        foreach (DaggerfallSiteRecord site in orderedSites)
        {
            foreach (DaggerfallSiteBuildingSource building in site.Exterior!.Buildings.Values
                .OrderBy(b => b.Id.BlockY).ThenBy(b => b.Id.BlockX).ThenBy(b => b.Id.Index))
            {
                DaggerfallWorldProfileKey profile = DaggerfallWorldProfileIds.Interior(site.Id, building.Id);
                foreach (BuildingSlotCandidate c in EnumerateBuildingCandidateDetails(site, building))
                {
                    if (c.ActorId == excludedActorId || officeHolders.IsActorDead(c.ActorId) || occupiedHolders.Contains(c.ActorId))
                        continue;

                    int score = 0;
                    if (!string.IsNullOrWhiteSpace(preferredFamilyId)
                        && string.Equals(c.FamilyId, preferredFamilyId, StringComparison.OrdinalIgnoreCase))
                        score += 100;
                    if (!string.IsNullOrWhiteSpace(c.SpawnerId) && preferredSpawners.Contains(c.SpawnerId))
                        score += 50;
                    if (officeFactionId > 0 && building.Source.FactionId == officeFactionId)
                        score += 25;
                    if (score == 0)
                        continue;

                    matches.Add((score, profile, c));
                }
            }
            if (matches.Count >= 8)
                break;
        }

        if (matches.Count == 0)
            return null;

        int topScore = matches.Max(m => m.Score);
        List<(DaggerfallWorldProfileKey Profile, BuildingSlotCandidate Candidate)> top =
        [
            .. matches
                .Where(m => m.Score == topScore)
                .OrderBy(m => m.Profile.LogicalId, StringComparer.Ordinal)
                .ThenBy(m => m.Candidate.SlotId, StringComparer.Ordinal)
                .Select(m => (m.Profile, m.Candidate)),
        ];
        int pickedIndex = DeterministicIndex(worldSocialSeed(), officeKey, openedMinute, top.Count);
        (DaggerfallWorldProfileKey homeProfile, BuildingSlotCandidate chosen) = top[pickedIndex];
        return new SuccessorCandidate(
            ActorId: chosen.ActorId,
            CastId: null,
            HolderFactionId: officeFactionId > 0 ? officeFactionId : null,
            DisplayName: chosen.DisplayName,
            Title: ResolveGenderedRulerTitle(kind, chosen.Blueprint.Gender, defaultTitle),
            CauseDetail: "promoted from the local ranks",
            HomeProfile: homeProfile,
            HomeSlotId: chosen.SlotId,
            Blueprint: chosen.Blueprint);
    }

    private static HashSet<string> ExpectedDeputySpawnersFor(DaggerfallOfficeKind kind, int officeFactionId)
    {
        if (kind is DaggerfallOfficeKind.Ruler or DaggerfallOfficeKind.CourtFactor)
            return new(["town-noble", "town-courtier", "town-palace-attendant"], StringComparer.Ordinal);
        if (kind == DaggerfallOfficeKind.TempleHead || officeFactionId == 450)
            return new(["town-priest", "town-temple-priest"], StringComparer.Ordinal);
        if (kind == DaggerfallOfficeKind.WatchCaptain)
            return new(["town-city-watch"], StringComparer.Ordinal);
        if (officeFactionId == DaggerfallConcreteGuildCatalog.MagesFactionId)
            return new(["town-mage-guild-member", "town-guild-member"], StringComparer.Ordinal);
        if (officeFactionId == DaggerfallConcreteGuildCatalog.FightersFactionId)
            return new(["town-fighters-guild-member", "town-knightly-order-member", "town-guild-member"], StringComparer.Ordinal);
        if (officeFactionId == DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId)
            return new(["town-dark-brotherhood-member", "town-guild-member"], StringComparer.Ordinal);
        return new(["town-guild-member", "town-mage-guild-member", "town-fighters-guild-member", "town-noble", "town-courtier"], StringComparer.Ordinal);
    }

    private static string? ResolveGenderedRulerTitle(DaggerfallOfficeKind kind, string? gender, string? defaultTitle)
    {
        if (kind != DaggerfallOfficeKind.Ruler)
            return defaultTitle;
        bool female = string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase);
        return defaultTitle switch
        {
            "King" or "Queen" => female ? "Queen" : "King",
            "Duke" or "Duchess" => female ? "Duchess" : "Duke",
            "Lord" or "Lady" => female ? "Lady" : "Lord",
            "Count" or "Countess" => female ? "Countess" : "Count",
            "Baron" or "Baroness" => female ? "Baroness" : "Baron",
            _ => defaultTitle,
        };
    }

    private string? ResolveSeatDynastyFamilyId(DaggerfallOfficeOverrideSave vacancy, int officeFactionId, int? officeRegion)
    {
        if (!string.IsNullOrWhiteSpace(vacancy.PreviousHolderFamilyId))
            return vacancy.PreviousHolderFamilyId;
        if (!string.IsNullOrWhiteSpace(vacancy.PreviousHolderCastId)
            && definitions.People.Individuals.TryGetValue(vacancy.PreviousHolderCastId, out DaggerfallCastIndividualDefinition? prevCast)
            && !string.IsNullOrWhiteSpace(prevCast.FamilyId))
            return prevCast.FamilyId;
        if (definitions.Factions.Factions.TryGetValue(officeFactionId, out DaggerfallFactionDefinition? faction))
        {
            foreach (int childId in faction.Children)
            {
                if (definitions.People.TryGetIndividualByFaction(childId, out DaggerfallCastIndividualDefinition? childCast)
                    && childCast is not null && !string.IsNullOrWhiteSpace(childCast.FamilyId))
                    return childCast.FamilyId;
            }
        }
        _ = officeRegion;
        return null;
    }

    private HashSet<long> CollectActiveTier1HoldersExcluding(string excludedOfficeKey)
    {
        HashSet<long> ids = [];
        foreach (DaggerfallOfficeOverrideSave over in _overrides.Values)
        {
            if (StringComparer.Ordinal.Equals(over.OfficeKey, excludedOfficeKey))
                continue;
            if (!over.Vacant && over.Kind is DaggerfallOfficeKind.Ruler or DaggerfallOfficeKind.MajorSeat && over.HolderActorId is long hid)
                ids.Add(hid);
        }
        foreach (DaggerfallBoardSeatBinding seat in definitions.BoardSeats.All)
        {
            string key = FactionOfficeKey(seat.OfficeFactionId);
            if (StringComparer.Ordinal.Equals(key, excludedOfficeKey) || _overrides.ContainsKey(key))
                continue;
            if (ResolveBoardSeatPersona(seat.SeatId) is { IsVacant: false, HolderActorId: long seatHolder })
                ids.Add(seatHolder);
        }
        return ids;
    }

    private (DaggerfallWorldProfileKey? Profile, string? SlotId, DaggerfallSiteId? SiteId) ResolveCanonicalFactionOfficeAnchor(
        int officeFactionId,
        string officeKey,
        int? officeRegion,
        OutgoingHolderSnapshot outgoing,
        string? newHolderCastId)
    {
        if (_overrides.TryGetValue(officeKey, out DaggerfallOfficeOverrideSave? prior) && prior.OfficeProfile is { } priorProfile)
        {
            DaggerfallWorldProfileKey key = priorProfile.Require();
            return (key, prior.OfficeSlotId, prior.OfficeSiteId ?? key.Site);
        }

        // Resolve canonical #11090 seat home from the office faction's baseline cast individual (e.g. king-gothryd -> Castle Daggerfall).
        if (definitions.Factions.Factions.TryGetValue(officeFactionId, out DaggerfallFactionDefinition? faction))
        {
            List<int> baselineCandidates = [];
            if (faction.Type == 4) baselineCandidates.Add(faction.Id);
            baselineCandidates.AddRange(faction.Children);
            if (baselineCandidates.Count == 0 && faction.Parent > 0) baselineCandidates.Add(faction.Parent);
            foreach (int cid in baselineCandidates)
            {
                if (definitions.People.TryGetIndividualByFaction(cid, out DaggerfallCastIndividualDefinition? baseCast)
                    && baseCast is not null
                    && ResolveCastHome(baseCast.Id, officeRegion) is { } baseHome
                    && DaggerfallCastHomes.ResolveProfileKey(baseHome) is { } baseProfile)
                {
                    return (baseProfile, $"cast:{baseCast.Id}", baseHome.Site.Id);
                }
            }
        }

        DaggerfallCastHomeLocation? fallbackHome = outgoing.Home ?? ResolveCastHome(newHolderCastId, officeRegion);
        if (fallbackHome is not null && DaggerfallCastHomes.ResolveProfileKey(fallbackHome) is { } fallbackProfile)
        {
            string? fallbackSlot = !string.IsNullOrWhiteSpace(outgoing.CastId) ? $"cast:{outgoing.CastId}"
                : !string.IsNullOrWhiteSpace(newHolderCastId) ? $"cast:{newHolderCastId}"
                : null;
            return (fallbackProfile, fallbackSlot, fallbackHome.Site.Id);
        }

        if (ResolveRegionCapitalSite(officeRegion) is { } capital)
        {
            if (capital.Exterior is { Buildings.Count: > 0 } ext
                && ext.Buildings.Values
                    .Where(b => b.Source.BuildingType == PalaceBuildingType)
                    .OrderBy(b => b.Id.BlockY).ThenBy(b => b.Id.BlockX).ThenBy(b => b.Id.Index)
                    .FirstOrDefault() is { } palace)
            {
                return (DaggerfallWorldProfileIds.Interior(capital.Id, palace.Id), "person/0", capital.Id);
            }
            return (null, null, capital.Id);
        }

        return (null, null, null);
    }

    private DaggerfallSiteRecord? FindSiteById(DaggerfallSiteId siteId)
    {
        if (sites is not null && sites.TryFind(siteId, out DaggerfallSiteRecord found))
            return found;
        return definitions.Locations.Records.FirstOrDefault(r => r.Id == siteId);
    }

    private DaggerfallOfficeActorBlueprint? BuildActorBlueprint(
        long? actorId,
        string? castId,
        string? displayName,
        string? familyId)
    {
        if (actorId is long id && npcs.TryGet(id, out DaggerfallNpc? npc) && npc is not null)
        {
            return new DaggerfallOfficeActorBlueprint(
                (int)npc.Kind,
                npc.StableKey ?? $"actor:{id}",
                npc.Site.Region,
                npc.Site.Location,
                npc.Site.Building,
                npc.Site.ProfileId,
                npc.Appearance.Race,
                npc.Appearance.Gender,
                npc.Appearance.BillboardArchive,
                npc.Appearance.BillboardRecord,
                npc.Appearance.NameSeed,
                npc.Appearance.FactionId,
                npc.Role,
                [.. npc.Services],
                DisplayName: displayName ?? npc.DisplayName,
                FamilyId: familyId ?? npc.FamilyId);
        }

        if (!string.IsNullOrWhiteSpace(castId)
            && definitions.People.Individuals.TryGetValue(castId, out DaggerfallCastIndividualDefinition? cast))
        {
            DaggerfallCastHomeLocation? home = ResolveCastHome(castId, null);
            DaggerfallWorldProfileKey? profile = DaggerfallCastHomes.ResolveProfileKey(home);
            return new DaggerfallOfficeActorBlueprint(
                (int)DaggerfallNpcKind.Static,
                $"cast:{cast.Id}",
                home?.Site.Id.Region ?? 0,
                home?.Site.Name ?? string.Empty,
                home?.Building is { } b ? $"{b.Id.BlockX}/{b.Id.BlockY}/{b.Id.Index}" : string.Empty,
                profile?.LogicalId,
                cast.Race ?? "breton",
                cast.Gender,
                cast.Archive,
                cast.Record,
                0,
                cast.FactionId,
                "person",
                ["talk"],
                DisplayName: displayName ?? cast.Name,
                FamilyId: familyId ?? cast.FamilyId);
        }

        return null;
    }

    private DaggerfallOfficeActorBlueprint? BuildLocalOfficeHolderBlueprint(
        DaggerfallLocalOffice office,
        long? actorId,
        string? displayName,
        string? familyId)
    {
        if (BuildActorBlueprint(actorId, null, displayName, familyId) is { } liveBp)
            return liveBp;

        DaggerfallSiteRecord? site = FindSiteById(office.SiteId);
        if (site is null)
            return null;

        if (office.BuildingId is { } bId && site.Exterior is not null
            && site.Exterior.Buildings.TryGetValue(bId, out DaggerfallSiteBuildingSource? building))
        {
            BuildingSlotCandidate? match = EnumerateBuildingCandidateDetails(site, building)
                .FirstOrDefault(c => c.ActorId == actorId || StringComparer.Ordinal.Equals(c.SlotId, office.SlotId));
            if (match is not null)
                return match.Blueprint with { DisplayName = displayName ?? match.DisplayName, FamilyId = familyId ?? match.FamilyId };
        }
        else if (office.Kind == DaggerfallOfficeKind.WatchCaptain)
        {
            int regionalPeopleFaction = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, site.Id.Region)?.Id ?? 0;
            return BuildExteriorSlotBlueprint(site, office.ProfileKey, office.SlotId, 0, regionalPeopleFaction) with
            {
                DisplayName = displayName ?? office.DisplayName,
                FamilyId = familyId ?? office.FamilyId,
            };
        }

        return null;
    }

    private DaggerfallOfficeActorBlueprint BuildExteriorSlotBlueprint(
        DaggerfallSiteRecord site,
        DaggerfallWorldProfileKey exteriorKey,
        string slotId,
        int index,
        int regionalPeopleFaction)
    {
        ulong socialSeed = worldSocialSeed();
        string walkerSlotKey = DaggerfallPeopleSlots.WalkerSlotKey(index);
        ulong slotSeed = DaggerfallPeopleSlots.SlotSeed(socialSeed, site.Id, walkerSlotKey);
        ushort nameSeed = (ushort)((slotSeed & 0xFFFFUL) == 0 ? 1 : (slotSeed & 0xFFFFUL));
        DaggerfallFactionDefinition? peopleFaction = regionalPeopleFaction > 0
            ? definitions.Factions.Factions.GetValueOrDefault(regionalPeopleFaction)
            : null;
        int packedSprite = peopleFaction is { Sprites.Count: > 0 } ? peopleFaction.Sprites[0] : (182 << 7);
        int archive = packedSprite >> 7;
        int record = packedSprite & 0x7F;
        string race = definitions.BuildingNames.TryGetNameBank(site.Id.Region, out int bank) && bank != 0 ? "redguard" : "breton";
        string name = ResolveDeterministicName(site.Id.Region, "Male", nameSeed, slotId);
        int townBuckets = DaggerfallHouseholds.TownHouseholdBucketCount(site);
        string? familyId = DaggerfallHouseholds.ResolveForExteriorWalker(socialSeed, site.Id, walkerSlotKey, isGuard: index % 32 == 0, townBuckets);
        return new DaggerfallOfficeActorBlueprint(
            (int)DaggerfallNpcKind.Civilian,
            slotId,
            site.Id.Region,
            site.Name,
            string.Empty,
            exteriorKey.LogicalId,
            race,
            "Male",
            archive,
            record,
            nameSeed,
            regionalPeopleFaction,
            "guard",
            ["talk"],
            DisplayName: name,
            FamilyId: familyId);
    }

    private static int DeterministicIndex(ulong socialSeed, string officeKey, long openedMinute, int count)
    {
        if (count <= 1) return 0;
        ulong hash = 14695981039346656037UL ^ socialSeed;
        hash = Mix64(hash ^ unchecked((ulong)openedMinute));
        foreach (char c in officeKey)
            hash = Mix64(hash ^ c);
        return checked((int)(hash % (ulong)count));
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

    private sealed record BuildingSlotCandidate(
        string SlotId,
        long ActorId,
        string DisplayName,
        string? FamilyId,
        string? SpawnerId,
        DaggerfallOfficeActorBlueprint Blueprint);

    private List<(string SlotId, long ActorId, string DisplayName, string? FamilyId)> EnumerateBuildingCandidates(
        DaggerfallSiteRecord site,
        DaggerfallSiteBuildingSource building) =>
        [.. EnumerateBuildingCandidateDetails(site, building)
            .Select(c => (c.SlotId, c.ActorId, c.DisplayName, c.FamilyId))];

    private List<BuildingSlotCandidate> EnumerateBuildingCandidateDetails(
        DaggerfallSiteRecord site,
        DaggerfallSiteBuildingSource building)
    {
        List<(BuildingSlotCandidate Candidate, int Priority)> scored = [];
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
        string buildingKey = $"{building.Id.BlockX}/{building.Id.BlockY}/{building.Id.Index}";

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
            DaggerfallOfficeActorBlueprint blueprint;
            if (!string.IsNullOrWhiteSpace(slot.CastId)
                && definitions.People.Individuals.TryGetValue(slot.CastId, out DaggerfallCastIndividualDefinition? castDef))
            {
                durableId = DaggerfallPeopleSlots.CastIndividualIdentity(castDef.Id);
                fallbackName = castDef.Name;
                familyId = castDef.FamilyId;
                blueprint = new DaggerfallOfficeActorBlueprint(
                    (int)DaggerfallNpcKind.Static,
                    $"cast:{castDef.Id}",
                    site.Id.Region,
                    site.Name,
                    buildingKey,
                    profileKey.LogicalId,
                    castDef.Race ?? "breton",
                    castDef.Gender,
                    castDef.Archive,
                    castDef.Record,
                    0,
                    castDef.FactionId,
                    "person",
                    ["talk"],
                    DisplayName: fallbackName,
                    FamilyId: familyId);
            }
            else
            {
                durableId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{profileKey.LogicalId}/{placementId}");
                int nameSeed = ordinal + 1;
                string gender = "Male";
                string race = definitions.BuildingNames.TryGetNameBank(site.Id.Region, out int bank) && bank != 0 ? "redguard" : "breton";
                int archive = 182;
                int record = 0;
                int factionId = slot.FactionId is > 0 ? slot.FactionId.Value : buildingFaction;
                if (!string.IsNullOrWhiteSpace(slot.SpawnerId)
                    && definitions.People.Spawners.TryGetValue(slot.SpawnerId, out DaggerfallPeopleSpawnerDefinition? spawner))
                {
                    string slotKey = DaggerfallPeopleSlots.InteriorBuildingSlotKey(building.Id.BlockX, building.Id.BlockY, slot.SlotId);
                    DaggerfallResolvedPeopleSlot resolved = DaggerfallPeopleSlots.ResolveSlot(
                        definitions.People, definitions.Factions, socialSeed, site.Id, slotKey, slot,
                        regionKey: regionName, climateKey: climateName);
                    int seeded = unchecked((int)(resolved.SlotSeed & 0x7FFF_FFFFUL));
                    nameSeed = seeded == 0 ? 1 : seeded;
                    gender = resolved.Visual.Gender;
                    archive = resolved.Visual.Archive;
                    record = resolved.Visual.Record;
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
                fallbackName = ResolveDeterministicName(site.Id.Region, gender, nameSeed, slot.SlotId);
                blueprint = new DaggerfallOfficeActorBlueprint(
                    (int)DaggerfallNpcKind.Static,
                    placementId,
                    site.Id.Region,
                    site.Name,
                    buildingKey,
                    profileKey.LogicalId,
                    race,
                    gender,
                    archive,
                    record,
                    nameSeed,
                    factionId,
                    "person",
                    ["talk"],
                    DisplayName: fallbackName,
                    FamilyId: familyId);
            }

            int priority = ScoreSlotPriority(bType, buildingFaction, slot);
            scored.Add((new BuildingSlotCandidate(placementId, durableId, fallbackName, familyId, slot.SpawnerId, blueprint), priority));
        }

        return
        [
            .. scored
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.Candidate.SlotId, StringComparer.Ordinal)
                .Select(item => item.Candidate),
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
                DaggerfallOfficeChangeCause.Law => "was banished by the court",
                _ => "departed",
            };
}
