using System.Globalization;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// Autonomous world producer for persistent <see cref="ActorLocationKind.OnOverlandBand"/> regional traffic
/// (merchant caravans and regional couriers). Dispatches real seeded actors from <c>daggerfall.people</c>
/// settlement slots onto deterministic routes between neighbouring exterior settlements as expedition time crosses
/// daytime dispatch windows, keeping the active traffic count bounded.
/// </summary>
internal sealed class DaggerfallOverlandTraffic
{
    internal const string BandIdPrefix = "traffic:";
    internal const int MaxActiveTrafficBands = 3;
    internal const long DispatchIntervalSeconds = 6L * DaggerfallCalendar.SecondsPerMinute * DaggerfallCalendar.MinutesPerHour;
    internal const double MinimumRouteDistancePixels = 2.0;
    internal const double MaximumRouteDistancePixels = 32.0;
    internal const double NearbyRegionRadiusPixels = 36.0;
    internal const double CaravanSpeedPixelsPerHour = 1.0;
    internal const double CourierSpeedPixelsPerHour = 2.1;

    private static readonly string[] CaravanSpawners = ["general-merchant", "banker", "tavern-keeper", "alchemist", "clothier", "house-resident"];
    private static readonly string[] CourierSpawners = ["house-resident", "guild-member", "palace-attendant", "courtier", "tavern-patron"];

    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallActorLedger _actorLedger;
    private readonly ActorsState _actors;
    private readonly DaggerfallNpcRegistry _npcs;
    private readonly DaggerfallExpedition _expedition;
    private readonly DaggerfallOverlandPartyCoordinator _overlandParties;
    private readonly Func<DaggerfallNpcAppearance, int, string> _generateName;
    private readonly Func<ulong> _worldSocialSeed;
    private readonly Func<DaggerfallSiteId?> _activeSiteId;

    internal DaggerfallOverlandTraffic(
        DaggerfallDefinitions definitions,
        DaggerfallActorLedger actorLedger,
        ActorsState actors,
        DaggerfallNpcRegistry npcs,
        DaggerfallExpedition expedition,
        DaggerfallOverlandPartyCoordinator overlandParties,
        Func<DaggerfallNpcAppearance, int, string> generateName,
        Func<ulong> worldSocialSeed,
        Func<DaggerfallSiteId?> activeSiteId)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _actorLedger = actorLedger ?? throw new ArgumentNullException(nameof(actorLedger));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _expedition = expedition ?? throw new ArgumentNullException(nameof(expedition));
        _overlandParties = overlandParties ?? throw new ArgumentNullException(nameof(overlandParties));
        _generateName = generateName ?? throw new ArgumentNullException(nameof(generateName));
        _worldSocialSeed = worldSocialSeed ?? throw new ArgumentNullException(nameof(worldSocialSeed));
        _activeSiteId = activeSiteId ?? throw new ArgumentNullException(nameof(activeSiteId));
    }

    /// <summary>
    /// Advances regional traffic scheduling across the interval [<paramref name="before"/>, <paramref name="after"/>].
    /// Each crossed daytime 6-hour window (06:00, 12:00, 18:00) may dispatch one persistent caravan or courier between
    /// real neighbouring settlements near <paramref name="partyPosition"/> up to <see cref="MaxActiveTrafficBands"/>.
    /// </summary>
    internal IReadOnlyList<DaggerfallPersistentOverlandBand> Step(
        DaggerfallCalendar before,
        DaggerfallCalendar after,
        (double X, double Y) partyPosition)
    {
        long startSec = before.ToAbsoluteSeconds();
        long endSec = after.ToAbsoluteSeconds();
        if (endSec <= startSec) return [];

        long firstWindow = (startSec / DispatchIntervalSeconds) + 1;
        long lastWindow = endSec / DispatchIntervalSeconds;
        if (lastWindow < firstWindow) return [];

        List<DaggerfallPersistentOverlandBand> dispatched = [];
        for (long window = firstWindow; window <= lastWindow; window++)
        {
            // Only dispatch on daytime 6-hour windows (06:00, 12:00, 18:00); skip midnight (00:00).
            if (window % 4 == 0) continue;
            if (ActiveTrafficBandCount() >= MaxActiveTrafficBands) break;
            if (TryDispatchWindow(window, partyPosition) is { } band)
                dispatched.Add(band);
        }
        return dispatched;
    }

    /// <summary>Attempts to deterministically dispatch one regional caravan or courier for <paramref name="windowIndex"/>.</summary>
    internal DaggerfallPersistentOverlandBand? TryDispatchWindow(long windowIndex, (double X, double Y) partyPosition)
    {
        if (ActiveTrafficBandCount() >= MaxActiveTrafficBands) return null;

        bool isCaravan = (windowIndex & 1L) != 0;
        string kindSlug = isCaravan ? "caravan" : "courier";
        string bandId = string.Create(CultureInfo.InvariantCulture, $"{BandIdPrefix}{windowIndex}:{kindSlug}");
        if (_expedition.PersistentBands.Any(b => StringComparer.Ordinal.Equals(b.BandId, bandId)))
            return null;

        DaggerfallSiteId? occupiedSite = _activeSiteId();
        List<DaggerfallSiteRecord> nearby = [];
        foreach (DaggerfallSiteRecord site in _definitions.Locations.Records)
        {
            if (site.Exterior is null) continue;
            if (DaggerfallSourcePopulation.TryResolveClimateRace(_definitions, site, out _) is null) continue;
            double dx = (site.MapPixelX + 0.5) - partyPosition.X;
            double dy = (site.MapPixelY + 0.5) - partyPosition.Y;
            if ((dx * dx + dy * dy) <= NearbyRegionRadiusPixels * NearbyRegionRadiusPixels)
                nearby.Add(site);
        }
        if (nearby.Count < 2) return null;

        ulong seed = StableHash.NonZeroFnv1a64(string.Create(CultureInfo.InvariantCulture,
            $"daggerfall.traffic:{_worldSocialSeed()}:{windowIndex}"));
        int originStart = (int)(seed % (ulong)nearby.Count);
        DaggerfallSiteRecord? origin = null;
        DaggerfallSiteRecord? destination = null;

        for (int i = 0; i < nearby.Count && origin is null; i++)
        {
            DaggerfallSiteRecord candidateOrigin = nearby[(originStart + i) % nearby.Count];
            if (occupiedSite is { } active && candidateOrigin.Id == active) continue;

            List<DaggerfallSiteRecord> destinations = [];
            foreach (DaggerfallSiteRecord candidateDest in nearby)
            {
                if (candidateDest.Id == candidateOrigin.Id) continue;
                double ddx = candidateDest.MapPixelX - candidateOrigin.MapPixelX;
                double ddy = candidateDest.MapPixelY - candidateOrigin.MapPixelY;
                double dist = Math.Sqrt(ddx * ddx + ddy * ddy);
                if (dist >= MinimumRouteDistancePixels && dist <= MaximumRouteDistancePixels)
                    destinations.Add(candidateDest);
            }
            if (destinations.Count == 0) continue;

            origin = candidateOrigin;
            destination = destinations[(int)((seed >> 17) % (ulong)destinations.Count)];
        }

        if (origin is null || destination is null) return null;
        if (TryStageTrafficMember(origin, isCaravan, seed) is not long memberId) return null;

        DaggerfallTravelMapPixel destPixel = new(destination.MapPixelX, destination.MapPixelY);
        DaggerfallWorldProfileKey destProfile = DaggerfallWorldProfileIds.Exterior(destination.Id);
        string bandName = isCaravan
            ? $"{origin.Name} Caravan"
            : $"{origin.Name} Courier";
        double speed = isCaravan ? CaravanSpeedPixelsPerHour : CourierSpeedPixelsPerHour;

        return _overlandParties.DispatchOverlandBand(
            bandId: bandId,
            name: bandName,
            kind: DaggerfallBandKind.Travellers,
            memberIds: [memberId],
            startX: origin.MapPixelX + 0.5,
            startY: origin.MapPixelY + 0.5,
            speedPixelsPerHour: speed,
            destination: new DaggerfallExpeditionMark(destPixel, destination.Id),
            destinationProfile: destProfile,
            camp: false,
            hostile: false);
    }

    private int ActiveTrafficBandCount() =>
        _expedition.PersistentBands.Count(b => b.BandId.StartsWith(BandIdPrefix, StringComparison.Ordinal));

    private long? TryStageTrafficMember(DaggerfallSiteRecord origin, bool isCaravan, ulong seed)
    {
        string? race = DaggerfallSourcePopulation.TryResolveClimateRace(_definitions, origin, out _);
        if (race is null) return null;

        DaggerfallWorldProfileKey originProfile = DaggerfallWorldProfileIds.Exterior(origin.Id);
        string[] preferredSpawners = isCaravan ? CaravanSpawners : CourierSpawners;

        if (origin.Exterior is { Blocks.Count: > 0 })
        {
            foreach (var block in origin.Exterior.Blocks)
            {
                IReadOnlyList<DaggerfallPeopleSlotDefinition> extSlots = _definitions.People.GetRmbExteriorSlots(block.SourceName);
                foreach (DaggerfallPeopleSlotDefinition slot in extSlots)
                {
                    if (slot.CastId is not null || slot.SpawnerId is null) continue;
                    if (!preferredSpawners.Contains(slot.SpawnerId, StringComparer.Ordinal)) continue;

                    string placementKey = DaggerfallPeopleSlots.ExteriorBlockSlotKey(block.X, block.Y, slot.SlotId);
                    DaggerfallResolvedPeopleSlot resolved = DaggerfallPeopleSlots.ResolveSlot(
                        _definitions.People, _definitions.Factions, _worldSocialSeed(), origin.Id, placementKey, slot);
                    string placedId = DaggerfallPeopleSlots.PlacedExteriorBlockPersonId(block.SourceName, block.X, block.Y, slot.SlotId);
                    string slotKey = $"{originProfile.LogicalId}/{placedId}";
                    long durableId = DaggerfallPeopleSlots.SeededSlotIdentity(origin.Id, slotKey);
                    if (!IsAvailableForDispatch(durableId)) continue;

                    int nameSeed = (int)(resolved.SlotSeed & 0x7FFFUL);
                    DaggerfallNpcAppearance look = new(race, resolved.Visual.Gender, resolved.Visual.Archive,
                        resolved.Visual.Record, nameSeed, resolved.PrimaryFactionId ?? 0);
                    string displayName = _generateName(look, origin.Id.Region);
                    ActorHarmPolicyKind harm = DaggerfallPeopleSlots.ParseHarmPolicy(resolved.Harm, ActorHarmPolicyKind.LawProtectedCivilian);
                    ActorInteractionPolicyKind interaction = DaggerfallPeopleSlots.ParseInteractionPolicy(resolved.Interaction);
                    ActorOrigin actorOrigin = ActorOrigin.Seeded(originProfile, placedId, resolved.SpawnerId, harm, interaction);
                    ActorSocialDiff social = new(
                        (int)DaggerfallNpcKind.Static,
                        placedId,
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
                        isCaravan ? "merchant" : "courier",
                        ["talk"],
                        DisplayNameOverride: displayName);
                    StageLedgerEntry(durableId, actorOrigin, social);
                    return durableId;
                }
            }
        }

        // Deterministic seeded traffic slot fallback when a site has no matching exterior block slots.
        string fallbackSpawner = isCaravan ? "general-merchant" : "house-resident";
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int ordinal = (int)((seed + (ulong)attempt) % 64UL);
            string slotId = string.Create(CultureInfo.InvariantCulture, $"population/traffic/{(isCaravan ? "caravan" : "courier")}/{ordinal}");
            long durableId = DaggerfallPeopleSlots.SeededSlotIdentity(origin.Id, $"{originProfile.LogicalId}/{slotId}");
            if (!IsAvailableForDispatch(durableId)) continue;

            string gender = ((seed >> 3) & 1UL) != 0 ? "Female" : "Male";
            int nameSeed = (int)((seed >> 8) & 0x7FFFUL) + 1;
            int factionId = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(_definitions.Factions, origin.Id.Region)?.Id ?? 0;
            DaggerfallNpcAppearance look = new(race, gender, 182, 0, nameSeed, factionId);
            string displayName = _generateName(look, origin.Id.Region);
            ActorOrigin actorOrigin = ActorOrigin.Seeded(originProfile, slotId, fallbackSpawner,
                ActorHarmPolicyKind.LawProtectedCivilian, ActorInteractionPolicyKind.FullDialogue);
            ActorSocialDiff social = new(
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
                isCaravan ? "merchant" : "courier",
                ["talk"],
                DisplayNameOverride: displayName);
            StageLedgerEntry(durableId, actorOrigin, social);
            return durableId;
        }
        return null;
    }

    private void StageLedgerEntry(long durableId, ActorOrigin baselineOrigin, ActorSocialDiff baselineSocial)
    {
        if (_actorLedger.TryGet(durableId, out ActorLedgerEntry? existing) && existing is not null)
        {
            ActorOrigin preservedOrigin = existing.Origin.Kind == ActorOriginKind.SeededSlot
                ? existing.Origin with
                {
                    SpawnerId = existing.Origin.SpawnerId ?? baselineOrigin.SpawnerId,
                    HarmPolicy = existing.Origin.HarmPolicy ?? baselineOrigin.HarmPolicy,
                    InteractionPolicy = existing.Origin.InteractionPolicy ?? baselineOrigin.InteractionPolicy,
                }
                : existing.Origin;
            ActorSocialDiff preservedSocial = existing.Social ?? baselineSocial with
            {
                DisplayNameOverride = existing.Referral?.DisplayNameOverride ?? baselineSocial.DisplayNameOverride,
                ReferralTags = existing.Referral is { Tags.Length: > 0 } referral ? [.. referral.Tags] : null,
            };
            _actorLedger.Upsert(existing with
            {
                Origin = preservedOrigin,
                Social = preservedSocial,
            });
            return;
        }

        _actorLedger.Upsert(new ActorLedgerEntry(durableId, baselineOrigin, Social: baselineSocial));
    }

    private bool IsAvailableForDispatch(long durableId)
    {
        if (_actors.TryGet(durableId, out _)) return false;
        if (!_npcs.IsGameplayActive(durableId)) return false;
        if (_actorLedger.TryGet(durableId, out ActorLedgerEntry? entry) && entry is not null)
        {
            if (entry.Location is not null) return false;
            if (entry.Social is { Presence: not (int)DaggerfallNpcPresence.Active }) return false;
            if (entry.Social is { StagedPartyRole: not null }) return false;
            if (entry.Combat is { DetachedCorpse: not null }
                || entry.Combat is { ForcedHostile: true }
                || entry.Combat is { PromotedArchetype: not null }
                || entry.Combat is { EncounterBandId: not null }
                || entry.Combat is { WabbajackActive: true }
                || entry.Combat is { WabbajackDefinition: not null })
                return false;
            if (entry.Vitals?.Stats.Snapshot.Tracks.Any(track =>
                StringComparer.Ordinal.Equals(track.Id, DaggerfallMechanicsIds.Health.Value) && track.Current <= 0.0) == true)
                return false;
        }
        return true;
    }
}
