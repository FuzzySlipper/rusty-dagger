using System.Globalization;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Lifecycle state of a shop, tavern, or family counter whose proprietor was murdered or arrested.</summary>
internal enum DaggerfallShopContinuityState
{
    /// <summary>Temporarily closed for bereavement after a proprietor's murder while surviving kin prepare to reopen.</summary>
    BereavementClosed = 0,

    /// <summary>Reopened under a surviving adult member of the proprietor's household.</summary>
    KinInherited = 1,

    /// <summary>Closed during bereavement/vacancy after a proprietor's murder with no living kin, awaiting town appointment.</summary>
    TownReplacementPending = 2,

    /// <summary>Reopened under a newly appointed non-kin town replacement so settlement commerce stays functional.</summary>
    TownReplaced = 3,

    /// <summary>Open under temporary kin or staff counter cover while the jailed proprietor serves a non-fatal sentence.</summary>
    ArrestCovered = 4,
}

/// <summary>Read-only runtime snapshot of one tracked premises continuity entry.</summary>
internal readonly record struct DaggerfallShopPremisesSnapshot(
    string ProfileLogicalId,
    DaggerfallSiteId SiteId,
    int BlockX,
    int BlockY,
    int BuildingIndex,
    int BuildingType,
    string? FamilyId,
    long OriginalProprietorId,
    string OriginalProprietorName,
    string OriginalSlotId,
    DaggerfallShopContinuityState State,
    int EventDay,
    int? ClosedUntilDay,
    int? JailedUntilDay,
    long? ReplacementActorId,
    string? ReplacementName,
    string? ReplacementOriginKind,
    int Generation);

/// <summary>Request emitted by <see cref="DaggerfallShopContinuity"/> to seat a kin heir, town replacement, or arrest cover at a counter slot.</summary>
internal sealed record DaggerfallShopReplacementSeatRequest(
    long ActorId,
    bool IsExistingActor,
    DaggerfallWorldProfileKey ShopProfile,
    string TargetSlotId,
    string OriginSlotKey,
    string DisplayName,
    string Role,
    string[] Services,
    string Race,
    string Gender,
    int BillboardArchive,
    int BillboardRecord,
    int NameSeed,
    int FactionId,
    string? FamilyId,
    DaggerfallWorldProfileKey? OriginHomeProfile = null);

/// <summary>Minimal saved identity of a surviving adult kin queued to inherit a shop after bereavement, even if off-scene on reopen day.</summary>
internal sealed record DaggerfallShopKinBlueprintSave(
    long ActorId,
    string SlotKey,
    string DisplayName,
    string Race,
    string Gender,
    int BillboardArchive,
    int BillboardRecord,
    int NameSeed,
    int FactionId,
    string? FamilyId,
    string? HomeProfileLogicalId = null,
    int? HomeProfileKind = null,
    int? HomeRegion = null,
    int? HomeSiteIndex = null)
{
    internal void Validate()
    {
        if (ActorId <= 0 || ActorId == DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(ActorId), "Saved shop kin blueprint actor id must be a positive non-player actor id.");
        ArgumentException.ThrowIfNullOrWhiteSpace(SlotKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Race);
        ArgumentException.ThrowIfNullOrWhiteSpace(Gender);
        if (!string.IsNullOrWhiteSpace(FamilyId) && !DaggerfallHouseholds.IsValidFamilyId(FamilyId))
            throw new ArgumentException($"Saved shop kin blueprint family id '{FamilyId}' is invalid.", nameof(FamilyId));
    }
}

/// <summary>Sparse persisted continuity record for one affected shop, tavern, or family counter.</summary>
internal sealed record DaggerfallShopPremisesSave(
    string ProfileLogicalId,
    int Region,
    int SiteIndex,
    int BlockX,
    int BlockY,
    int BuildingIndex,
    int BuildingType,
    string? FamilyId,
    long OriginalProprietorId,
    string OriginalProprietorName,
    string OriginalSlotId,
    string OriginalRole,
    string[] OriginalServices,
    string OriginalRace,
    string OriginalGender,
    int OriginalArchive,
    int OriginalRecord,
    int OriginalFactionId,
    DaggerfallShopContinuityState State,
    int EventDay,
    int? ClosedUntilDay = null,
    int? JailedUntilDay = null,
    long? ReplacementActorId = null,
    string? ReplacementName = null,
    string? ReplacementOriginKind = null,
    int Generation = 0,
    string? PriorCoverRole = null,
    string[]? PriorCoverServices = null,
    DaggerfallShopKinBlueprintSave? PendingKin = null)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProfileLogicalId);
        if (Region is < 0 or > 61)
            throw new ArgumentOutOfRangeException(nameof(Region), "Saved shop continuity region is out of range.");
        if (SiteIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(SiteIndex), "Saved shop continuity site index must be non-negative.");
        if (BlockX < 0 || BlockY < 0 || BuildingIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(BuildingIndex), "Saved shop continuity building coordinates must be non-negative.");
        if (OriginalProprietorId <= 0 || OriginalProprietorId == DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(OriginalProprietorId), "Saved shop continuity original proprietor id must be a positive non-player actor id.");
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginalProprietorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginalSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginalRole);
        ArgumentNullException.ThrowIfNull(OriginalServices);
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginalRace);
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginalGender);
        if (!Enum.IsDefined(State))
            throw new ArgumentOutOfRangeException(nameof(State), "Saved shop continuity state is invalid.");
        if (EventDay < 0)
            throw new ArgumentOutOfRangeException(nameof(EventDay), "Saved shop continuity event day must be non-negative.");
        if (ClosedUntilDay is < 0)
            throw new ArgumentOutOfRangeException(nameof(ClosedUntilDay), "Saved shop continuity closed-until day must be non-negative.");
        if (JailedUntilDay is < 0)
            throw new ArgumentOutOfRangeException(nameof(JailedUntilDay), "Saved shop continuity jailed-until day must be non-negative.");
        if (ReplacementActorId is <= 0 or DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(ReplacementActorId), "Saved shop continuity replacement actor id must be a positive non-player actor id.");
        if (Generation < 0)
            throw new ArgumentOutOfRangeException(nameof(Generation), "Saved shop continuity generation must be non-negative.");
        if (!string.IsNullOrWhiteSpace(FamilyId) && !DaggerfallHouseholds.IsValidFamilyId(FamilyId))
            throw new ArgumentException($"Saved shop continuity family id '{FamilyId}' is invalid.", nameof(FamilyId));
        PendingKin?.Validate();
    }
}

/// <summary>Sparse persisted snapshot of shop bereavement closures, kin inheritances, town replacements, and arrest covers; empty when untouched.</summary>
internal sealed record DaggerfallShopContinuitySave(DaggerfallShopPremisesSave[] Premises)
{
    internal static DaggerfallShopContinuitySave Empty { get; } = new([]);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Premises);
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (DaggerfallShopPremisesSave entry in Premises)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Validate();
            if (!keys.Add(entry.ProfileLogicalId))
                throw new ArgumentException($"Saved shop continuity repeats profile '{entry.ProfileLogicalId}'.");
        }
    }
}

/// <summary>
/// Proprietor continuity owner (Den #11161, settled design <c>wiki/rusty-dagger/households-and-feuds.md</c>):
/// - Murdering a shop, tavern, or family-counter proprietor closes the premises for a tuned bereavement window.
/// - After bereavement, a surviving adult kin member inherits the counter with grieving dialogue; when no living
///   adult kin remain, a newly appointed non-kin town replacement takes over after a short vacancy so commerce continues.
/// - Arresting a proprietor (non-fatal custody) keeps the premises open under temporary kin/staff cover until release,
///   then returns the original proprietor unless they were exiled, banished, or killed in the meantime.
/// Untouched premises write zero save bytes.
/// </summary>
internal sealed class DaggerfallShopContinuity(
    DaggerfallNpcRegistry npcs,
    DaggerfallActorLedger actorLedger,
    ActorsState actors,
    Func<long, bool> isActorBanished,
    Func<long, bool> isActorExiled,
    Func<ulong> worldSocialSeed,
    Func<DaggerfallNpcAppearance, int, string?, string> generatePersonName,
    Action<DaggerfallShopReplacementSeatRequest> seatReplacement,
    Action<long, DaggerfallWorldProfileKey> releaseFromCustody,
    Action<long, bool, string?, string[]?> endTemporaryCover,
    DaggerfallSocialTuning? socialTuning = null)
{
    private sealed class PremisesState
    {
        internal required string ProfileLogicalId { get; init; }
        internal required int Region { get; init; }
        internal required int SiteIndex { get; init; }
        internal required int BlockX { get; init; }
        internal required int BlockY { get; init; }
        internal required int BuildingIndex { get; init; }
        internal required int BuildingType { get; init; }
        internal string? FamilyId { get; set; }
        internal long OriginalProprietorId { get; set; }
        internal string OriginalProprietorName { get; set; } = string.Empty;
        internal string OriginalSlotId { get; set; } = string.Empty;
        internal string OriginalRole { get; set; } = string.Empty;
        internal string[] OriginalServices { get; set; } = [];
        internal string OriginalRace { get; set; } = "breton";
        internal string OriginalGender { get; set; } = "male";
        internal int OriginalArchive { get; set; } = 182;
        internal int OriginalRecord { get; set; }
        internal int OriginalFactionId { get; set; }
        internal DaggerfallShopContinuityState State { get; set; }
        internal int EventDay { get; set; }
        internal int? ClosedUntilDay { get; set; }
        internal int? JailedUntilDay { get; set; }
        internal long? ReplacementActorId { get; set; }
        internal string? ReplacementName { get; set; }
        internal string? ReplacementOriginKind { get; set; }
        internal int Generation { get; set; }
        internal string? PriorCoverRole { get; set; }
        internal string[]? PriorCoverServices { get; set; }
        internal DaggerfallShopKinBlueprintSave? PendingKin { get; set; }

        internal DaggerfallSiteId SiteId => new(Region, SiteIndex);
        internal DaggerfallWorldProfileKey ProfileKey => new(SiteId, DaggerfallWorldProfileKind.Interior, ProfileLogicalId);

        internal DaggerfallShopPremisesSnapshot ToSnapshot() => new(
            ProfileLogicalId, SiteId, BlockX, BlockY, BuildingIndex, BuildingType,
            FamilyId, OriginalProprietorId, OriginalProprietorName, OriginalSlotId,
            State, EventDay, ClosedUntilDay, JailedUntilDay,
            ReplacementActorId, ReplacementName, ReplacementOriginKind, Generation);

        internal DaggerfallShopPremisesSave ToSave() => new(
            ProfileLogicalId, Region, SiteIndex, BlockX, BlockY, BuildingIndex, BuildingType,
            FamilyId, OriginalProprietorId, OriginalProprietorName, OriginalSlotId,
            OriginalRole, [.. OriginalServices], OriginalRace, OriginalGender,
            OriginalArchive, OriginalRecord, OriginalFactionId,
            State, EventDay, ClosedUntilDay, JailedUntilDay,
            ReplacementActorId, ReplacementName, ReplacementOriginKind, Generation,
            PriorCoverRole, PriorCoverServices is null ? null : [.. PriorCoverServices],
            PendingKin);
    }

    private readonly DaggerfallSocialTuning _tuning = (socialTuning ?? DaggerfallSocialTuning.Default).Validate();
    private readonly Dictionary<string, PremisesState> _premises = new(StringComparer.Ordinal);

    internal static bool IsProprietorServices(IReadOnlyList<string>? services)
    {
        if (services is null || services.Count == 0)
            return false;
        foreach (string s in services)
        {
            if (s is "shop" or "merchant" or "buy-items" or "sell-items" or "food-drink" or "banking")
                return true;
        }
        return false;
    }

    internal static bool IsEligibleProprietorBuildingType(int buildingType) =>
        DaggerfallNpcServiceFacts.IsShop(buildingType)
        || buildingType == 15
        || buildingType == 3
        || buildingType is >= 17 and <= 23;

    internal DaggerfallShopContinuitySave Capture()
    {
        if (_premises.Count == 0)
            return DaggerfallShopContinuitySave.Empty;
        return new([.. _premises.Values
            .OrderBy(p => p.ProfileLogicalId, StringComparer.Ordinal)
            .Select(p => p.ToSave())]);
    }

    internal void Restore(DaggerfallShopContinuitySave? saved)
    {
        _premises.Clear();
        if (saved is null || saved.Premises.Length == 0)
            return;
        saved.Validate();
        foreach (DaggerfallShopPremisesSave entry in saved.Premises)
        {
            _premises[entry.ProfileLogicalId] = new PremisesState
            {
                ProfileLogicalId = entry.ProfileLogicalId,
                Region = entry.Region,
                SiteIndex = entry.SiteIndex,
                BlockX = entry.BlockX,
                BlockY = entry.BlockY,
                BuildingIndex = entry.BuildingIndex,
                BuildingType = entry.BuildingType,
                FamilyId = entry.FamilyId,
                OriginalProprietorId = entry.OriginalProprietorId,
                OriginalProprietorName = entry.OriginalProprietorName,
                OriginalSlotId = entry.OriginalSlotId,
                OriginalRole = entry.OriginalRole,
                OriginalServices = [.. entry.OriginalServices],
                OriginalRace = entry.OriginalRace,
                OriginalGender = entry.OriginalGender,
                OriginalArchive = entry.OriginalArchive,
                OriginalRecord = entry.OriginalRecord,
                OriginalFactionId = entry.OriginalFactionId,
                State = entry.State,
                EventDay = entry.EventDay,
                ClosedUntilDay = entry.ClosedUntilDay,
                JailedUntilDay = entry.JailedUntilDay,
                ReplacementActorId = entry.ReplacementActorId,
                ReplacementName = entry.ReplacementName,
                ReplacementOriginKind = entry.ReplacementOriginKind,
                Generation = entry.Generation,
                PriorCoverRole = entry.PriorCoverRole,
                PriorCoverServices = entry.PriorCoverServices is null ? null : [.. entry.PriorCoverServices],
                PendingKin = entry.PendingKin,
            };
        }
    }

    internal IReadOnlyList<DaggerfallShopPremisesSnapshot> AllSnapshots() =>
        [.. _premises.Values.OrderBy(p => p.ProfileLogicalId, StringComparer.Ordinal).Select(p => p.ToSnapshot())];

    internal bool TryGetPremises(string profileLogicalId, out DaggerfallShopPremisesSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(profileLogicalId) && _premises.TryGetValue(profileLogicalId, out PremisesState? state))
        {
            snapshot = state.ToSnapshot();
            return true;
        }
        snapshot = default;
        return false;
    }

    internal bool TryGetPremisesForBuilding(DaggerfallSiteId siteId, DaggerfallSiteBuildingId buildingId, out DaggerfallShopPremisesSnapshot snapshot)
    {
        DaggerfallWorldProfileKey interiorKey = DaggerfallWorldProfileIds.Interior(siteId, buildingId);
        return TryGetPremises(interiorKey.LogicalId, out snapshot);
    }

    /// <summary>
    /// Returns a clear exterior door closure notice while a shop, tavern, or family counter is closed for
    /// bereavement or awaiting a newly appointed town replacement; returns null when open or unaffected.
    /// </summary>
    internal string? GetClosedNotice(DaggerfallSiteId siteId, DaggerfallSiteBuildingId buildingId, int currentDay)
    {
        DaggerfallWorldProfileKey interiorKey = DaggerfallWorldProfileIds.Interior(siteId, buildingId);
        if (!_premises.TryGetValue(interiorKey.LogicalId, out PremisesState? state))
            return null;
        if (state.State is not (DaggerfallShopContinuityState.BereavementClosed or DaggerfallShopContinuityState.TownReplacementPending))
            return null;
        if (state.ClosedUntilDay is int until && currentDay >= until)
            return null;

        int daysLeft = state.ClosedUntilDay is int closeUntil ? Math.Max(1, closeUntil - currentDay) : 1;
        string dayUnit = daysLeft == 1 ? "day" : "days";
        return state.State == DaggerfallShopContinuityState.BereavementClosed
            ? $"Closed in mourning following the death of {state.OriginalProprietorName}. Reopens in {daysLeft} {dayUnit}."
            : $"Closed in mourning following the death of {state.OriginalProprietorName} while the town appoints a new proprietor. Reopens in {daysLeft} {dayUnit}.";
    }

    /// <summary>
    /// Reacts when a proprietor of a shop, tavern, or family counter is murdered: closes the premises for
    /// bereavement and schedules either kin inheritance or a town-appointed replacement.
    /// </summary>
    internal DaggerfallShopPremisesSnapshot? ReactToProprietorDeath(
        long actorId,
        DaggerfallNpc? npc,
        DaggerfallWorldProfileKey activeProfile,
        DaggerfallSiteProfile? profileObj,
        int currentDay)
    {
        if (actorId <= 0 || actorId == DaggerfallActorIdentity.PlayerEntityId || npc is null)
            return null;
        if (!IsProprietorServices(npc.Services))
            return null;

        if (!TryResolveProprietorPremises(npc, activeProfile, profileObj, out DaggerfallWorldProfileKey shopProfile,
                out DaggerfallSiteBuildingId buildingId, out int buildingType, out string slotId))
        {
            return null;
        }
        if (!IsEligibleProprietorBuildingType(buildingType))
            return null;

        int generation = 0;
        if (_premises.TryGetValue(shopProfile.LogicalId, out PremisesState? prior))
        {
            // If an arrest cover was killed while the original proprietor is jailed, keep the jail sentence and assign new cover.
            if (prior.State == DaggerfallShopContinuityState.ArrestCovered && prior.ReplacementActorId == actorId)
            {
                prior.ReplacementActorId = null;
                prior.ReplacementName = null;
                prior.ReplacementOriginKind = null;
                prior.PriorCoverRole = null;
                prior.PriorCoverServices = null;
                prior.Generation++;
                AssignArrestCover(prior, currentDay);
                return prior.ToSnapshot();
            }
            // If the jailed original died while covered, end temporary cover and transition cleanly into bereavement/inheritance.
            if (prior.State == DaggerfallShopContinuityState.ArrestCovered && prior.OriginalProprietorId == actorId)
            {
                if (prior.ReplacementActorId is long coverId)
                {
                    endTemporaryCover(coverId, string.Equals(prior.ReplacementOriginKind, "kin", StringComparison.Ordinal),
                        prior.PriorCoverRole, prior.PriorCoverServices);
                }
            }
            generation = prior.Generation + 1;
            if (!string.IsNullOrWhiteSpace(prior.OriginalSlotId))
                slotId = prior.OriginalSlotId;
        }

        string proprietorName = !string.IsNullOrWhiteSpace(npc.DisplayName) ? npc.DisplayName! : npc.Role;
        PremisesState state = new()
        {
            ProfileLogicalId = shopProfile.LogicalId,
            Region = shopProfile.Site.Region,
            SiteIndex = shopProfile.Site.Index,
            BlockX = buildingId.BlockX,
            BlockY = buildingId.BlockY,
            BuildingIndex = buildingId.Index,
            BuildingType = buildingType,
            FamilyId = npc.FamilyId,
            OriginalProprietorId = actorId,
            OriginalProprietorName = proprietorName,
            OriginalSlotId = slotId,
            OriginalRole = npc.Role,
            OriginalServices = [.. npc.Services],
            OriginalRace = npc.Appearance.Race,
            OriginalGender = npc.Appearance.Gender,
            OriginalArchive = npc.Appearance.BillboardArchive,
            OriginalRecord = npc.Appearance.BillboardRecord,
            OriginalFactionId = npc.Appearance.FactionId,
            EventDay = Math.Max(0, currentDay),
            Generation = generation,
        };

        DaggerfallNpc? survivingKin = FindSurvivingAdultKin(state.FamilyId, excludeActorId: actorId, state.SiteId);
        if (survivingKin is not null)
        {
            state.State = DaggerfallShopContinuityState.BereavementClosed;
            state.ClosedUntilDay = checked(state.EventDay + _tuning.ShopBereavementClosureDays);
            state.ReplacementActorId = survivingKin.DurableId;
            state.ReplacementName = survivingKin.DisplayName ?? survivingKin.Role;
            state.ReplacementOriginKind = "kin";
            state.PendingKin = CaptureKinBlueprint(survivingKin);
        }
        else
        {
            state.State = DaggerfallShopContinuityState.TownReplacementPending;
            state.ClosedUntilDay = checked(state.EventDay + _tuning.ShopTownReplacementVacancyDays);
        }

        _premises[shopProfile.LogicalId] = state;

        // If configured bereavement/vacancy window is 0 days, seat immediately.
        if (state.ClosedUntilDay is int closeDay && currentDay >= closeDay)
            AdvancePremises(state, currentDay);

        return state.ToSnapshot();
    }

    /// <summary>
    /// Reacts when a shop, tavern, or family-counter proprietor is arrested (non-fatal jail custody):
    /// keeps the premises open under temporary kin/staff cover and schedules original return upon release.
    /// </summary>
    internal DaggerfallShopPremisesSnapshot? ReactToProprietorArrest(
        long actorId,
        DaggerfallNpc? npc,
        bool banished,
        bool imprisoned,
        DaggerfallWorldProfileKey activeProfile,
        DaggerfallSiteProfile? profileObj,
        int currentDay)
    {
        if (actorId <= 0 || actorId == DaggerfallActorIdentity.PlayerEntityId || npc is null)
            return null;
        if (!IsProprietorServices(npc.Services))
            return null;
        if (!TryResolveProprietorPremises(npc, activeProfile, profileObj, out DaggerfallWorldProfileKey shopProfile,
                out DaggerfallSiteBuildingId buildingId, out int buildingType, out string slotId))
        {
            return null;
        }
        if (!IsEligibleProprietorBuildingType(buildingType))
            return null;

        int generation = 0;
        if (_premises.TryGetValue(shopProfile.LogicalId, out PremisesState? prior))
        {
            if (prior.State == DaggerfallShopContinuityState.ArrestCovered && prior.OriginalProprietorId == actorId)
            {
                // Repeated arrest while already jailed: extend sentence or transition if banished.
                if (banished)
                    return TransitionLostProprietorToPermanentReplacement(prior, currentDay);
                prior.JailedUntilDay = Math.Max(prior.JailedUntilDay ?? currentDay, checked(currentDay + _tuning.NpcJailSentenceDays));
                return prior.ToSnapshot();
            }
            generation = prior.Generation + 1;
            if (!string.IsNullOrWhiteSpace(prior.OriginalSlotId))
                slotId = prior.OriginalSlotId;
        }

        string proprietorName = !string.IsNullOrWhiteSpace(npc.DisplayName) ? npc.DisplayName! : npc.Role;
        PremisesState state = new()
        {
            ProfileLogicalId = shopProfile.LogicalId,
            Region = shopProfile.Site.Region,
            SiteIndex = shopProfile.Site.Index,
            BlockX = buildingId.BlockX,
            BlockY = buildingId.BlockY,
            BuildingIndex = buildingId.Index,
            BuildingType = buildingType,
            FamilyId = npc.FamilyId,
            OriginalProprietorId = actorId,
            OriginalProprietorName = proprietorName,
            OriginalSlotId = slotId,
            OriginalRole = npc.Role,
            OriginalServices = [.. npc.Services],
            OriginalRace = npc.Appearance.Race,
            OriginalGender = npc.Appearance.Gender,
            OriginalArchive = npc.Appearance.BillboardArchive,
            OriginalRecord = npc.Appearance.BillboardRecord,
            OriginalFactionId = npc.Appearance.FactionId,
            EventDay = Math.Max(0, currentDay),
            Generation = generation,
        };
        _premises[shopProfile.LogicalId] = state;

        if (banished || !imprisoned)
            return TransitionLostProprietorToPermanentReplacement(state, currentDay);

        state.State = DaggerfallShopContinuityState.ArrestCovered;
        state.JailedUntilDay = checked(state.EventDay + _tuning.NpcJailSentenceDays);
        state.ClosedUntilDay = null;
        AssignArrestCover(state, currentDay);
        return state.ToSnapshot();
    }

    /// <summary>
    /// Advances bereavement windows, town-replacement vacancies, and non-fatal jail releases on calendar day change.
    /// Returns the number of premises that transitioned state.
    /// </summary>
    internal int AdvanceDay(int currentDay)
    {
        if (_premises.Count == 0)
            return 0;

        int transitioned = 0;
        List<string> toRemove = [];
        foreach ((string key, PremisesState state) in _premises.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray())
        {
            if (AdvancePremises(state, currentDay, out bool removeRecord))
                transitioned++;
            if (removeRecord)
                toRemove.Add(key);
        }

        foreach (string key in toRemove)
            _premises.Remove(key);

        return transitioned;
    }

    /// <summary>Builds a continuity-aware greeting line for a kin heir, newly appointed town replacement, or temporary arrest cover.</summary>
    internal string? BuildGreetingLine(DaggerfallNpc npc, int currentDay)
    {
        ArgumentNullException.ThrowIfNull(npc);
        PremisesState? matched = FindPremisesByReplacement(npc.DurableId);
        if (matched is null)
            return null;

        return matched.State switch
        {
            DaggerfallShopContinuityState.KinInherited =>
                $"I have taken over the counter since {matched.OriginalProprietorName}'s death — our house still grieves, though the shop must go on.",
            DaggerfallShopContinuityState.TownReplaced =>
                $"The town appointed me to reopen this counter after {matched.OriginalProprietorName} passed with no kin left to carry it on.",
            DaggerfallShopContinuityState.ArrestCovered =>
                matched.JailedUntilDay is int until && until > currentDay
                    ? $"I am minding the counter while {matched.OriginalProprietorName} is held by the watch (expected back in {until - currentDay} {(until - currentDay == 1 ? "day" : "days")})."
                    : $"I am minding the counter while {matched.OriginalProprietorName} is held by the watch.",
            _ => null,
        };
    }

    /// <summary>Builds an ambient bark for a kin heir, town replacement, or temporary arrest cover.</summary>
    internal string? BuildAmbientBark(DaggerfallNpc npc, int currentDay) =>
        BuildGreetingLine(npc, currentDay);

    private bool AdvancePremises(PremisesState state, int currentDay) =>
        AdvancePremises(state, currentDay, out _);

    private bool AdvancePremises(PremisesState state, int currentDay, out bool removeRecord)
    {
        removeRecord = false;
        switch (state.State)
        {
            case DaggerfallShopContinuityState.BereavementClosed:
            {
                if (state.ClosedUntilDay is int closeDay && currentDay < closeDay)
                    return false;

                DaggerfallNpc? kin = TryResolveStillEligibleKin(state);
                if (kin is not null)
                {
                    SeatKinInheritance(state, kin);
                    return true;
                }

                if (state.PendingKin is { } savedKin && IsPendingKinStillAliveAndFree(savedKin.ActorId))
                {
                    SeatKinInheritanceFromBlueprint(state, savedKin);
                    return true;
                }

                // All kin died or became unavailable during bereavement: fall back to town replacement vacancy.
                int vacancyEnd = checked(state.EventDay + _tuning.ShopTownReplacementVacancyDays);
                state.State = DaggerfallShopContinuityState.TownReplacementPending;
                state.ClosedUntilDay = vacancyEnd;
                state.ReplacementActorId = null;
                state.ReplacementName = null;
                state.ReplacementOriginKind = null;
                state.PendingKin = null;
                if (currentDay >= vacancyEnd)
                    SeatTownReplacement(state);
                return true;
            }
            case DaggerfallShopContinuityState.TownReplacementPending:
            {
                if (state.ClosedUntilDay is int openDay && currentDay < openDay)
                    return false;
                SeatTownReplacement(state);
                return true;
            }
            case DaggerfallShopContinuityState.ArrestCovered:
            {
                bool originalLost = IsActorDeadOrRemoved(state.OriginalProprietorId)
                    || isActorBanished(state.OriginalProprietorId)
                    || isActorExiled(state.OriginalProprietorId);
                if (originalLost)
                {
                    _ = TransitionLostProprietorToPermanentReplacement(state, currentDay);
                    return true;
                }

                if (state.JailedUntilDay is int releaseDay && currentDay < releaseDay)
                    return false;

                // Release original proprietor back to their counter and end temporary cover cleanly.
                if (state.ReplacementActorId is long coverId)
                {
                    endTemporaryCover(
                        coverId,
                        string.Equals(state.ReplacementOriginKind, "kin", StringComparison.Ordinal),
                        state.PriorCoverRole,
                        state.PriorCoverServices);
                }
                releaseFromCustody(state.OriginalProprietorId, state.ProfileKey);
                removeRecord = true;
                return true;
            }
            default:
                return false;
        }
    }

    private DaggerfallShopPremisesSnapshot TransitionLostProprietorToPermanentReplacement(PremisesState state, int currentDay)
    {
        // If already covered by a living adult kin, promote that kin directly to permanent inheritance.
        if (state.ReplacementActorId is long existingCoverId
            && string.Equals(state.ReplacementOriginKind, "kin", StringComparison.Ordinal)
            && !IsActorDeadOrRemoved(existingCoverId)
            && !IsActorOffStage(existingCoverId))
        {
            state.State = DaggerfallShopContinuityState.KinInherited;
            state.JailedUntilDay = null;
            state.ClosedUntilDay = null;
            state.PriorCoverRole = null;
            state.PriorCoverServices = null;
            return state.ToSnapshot();
        }

        // Release any temporary staff cover before permanent inheritance/replacement.
        if (state.ReplacementActorId is long oldCoverId)
        {
            endTemporaryCover(oldCoverId, string.Equals(state.ReplacementOriginKind, "kin", StringComparison.Ordinal),
                state.PriorCoverRole, state.PriorCoverServices);
            state.ReplacementActorId = null;
            state.ReplacementName = null;
            state.ReplacementOriginKind = null;
            state.PriorCoverRole = null;
            state.PriorCoverServices = null;
        }

        state.JailedUntilDay = null;
        DaggerfallNpc? kin = FindSurvivingAdultKin(state.FamilyId, state.OriginalProprietorId, state.SiteId);
        if (kin is not null)
        {
            SeatKinInheritance(state, kin);
        }
        else
        {
            SeatTownReplacement(state);
        }
        return state.ToSnapshot();
    }

    private void SeatKinInheritance(PremisesState state, DaggerfallNpc kin)
    {
        string kinName = !string.IsNullOrWhiteSpace(kin.DisplayName) ? kin.DisplayName! : kin.Role;
        string[] mergedServices = MergeShopServices(kin.Services, state.OriginalServices);
        state.State = DaggerfallShopContinuityState.KinInherited;
        state.ClosedUntilDay = null;
        state.JailedUntilDay = null;
        state.ReplacementActorId = kin.DurableId;
        state.ReplacementName = kinName;
        state.ReplacementOriginKind = "kin";
        state.PriorCoverRole = null;
        state.PriorCoverServices = null;
        state.PendingKin = null;

        seatReplacement(new DaggerfallShopReplacementSeatRequest(
            ActorId: kin.DurableId,
            IsExistingActor: true,
            ShopProfile: state.ProfileKey,
            TargetSlotId: state.OriginalSlotId,
            OriginSlotKey: kin.StableKey,
            DisplayName: kinName,
            Role: state.OriginalRole,
            Services: mergedServices,
            Race: kin.Appearance.Race,
            Gender: kin.Appearance.Gender,
            BillboardArchive: kin.Appearance.BillboardArchive,
            BillboardRecord: kin.Appearance.BillboardRecord,
            NameSeed: kin.Appearance.NameSeed,
            FactionId: state.OriginalFactionId != 0 ? state.OriginalFactionId : kin.Appearance.FactionId,
            FamilyId: kin.FamilyId ?? state.FamilyId,
            OriginHomeProfile: kin.Profile));
    }

    private void SeatKinInheritanceFromBlueprint(PremisesState state, DaggerfallShopKinBlueprintSave savedKin)
    {
        state.State = DaggerfallShopContinuityState.KinInherited;
        state.ClosedUntilDay = null;
        state.JailedUntilDay = null;
        state.ReplacementActorId = savedKin.ActorId;
        state.ReplacementName = savedKin.DisplayName;
        state.ReplacementOriginKind = "kin";
        state.PriorCoverRole = null;
        state.PriorCoverServices = null;
        state.PendingKin = null;

        DaggerfallWorldProfileKey? homeProfile = !string.IsNullOrWhiteSpace(savedKin.HomeProfileLogicalId)
            && savedKin.HomeProfileKind is int kind
            && savedKin.HomeRegion is int reg
            && savedKin.HomeSiteIndex is int idx
                ? new DaggerfallWorldProfileKey(new DaggerfallSiteId(reg, idx), (DaggerfallWorldProfileKind)kind, savedKin.HomeProfileLogicalId)
                : null;

        seatReplacement(new DaggerfallShopReplacementSeatRequest(
            ActorId: savedKin.ActorId,
            IsExistingActor: true,
            ShopProfile: state.ProfileKey,
            TargetSlotId: state.OriginalSlotId,
            OriginSlotKey: savedKin.SlotKey,
            DisplayName: savedKin.DisplayName,
            Role: state.OriginalRole,
            Services: [.. state.OriginalServices],
            Race: savedKin.Race,
            Gender: savedKin.Gender,
            BillboardArchive: savedKin.BillboardArchive,
            BillboardRecord: savedKin.BillboardRecord,
            NameSeed: savedKin.NameSeed,
            FactionId: state.OriginalFactionId != 0 ? state.OriginalFactionId : savedKin.FactionId,
            FamilyId: savedKin.FamilyId ?? state.FamilyId,
            OriginHomeProfile: homeProfile));
    }

    private static DaggerfallShopKinBlueprintSave CaptureKinBlueprint(DaggerfallNpc kin) => new(
        ActorId: kin.DurableId,
        SlotKey: !string.IsNullOrWhiteSpace(kin.StableKey) ? kin.StableKey : $"kin-{kin.DurableId}",
        DisplayName: !string.IsNullOrWhiteSpace(kin.DisplayName) ? kin.DisplayName! : kin.Role,
        Race: kin.Appearance.Race,
        Gender: kin.Appearance.Gender,
        BillboardArchive: kin.Appearance.BillboardArchive,
        BillboardRecord: kin.Appearance.BillboardRecord,
        NameSeed: kin.Appearance.NameSeed,
        FactionId: kin.Appearance.FactionId,
        FamilyId: kin.FamilyId,
        HomeProfileLogicalId: kin.Profile?.LogicalId,
        HomeProfileKind: kin.Profile is { } hp ? (int)hp.Kind : null,
        HomeRegion: kin.Profile?.Site.Region,
        HomeSiteIndex: kin.Profile?.Site.Index);

    private bool IsPendingKinStillAliveAndFree(long actorId) =>
        actorId > 0
        && actorId != DaggerfallActorIdentity.PlayerEntityId
        && !IsActorDeadOrRemoved(actorId)
        && !IsActorOffStage(actorId)
        && !isActorBanished(actorId)
        && !isActorExiled(actorId);

    private void SeatTownReplacement(PremisesState state)
    {
        string originSlotKey = $"shop-replacement/{state.OriginalSlotId}/g{state.Generation}";
        long replacementId = DaggerfallPeopleSlots.SeededSlotIdentity(state.SiteId, $"{state.ProfileLogicalId}/{originSlotKey}");
        ulong seedHash = StableHash.NonZeroFnv1a64(string.Create(
            CultureInfo.InvariantCulture,
            $"daggerfall.shop-replacement:{worldSocialSeed()}:{state.ProfileLogicalId}:{state.Generation}"));
        int nameSeed = checked((int)(seedHash & 0x7FFF_FFFFUL));
        string gender = (seedHash & 1UL) == 0UL ? "Male" : "Female";
        string replacementFamily = DaggerfallHouseholds.TownPoolHouseholdId(
            worldSocialSeed(),
            state.SiteId,
            $"{state.ProfileLogicalId}/town-replacement/{state.Generation}");
        if (string.Equals(replacementFamily, state.FamilyId, StringComparison.Ordinal))
        {
            replacementFamily = DaggerfallHouseholds.TownPoolHouseholdId(
                worldSocialSeed(),
                state.SiteId,
                $"{state.ProfileLogicalId}/town-replacement/{state.Generation}:alt");
        }
        DaggerfallNpcAppearance replacementLook = new(
            state.OriginalRace, gender, state.OriginalArchive, state.OriginalRecord, nameSeed, state.OriginalFactionId);
        string generatedName = generatePersonName(replacementLook, state.Region, replacementFamily);
        if (string.IsNullOrWhiteSpace(generatedName))
            generatedName = $"Merchant {state.Generation + 1}";

        state.State = DaggerfallShopContinuityState.TownReplaced;
        state.ClosedUntilDay = null;
        state.JailedUntilDay = null;
        state.ReplacementActorId = replacementId;
        state.ReplacementName = generatedName;
        state.ReplacementOriginKind = "town";
        state.PriorCoverRole = null;
        state.PriorCoverServices = null;

        seatReplacement(new DaggerfallShopReplacementSeatRequest(
            ActorId: replacementId,
            IsExistingActor: false,
            ShopProfile: state.ProfileKey,
            TargetSlotId: state.OriginalSlotId,
            OriginSlotKey: originSlotKey,
            DisplayName: generatedName,
            Role: state.OriginalRole,
            Services: [.. state.OriginalServices],
            Race: state.OriginalRace,
            Gender: gender,
            BillboardArchive: state.OriginalArchive,
            BillboardRecord: state.OriginalRecord,
            NameSeed: nameSeed,
            FactionId: state.OriginalFactionId,
            FamilyId: replacementFamily));
    }

    private void AssignArrestCover(PremisesState state, int currentDay)
    {
        DaggerfallNpc? kin = FindSurvivingAdultKin(state.FamilyId, excludeActorId: state.OriginalProprietorId, state.SiteId);
        if (kin is not null)
        {
            string kinName = !string.IsNullOrWhiteSpace(kin.DisplayName) ? kin.DisplayName! : kin.Role;
            string[] mergedServices = MergeShopServices(kin.Services, state.OriginalServices);
            state.ReplacementActorId = kin.DurableId;
            state.ReplacementName = kinName;
            state.ReplacementOriginKind = "kin";
            state.PriorCoverRole = kin.Role;
            state.PriorCoverServices = [.. kin.Services];

            seatReplacement(new DaggerfallShopReplacementSeatRequest(
                ActorId: kin.DurableId,
                IsExistingActor: true,
                ShopProfile: state.ProfileKey,
                TargetSlotId: state.OriginalSlotId,
                OriginSlotKey: kin.StableKey,
                DisplayName: kinName,
                Role: state.OriginalRole,
                Services: mergedServices,
                Race: kin.Appearance.Race,
                Gender: kin.Appearance.Gender,
                BillboardArchive: kin.Appearance.BillboardArchive,
                BillboardRecord: kin.Appearance.BillboardRecord,
                NameSeed: kin.Appearance.NameSeed,
                FactionId: state.OriginalFactionId != 0 ? state.OriginalFactionId : kin.Appearance.FactionId,
                FamilyId: kin.FamilyId ?? state.FamilyId));
            return;
        }

        string originSlotKey = $"arrest-cover/{state.OriginalSlotId}/d{currentDay}/g{state.Generation}";
        long coverId = DaggerfallPeopleSlots.SeededSlotIdentity(state.SiteId, $"{state.ProfileLogicalId}/{originSlotKey}");
        ulong seedHash = StableHash.NonZeroFnv1a64(string.Create(
            CultureInfo.InvariantCulture,
            $"daggerfall.shop-arrest-cover:{worldSocialSeed()}:{state.ProfileLogicalId}:{currentDay}:{state.Generation}"));
        int nameSeed = checked((int)(seedHash & 0x7FFF_FFFFUL));
        string gender = (seedHash & 1UL) == 0UL ? "Male" : "Female";
        DaggerfallNpcAppearance coverLook = new(
            state.OriginalRace, gender, state.OriginalArchive, state.OriginalRecord, nameSeed, state.OriginalFactionId);
        string coverName = generatePersonName(coverLook, state.Region, null);
        if (string.IsNullOrWhiteSpace(coverName))
            coverName = "Counter Clerk";

        state.ReplacementActorId = coverId;
        state.ReplacementName = coverName;
        state.ReplacementOriginKind = "arrest-cover";
        state.PriorCoverRole = null;
        state.PriorCoverServices = null;

        seatReplacement(new DaggerfallShopReplacementSeatRequest(
            ActorId: coverId,
            IsExistingActor: false,
            ShopProfile: state.ProfileKey,
            TargetSlotId: state.OriginalSlotId,
            OriginSlotKey: originSlotKey,
            DisplayName: coverName,
            Role: state.OriginalRole,
            Services: [.. state.OriginalServices],
            Race: state.OriginalRace,
            Gender: gender,
            BillboardArchive: state.OriginalArchive,
            BillboardRecord: state.OriginalRecord,
            NameSeed: nameSeed,
            FactionId: state.OriginalFactionId,
            FamilyId: null));
    }

    private DaggerfallNpc? TryResolveStillEligibleKin(PremisesState state)
    {
        if (state.ReplacementActorId is long pendingKinId
            && npcs.TryGet(pendingKinId, out DaggerfallNpc? pendingKin)
            && IsEligibleLivingAdultKin(pendingKin, state.FamilyId, state.OriginalProprietorId, state.SiteId, allowCurrentId: pendingKinId))
        {
            return pendingKin;
        }
        return FindSurvivingAdultKin(state.FamilyId, state.OriginalProprietorId, state.SiteId);
    }

    private DaggerfallNpc? FindSurvivingAdultKin(string? familyId, long excludeActorId, DaggerfallSiteId siteId)
    {
        if (string.IsNullOrWhiteSpace(familyId) || !DaggerfallHouseholds.IsValidFamilyId(familyId))
            return null;

        foreach (DaggerfallNpc candidate in npcs.All.OrderBy(n => n.DurableId))
        {
            if (IsEligibleLivingAdultKin(candidate, familyId, excludeActorId, siteId))
                return candidate;
        }
        return null;
    }

    private bool IsEligibleLivingAdultKin(
        DaggerfallNpc candidate,
        string? familyId,
        long excludeActorId,
        DaggerfallSiteId siteId,
        long? allowCurrentId = null)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            return false;
        if (candidate.DurableId <= 0 || candidate.DurableId == DaggerfallActorIdentity.PlayerEntityId || candidate.DurableId == excludeActorId)
            return false;
        string? candidateFamilyId = ResolveCandidateFamilyId(candidate);
        if (!string.Equals(candidateFamilyId, familyId, StringComparison.Ordinal))
            return false;
        if (candidate.Presence == DaggerfallNpcPresence.Removed)
            return false;
        if (DaggerfallNpcServiceFacts.IsChild(candidate.Appearance))
            return false;
        if (IsActorDeadOrRemoved(candidate.DurableId) || IsActorOffStage(candidate.DurableId)
            || isActorBanished(candidate.DurableId) || isActorExiled(candidate.DurableId))
            return false;
        if (DaggerfallHouseholds.IsLocal(familyId) && candidate.Site.Region != siteId.Region)
            return false;

        foreach (PremisesState other in _premises.Values)
        {
            if (other.ReplacementActorId == candidate.DurableId
                && candidate.DurableId != allowCurrentId
                && other.State is DaggerfallShopContinuityState.KinInherited or DaggerfallShopContinuityState.ArrestCovered)
            {
                return false;
            }
        }
        return true;
    }

    private string? ResolveCandidateFamilyId(DaggerfallNpc candidate)
    {
        if (actors.TryGet(candidate.DurableId, out ActorState? live)
            && live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
            && !string.IsNullOrWhiteSpace(overlay.FamilyId))
        {
            return overlay.FamilyId;
        }
        return candidate.FamilyId;
    }

    private bool IsActorDeadOrRemoved(long actorId) =>
        actorLedger.TryGet(actorId, out ActorLedgerEntry? entry)
        && entry?.Location?.Kind is ActorLocationKind.Dead or ActorLocationKind.Removed;

    private bool IsActorOffStage(long actorId) =>
        actorLedger.TryGet(actorId, out ActorLedgerEntry? entry)
        && entry?.Location?.Kind == ActorLocationKind.OffStage;

    private PremisesState? FindPremisesByReplacement(long actorId)
    {
        foreach (PremisesState state in _premises.Values)
        {
            if (state.ReplacementActorId == actorId
                && state.State is DaggerfallShopContinuityState.KinInherited
                    or DaggerfallShopContinuityState.TownReplaced
                    or DaggerfallShopContinuityState.ArrestCovered)
            {
                return state;
            }
        }
        return null;
    }

    private static bool TryResolveProprietorPremises(
        DaggerfallNpc npc,
        DaggerfallWorldProfileKey activeProfile,
        DaggerfallSiteProfile? profileObj,
        out DaggerfallWorldProfileKey shopProfile,
        out DaggerfallSiteBuildingId buildingId,
        out int buildingType,
        out string slotId)
    {
        shopProfile = default;
        buildingId = default;
        buildingType = -1;
        slotId = !string.IsNullOrWhiteSpace(npc.StableKey) ? npc.StableKey : $"npc-{npc.DurableId}";

        string? candidateLogicalId = !string.IsNullOrWhiteSpace(npc.Site.ProfileId)
            ? npc.Site.ProfileId
            : npc.Profile?.Kind == DaggerfallWorldProfileKind.Interior
                ? npc.Profile.Value.LogicalId
                : activeProfile.Kind == DaggerfallWorldProfileKind.Interior
                    ? activeProfile.LogicalId
                    : null;

        if (string.IsNullOrWhiteSpace(candidateLogicalId)
            || !DaggerfallWorldProfileIds.TryParse(candidateLogicalId, out DaggerfallWorldProfileKey parsedProfile, out DaggerfallSiteBuildingId? parsedBuilding)
            || parsedBuilding is not { } resolvedBuilding)
        {
            return false;
        }

        shopProfile = DaggerfallWorldProfileIds.Interior(parsedProfile.Site, resolvedBuilding);
        buildingId = resolvedBuilding;

        if (profileObj is not null
            && profileObj.ProfileKey == shopProfile
            && profileObj.InteriorBuilding is { } ib)
        {
            buildingType = ib.BuildingType;
        }
        else
        {
            // When arrested off-site (e.g. while walking outdoors), infer building category from the proprietor's canonical services.
            buildingType = npc.Services.Contains(DaggerfallNpcServiceFacts.TavernService, StringComparer.Ordinal)
                ? 15
                : npc.Services.Contains("banking", StringComparer.Ordinal)
                    ? 3
                    : 6; // GeneralStore shop category
        }

        return true;
    }

    private static string[] MergeShopServices(IReadOnlyList<string> existingServices, IReadOnlyList<string> shopServices)
    {
        List<string> merged = [];
        foreach (string s in existingServices)
        {
            if (!merged.Contains(s, StringComparer.Ordinal))
                merged.Add(s);
        }
        foreach (string s in shopServices)
        {
            if (!merged.Contains(s, StringComparer.Ordinal))
                merged.Add(s);
        }
        return [.. merged];
    }
}
