using System.Globalization;
using System.Text.Json.Serialization;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Lifecycle state of a sealed dispatch carrying a political board-game action or warrant.</summary>
internal enum DaggerfallDispatchStatus
{
    /// <summary>Courier is riding along the road from origin to destination.</summary>
    InTransit = 0,

    /// <summary>Player took custody of the sealed dispatch to deliver it personally.</summary>
    CarriedByPlayer = 1,

    /// <summary>Dispatch arrived at its destination (by courier on schedule or accelerated by the player).</summary>
    Delivered = 2,

    /// <summary>Player paid to buy/inspect the dispatch, revealing the move before resolution.</summary>
    BoughtAndRevealed = 3,

    /// <summary>Player intercepted/seized the dispatch or slew the courier, cancelling the move.</summary>
    Intercepted = 4,

    /// <summary>The courier was lost on the road (raided, or scattered in a clash): the move never arrives (#11178).</summary>
    Waylaid = 5,
}

/// <summary>
/// Typed outcome emitted whenever a board-game courier dispatch settles or is altered by the player.
/// Consumed by Track B's weekly board game so delivering, buying, or intercepting a dispatch changes that move.
/// </summary>
internal sealed record DaggerfallDispatchResolution(
    [property: JsonRequired] string DispatchId,
    [property: JsonRequired] string SeatId,
    [property: JsonRequired] string ActionKind,
    [property: JsonRequired] DaggerfallDispatchStatus Outcome,
    [property: JsonRequired] int ResolvedDay,
    bool MoveCancelled = false,
    bool MoveAccelerated = false,
    bool MoveRevealed = false,
    bool DeliveredByPlayer = false)
{
    internal DaggerfallDispatchResolution Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SeatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ActionKind);
        if (!Enum.IsDefined(Outcome))
            throw new ArgumentOutOfRangeException(nameof(Outcome), Outcome, $"Dispatch resolution '{DispatchId}' names an invalid status.");
        if (ResolvedDay < 0)
            throw new ArgumentOutOfRangeException(nameof(ResolvedDay), ResolvedDay, $"Dispatch resolution '{DispatchId}' has a negative resolved day.");
        return this;
    }
}

/// <summary>One sparse persisted sealed courier dispatch carrying a pending board-game action or warrant.</summary>
internal sealed record DaggerfallCourierDispatchSave(
    [property: JsonRequired] string DispatchId,
    [property: JsonRequired] string SeatId,
    [property: JsonRequired] string ActionKind,
    [property: JsonRequired] string Summary,
    [property: JsonRequired] DaggerfallSiteIdSave OriginSite,
    [property: JsonRequired] DaggerfallSiteIdSave DestinationSite,
    [property: JsonRequired] long DispatchedAtMinute,
    [property: JsonRequired] long ExpectedArrivalMinute,
    [property: JsonRequired] DaggerfallDispatchStatus Status,
    int? SourceFactionId = null,
    int? TargetFactionId = null,
    int BribePriceGold = 150,
    int DeliveryRewardGold = 100,
    string? LinkedWavefrontId = null,
    string? LinkedConditionTokenId = null,
    string? OverlandBandId = null,
    long? CourierActorId = null,
    bool DeliveredByPlayer = false,
    bool Revealed = false,
    long? ResolvedMinute = null)
{
    internal DaggerfallCourierDispatchSave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SeatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ActionKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(Summary);
        ArgumentNullException.ThrowIfNull(OriginSite);
        ArgumentNullException.ThrowIfNull(DestinationSite);
        _ = OriginSite.Require();
        _ = DestinationSite.Require();
        if (!Enum.IsDefined(Status))
            throw new ArgumentOutOfRangeException(nameof(Status), Status, $"Courier dispatch '{DispatchId}' names an invalid status.");
        if (DispatchedAtMinute < 0 || ExpectedArrivalMinute < DispatchedAtMinute)
            throw new ArgumentOutOfRangeException(nameof(ExpectedArrivalMinute), $"Courier dispatch '{DispatchId}' has invalid dispatch/arrival timestamps.");
        if (SourceFactionId is < 0)
            throw new ArgumentOutOfRangeException(nameof(SourceFactionId));
        if (TargetFactionId is < 0)
            throw new ArgumentOutOfRangeException(nameof(TargetFactionId));
        if (BribePriceGold < 0 || DeliveryRewardGold < 0)
            throw new ArgumentOutOfRangeException(nameof(BribePriceGold));
        if (CourierActorId is <= 0 or DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(CourierActorId));
        return this;
    }
}

/// <summary>Sparse persisted courier-dispatch snapshot. Empty when no board-game dispatches are tracked.</summary>
internal sealed record DaggerfallCourierDispatchesSave(
    DaggerfallCourierDispatchSave[] Dispatches,
    DaggerfallDispatchResolution[] Resolutions)
{
    internal static DaggerfallCourierDispatchesSave Empty { get; } = new([], []);

    internal DaggerfallCourierDispatchesSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Dispatches);
        ArgumentNullException.ThrowIfNull(Resolutions);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallCourierDispatchSave dispatch in Dispatches)
        {
            ArgumentNullException.ThrowIfNull(dispatch);
            dispatch.Validate();
            if (!ids.Add(dispatch.DispatchId))
                throw new ArgumentException($"Saved courier dispatches repeat id '{dispatch.DispatchId}'.", nameof(Dispatches));
        }
        foreach (DaggerfallDispatchResolution res in Resolutions)
        {
            ArgumentNullException.ThrowIfNull(res);
            res.Validate();
        }
        return this;
    }
}

/// <summary>Outcome of a player dialogue/action on a courier dispatch.</summary>
internal sealed record DaggerfallDispatchActionResult(
    bool Accepted,
    string Message,
    DaggerfallCourierDispatchSave? Dispatch = null,
    DaggerfallDispatchResolution? Resolution = null);

/// <summary>
/// Manages sealed couriers carrying pending political board-game actions or warrants (Patch Q10).
/// Couriers ride along real overland routes (<see cref="DaggerfallOverlandPartyCoordinator"/>) with elapsed-time
/// arrival modified by regional <see cref="DaggerfallWordSpread"/> disruption, and let the player take part
/// (carry &amp; deliver to accelerate), buy/inspect (reveal early), or intercept/kill (cancel the move and freeze
/// linked wavefronts).
/// </summary>
internal sealed class DaggerfallCourierDispatches(
    DaggerfallDefinitions definitions,
    DaggerfallWordSpread wordSpread,
    DaggerfallWorldConditions worldConditions,
    DaggerfallSocialState social,
    DaggerfallActorLedger actorLedger,
    Func<int, bool> trySpendGold,
    Action<int> grantGold,
    Func<ulong> worldSocialSeed,
    Func<DaggerfallNpcAppearance, int, string>? generateName = null,
    Func<DaggerfallOverlandPartyCoordinator?>? overlandParties = null,
    Func<DaggerfallExpedition?>? expedition = null)
{
    internal const string DispatchBandPrefix = "dispatch:";
    internal const int DefaultBribePriceGold = 150;
    internal const int DefaultDeliveryRewardGold = 100;
    internal const int DeliveryReputationBonus = 5;

    private readonly Dictionary<string, DaggerfallCourierDispatchSave> _dispatches = new(StringComparer.Ordinal);
    private readonly List<DaggerfallDispatchResolution> _resolutions = [];
    private Action<DaggerfallDispatchResolution>? _onBoardMoveResolved;

    internal int Count => _dispatches.Count;
    internal IReadOnlyCollection<DaggerfallCourierDispatchSave> All => _dispatches.Values;
    internal IReadOnlyList<DaggerfallDispatchResolution> Resolutions => _resolutions;

    /// <summary>Binds Track B's weekly board-game consumer to receive player-modified dispatch outcomes.</summary>
    internal void BindBoardMoveCallback(Action<DaggerfallDispatchResolution>? callback) =>
        _onBoardMoveResolved = callback;

    internal bool TryGet(string dispatchId, out DaggerfallCourierDispatchSave? dispatch) =>
        _dispatches.TryGetValue(dispatchId, out dispatch);

    /// <summary>Returns an active dispatch currently carried by <paramref name="courierActorId"/> (for dialogue/death hooks).</summary>
    internal DaggerfallCourierDispatchSave? TryGetForCourier(long courierActorId) =>
        _dispatches.Values
            .Where(d => d.CourierActorId == courierActorId
                && d.Status is DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.BoughtAndRevealed)
            .OrderByDescending(d => d.DispatchedAtMinute)
            .FirstOrDefault();

    /// <summary>Returns a player-carried dispatch addressed to <paramref name="site"/> (exact destination site match).</summary>
    internal DaggerfallCourierDispatchSave? TryGetPlayerCarriedFor(DaggerfallSiteRecord site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return _dispatches.Values
            .Where(d => d.Status == DaggerfallDispatchStatus.CarriedByPlayer
                && d.DestinationSite.Require() == site.Id)
            .OrderBy(d => d.DispatchedAtMinute)
            .FirstOrDefault();
    }

    /// <summary>Returns all active dispatches currently carried by the player.</summary>
    internal IReadOnlyList<DaggerfallCourierDispatchSave> PlayerCarriedDispatches() =>
        [.. _dispatches.Values
            .Where(d => d.Status == DaggerfallDispatchStatus.CarriedByPlayer)
            .OrderBy(d => d.DispatchedAtMinute)];

    /// <summary>
    /// Stages a new sealed dispatch carrying a pending board-game move from <paramref name="origin"/> to
    /// <paramref name="destination"/>. Computes arrival from elapsed road distance and courier disruption, and
    /// optionally dispatches a live persistent <see cref="ActorLocationKind.OnOverlandBand"/> courier actor on the
    /// expedition map so the player can physically intercept or talk to them.
    /// </summary>
    internal DaggerfallCourierDispatchSave StageDispatch(
        string dispatchId,
        string seatId,
        string actionKind,
        string summary,
        DaggerfallSiteRecord origin,
        DaggerfallSiteRecord destination,
        long currentMinute,
        int currentDay,
        int? sourceFactionId = null,
        int? targetFactionId = null,
        int bribePriceGold = DefaultBribePriceGold,
        int deliveryRewardGold = DefaultDeliveryRewardGold,
        string? linkedWavefrontId = null,
        string? linkedConditionTokenId = null,
        long? courierActorId = null,
        bool stageOverlandBand = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(seatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);

        double routeDistance = wordSpread.RouteDistancePixels(
            origin.MapPixelX, origin.MapPixelY, destination.MapPixelX, destination.MapPixelY);
        // A courier riding a band meets the road's dangers as it goes (#11178); one with no band has them priced into its speed.
        bool riding = stageOverlandBand && overlandParties?.Invoke() is not null;
        double disruption = riding ? 1d : wordSpread.CourierDisruption(origin.Id.Region, destination.Id.Region, currentDay).Multiplier;
        double effectiveSpeed = wordSpread.Tuning.CourierSpeedPixelsPerHour * disruption;
        long travelMinutes = Math.Max(60L, checked((long)Math.Ceiling((routeDistance / effectiveSpeed) * 60d)));
        long expectedArrival = checked(currentMinute + travelMinutes);
        // Set the persistent band's flat-road equivalent speed so traversing the terrain-aware overland route
        // arrives in exact lockstep with ExpectedArrivalMinute.
        double travelHours = travelMinutes / 60d;
        double bandSpeedPixelsPerHour = routeDistance > 0d
            ? Math.Max(1e-6, routeDistance / travelHours)
            : effectiveSpeed;

        string? overlandBandId = null;
        long? resolvedCourierId = courierActorId;

        if (stageOverlandBand && overlandParties?.Invoke() is { } coordinator)
        {
            resolvedCourierId ??= StageCourierActor(origin, dispatchId, sourceFactionId);
            if (resolvedCourierId is long cid)
            {
                string bandId = $"{DispatchBandPrefix}{dispatchId}";
                DaggerfallTravelMapPixel destPixel = new(destination.MapPixelX, destination.MapPixelY);
                DaggerfallWorldProfileKey destProfile = DaggerfallWorldProfileIds.Exterior(destination.Id);
                string seatLabel = definitions.BoardSeats.TryGetBySeatId(seatId, out DaggerfallBoardSeatBinding? seat) && seat is not null
                    ? seat.SeatName
                    : seatId;
                _ = coordinator.DispatchOverlandBand(
                    bandId: bandId,
                    name: $"{seatLabel} Envoy",
                    kind: DaggerfallBandKind.Courier,
                    memberIds: [cid],
                    startX: origin.MapPixelX + 0.5,
                    startY: origin.MapPixelY + 0.5,
                    speedPixelsPerHour: bandSpeedPixelsPerHour,
                    destination: new DaggerfallExpeditionMark(destPixel, destination.Id),
                    destinationProfile: destProfile,
                    camp: false,
                    hostile: false,
                    faction: seatLabel,
                    originSite: origin.Id);
                overlandBandId = bandId;
            }
        }

        DaggerfallCourierDispatchSave dispatch = new(
            DispatchId: dispatchId,
            SeatId: seatId,
            ActionKind: actionKind,
            Summary: summary,
            OriginSite: new DaggerfallSiteIdSave(origin.Id.Region, origin.Id.Index),
            DestinationSite: new DaggerfallSiteIdSave(destination.Id.Region, destination.Id.Index),
            DispatchedAtMinute: currentMinute,
            ExpectedArrivalMinute: expectedArrival,
            Status: DaggerfallDispatchStatus.InTransit,
            SourceFactionId: sourceFactionId,
            TargetFactionId: targetFactionId,
            BribePriceGold: bribePriceGold,
            DeliveryRewardGold: deliveryRewardGold,
            LinkedWavefrontId: linkedWavefrontId,
            LinkedConditionTokenId: linkedConditionTokenId,
            OverlandBandId: overlandBandId,
            CourierActorId: resolvedCourierId);
        dispatch.Validate();
        _dispatches[dispatchId] = dispatch;
        return dispatch;
    }

    /// <summary>
    /// Advances elapsed calendar time: any <see cref="DaggerfallDispatchStatus.InTransit"/> or
    /// <see cref="DaggerfallDispatchStatus.BoughtAndRevealed"/> courier whose <see cref="DaggerfallCourierDispatchSave.ExpectedArrivalMinute"/>
    /// has passed settles as <see cref="DaggerfallDispatchStatus.Delivered"/> automatically (zero off-screen stepping).
    /// </summary>
    internal IReadOnlyList<DaggerfallDispatchResolution> AdvanceToMinute(long currentMinute, int currentDay)
    {
        if (_dispatches.Count == 0)
            return [];

        List<DaggerfallDispatchResolution> newlyResolved = [];
        foreach (DaggerfallCourierDispatchSave dispatch in _dispatches.Values.ToArray())
        {
            if (dispatch.Status is not (DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.BoughtAndRevealed))
                continue;
            if (currentMinute < dispatch.ExpectedArrivalMinute)
                continue;

            DaggerfallCourierDispatchSave delivered = dispatch with
            {
                Status = DaggerfallDispatchStatus.Delivered,
                DeliveredByPlayer = false,
                OverlandBandId = null,
                ResolvedMinute = currentMinute,
            };
            _dispatches[dispatch.DispatchId] = delivered;
            RetireOverlandBand(dispatch.OverlandBandId, dispatch.CourierActorId);

            DaggerfallDispatchResolution resolution = RecordResolution(new(
                DispatchId: dispatch.DispatchId,
                SeatId: dispatch.SeatId,
                ActionKind: dispatch.ActionKind,
                Outcome: DaggerfallDispatchStatus.Delivered,
                ResolvedDay: currentDay,
                MoveCancelled: false,
                MoveAccelerated: false,
                MoveRevealed: dispatch.Revealed,
                DeliveredByPlayer: false));
            newlyResolved.Add(resolution);
        }
        return newlyResolved;
    }

    /// <summary>
    /// Settles an active dispatch immediately when its live <see cref="ActorLocationKind.OnOverlandBand"/> courier
    /// reaches the addressed destination during overland expedition travel, keeping physical band arrival and
    /// dispatch resolution synchronized (idempotent with <see cref="AdvanceToMinute"/>).
    /// </summary>
    internal DaggerfallDispatchResolution? OnOverlandBandArrived(string bandId, long currentMinute, int currentDay)
    {
        if (string.IsNullOrWhiteSpace(bandId) || _dispatches.Count == 0)
            return null;

        DaggerfallCourierDispatchSave? dispatch = _dispatches.Values.FirstOrDefault(d =>
            StringComparer.Ordinal.Equals(d.OverlandBandId, bandId)
            && d.Status is DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.BoughtAndRevealed);
        if (dispatch is null)
            return null;

        DaggerfallCourierDispatchSave delivered = dispatch with
        {
            Status = DaggerfallDispatchStatus.Delivered,
            DeliveredByPlayer = false,
            OverlandBandId = null,
            ResolvedMinute = currentMinute,
        };
        _dispatches[dispatch.DispatchId] = delivered;
        RetireCourierLedgerMember(bandId, dispatch.CourierActorId);

        return RecordResolution(new(
            DispatchId: dispatch.DispatchId,
            SeatId: dispatch.SeatId,
            ActionKind: dispatch.ActionKind,
            Outcome: DaggerfallDispatchStatus.Delivered,
            ResolvedDay: currentDay,
            MoveCancelled: false,
            MoveAccelerated: false,
            MoveRevealed: dispatch.Revealed,
            DeliveredByPlayer: false));
    }

    /// <summary>
    /// Puts back an active dispatch's arrival by <paramref name="minutes"/> when its courier's band is delayed on the road
    /// (#11178), so its schedule keeps step with the band.
    /// </summary>
    internal void OnOverlandBandDelayed(string bandId, long minutes)
    {
        if (ActiveOnBand(bandId) is { } dispatch)
            _dispatches[dispatch.DispatchId] = dispatch with { ExpectedArrivalMinute = checked(dispatch.ExpectedArrivalMinute + minutes) };
    }

    /// <summary>
    /// Settles an active dispatch whose courier's band was lost on its way (raided on the road, or scattered in a clash
    /// off-screen, #11178): its move never arrives, as an intercepted one does not, though no one holds the pouch.
    /// </summary>
    internal DaggerfallDispatchResolution? OnOverlandBandLost(string bandId, long currentMinute, int currentDay)
    {
        if (ActiveOnBand(bandId) is not { } dispatch) return null;
        _dispatches[dispatch.DispatchId] = dispatch with
        {
            Status = DaggerfallDispatchStatus.Waylaid,
            OverlandBandId = null,
            ResolvedMinute = currentMinute,
        };
        RetireCourierLedgerMember(bandId, dispatch.CourierActorId);
        if (!string.IsNullOrWhiteSpace(dispatch.LinkedWavefrontId))
            wordSpread.InterceptWavefront(dispatch.LinkedWavefrontId, currentMinute, currentDay);
        if (!string.IsNullOrWhiteSpace(dispatch.LinkedConditionTokenId))
            worldConditions.Remove(dispatch.LinkedConditionTokenId);
        return RecordResolution(new(
            DispatchId: dispatch.DispatchId,
            SeatId: dispatch.SeatId,
            ActionKind: dispatch.ActionKind,
            Outcome: DaggerfallDispatchStatus.Waylaid,
            ResolvedDay: currentDay,
            MoveCancelled: true,
            MoveRevealed: dispatch.Revealed));
    }

    private DaggerfallCourierDispatchSave? ActiveOnBand(string bandId) =>
        string.IsNullOrWhiteSpace(bandId) ? null : _dispatches.Values.FirstOrDefault(d =>
            StringComparer.Ordinal.Equals(d.OverlandBandId, bandId)
            && d.Status is DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.BoughtAndRevealed);

    /// <summary>Player takes part by offering to carry the sealed dispatch personally to its destination.</summary>
    internal DaggerfallDispatchActionResult AcceptDelivery(string dispatchId, long currentMinute)
    {
        if (!_dispatches.TryGetValue(dispatchId, out DaggerfallCourierDispatchSave? dispatch))
            return new(false, "That dispatch is no longer active.");
        if (dispatch.Status is not (DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.BoughtAndRevealed))
            return new(false, "That dispatch can no longer be taken for delivery.");

        DaggerfallCourierDispatchSave updated = dispatch with
        {
            Status = DaggerfallDispatchStatus.CarriedByPlayer,
            OverlandBandId = null,
        };
        _dispatches[dispatchId] = updated;
        RetireOverlandBand(dispatch.OverlandBandId, dispatch.CourierActorId);

        string destName = ResolveSiteName(dispatch.DestinationSite.Require());
        return new(true,
            $"You take the sealed pouch ({dispatch.Summary}) to deliver to {destName} for {dispatch.DeliveryRewardGold} gold.",
            updated);
    }

    /// <summary>Completes player delivery at the destination town, accelerating the move and paying the reward.</summary>
    internal DaggerfallDispatchActionResult TurnInDelivery(
        string dispatchId,
        DaggerfallSiteRecord atSite,
        long currentMinute,
        int currentDay)
    {
        ArgumentNullException.ThrowIfNull(atSite);
        if (!_dispatches.TryGetValue(dispatchId, out DaggerfallCourierDispatchSave? dispatch))
            return new(false, "That dispatch is no longer active.");
        if (dispatch.Status != DaggerfallDispatchStatus.CarriedByPlayer)
            return new(false, "You are not carrying that dispatch.");

        DaggerfallSiteId destId = dispatch.DestinationSite.Require();
        if (atSite.Id != destId)
            return new(false, $"That dispatch is addressed to {ResolveSiteName(destId)}, not {atSite.Name}.");

        DaggerfallCourierDispatchSave delivered = dispatch with
        {
            Status = DaggerfallDispatchStatus.Delivered,
            DeliveredByPlayer = true,
            ResolvedMinute = currentMinute,
        };
        _dispatches[dispatchId] = delivered;

        if (dispatch.DeliveryRewardGold > 0)
            grantGold(dispatch.DeliveryRewardGold);
        if (dispatch.SourceFactionId is int sf && sf > 0 && definitions.Factions.Factions.ContainsKey(sf))
            social.ChangeFactionReputation(sf, DeliveryReputationBonus, DaggerfallFactionReputationChange.Direct);
        if (!string.IsNullOrWhiteSpace(dispatch.LinkedWavefrontId))
            wordSpread.AccelerateInRegion(dispatch.LinkedWavefrontId, destId.Region);

        DaggerfallDispatchResolution resolution = RecordResolution(new(
            DispatchId: dispatch.DispatchId,
            SeatId: dispatch.SeatId,
            ActionKind: dispatch.ActionKind,
            Outcome: DaggerfallDispatchStatus.Delivered,
            ResolvedDay: currentDay,
            MoveCancelled: false,
            MoveAccelerated: true,
            MoveRevealed: dispatch.Revealed,
            DeliveredByPlayer: true));

        return new(true,
            $"Dispatch delivered ({dispatch.Summary}). You receive {dispatch.DeliveryRewardGold} gold; word moves ahead of schedule.",
            delivered,
            resolution);
    }

    /// <summary>Player buys/bribes the courier to inspect the sealed dispatch, revealing the pending board-game move early.</summary>
    internal DaggerfallDispatchActionResult BuyDispatch(string dispatchId, long currentMinute, int currentDay)
    {
        if (!_dispatches.TryGetValue(dispatchId, out DaggerfallCourierDispatchSave? dispatch))
            return new(false, "That dispatch is no longer active.");
        if (dispatch.Status is not (DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.CarriedByPlayer))
            return new(false, "That dispatch is no longer available to buy.");
        if (dispatch.Revealed)
            return new(true, $"You already know its contents: {dispatch.Summary}", dispatch);
        if (dispatch.BribePriceGold > 0 && !trySpendGold(dispatch.BribePriceGold))
            return new(false, $"The courier demands {dispatch.BribePriceGold} gold to break the seal.");

        DaggerfallCourierDispatchSave updated = dispatch with
        {
            Status = dispatch.Status == DaggerfallDispatchStatus.CarriedByPlayer
                ? DaggerfallDispatchStatus.CarriedByPlayer
                : DaggerfallDispatchStatus.BoughtAndRevealed,
            Revealed = true,
        };
        _dispatches[dispatchId] = updated;

        // Publish the leaked intelligence into the local wavefront so notice boards and talkers can gossip about it!
        DaggerfallSiteRecord? originSite = FindSite(dispatch.OriginSite.Require());
        if (originSite is not null)
        {
            wordSpread.RecordNews(
                eventId: $"wf:leak:{dispatch.DispatchId}",
                headline: $"Leaked Dispatch ({dispatch.SeatId})",
                body: $"Leaked word of a sealed move: {dispatch.Summary}",
                originSite: originSite,
                currentMinute: currentMinute,
                factionId: dispatch.SourceFactionId);
            wordSpread.AccelerateInRegion($"wf:leak:{dispatch.DispatchId}", dispatch.OriginSite.Require().Region);
            wordSpread.AccelerateInRegion($"wf:leak:{dispatch.DispatchId}", dispatch.DestinationSite.Require().Region);
        }

        DaggerfallDispatchResolution resolution = RecordResolution(new(
            DispatchId: dispatch.DispatchId,
            SeatId: dispatch.SeatId,
            ActionKind: dispatch.ActionKind,
            Outcome: DaggerfallDispatchStatus.BoughtAndRevealed,
            ResolvedDay: currentDay,
            MoveCancelled: false,
            MoveAccelerated: false,
            MoveRevealed: true,
            DeliveredByPlayer: false));

        return new(true,
            $"You pay {dispatch.BribePriceGold} gold and read the sealed dispatch: {dispatch.Summary}",
            updated,
            resolution);
    }

    /// <summary>
    /// Player intercepts/seizes the dispatch (or kills the courier), cancelling the pending board-game move,
    /// freezing any linked wavefront, and stripping any linked pending condition token.
    /// </summary>
    internal DaggerfallDispatchActionResult InterceptDispatch(
        string dispatchId,
        long currentMinute,
        int currentDay,
        bool courierSlain = false)
    {
        if (!_dispatches.TryGetValue(dispatchId, out DaggerfallCourierDispatchSave? dispatch))
            return new(false, "That dispatch is no longer active.");
        if (dispatch.Status is DaggerfallDispatchStatus.Delivered or DaggerfallDispatchStatus.Intercepted or DaggerfallDispatchStatus.Waylaid)
            return new(false, "That dispatch has already resolved.");

        DaggerfallCourierDispatchSave intercepted = dispatch with
        {
            Status = DaggerfallDispatchStatus.Intercepted,
            Revealed = true,
            OverlandBandId = null,
            ResolvedMinute = currentMinute,
        };
        _dispatches[dispatchId] = intercepted;
        RetireOverlandBand(dispatch.OverlandBandId, dispatch.CourierActorId);

        if (!string.IsNullOrWhiteSpace(dispatch.LinkedWavefrontId))
            wordSpread.InterceptWavefront(dispatch.LinkedWavefrontId, currentMinute, currentDay);
        if (!string.IsNullOrWhiteSpace(dispatch.LinkedConditionTokenId))
            worldConditions.Remove(dispatch.LinkedConditionTokenId);

        DaggerfallDispatchResolution resolution = RecordResolution(new(
            DispatchId: dispatch.DispatchId,
            SeatId: dispatch.SeatId,
            ActionKind: dispatch.ActionKind,
            Outcome: DaggerfallDispatchStatus.Intercepted,
            ResolvedDay: currentDay,
            MoveCancelled: true,
            MoveAccelerated: false,
            MoveRevealed: true,
            DeliveredByPlayer: false));

        string message = courierSlain
            ? $"The slain courier carried a sealed dispatch ({dispatch.Summary}); the move has been halted."
            : $"You seize the sealed dispatch ({dispatch.Summary}); the move will not reach {ResolveSiteName(dispatch.DestinationSite.Require())}.";
        return new(true, message, intercepted, resolution);
    }

    /// <summary>Reacts to a live courier dying in play: cancels any active dispatch they carried and freezes its wavefront.</summary>
    internal IReadOnlyList<DaggerfallDispatchResolution> ReactToCourierDeath(long deadActorId, long currentMinute, int currentDay)
    {
        if (deadActorId <= 0 || deadActorId == DaggerfallActorIdentity.PlayerEntityId)
            return [];

        List<DaggerfallDispatchResolution> results = [];
        foreach (DaggerfallCourierDispatchSave dispatch in _dispatches.Values.ToArray())
        {
            if (dispatch.CourierActorId != deadActorId) continue;
            if (dispatch.Status is not (DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.BoughtAndRevealed)) continue;
            DaggerfallDispatchActionResult intercepted = InterceptDispatch(dispatch.DispatchId, currentMinute, currentDay, courierSlain: true);
            if (intercepted.Resolution is { } res)
                results.Add(res);
        }
        return results;
    }

    internal DaggerfallCourierDispatchesSave Capture() =>
        new(
            [.. _dispatches.Values.OrderBy(d => d.DispatchId, StringComparer.Ordinal)],
            [.. _resolutions]);

    internal void Restore(DaggerfallCourierDispatchesSave? saved)
    {
        _dispatches.Clear();
        _resolutions.Clear();
        if (saved is null)
            return;
        saved.Validate();
        foreach (DaggerfallCourierDispatchSave dispatch in saved.Dispatches)
            _dispatches[dispatch.DispatchId] = dispatch;
        _resolutions.AddRange(saved.Resolutions);
    }

    /// <summary>Forgets a settled dispatch and its resolutions once the board has taken what it needed of them.</summary>
    internal bool Forget(string dispatchId)
    {
        if (!_dispatches.TryGetValue(dispatchId, out DaggerfallCourierDispatchSave? dispatch)
            || dispatch.Status is not (DaggerfallDispatchStatus.Delivered or DaggerfallDispatchStatus.Intercepted or DaggerfallDispatchStatus.Waylaid))
            return false;
        _dispatches.Remove(dispatchId);
        _resolutions.RemoveAll(resolution => resolution.DispatchId == dispatchId);
        return true;
    }

    private DaggerfallDispatchResolution RecordResolution(DaggerfallDispatchResolution resolution)
    {
        resolution.Validate();
        _resolutions.Add(resolution);
        _onBoardMoveResolved?.Invoke(resolution);
        return resolution;
    }

    private void RetireOverlandBand(string? bandId, long? courierActorId)
    {
        if (string.IsNullOrWhiteSpace(bandId))
            return;

        _ = expedition?.Invoke()?.RemovePersistentBand(bandId);
        RetireCourierLedgerMember(bandId, courierActorId);
    }

    private void RetireCourierLedgerMember(string bandId, long? courierActorId)
    {
        // Release any OnOverlandBand members of this retired dispatch band so saves neither keep orphaned
        // OnOverlandBand references nor leak permanent AtSite entries at off-screen destinations.
        foreach (ActorLedgerEntry member in actorLedger.EntriesOnOverlandBand(bandId))
            ReleaseOffStageOrPrune(member);

        if (courierActorId is long cid && actorLedger.TryGet(cid, out ActorLedgerEntry? entry)
            && entry?.Location is { Kind: ActorLocationKind.OnOverlandBand, BandId: { } memberBandId }
            && StringComparer.Ordinal.Equals(memberBandId, bandId))
        {
            ReleaseOffStageOrPrune(entry);
        }
    }

    private void ReleaseOffStageOrPrune(ActorLedgerEntry entry)
    {
        bool untouchedStagedCourier = entry.Origin.SlotKey is { } slotKey
            && slotKey.StartsWith("population/dispatch/", StringComparison.Ordinal)
            && entry.Vitals is null
            && entry.Combat is null
            && entry.Referral is null;
        if (untouchedStagedCourier)
            actorLedger.Remove(entry.DurableId);
        else
            actorLedger.Upsert(entry with { Location = ActorLocationOverride.OffStage });
    }

    private long? StageCourierActor(DaggerfallSiteRecord origin, string dispatchId, int? sourceFactionId)
    {
        string race = DaggerfallSourcePopulation.TryResolveClimateRace(definitions, origin, out _) ?? "breton";
        DaggerfallWorldProfileKey originProfile = DaggerfallWorldProfileIds.Exterior(origin.Id);
        string slotId = $"population/dispatch/{dispatchId}";
        ulong seed = DaggerfallPeopleSlots.SlotSeed(worldSocialSeed(), origin.Id, slotId);
        long durableId = DaggerfallPeopleSlots.SeededSlotIdentity(origin.Id, $"{originProfile.LogicalId}/{slotId}");

        string gender = ((seed >> 3) & 1UL) != 0 ? "Female" : "Male";
        int nameSeed = (int)((seed >> 8) & 0x7FFFUL) + 1;
        int factionId = sourceFactionId is int sf && sf > 0
            ? sf
            : DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, origin.Id.Region)?.Id ?? 0;
        DaggerfallNpcAppearance look = new(race, gender, 182, 0, nameSeed, factionId);
        string displayName = generateName?.Invoke(look, origin.Id.Region) ?? "Royal Courier";
        ActorOrigin actorOrigin = ActorOrigin.Seeded(originProfile, slotId, "courtier",
            ActorHarmPolicyKind.LawProtectedCivilian, ActorInteractionPolicyKind.FullDialogue);
        ActorSocialDiff socialDiff = new(
            (int)DaggerfallNpcKind.Civilian,
            slotId,
            origin.Id.Region,
            origin.Name,
            string.Empty,
            originProfile.LogicalId,
            look.Race,
            look.Gender,
            look.BillboardArchive,
            look.BillboardRecord,
            look.NameSeed,
            look.FactionId,
            "courier",
            ["talk"],
            DisplayNameOverride: displayName);
        actorLedger.Upsert(new ActorLedgerEntry(durableId, actorOrigin, Social: socialDiff));
        return durableId;
    }

    private DaggerfallSiteRecord? FindSite(DaggerfallSiteId id) =>
        definitions.Locations.Records.FirstOrDefault(r => r.Id == id);

    private string ResolveSiteName(DaggerfallSiteId id) =>
        FindSite(id)?.Name ?? definitions.BuildingNames.RegionName(id.Region);
}
