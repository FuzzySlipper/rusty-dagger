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

/// <summary>
/// One sparse persisted office override (<c>office -&gt; holder, previous holder, cause</c>).
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
    bool Vacant = false)
{
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
        if (CauseDetail is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(CauseDetail);
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
    string? CauseDetail = null);

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
    string? FamilyId = null);

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
    Func<DaggerfallNpcAppearance, int, string>? defaultName = null)
{
    private const int GuildHallBuildingType = 11;
    private const int TempleBuildingType = 14;
    private const int PalaceBuildingType = 16;
    private const int SpecialGuildOrHouseBuildingType = 18;
    private const int ProvinceFactionType = 7;

    private readonly Dictionary<string, DaggerfallOfficeOverrideSave> _overrides = new(StringComparer.Ordinal);

    internal int OverrideCount => _overrides.Count;
    internal IReadOnlyCollection<DaggerfallOfficeOverrideSave> Overrides => _overrides.Values;

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
    /// Writes a sparse override for a faction office or board-game seat (card move, quest outcome, death, or succession).
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
        long? previousHolderActorId = null)
    {
        if (officeFactionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(officeFactionId), "Faction office id must be positive.");

        string key = FactionOfficeKey(officeFactionId);
        definitions.BoardSeats.TryGetByFactionId(officeFactionId, out DaggerfallBoardSeatBinding? seat);

        // Snapshot current holder before overwriting so predecessor history is preserved for macros & news.
        if (previousHolderName is null && previousHolderActorId is null)
        {
            DaggerfallOfficeHolder? current = officeHolders.ResolveOffice(officeFactionId, seat?.HomeRegion);
            if (current is { HasLivingHolder: true })
            {
                previousHolderName = current.DisplayName;
                previousHolderActorId = current.ActorId;
            }
            else if (_overrides.TryGetValue(key, out DaggerfallOfficeOverrideSave? prior))
            {
                previousHolderName = prior.HolderDisplayName ?? prior.PreviousHolderName;
                previousHolderActorId = prior.HolderActorId ?? prior.PreviousHolderActorId;
            }
        }

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
            PreviousHolderActorId: previousHolderActorId,
            PreviousHolderName: previousHolderName,
            Cause: cause,
            CauseDetail: causeDetail,
            ChangedDay: changedDay,
            Vacant: false);
        saved.Validate();
        _overrides[key] = saved;
        return saved;
    }

    /// <summary>Writes a sparse override for a board-game seat by its B1 <paramref name="seatId"/>.</summary>
    internal DaggerfallOfficeOverrideSave SetBoardSeatHolder(
        string seatId,
        long? holderActorId,
        string? holderCastId,
        int? holderFactionId,
        string? holderDisplayName,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        string? title = null)
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
            title ?? seat.DefaultTitle);
    }

    /// <summary>Marks a faction office or board-game seat explicitly vacant, recording the outgoing holder and cause.</summary>
    internal DaggerfallOfficeOverrideSave VacateFactionOffice(
        int officeFactionId,
        DaggerfallOfficeChangeCause cause,
        int changedDay,
        string? causeDetail = null,
        string? previousHolderName = null,
        long? previousHolderActorId = null)
    {
        if (officeFactionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(officeFactionId), "Faction office id must be positive.");

        string key = FactionOfficeKey(officeFactionId);
        definitions.BoardSeats.TryGetByFactionId(officeFactionId, out DaggerfallBoardSeatBinding? seat);
        DaggerfallOfficeKind kind = seat is not null ? ToOfficeKind(seat.Kind) : DaggerfallOfficeKind.Ruler;

        if (previousHolderName is null && previousHolderActorId is null)
        {
            DaggerfallOfficeHolder? current = officeHolders.ResolveOffice(officeFactionId, seat?.HomeRegion);
            if (current is { HasLivingHolder: true })
            {
                previousHolderName = current.DisplayName;
                previousHolderActorId = current.ActorId;
            }
            else if (_overrides.TryGetValue(key, out DaggerfallOfficeOverrideSave? prior))
            {
                previousHolderName = prior.HolderDisplayName ?? prior.PreviousHolderName;
                previousHolderActorId = prior.HolderActorId ?? prior.PreviousHolderActorId;
            }
        }

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
            PreviousHolderActorId: previousHolderActorId,
            PreviousHolderName: previousHolderName,
            Cause: cause,
            CauseDetail: causeDetail,
            ChangedDay: changedDay,
            Vacant: true);
        saved.Validate();
        _overrides[key] = saved;
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
        bool vacant = false)
    {
        ArgumentNullException.ThrowIfNull(office);
        string? resolvedName = vacant
            ? null
            : (!string.IsNullOrWhiteSpace(holderDisplayName)
                ? holderDisplayName
                : holderActorId is long id && npcs.TryGet(id, out DaggerfallNpc? npc) && npc is not null
                    ? npc.DisplayName ?? npc.Role
                    : null);

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
            PreviousHolderActorId: office.HolderActorId,
            PreviousHolderName: string.IsNullOrWhiteSpace(office.DisplayName) ? office.PreviousHolderName : office.DisplayName,
            Cause: cause,
            CauseDetail: causeDetail,
            ChangedDay: changedDay,
            Vacant: vacant);
        saved.Validate();
        _overrides[office.OfficeKey] = saved;
        return saved;
    }

    /// <summary>Removes a sparse override, returning the office to its zero-byte seeded baseline.</summary>
    internal bool ClearOverride(string officeKey) => _overrides.Remove(officeKey);

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
                CauseDetail: over?.CauseDetail ?? holder?.PreviousHolderFate);
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
                CauseDetail: over?.CauseDetail ?? holder.PreviousHolderFate);
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
    /// override recording the slain holder and promoting a living successor (or marking vacant).
    /// Ordinary non-office deaths write zero bytes.
    /// </summary>
    internal IReadOnlyList<DaggerfallOfficeOverrideSave> ReactToDeath(
        long deadActorId,
        int currentDay,
        DaggerfallSiteRecord? activeSite = null)
    {
        if (deadActorId <= 0 || deadActorId == DaggerfallActorIdentity.PlayerEntityId)
            return [];

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
                recorded.Add(RecordFactionHolderDeath(fid, existing.Kind, deadActorId, existingSlainName, currentDay));
            }
            else if (activeSite is not null
                && ResolveLocalOffices(activeSite, ignoreDeadActorId: deadActorId)
                    .FirstOrDefault(o => StringComparer.Ordinal.Equals(o.OfficeKey, existing.OfficeKey)) is { } local)
            {
                recorded.Add(RecordLocalHolderDeath(
                    activeSite,
                    local with { HolderActorId = deadActorId, DisplayName = existingSlainName ?? local.DisplayName },
                    deadActorId,
                    currentDay));
            }
            else
            {
                DaggerfallOfficeOverrideSave vacated = existing with
                {
                    HolderActorId = null,
                    HolderCastId = null,
                    HolderFactionId = null,
                    HolderDisplayName = null,
                    PreviousHolderActorId = deadActorId,
                    PreviousHolderName = existing.HolderDisplayName ?? existing.PreviousHolderName,
                    Cause = DaggerfallOfficeChangeCause.Death,
                    CauseDetail = "was slain",
                    ChangedDay = currentDay,
                    Vacant = true,
                };
                vacated.Validate();
                _overrides[existing.OfficeKey] = vacated;
                recorded.Add(vacated);
            }
        }

        // 2. Check untouched baseline canonical major seats and regional province rulers.
        HashSet<int> checkedFactions = [];
        foreach (DaggerfallBoardSeatBinding seat in definitions.BoardSeats.All)
        {
            if (!checkedFactions.Add(seat.OfficeFactionId) || _overrides.ContainsKey(FactionOfficeKey(seat.OfficeFactionId)))
                continue;
            if (MatchesHolderActor(seat.OfficeFactionId, seat.HomeRegion, deadActorId, out string? slainName))
                recorded.Add(RecordFactionHolderDeath(seat.OfficeFactionId, ToOfficeKind(seat.Kind), deadActorId, slainName, currentDay));
        }

        foreach (DaggerfallFactionDefinition province in definitions.Factions.Factions.Values
            .Where(f => f.Type == ProvinceFactionType)
            .OrderBy(f => f.Id))
        {
            if (!checkedFactions.Add(province.Id) || _overrides.ContainsKey(FactionOfficeKey(province.Id)))
                continue;
            if (MatchesHolderActor(province.Id, province.Region >= 0 ? province.Region : null, deadActorId, out string? slainName))
                recorded.Add(RecordFactionHolderDeath(province.Id, DaggerfallOfficeKind.Ruler, deadActorId, slainName, currentDay));
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
                    recorded.Add(RecordLocalHolderDeath(activeSite, local, deadActorId, currentDay));
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

    private bool MatchesHolderActor(int officeFactionId, int? preferredRegion, long deadActorId, out string? slainName)
    {
        slainName = null;
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
        string? slainName,
        int currentDay)
    {
        // Temporarily remove any override pointing at the dead actor so ResolveOffice walks to the next living successor.
        string key = FactionOfficeKey(officeFactionId);
        _overrides.Remove(key);
        definitions.BoardSeats.TryGetByFactionId(officeFactionId, out DaggerfallBoardSeatBinding? seat);
        DaggerfallOfficeHolder? next = officeHolders.ResolveOffice(officeFactionId, seat?.HomeRegion);
        if (next is { HasLivingHolder: true } successor && successor.ActorId != deadActorId)
        {
            return SetFactionOfficeHolder(
                officeFactionId,
                kind,
                holderActorId: successor.ActorId,
                holderCastId: successor.CastId,
                holderFactionId: successor.HolderFactionId,
                holderDisplayName: successor.DisplayName,
                cause: DaggerfallOfficeChangeCause.Death,
                changedDay: currentDay,
                causeDetail: "was slain",
                title: successor.Title ?? seat?.DefaultTitle,
                previousHolderName: slainName ?? next.PreviousHolderName,
                previousHolderActorId: deadActorId);
        }

        return VacateFactionOffice(
            officeFactionId,
            cause: DaggerfallOfficeChangeCause.Death,
            changedDay: currentDay,
            causeDetail: "was slain",
            previousHolderName: slainName,
            previousHolderActorId: deadActorId);
    }

    private DaggerfallOfficeOverrideSave RecordLocalHolderDeath(
        DaggerfallSiteRecord site,
        DaggerfallLocalOffice local,
        long deadActorId,
        int currentDay)
    {
        // Try to promote the next living talkable slot in the same building if one exists.
        if (local.BuildingId is { } bId && site.Exterior is not null
            && site.Exterior.Buildings.TryGetValue(bId, out DaggerfallSiteBuildingSource? building))
        {
            List<(string SlotId, long ActorId, string DisplayName, string? FamilyId)> candidates = EnumerateBuildingCandidates(site, building);
            foreach ((string _, long candidateId, string candidateName, string? _) in candidates)
            {
                if (candidateId == deadActorId || officeHolders.IsActorDead(candidateId))
                    continue;
                return SetLocalOfficeHolder(
                    local,
                    holderActorId: candidateId,
                    holderDisplayName: candidateName,
                    cause: DaggerfallOfficeChangeCause.Death,
                    changedDay: currentDay,
                    causeDetail: "was slain",
                    vacant: false);
            }
        }

        return SetLocalOfficeHolder(
            local,
            holderActorId: null,
            holderDisplayName: null,
            cause: DaggerfallOfficeChangeCause.Death,
            changedDay: currentDay,
            causeDetail: "was slain",
            vacant: true);
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
                    CauseDetail: over.CauseDetail);
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
                FamilyId: overrideAlive ? overrideFamily : null);
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
