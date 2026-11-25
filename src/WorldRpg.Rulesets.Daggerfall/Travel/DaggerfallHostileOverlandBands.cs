using System.Globalization;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>One hostile guild or ruling court faction eligible to dispatch an overland pursuit band.</summary>
internal sealed record DaggerfallHostileFactionStanding(
    int FactionId,
    string Name,
    int Reputation,
    int PreferredMobileId);

/// <summary>Standing snapshot read from Society/Crime at dispatch time so Travel never depends on Society types.</summary>
internal sealed record DaggerfallHostileStandingSnapshot(
    bool WantedOrBanished,
    IReadOnlyList<DaggerfallHostileFactionStanding> HostileGuilds,
    IReadOnlyList<DaggerfallHostileFactionStanding> HostileCourts);

/// <summary>
/// Autonomous world producer for hostile persistent <see cref="ActorLocationKind.OnOverlandBand"/> bands:
/// regional bounty hunters (when wanted or banished), guild hit squads (when deeply hostile with a major guild),
/// and rival court patrols (when deeply hostile with a region's ruling faction). Dispatches minted combatants
/// with durable <see cref="DaggerfallActorLedger"/> entries and <c>Hostile: true</c> so <see cref="DaggerfallExpedition"/>
/// spots, chases, and materializes them cleanly in 3D upon intercept.
/// </summary>
internal sealed class DaggerfallHostileOverlandBands
{
    internal const string BandIdPrefix = "hostile:";
    internal const int MaxActiveHostileBands = 2;
    internal const long DispatchIntervalSeconds = 6L * DaggerfallCalendar.SecondsPerMinute * DaggerfallCalendar.MinutesPerHour;
    internal const int GuildHitSquadThreshold = -25;
    internal const int RivalPatrolThreshold = -30;
    internal const double MinimumDispatchDistancePixels = 2.0;
    internal const double MaximumDispatchDistancePixels = 28.0;
    internal const double BountyHunterSpeedPixelsPerHour = 2.2;
    internal const double HitSquadSpeedPixelsPerHour = 2.3;
    internal const double RivalPatrolSpeedPixelsPerHour = 1.9;

    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallActorLedger _actorLedger;
    private readonly DurableIdentityAllocator _identities;
    private readonly DaggerfallExpedition _expedition;
    private readonly DaggerfallOverlandPartyCoordinator _overlandParties;
    private readonly Func<int, DaggerfallHostileStandingSnapshot> _readStanding;
    private readonly Func<int> _playerLevel;
    private readonly Func<ulong> _worldSocialSeed;
    private readonly Func<DaggerfallSiteId?> _activeSiteId;

    internal DaggerfallHostileOverlandBands(
        DaggerfallDefinitions definitions,
        DaggerfallActorLedger actorLedger,
        DurableIdentityAllocator identities,
        DaggerfallExpedition expedition,
        DaggerfallOverlandPartyCoordinator overlandParties,
        Func<int, DaggerfallHostileStandingSnapshot> readStanding,
        Func<int> playerLevel,
        Func<ulong> worldSocialSeed,
        Func<DaggerfallSiteId?> activeSiteId)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _actorLedger = actorLedger ?? throw new ArgumentNullException(nameof(actorLedger));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _expedition = expedition ?? throw new ArgumentNullException(nameof(expedition));
        _overlandParties = overlandParties ?? throw new ArgumentNullException(nameof(overlandParties));
        _readStanding = readStanding ?? throw new ArgumentNullException(nameof(readStanding));
        _playerLevel = playerLevel ?? throw new ArgumentNullException(nameof(playerLevel));
        _worldSocialSeed = worldSocialSeed ?? throw new ArgumentNullException(nameof(worldSocialSeed));
        _activeSiteId = activeSiteId ?? throw new ArgumentNullException(nameof(activeSiteId));
    }

    /// <summary>Advances hostile overland pursuit scheduling across [<paramref name="before"/>, <paramref name="after"/>].</summary>
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
            if (ActiveHostileBandCount() >= MaxActiveHostileBands) break;
            if (TryDispatchWindow(window, partyPosition) is { } band)
                dispatched.Add(band);
        }
        return dispatched;
    }

    /// <summary>Deterministically dispatches one hostile pursuit band for <paramref name="windowIndex"/> if standing warrants it.</summary>
    internal DaggerfallPersistentOverlandBand? TryDispatchWindow(long windowIndex, (double X, double Y) partyPosition)
    {
        if (ActiveHostileBandCount() >= MaxActiveHostileBands) return null;

        DaggerfallTravelMapPixel partyPixel = new(
            Math.Clamp((int)Math.Floor(partyPosition.X), 0, _definitions.Terrain.Width - 1),
            Math.Clamp((int)Math.Floor(partyPosition.Y), 0, _definitions.Terrain.Height - 1));
        DaggerfallSiteRecord hostSite = _expedition.HostAt(partyPixel);
        int region = hostSite.Id.Region;
        string regionName = _definitions.BuildingNames.RegionName(region);

        DaggerfallHostileStandingSnapshot standing = _readStanding(region);
        List<(string Slug, string BandName, int PreferredMobileId, int MemberCount, double Speed)> candidates = [];
        if (standing.WantedOrBanished)
            candidates.Add(("bounty", $"{regionName} Bounty Hunters", 144, 2, BountyHunterSpeedPixelsPerHour));
        foreach (DaggerfallHostileFactionStanding guild in standing.HostileGuilds
            .Where(g => g.Reputation <= GuildHitSquadThreshold)
            .OrderBy(g => g.Reputation).ThenBy(g => g.FactionId))
        {
            candidates.Add(($"hitsquad-{guild.FactionId}", $"{guild.Name} Hit Squad", guild.PreferredMobileId, 2, HitSquadSpeedPixelsPerHour));
        }
        foreach (DaggerfallHostileFactionStanding court in standing.HostileCourts
            .Where(c => c.Reputation <= RivalPatrolThreshold)
            .OrderBy(c => c.Reputation).ThenBy(c => c.FactionId))
        {
            candidates.Add(($"patrol-{court.FactionId}", $"{court.Name} Patrol", court.PreferredMobileId, 2, RivalPatrolSpeedPixelsPerHour));
        }
        if (candidates.Count == 0) return null;

        ulong seed = StableHash.NonZeroFnv1a64(string.Create(CultureInfo.InvariantCulture,
            $"daggerfall.hostile-band:{_worldSocialSeed()}:{region}:{windowIndex}"));
        var chosen = candidates[(int)(seed % (ulong)candidates.Count)];
        string bandId = string.Create(CultureInfo.InvariantCulture, $"{BandIdPrefix}{chosen.Slug}:{windowIndex}");
        if (_expedition.PersistentBands.Any(b => StringComparer.Ordinal.Equals(b.BandId, bandId)))
            return null;

        DaggerfallActorDefinition? enemyDef = ResolveCombatantDefinition(chosen.PreferredMobileId);
        if (enemyDef is null) return null;

        (double startX, double startY) = ResolveDispatchOrigin(partyPosition, seed);
        int level = Math.Max(1, _playerLevel());
        List<long> memberIds = [];
        for (int i = 0; i < chosen.MemberCount; i++)
        {
            DurableIdentityReference identity = _identities.Allocate(DurableIdentityKind.Actor);
            long durableId = checked((long)identity.Value);
            ulong memberSeed = StableHash.NonZeroFnv1a64($"{bandId}:member:{i}:{seed}");
            string gender = ((memberSeed >> 5) & 1UL) != 0 ? "female" : "male";
            ActorOrigin origin = ActorOrigin.MintedActor(enemyDef.Id.Value, level, memberSeed, gender);
            ActorCombatStateDiff combat = new(ForcedHostile: true);
            _actorLedger.Upsert(new ActorLedgerEntry(durableId, origin, Combat: combat));
            memberIds.Add(durableId);
        }

        return _overlandParties.DispatchOverlandBand(
            bandId: bandId,
            name: chosen.BandName,
            kind: DaggerfallBandKind.Bandits,
            memberIds: memberIds,
            startX: startX,
            startY: startY,
            speedPixelsPerHour: chosen.Speed,
            destination: new DaggerfallExpeditionMark(partyPixel, hostSite.Id),
            destinationProfile: null,
            camp: false,
            hostile: true,
            mobile: enemyDef.MobileId);
    }

    private int ActiveHostileBandCount() =>
        _expedition.PersistentBands.Count(b => b.BandId.StartsWith(BandIdPrefix, StringComparison.Ordinal));

    private DaggerfallActorDefinition? ResolveCombatantDefinition(int preferredMobileId)
    {
        DaggerfallActorDefinition? preferred = _definitions.Actors.Values
            .FirstOrDefault(a => a.Kind == DaggerfallActorKinds.EnemyClass && a.MobileId == preferredMobileId);
        if (preferred is not null) return preferred;
        return _definitions.Actors.Values
            .Where(a => a.Kind == DaggerfallActorKinds.EnemyClass && a.MobileId is not null)
            .OrderBy(a => a.MobileId)
            .FirstOrDefault();
    }

    private (double X, double Y) ResolveDispatchOrigin((double X, double Y) partyPosition, ulong seed)
    {
        DaggerfallSiteId? occupied = _activeSiteId();
        List<DaggerfallSiteRecord> nearby = [];
        foreach (DaggerfallSiteRecord site in _definitions.Locations.Records)
        {
            if (site.Exterior is null) continue;
            if (occupied is { } active && site.Id == active) continue;
            double dx = (site.MapPixelX + 0.5) - partyPosition.X;
            double dy = (site.MapPixelY + 0.5) - partyPosition.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist >= MinimumDispatchDistancePixels && dist <= MaximumDispatchDistancePixels)
                nearby.Add(site);
        }
        if (nearby.Count > 0)
        {
            DaggerfallSiteRecord origin = nearby[(int)((seed >> 11) % (ulong)nearby.Count)];
            return (origin.MapPixelX + 0.5, origin.MapPixelY + 0.5);
        }

        double angle = ((seed >> 19) % 360UL) * (Math.PI / 180.0);
        double offset = 4.0;
        double x = Math.Clamp(partyPosition.X + Math.Cos(angle) * offset, 0.5, _definitions.Terrain.Width - 0.5);
        double y = Math.Clamp(partyPosition.Y + Math.Sin(angle) * offset, 0.5, _definitions.Terrain.Height - 0.5);
        return (x, y);
    }
}
