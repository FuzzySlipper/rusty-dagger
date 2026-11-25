using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>HUD portrait entry projected directly from an <see cref="ActorLocationKind.WithParty"/> ledger member.</summary>
internal sealed record DaggerfallPartyPortrait(long DurableId, ActorPartyRole Role, string Name, string MediaId, int Order);

/// <summary>
/// Coordinates persistent <see cref="ActorLocationKind.OnOverlandBand"/> travelling parties and cross-boundary
/// <see cref="ActorLocationKind.WithParty"/> staging (drivers, escorts, companions) between the expedition map,
/// <see cref="DaggerfallActorLedger"/>, and active 3D scenes (<c>EntityStore</c>) with zero duplicate bodies.
/// </summary>
internal sealed class DaggerfallOverlandPartyCoordinator
{
    private readonly DaggerfallExpedition _expedition;
    private readonly DaggerfallActorLedger _actorLedger;
    private readonly ActorsState _actors;
    private readonly DaggerfallNpcRegistry _npcs;
    private readonly DaggerfallActorRoster _roster;
    private readonly Func<DaggerfallSiteProfile> _activeProfile;
    private readonly Func<DaggerfallSiteAppearance> _activeAppearance;
    private readonly Func<WorldPoint, WorldPoint> _localToProfile;
    private readonly Func<Vector3> _sceneTranslation;
    private readonly Func<DaggerfallNpc, ActorSocialDiff?, ActorSocialDiff> _toSocialDiff;
    private readonly Func<long, ActorLedgerEntry?> _captureLiveForCustody;
    private readonly Func<DaggerfallSiteId, (int X, int Y)?>? _siteCoordinates;
    private readonly Action<long>? _provokeHostile;

    private readonly List<(string BandId, DaggerfallEncounterStaging Staging)> _pendingIntercepts = [];

    internal DaggerfallOverlandPartyCoordinator(
        DaggerfallExpedition expedition,
        DaggerfallActorLedger actorLedger,
        ActorsState actors,
        DaggerfallNpcRegistry npcs,
        DaggerfallActorRoster roster,
        Func<DaggerfallSiteProfile> activeProfile,
        Func<DaggerfallSiteAppearance> activeAppearance,
        Func<WorldPoint, WorldPoint> localToProfile,
        Func<Vector3> sceneTranslation,
        Func<DaggerfallNpc, ActorSocialDiff?, ActorSocialDiff> toSocialDiff,
        Func<long, ActorLedgerEntry?> captureLiveForCustody,
        Func<DaggerfallSiteId, (int X, int Y)?>? siteCoordinates = null,
        Action<long>? provokeHostile = null)
    {
        _expedition = expedition ?? throw new ArgumentNullException(nameof(expedition));
        _actorLedger = actorLedger ?? throw new ArgumentNullException(nameof(actorLedger));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _activeProfile = activeProfile ?? throw new ArgumentNullException(nameof(activeProfile));
        _activeAppearance = activeAppearance ?? throw new ArgumentNullException(nameof(activeAppearance));
        _localToProfile = localToProfile ?? throw new ArgumentNullException(nameof(localToProfile));
        _sceneTranslation = sceneTranslation ?? throw new ArgumentNullException(nameof(sceneTranslation));
        _toSocialDiff = toSocialDiff ?? throw new ArgumentNullException(nameof(toSocialDiff));
        _captureLiveForCustody = captureLiveForCustody ?? throw new ArgumentNullException(nameof(captureLiveForCustody));
        _siteCoordinates = siteCoordinates;
        _provokeHostile = provokeHostile;
    }

    internal IReadOnlyList<long> StagedPartyMemberIds =>
    [
        .. _actors.All
            .Where(a => a.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov)
                && !string.IsNullOrWhiteSpace(ov.StagedPartyRole))
            .Select(a => a.DurableId)
            .Order(),
    ];

    /// <summary>
    /// Dispatches one or more durable actors onto a persistent <see cref="ActorLocationKind.OnOverlandBand"/> party:
    /// records each member in <see cref="DaggerfallActorLedger"/> with <c>OnOverlandBand(bandId)</c> (suppressing its
    /// home <c>SeededSlot</c> while travelling), unloads any live <c>EntityStore</c> instance so zero duplicate bodies
    /// exist, and registers the persistent band on <see cref="DaggerfallExpedition"/>.
    /// </summary>
    internal DaggerfallPersistentOverlandBand DispatchOverlandBand(
        string bandId,
        string name,
        DaggerfallBandKind kind,
        IReadOnlyList<long> memberIds,
        double startX,
        double startY,
        double speedPixelsPerHour,
        DaggerfallExpeditionMark? destination = null,
        DaggerfallWorldProfileKey? destinationProfile = null,
        bool camp = false,
        bool hostile = false,
        int? mobile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bandId);
        ArgumentNullException.ThrowIfNull(memberIds);
        if (memberIds.Count == 0)
            throw new ArgumentException("A persistent overland band requires at least one durable actor member.", nameof(memberIds));

        foreach (long memberId in memberIds.Distinct())
        {
            ActorLedgerEntry entry = EnsureLedgerEntry(memberId);
            _actorLedger.Upsert(entry with
            {
                Location = ActorLocationOverride.OnOverlandBand(bandId),
                Pose = null,
            });
            _roster.UnloadActor(memberId);
        }

        double heading = 0d;
        if (destination is { } dest)
        {
            double dx = (dest.Pixel.X + .5) - startX;
            double dy = (dest.Pixel.Y + .5) - startY;
            if (dx * dx + dy * dy > 1e-9)
                heading = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        }

        DaggerfallPersistentBandSave save = new(
            bandId,
            name,
            kind,
            startX,
            startY,
            heading,
            speedPixelsPerHour,
            destination,
            destinationProfile is { } dp ? DaggerfallWorldProfileKeySave.Capture(dp) : null,
            camp,
            Spotted: false,
            Hostile: hostile,
            Mobile: mobile);
        return _expedition.RegisterPersistentBand(save);
    }

    /// <summary>Queues a met persistent band's members to materialize when the player enters the 3D scene.</summary>
    internal void OnMeetPersistentBand(DaggerfallPersistentOverlandBand band, DaggerfallTravelMapPixel pixel, DaggerfallEncounterStaging staging)
    {
        ArgumentNullException.ThrowIfNull(band);
        if (!_pendingIntercepts.Any(item => StringComparer.Ordinal.Equals(item.BandId, band.BandId)))
            _pendingIntercepts.Add((band.BandId, staging));
    }

    /// <summary>
    /// Transitions a persistent band's members off <see cref="ActorLocationKind.OnOverlandBand"/> upon destination arrival.
    /// Regional traffic bands complete a two-leg outbound-and-return lifecycle back to their home-town slot (self-pruning
    /// untouched slots back to zero ledger entries so slots are reused instead of leaking <c>AtSite</c> orphans).
    /// Unintercepted hostile bands release their minted ledger entries off-screen.
    /// </summary>
    internal IReadOnlyList<long> OnPersistentBandArrived(DaggerfallPersistentOverlandBand band)
    {
        ArgumentNullException.ThrowIfNull(band);
        DaggerfallWorldProfileKey arrivalProfile = band.DestinationProfile?.Require()
            ?? (band.Destination is { } dest
                ? DaggerfallWorldProfileIds.Exterior(_expedition.HostAt(dest.Pixel).Id)
                : _activeProfile().ProfileKey);
        DaggerfallSiteProfile active = _activeProfile();
        bool arrivingAtActive = active.ProfileKey == arrivalProfile;

        if (band.BandId.StartsWith(DaggerfallHostileOverlandBands.BandIdPrefix, StringComparison.Ordinal) && !arrivingAtActive)
        {
            foreach (ActorLedgerEntry member in _actorLedger.EntriesOnOverlandBand(band.BandId))
                _actorLedger.Remove(member.DurableId);
            return [];
        }

        if (band.BandId.StartsWith(DaggerfallOverlandTraffic.BandIdPrefix, StringComparison.Ordinal))
        {
            IReadOnlyList<ActorLedgerEntry> members = _actorLedger.EntriesOnOverlandBand(band.BandId);
            if (members.Count == 0) return [];

            bool isReturnLeg = band.BandId.EndsWith(DaggerfallOverlandTraffic.ReturnBandSuffix, StringComparison.Ordinal);
            if (!isReturnLeg && !arrivingAtActive)
            {
                DaggerfallWorldProfileKey? homeProfile = members[0].Origin.SiteProfile?.Require();
                if (homeProfile is { } hp && hp != arrivalProfile && _siteCoordinates?.Invoke(hp.Site) is { } homePixel)
                {
                    _ = DispatchOverlandBand(
                        bandId: band.BandId + DaggerfallOverlandTraffic.ReturnBandSuffix,
                        name: band.Name,
                        kind: band.Kind,
                        memberIds: [.. members.Select(m => m.DurableId)],
                        startX: band.X,
                        startY: band.Y,
                        speedPixelsPerHour: band.SpeedPixelsPerHour,
                        destination: new DaggerfallExpeditionMark(new(homePixel.X, homePixel.Y), hp.Site),
                        destinationProfile: hp,
                        camp: false,
                        hostile: false,
                        mobile: band.Mobile);
                    return [];
                }
            }

            if (isReturnLeg || !arrivingAtActive)
                return ReturnTrafficMembersHome(members, active, arrivingAtActive);
        }

        return DeliverBandMembers(band.BandId, arrivalProfile, anchorLocalPose: null, landingYaw: 0f);
    }

    /// <summary>
    /// Releases any regional <c>traffic:</c> travellers who arrived visibly at <paramref name="departingProfile"/>
    /// or were encountered by the player there when the player leaves the scene (opening the expedition map or
    /// crossing to another site). Captures any player-created deltas, then either dispatches a <c>:return</c>
    /// band home (when departing to the expedition map) or returns them directly to their home slot (self-pruning
    /// untouched slots back to zero ledger bytes), preventing permanent <c>AtSite</c> orphans.
    /// </summary>
    internal IReadOnlyList<long> ReleaseDepartingTrafficVisitors(
        DaggerfallWorldProfileKey departingProfile,
        bool dispatchReturnBand)
    {
        List<long> released = [];
        DaggerfallSiteProfile active = _activeProfile();

        foreach (ActorLedgerEntry entry in _actorLedger.All.ToArray())
        {
            if (entry.Location is not { Kind: ActorLocationKind.AtSite, TrafficVisitor: true, Profile: { } locProfile }
                || locProfile.Require() != departingProfile)
                continue;
            if (entry.Social?.StagedPartyRole is not null)
                continue;

            long actorId = entry.DurableId;
            if (_actors.TryGet(actorId, out ActorState? live))
            {
                if (live.IsDefeated)
                    continue;
                if (live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov)
                    && !string.IsNullOrWhiteSpace(ov.StagedPartyRole))
                    continue;
                if (_captureLiveForCustody(actorId) is { } captured)
                    _actorLedger.Upsert(captured with { Location = captured.Location is { } loc ? loc with { TrafficVisitor = false } : null });
                _roster.UnloadActor(actorId);
            }

            ActorLedgerEntry current = _actorLedger.TryGet(actorId, out ActorLedgerEntry? latest) && latest is not null
                ? latest
                : entry;
            if (current.Location?.Kind is ActorLocationKind.Dead or ActorLocationKind.WithParty or ActorLocationKind.Removed)
                continue;

            DaggerfallWorldProfileKey? homeProfile = current.Origin.SiteProfile?.Require();
            if (dispatchReturnBand
                && homeProfile is { } hp
                && hp != departingProfile
                && _siteCoordinates?.Invoke(hp.Site) is { } homePixel
                && _siteCoordinates?.Invoke(departingProfile.Site) is { } fromPixel)
            {
                string returnBandId = $"{DaggerfallOverlandTraffic.BandIdPrefix}visitor-{actorId}-{hp.Site.Region}-{hp.Site.Index}{DaggerfallOverlandTraffic.ReturnBandSuffix}";
                if (!_expedition.PersistentBands.Any(b => StringComparer.Ordinal.Equals(b.BandId, returnBandId)))
                {
                    string name = current.Social?.DisplayNameOverride ?? "Regional Traveller";
                    double speed = string.Equals(current.Social?.Role, "courier", StringComparison.OrdinalIgnoreCase)
                        ? DaggerfallOverlandTraffic.CourierSpeedPixelsPerHour
                        : DaggerfallOverlandTraffic.CaravanSpeedPixelsPerHour;
                    _ = DispatchOverlandBand(
                        bandId: returnBandId,
                        name: name,
                        kind: DaggerfallBandKind.Travellers,
                        memberIds: [actorId],
                        startX: fromPixel.X + 0.5,
                        startY: fromPixel.Y + 0.5,
                        speedPixelsPerHour: speed,
                        destination: new DaggerfallExpeditionMark(new(homePixel.X, homePixel.Y), hp.Site),
                        destinationProfile: hp,
                        camp: false,
                        hostile: false);
                    released.Add(actorId);
                    continue;
                }
            }

            _ = ReturnTrafficMembersHome([current], active, arrivingAtActive: false);
            released.Add(actorId);
        }

        return released;
    }

    private IReadOnlyList<long> ReturnTrafficMembersHome(
        IReadOnlyList<ActorLedgerEntry> members,
        DaggerfallSiteProfile active,
        bool arrivingAtActive)
    {
        List<long> returned = [];
        foreach (ActorLedgerEntry member in members)
        {
            if (!HasPlayerDeltas(member))
            {
                _actorLedger.Remove(member.DurableId);
            }
            else
            {
                _actorLedger.Upsert(member with { Location = null, Pose = null });
            }

            if (arrivingAtActive && member.Origin.SiteProfile?.Require() == active.ProfileKey)
            {
                ActorLedgerEntry entry = _actorLedger.TryGet(member.DurableId, out ActorLedgerEntry? kept) && kept is not null
                    ? kept
                    : member with { Location = null, Pose = null };
                WorldPoint spot = active.Project.PlayerPosition is { } pp
                    ? DaggerfallSceneOrigin.Shift(pp, _sceneTranslation())
                    : default;
                _ = _roster.MaterializeLedgerActorAt(entry, active, spot, 0f, _activeAppearance(), _sceneTranslation());
            }
            returned.Add(member.DurableId);
        }
        return returned;
    }

    private static bool HasPlayerDeltas(ActorLedgerEntry entry) =>
        entry.Vitals is not null
        || entry.Combat is not null
        || entry.Referral is not null
        || entry.Social is { } s && (
            s.Met
            || s.DispositionDelta != 0
            || s.WitnessedCrimes is { Length: > 0 }
            || s.ReferralTags is { Length: > 0 }
            || s.Muted
            || s.EscortPortrait is not null
            || s.StagedPartyRole is not null
            || s.Presence != (int)DaggerfallNpcPresence.Active);

    /// <summary>
    /// Runs right after <see cref="DaggerfallExpedition"/> relocates the player into a 3D scene: materializes any
    /// intercepted persistent overland bands beside the player and stages travelling <c>Driver</c>/<c>Companion</c>
    /// party members on foot with their exact durable IDs.
    /// </summary>
    internal void OnEnterSceneFromExpedition(DaggerfallSiteProfile activeProfile, WorldPoint landingLocalPose, float landingYaw)
    {
        ArgumentNullException.ThrowIfNull(activeProfile);
        if (_pendingIntercepts.Count > 0)
        {
            (string BandId, DaggerfallEncounterStaging Staging)[] intercepts = [.. _pendingIntercepts];
            _pendingIntercepts.Clear();
            foreach ((string bandId, _) in intercepts)
                _ = DeliverBandMembers(bandId, activeProfile.ProfileKey, landingLocalPose, landingYaw);
        }

        _ = StagePartyMembers(activeProfile, landingLocalPose, landingYaw,
            role => role is ActorPartyRole.Driver or ActorPartyRole.Companion);
    }

    /// <summary>
    /// Places a durable actor into <see cref="ActorLocationKind.WithParty"/> (driver, escort, or companion),
    /// persisting its HUD portrait in <see cref="DaggerfallActorLedger"/> and unloading it from <c>EntityStore</c>
    /// so <c>QueryNearby</c> never sees travelling party members while on the expedition map.
    /// </summary>
    internal void JoinParty(long actorId, ActorPartyRole role, string? portraitMedia = null, int? explicitOrder = null)
    {
        ActorPoseOverride? rememberedPose = null;
        if (_actors.TryGet(actorId, out ActorState? liveActor))
        {
            WorldPoint profilePos = _localToProfile(liveActor.Position);
            rememberedPose = new ActorPoseOverride(profilePos.X, profilePos.Y, profilePos.Z, liveActor.HeadingYawRadians);
        }
        else if (_npcs.TryGet(actorId, out DaggerfallNpc? placedNpc)
            && placedNpc.X is float nx && placedNpc.Y is float ny && placedNpc.Z is float nz)
        {
            rememberedPose = new ActorPoseOverride(nx, ny, nz, 0f);
        }

        ActorLedgerEntry entry = EnsureLedgerEntry(actorId);
        int nextOrder = explicitOrder is > 0 and int forcedOrder
            ? forcedOrder
            : entry.Social?.EscortOrder is > 0 and int existingOrder
                ? existingOrder
                : (_actorLedger.EntriesWithParty().Select(e => e.Social?.EscortOrder ?? 0).DefaultIfEmpty(0).Max() + 1);
        ActorSocialDiff? social = entry.Social is { } s
            ? s with { EscortPortrait = portraitMedia ?? s.EscortPortrait, EscortOrder = nextOrder, StagedPartyRole = null }
            : null;
        _actorLedger.Upsert(entry with
        {
            Location = ActorLocationOverride.WithParty(role),
            Pose = rememberedPose ?? entry.Pose,
            Social = social,
        });
        _roster.UnloadActor(actorId);
    }

    /// <summary>
    /// Materializes matching <see cref="ActorLocationKind.WithParty"/> members into <c>EntityStore</c> beside
    /// <paramref name="anchorLocalPose"/> (e.g., at camp, dungeon exterior, or delivery) as their exact same durable IDs.
    /// Persists <see cref="ActorSocialDiff.StagedPartyRole"/> on both ledger and live overlay so saving while staged
    /// preserves party membership across reload (both at home and away from home).
    /// </summary>
    internal IReadOnlyList<long> StagePartyMembers(
        DaggerfallSiteProfile activeProfile,
        WorldPoint anchorLocalPose,
        float yawRadians,
        Func<ActorPartyRole, bool>? roleFilter = null)
    {
        ArgumentNullException.ThrowIfNull(activeProfile);
        List<long> staged = [];
        IReadOnlyList<ActorLedgerEntry> party = _actorLedger.EntriesWithParty();
        int ordinal = 0;
        foreach (ActorLedgerEntry entry in party)
        {
            ActorPartyRole role = entry.Location?.PartyRole ?? ActorPartyRole.Escort;
            if (roleFilter is not null && !roleFilter(role)) continue;

            WorldPoint spot = OffsetBeside(anchorLocalPose, yawRadians, ordinal++, forwardDistance: 2.5f);
            if (StagePartyMemberCore(entry, role, activeProfile, spot, yawRadians) is not null)
                staged.Add(entry.DurableId);
        }
        return staged;
    }

    /// <summary>Stages one specific <see cref="ActorLocationKind.WithParty"/> member into <c>EntityStore</c> at <paramref name="localPose"/>.</summary>
    internal ActorState? StagePartyMember(
        long actorId,
        DaggerfallSiteProfile activeProfile,
        WorldPoint localPose,
        float yawRadians)
    {
        ArgumentNullException.ThrowIfNull(activeProfile);
        if (!_actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) || entry?.Location?.Kind != ActorLocationKind.WithParty)
            return null;
        ActorPartyRole role = entry.Location.PartyRole ?? ActorPartyRole.Companion;
        return StagePartyMemberCore(entry, role, activeProfile, localPose, yawRadians);
    }

    /// <summary>Re-stages a specific set of <see cref="ActorLocationKind.WithParty"/> members beside <paramref name="anchorLocalPose"/>.</summary>
    internal IReadOnlyList<long> StagePartyMembersById(
        IReadOnlyCollection<long> actorIds,
        DaggerfallSiteProfile activeProfile,
        WorldPoint anchorLocalPose,
        float yawRadians)
    {
        ArgumentNullException.ThrowIfNull(actorIds);
        ArgumentNullException.ThrowIfNull(activeProfile);
        if (actorIds.Count == 0) return [];
        List<long> staged = [];
        int ordinal = 0;
        foreach (long actorId in actorIds)
        {
            if (!_actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) || entry?.Location?.Kind != ActorLocationKind.WithParty)
                continue;
            ActorPartyRole role = entry.Location.PartyRole ?? ActorPartyRole.Companion;
            WorldPoint spot = OffsetBeside(anchorLocalPose, yawRadians, ordinal++, forwardDistance: 2.0f);
            if (StagePartyMemberCore(entry, role, activeProfile, spot, yawRadians) is not null)
                staged.Add(actorId);
        }
        return staged;
    }

    private ActorState? StagePartyMemberCore(
        ActorLedgerEntry entry,
        ActorPartyRole role,
        DaggerfallSiteProfile activeProfile,
        WorldPoint spot,
        float yawRadians)
    {
        WorldPoint profilePoint = _localToProfile(spot);
        ActorPoseOverride poseOverride = new(profilePoint.X, profilePoint.Y, profilePoint.Z, yawRadians);

        bool isHome = entry.Origin.Kind is ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual
            && entry.Origin.SiteProfile?.Require() == activeProfile.ProfileKey;
        ActorSocialDiff? updatedSocial = entry.Social is { } s
            ? s with { StagedPartyRole = role }
            : null;
        ActorLedgerEntry updated = entry with
        {
            Location = isHome ? null : ActorLocationOverride.AtSite(activeProfile.ProfileKey, poseOverride, detached: false),
            Pose = poseOverride,
            Social = updatedSocial,
        };
        _actorLedger.Upsert(updated);
        if (_roster.MaterializeLedgerActorAt(updated, activeProfile, spot, yawRadians, _activeAppearance(), _sceneTranslation()) is { } live)
        {
            if (live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? ov))
            {
                ov.StagedPartyRole = role.ToString();
                if (updatedSocial is not null)
                {
                    ov.EscortPortraitMedia = updatedSocial.EscortPortrait;
                    ov.EscortOrder = updatedSocial.EscortOrder;
                }
            }
            return live;
        }
        return null;
    }

    /// <summary>
    /// Returns every staged party member (driver/companion) back to <see cref="ActorLocationKind.WithParty"/>,
    /// capturing its live vitals, inventory, combat state, and active effects before unloading it from <c>EntityStore</c>
    /// when opening the expedition map or departing camp.
    /// </summary>
    internal IReadOnlyList<long> BoardStagedPartyMembers()
    {
        List<long> boarded = [];
        foreach (ActorState live in _actors.All.ToArray())
        {
            if (!live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
                || string.IsNullOrWhiteSpace(overlay.StagedPartyRole)
                || !Enum.TryParse<ActorPartyRole>(overlay.StagedPartyRole, out ActorPartyRole role))
                continue;

            long actorId = live.DurableId;
            if (live.IsDefeated)
            {
                overlay.StagedPartyRole = null;
                continue;
            }
            string? portrait = overlay.EscortPortraitMedia;
            int order = overlay.EscortOrder;
            ActorLedgerEntry entry = EnsureLedgerEntry(actorId);
            ActorSocialDiff? social = entry.Social is { } s
                ? s with
                {
                    EscortPortrait = portrait ?? s.EscortPortrait,
                    EscortOrder = order > 0 ? order : s.EscortOrder,
                    StagedPartyRole = null,
                }
                : null;
            _actorLedger.Upsert(entry with
            {
                Location = ActorLocationOverride.WithParty(role),
                Pose = null,
                Social = social,
            });
            _roster.UnloadActor(actorId);
            boarded.Add(actorId);
        }
        return boarded;
    }

    /// <summary>Releases a party member permanently at <paramref name="profile"/> (e.g., delivery or dismissal).</summary>
    internal void ReleaseFromParty(long actorId, DaggerfallWorldProfileKey profile, WorldPoint? localPose = null)
    {
        if (_actors.TryGet(actorId, out ActorState? live)
            && live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? liveOverlay))
        {
            liveOverlay.StagedPartyRole = null;
            liveOverlay.EscortPortraitMedia = null;
            liveOverlay.EscortOrder = 0;
        }
        if (!_actorLedger.TryGet(actorId, out ActorLedgerEntry? entry) || entry is null) return;
        ActorPoseOverride? pose = null;
        if (localPose is { } lp)
        {
            WorldPoint pp = _localToProfile(lp);
            pose = new(pp.X, pp.Y, pp.Z, 0f);
        }
        bool isHome = entry.Origin.Kind is ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual
            && entry.Origin.SiteProfile?.Require() == profile;
        ActorLedgerEntry released = entry with
        {
            Location = isHome ? null : ActorLocationOverride.AtSite(profile, pose, detached: false),
            Pose = pose ?? entry.Pose,
            Social = entry.Social is { } s ? s with { EscortPortrait = null, EscortOrder = 0, StagedPartyRole = null } : null,
        };
        _actorLedger.Upsert(released);
        if (profile == _activeProfile().ProfileKey && localPose is { } spot)
            _ = _roster.MaterializeLedgerActorAt(released, _activeProfile(), spot, 0f, _activeAppearance(), _sceneTranslation());
    }

    /// <summary>HUD portraits driven directly from <see cref="DaggerfallActorLedger.EntriesWithParty"/> and currently staged hired party members.</summary>
    internal IReadOnlyList<DaggerfallPartyPortrait> PartyPortraits()
    {
        Dictionary<long, DaggerfallPartyPortrait> result = [];
        foreach (ActorLedgerEntry entry in _actorLedger.EntriesWithParty())
        {
            if (entry.Social is not { EscortPortrait: { Length: > 0 } media } social) continue;
            string name = social.DisplayNameOverride ?? social.Role;
            ActorPartyRole role = entry.Location?.PartyRole ?? ActorPartyRole.Escort;
            result[entry.DurableId] = new(entry.DurableId, role, name, media, social.EscortOrder);
        }
        foreach (ActorState live in _actors.All)
        {
            if (live.IsDefeated || result.ContainsKey(live.DurableId)) continue;
            if (!live.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
                || string.IsNullOrWhiteSpace(overlay.StagedPartyRole)
                || string.IsNullOrWhiteSpace(overlay.EscortPortraitMedia)
                || !Enum.TryParse<ActorPartyRole>(overlay.StagedPartyRole, out ActorPartyRole role))
                continue;
            string name = _npcs.TryGet(live.DurableId, out DaggerfallNpc? npc)
                ? npc.DisplayName ?? npc.Role
                : role.ToString();
            result[live.DurableId] = new(live.DurableId, role, name, overlay.EscortPortraitMedia, overlay.EscortOrder);
        }
        return [.. result.Values.OrderBy(p => p.Order).ThenBy(p => p.DurableId)];
    }

    private IReadOnlyList<long> DeliverBandMembers(
        string bandId,
        DaggerfallWorldProfileKey arrivalProfile,
        WorldPoint? anchorLocalPose,
        float landingYaw)
    {
        IReadOnlyList<ActorLedgerEntry> members = _actorLedger.EntriesOnOverlandBand(bandId);
        if (members.Count == 0) return [];

        DaggerfallSiteProfile active = _activeProfile();
        bool arrivingAtActive = active.ProfileKey == arrivalProfile;
        bool isTrafficVisitor = bandId.StartsWith(DaggerfallOverlandTraffic.BandIdPrefix, StringComparison.Ordinal);
        List<long> delivered = [];

        for (int i = 0; i < members.Count; i++)
        {
            ActorLedgerEntry member = members[i];
            WorldPoint? targetLocal = anchorLocalPose is { } anchor
                ? OffsetBeside(anchor, landingYaw, i, forwardDistance: 4.0f)
                : null;
            ActorPoseOverride? arrivalPose = null;
            if (targetLocal is { } spot)
            {
                WorldPoint pp = _localToProfile(spot);
                arrivalPose = new(pp.X, pp.Y, pp.Z, landingYaw + MathF.PI);
            }

            _actorLedger.ClearBandLocation(member.DurableId, arrivalProfile, arrivalPose, detached: !arrivingAtActive, trafficVisitor: isTrafficVisitor);
            ActorLedgerEntry updated = _actorLedger.TryGet(member.DurableId, out ActorLedgerEntry? after) && after is not null
                ? after
                : member with { Location = null, Pose = arrivalPose };

            if (arrivingAtActive)
            {
                WorldPoint materializeSpot = targetLocal
                    ?? (updated.Pose is { } up ? DaggerfallSceneOrigin.Shift(new WorldPoint(up.X, up.Y, up.Z), _sceneTranslation())
                        : active.Project.PlayerPosition is { } pp ? DaggerfallSceneOrigin.Shift(pp, _sceneTranslation()) : default);
                ActorState? live = _roster.MaterializeLedgerActorAt(updated, active, materializeSpot, arrivalPose?.HeadingRadians ?? 0f,
                    _activeAppearance(), _sceneTranslation());
                if (live is not null && (updated.Combat?.ForcedHostile == true
                    || bandId.StartsWith(DaggerfallHostileOverlandBands.BandIdPrefix, StringComparison.Ordinal)))
                {
                    _provokeHostile?.Invoke(live.DurableId);
                }
            }
            delivered.Add(member.DurableId);
        }
        return delivered;
    }

    private ActorLedgerEntry EnsureLedgerEntry(long actorId)
    {
        if (_captureLiveForCustody(actorId) is { } liveCaptured)
            return liveCaptured;

        _actorLedger.TryGet(actorId, out ActorLedgerEntry? existing);
        ActorOrigin? origin = existing?.Origin;
        ActorSocialDiff? social = existing?.Social;
        ActorReferralDiff? referral = existing?.Referral;

        if (_npcs.TryGet(actorId, out DaggerfallNpc? npc))
        {
            social = _toSocialDiff(npc, social);
            DaggerfallWorldProfileKey? homeProfile = npc.Profile;
            origin ??= new ActorOrigin(
                DaggerfallPeopleSlots.IsDeterministicId(actorId) ? ActorOriginKind.SeededSlot : ActorOriginKind.Minted,
                SiteProfile: homeProfile is { } p ? DaggerfallWorldProfileKeySave.Capture(p) : null,
                SlotKey: DaggerfallPeopleSlots.IsDeterministicId(actorId) ? npc.StableKey : null,
                DefinitionId: DaggerfallPeopleSlots.IsDeterministicId(actorId)
                    ? null
                    : npc.Kind == DaggerfallNpcKind.Civilian ? DaggerfallActorKinds.Civilian : DaggerfallActorKinds.StaticNpc);
            if (origin.SiteProfile is null && homeProfile is { } hp)
                origin = origin with { SiteProfile = DaggerfallWorldProfileKeySave.Capture(hp) };
        }

        if (origin is null)
            throw new InvalidOperationException($"Actor {actorId} has no origin or NPC record to enter overland travel or party custody.");

        return existing is not null
            ? existing with { Origin = origin, Social = social ?? existing.Social, Referral = referral ?? existing.Referral }
            : new ActorLedgerEntry(actorId, origin, Social: social, Referral: referral);
    }

    private static WorldPoint OffsetBeside(WorldPoint anchor, float yawRadians, int ordinal, float forwardDistance)
    {
        Vector3 forward = ActorHeading.Forward(yawRadians);
        Vector3 right = new(forward.Z, 0f, -forward.X);
        float lateral = ordinal == 0 ? 0f : ((ordinal % 2 == 1 ? 1f : -1f) * ((ordinal + 1) / 2) * 1.5f);
        Vector3 pos = anchor.ToVector() + forward * forwardDistance + right * lateral;
        return new WorldPoint(pos.X, anchor.Y, pos.Z);
    }
}
