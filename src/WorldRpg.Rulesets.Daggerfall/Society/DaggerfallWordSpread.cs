using System.Globalization;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What kind of standing or news event is spreading outward from its origin.</summary>
internal enum DaggerfallWordEventKind
{
    /// <summary>Reported crime / regional bounty penalty spreading from the crime scene.</summary>
    CrimeBounty = 0,

    /// <summary>Good or bad faction standing or guild promotion spreading from where it was earned.</summary>
    FactionStanding = 1,

    /// <summary>Public news, decree, or rumour spreading along courier roads.</summary>
    NewsRumor = 2,
}

/// <summary>Whether a wavefront event has reached a queried town or site.</summary>
internal enum DaggerfallWordArrivalStatus
{
    /// <summary>Target site lies within the configurable instant-news bubble around the origin.</summary>
    InstantBubble = 0,

    /// <summary>Couriers have had enough elapsed road-travel time to reach the target site.</summary>
    ArrivedByCourier = 1,

    /// <summary>A sparse override (such as player-delivered dispatch) accelerated arrival here.</summary>
    AcceleratedOverride = 2,

    /// <summary>Still in transit outside the instant bubble (may be delayed by war, monster surge, or plague).</summary>
    InTransit = 3,

    /// <summary>Blocked from reaching the target site (intercepted courier or sparse suppression override).</summary>
    Blocked = 4,
}

/// <summary>Result of evaluating one <see cref="DaggerfallWordWavefrontSave"/> at a specific site and calendar minute.</summary>
internal sealed record DaggerfallWordArrivalEvaluation(
    string EventId,
    DaggerfallWordArrivalStatus Status,
    double DistancePixels,
    double EffectiveSpeedPixelsPerHour,
    double CourierDisruptionMultiplier,
    long RemainingMinutes,
    string? DisruptionReason = null)
{
    internal bool HasArrived => Status is DaggerfallWordArrivalStatus.InstantBubble
        or DaggerfallWordArrivalStatus.ArrivedByCourier
        or DaggerfallWordArrivalStatus.AcceleratedOverride;
}

/// <summary>
/// One sparse persisted wavefront event (<c>origin pixel + emission time + delta/news + sparse overrides</c>).
/// Advances strictly from elapsed calendar time and road distance, so time spent in a dungeon advances word
/// naturally with zero off-screen stepping.
/// </summary>
internal sealed record DaggerfallWordWavefrontSave(
    [property: JsonRequired] string EventId,
    [property: JsonRequired] DaggerfallWordEventKind Kind,
    [property: JsonRequired] int OriginRegion,
    [property: JsonRequired] int OriginPixelX,
    [property: JsonRequired] int OriginPixelY,
    [property: JsonRequired] long EmittedAtMinute,
    int? FactionId = null,
    int Delta = 0,
    int? GuildRank = null,
    string? Headline = null,
    string? Body = null,
    int[]? SuppressedRegions = null,
    int[]? AcceleratedRegions = null,
    bool CourierIntercepted = false,
    double? FrozenRadiusPixels = null)
{
    internal DaggerfallWordWavefrontSave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(EventId);
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, $"Word wavefront '{EventId}' names an invalid kind.");
        if (OriginRegion is < 0 or > 61)
            throw new ArgumentOutOfRangeException(nameof(OriginRegion), OriginRegion, $"Word wavefront '{EventId}' names no classic region.");
        if (EmittedAtMinute < 0)
            throw new ArgumentOutOfRangeException(nameof(EmittedAtMinute), EmittedAtMinute, $"Word wavefront '{EventId}' emitted before calendar origin.");
        if (FactionId is < 0)
            throw new ArgumentOutOfRangeException(nameof(FactionId), FactionId, $"Word wavefront '{EventId}' cannot have a negative faction id.");
        if (FrozenRadiusPixels is double fr && (!double.IsFinite(fr) || fr < 0d))
            throw new ArgumentOutOfRangeException(nameof(FrozenRadiusPixels), FrozenRadiusPixels, $"Word wavefront '{EventId}' frozen radius must be finite and non-negative.");
        if (SuppressedRegions is not null)
        {
            foreach (int r in SuppressedRegions)
                if (r is < 0 or > 61) throw new ArgumentOutOfRangeException(nameof(SuppressedRegions), r, "Suppressed region is outside 0..61.");
        }
        if (AcceleratedRegions is not null)
        {
            foreach (int r in AcceleratedRegions)
                if (r is < 0 or > 61) throw new ArgumentOutOfRangeException(nameof(AcceleratedRegions), r, "Accelerated region is outside 0..61.");
        }
        return this;
    }
}

/// <summary>Sparse persisted word-spread snapshot. Empty when no standing/news wavefronts are tracked.</summary>
internal sealed record DaggerfallWordSpreadSave(DaggerfallWordWavefrontSave[] Wavefronts)
{
    internal static DaggerfallWordSpreadSave Empty { get; } = new([]);

    internal DaggerfallWordSpreadSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Wavefronts);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallWordWavefrontSave wf in Wavefronts)
        {
            ArgumentNullException.ThrowIfNull(wf);
            wf.Validate();
            if (!ids.Add(wf.EventId))
                throw new ArgumentException($"Saved word spread repeats event id '{wf.EventId}'.", nameof(Wavefronts));
        }
        return this;
    }
}

/// <summary>
/// Owns elapsed-time road-distance news and standing spread (good and bad). Within <see cref="DaggerfallSocialTuning.InstantNewsBubblePixels"/>
/// of an event's origin, word is instant; outside the bubble, word advances by courier speed modified by active
/// regional <see cref="IDaggerfallWorldConditionSource"/> disruptions (war zones, monster surges, plague quarantines)
/// and sparse overrides (intercepted or player-delivered couriers).
/// </summary>
internal sealed class DaggerfallWordSpread(
    DaggerfallSocialTuning tuning,
    IDaggerfallWorldConditionSource conditions)
{
    internal const long MaxRetainedWavefrontMinutes = 30L * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour;
    internal const double MinCourierDisruptionMultiplier = 0.10;
    private const double GrainShortageCourierMultiplier = 0.75;

    private readonly DaggerfallSocialTuning _tuning = tuning.Validate();
    private readonly IDaggerfallWorldConditionSource _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
    private readonly Dictionary<string, DaggerfallWordWavefrontSave> _wavefronts = new(StringComparer.Ordinal);
    private int _nextSequence;

    internal DaggerfallSocialTuning Tuning => _tuning;
    internal int Count => _wavefronts.Count;
    internal IReadOnlyCollection<DaggerfallWordWavefrontSave> Wavefronts => _wavefronts.Values;

    /// <summary>Records a reported regional crime penalty wavefront originating at <paramref name="originSite"/>.</summary>
    internal DaggerfallWordWavefrontSave RecordCrime(
        string operationId,
        int region,
        int reputationLoss,
        DaggerfallSiteRecord originSite,
        long currentMinute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(originSite);
        int delta = reputationLoss > 0 ? -reputationLoss : reputationLoss;
        string id = $"wf:crime:{operationId}";
        DaggerfallWordWavefrontSave wf = new(
            EventId: id,
            Kind: DaggerfallWordEventKind.CrimeBounty,
            OriginRegion: region,
            OriginPixelX: originSite.MapPixelX,
            OriginPixelY: originSite.MapPixelY,
            EmittedAtMinute: currentMinute,
            Delta: delta);
        wf.Validate();
        _wavefronts[id] = wf;
        return wf;
    }

    /// <summary>Records a faction standing change or guild rank promotion spreading outward from <paramref name="originSite"/>.</summary>
    internal DaggerfallWordWavefrontSave RecordFactionStanding(
        int factionId,
        int delta,
        int? guildRank,
        DaggerfallSiteRecord originSite,
        long currentMinute,
        string? headline = null,
        string? body = null,
        string? eventId = null)
    {
        ArgumentNullException.ThrowIfNull(originSite);
        string id = string.IsNullOrWhiteSpace(eventId)
            ? string.Create(CultureInfo.InvariantCulture, $"wf:standing:{factionId}:{currentMinute}:{++_nextSequence}")
            : eventId;
        DaggerfallWordWavefrontSave wf = new(
            EventId: id,
            Kind: DaggerfallWordEventKind.FactionStanding,
            OriginRegion: originSite.Id.Region,
            OriginPixelX: originSite.MapPixelX,
            OriginPixelY: originSite.MapPixelY,
            EmittedAtMinute: currentMinute,
            FactionId: factionId,
            Delta: delta,
            GuildRank: guildRank,
            Headline: headline,
            Body: body);
        wf.Validate();
        _wavefronts[id] = wf;
        return wf;
    }

    /// <summary>Records a news/rumour wavefront spreading outward from <paramref name="originSite"/>.</summary>
    internal DaggerfallWordWavefrontSave RecordNews(
        string eventId,
        string headline,
        string body,
        DaggerfallSiteRecord originSite,
        long currentMinute,
        int? factionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(headline);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentNullException.ThrowIfNull(originSite);
        DaggerfallWordWavefrontSave wf = new(
            EventId: eventId,
            Kind: DaggerfallWordEventKind.NewsRumor,
            OriginRegion: originSite.Id.Region,
            OriginPixelX: originSite.MapPixelX,
            OriginPixelY: originSite.MapPixelY,
            EmittedAtMinute: currentMinute,
            FactionId: factionId,
            Headline: headline,
            Body: body);
        wf.Validate();
        _wavefronts[eventId] = wf;
        return wf;
    }

    internal bool TryGet(string eventId, out DaggerfallWordWavefrontSave? wavefront) =>
        _wavefronts.TryGetValue(eventId, out wavefront);

    /// <summary>
    /// Sparse override: intercepting a courier freezes this wavefront at the radius it had reached at
    /// <paramref name="currentMinute"/> (at least the instant bubble), preventing word from reaching further towns.
    /// </summary>
    internal bool InterceptWavefront(string eventId, long currentMinute, int? currentDay = null)
    {
        if (!_wavefronts.TryGetValue(eventId, out DaggerfallWordWavefrontSave? wf))
            return false;

        int day = currentDay ?? checked((int)(currentMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        (double multiplier, _) = CourierDisruption(wf.OriginRegion, wf.OriginRegion, day);
        double elapsedHours = Math.Max(0d, (currentMinute - wf.EmittedAtMinute) / 60d);
        double reachedRadius = _tuning.InstantNewsBubblePixels + elapsedHours * (_tuning.CourierSpeedPixelsPerHour * multiplier);
        double frozen = wf.FrozenRadiusPixels is double existing ? Math.Min(existing, reachedRadius) : reachedRadius;
        _wavefronts[eventId] = wf with { CourierIntercepted = true, FrozenRadiusPixels = frozen };
        return true;
    }

    /// <summary>Sparse override: explicitly suppresses a wavefront from being recognized in <paramref name="region"/>.</summary>
    internal bool SuppressInRegion(string eventId, int region)
    {
        if (region is < 0 or > 61 || !_wavefronts.TryGetValue(eventId, out DaggerfallWordWavefrontSave? wf))
            return false;
        HashSet<int> suppressed = [.. wf.SuppressedRegions ?? []];
        suppressed.Add(region);
        HashSet<int> accelerated = [.. wf.AcceleratedRegions ?? []];
        accelerated.Remove(region);
        _wavefronts[eventId] = wf with
        {
            SuppressedRegions = [.. suppressed.Order()],
            AcceleratedRegions = accelerated.Count > 0 ? [.. accelerated.Order()] : null,
        };
        return true;
    }

    /// <summary>Sparse override: accelerates arrival of a wavefront in <paramref name="region"/> (e.g. player-delivered dispatch).</summary>
    internal bool AccelerateInRegion(string eventId, int region)
    {
        if (region is < 0 or > 61 || !_wavefronts.TryGetValue(eventId, out DaggerfallWordWavefrontSave? wf))
            return false;
        HashSet<int> accelerated = [.. wf.AcceleratedRegions ?? []];
        accelerated.Add(region);
        HashSet<int> suppressed = [.. wf.SuppressedRegions ?? []];
        suppressed.Remove(region);
        _wavefronts[eventId] = wf with
        {
            AcceleratedRegions = [.. accelerated.Order()],
            SuppressedRegions = suppressed.Count > 0 ? [.. suppressed.Order()] : null,
        };
        return true;
    }

    /// <summary>Evaluates whether <paramref name="wavefront"/> has reached <paramref name="targetSite"/> at <paramref name="currentMinute"/>.</summary>
    internal DaggerfallWordArrivalEvaluation EvaluateAt(
        DaggerfallWordWavefrontSave wavefront,
        DaggerfallSiteRecord targetSite,
        long currentMinute,
        int? currentDay = null)
    {
        ArgumentNullException.ThrowIfNull(wavefront);
        ArgumentNullException.ThrowIfNull(targetSite);
        int targetRegion = targetSite.Id.Region;
        double dx = targetSite.MapPixelX - wavefront.OriginPixelX;
        double dy = targetSite.MapPixelY - wavefront.OriginPixelY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        int day = currentDay ?? checked((int)(currentMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        (double disruption, string? reason) = CourierDisruption(wavefront.OriginRegion, targetRegion, day);
        double effectiveSpeed = _tuning.CourierSpeedPixelsPerHour * disruption;

        // 1. Sparse overrides take precedence.
        if (wavefront.AcceleratedRegions is { Length: > 0 } acc && acc.Contains(targetRegion))
            return new(wavefront.EventId, DaggerfallWordArrivalStatus.AcceleratedOverride, distance, effectiveSpeed, disruption, 0, reason);
        if (wavefront.SuppressedRegions is { Length: > 0 } sup && sup.Contains(targetRegion))
            return new(wavefront.EventId, DaggerfallWordArrivalStatus.Blocked, distance, effectiveSpeed, disruption, long.MaxValue, "suppressed");

        // 2. Instant bubble: within configurable radius of origin, word is immediate.
        if (distance <= _tuning.InstantNewsBubblePixels + 1e-9)
            return new(wavefront.EventId, DaggerfallWordArrivalStatus.InstantBubble, distance, effectiveSpeed, disruption, 0, reason);

        // 3. Intercepted courier stops further spread beyond the frozen radius.
        if (wavefront.CourierIntercepted)
        {
            double frozen = wavefront.FrozenRadiusPixels ?? _tuning.InstantNewsBubblePixels;
            return distance <= frozen + 1e-9
                ? new(wavefront.EventId, DaggerfallWordArrivalStatus.ArrivedByCourier, distance, effectiveSpeed, disruption, 0, reason)
                : new(wavefront.EventId, DaggerfallWordArrivalStatus.Blocked, distance, effectiveSpeed, disruption, long.MaxValue, "courier intercepted");
        }

        // 4. Elapsed-time road-distance courier spread outside the bubble.
        double roadDistance = Math.Max(0d, distance - _tuning.InstantNewsBubblePixels);
        long travelMinutes = checked((long)Math.Ceiling((roadDistance / effectiveSpeed) * 60d));
        long elapsedMinutes = Math.Max(0L, currentMinute - wavefront.EmittedAtMinute);
        if (elapsedMinutes >= travelMinutes)
            return new(wavefront.EventId, DaggerfallWordArrivalStatus.ArrivedByCourier, distance, effectiveSpeed, disruption, 0, reason);

        return new(wavefront.EventId, DaggerfallWordArrivalStatus.InTransit, distance, effectiveSpeed, disruption,
            travelMinutes - elapsedMinutes, reason);
    }

    /// <summary>
    /// Computes the combined courier speed multiplier in [<see cref="MinCourierDisruptionMultiplier"/>, 1.0] from
    /// active world conditions in either the origin or destination region. Both crimes committed inside a war zone /
    /// monster surge and good news (like Mages Guild rank gossip) heading to a front-line town share this disruption.
    /// </summary>
    internal (double Multiplier, string? Reason) CourierDisruption(int originRegion, int targetRegion, int currentDay)
    {
        double multiplier = 1.0;
        List<string> reasons = [];

        int[] regions = originRegion == targetRegion ? [originRegion] : [originRegion, targetRegion];
        int warSeverity = 0;
        int surgeSeverity = 0;
        int plagueSeverity = 0;
        int shortageSeverity = 0;

        foreach (int region in regions)
        {
            if (region is < 0 or > 61) continue;
            warSeverity = Math.Max(warSeverity, _conditions.MaxSeverity(region, DaggerfallWorldConditionKind.BorderTension, currentDay));
            surgeSeverity = Math.Max(surgeSeverity, _conditions.MaxSeverity(region, DaggerfallWorldConditionKind.MonsterSurge, currentDay));
            plagueSeverity = Math.Max(plagueSeverity, _conditions.MaxSeverity(region, DaggerfallWorldConditionKind.PlagueScare, currentDay));
            shortageSeverity = Math.Max(shortageSeverity, _conditions.MaxSeverity(region, DaggerfallWorldConditionKind.GrainShortage, currentDay));
        }

        if (warSeverity > 0)
        {
            double factor = warSeverity >= 3 ? Math.Min(_tuning.WarZoneCourierSpeedMultiplier, 0.15) : _tuning.WarZoneCourierSpeedMultiplier;
            multiplier *= factor;
            reasons.Add("war-zone border disruption");
        }
        if (surgeSeverity > 0)
        {
            double factor = surgeSeverity >= 3 ? Math.Min(_tuning.MonsterSurgeCourierSpeedMultiplier, 0.25) : _tuning.MonsterSurgeCourierSpeedMultiplier;
            multiplier *= factor;
            reasons.Add("monster surge on the roads");
        }
        if (plagueSeverity > 0)
        {
            multiplier *= _tuning.PlagueCourierSpeedMultiplier;
            reasons.Add("plague quarantine");
        }
        if (shortageSeverity > 0)
        {
            multiplier *= GrainShortageCourierMultiplier;
            reasons.Add("raided supply roads");
        }

        double clamped = Math.Clamp(multiplier, MinCourierDisruptionMultiplier, 1.0);
        return (clamped, reasons.Count > 0 ? string.Join(", ", reasons) : null);
    }

    /// <summary>
    /// Total unarrived crime reputation penalty (a non-negative offset to add back to raw regional reputation)
    /// for crimes recorded in <paramref name="targetSite"/>'s region whose word has NOT yet reached <paramref name="targetSite"/>.
    /// </summary>
    internal int UnarrivedCrimePenaltyOffset(DaggerfallSiteRecord? targetSite, long currentMinute, int? currentDay = null)
    {
        if (targetSite is null || _wavefronts.Count == 0)
            return 0;

        int offset = 0;
        int region = targetSite.Id.Region;
        foreach (DaggerfallWordWavefrontSave wf in _wavefronts.Values)
        {
            if (wf.Kind != DaggerfallWordEventKind.CrimeBounty || wf.OriginRegion != region || wf.Delta >= 0)
                continue;
            if (!EvaluateAt(wf, targetSite, currentMinute, currentDay).HasArrived)
                offset += -wf.Delta;
        }
        return offset;
    }

    /// <summary>
    /// Total unarrived faction standing delta for <paramref name="factionId"/> at <paramref name="targetSite"/>:
    /// subtracting this from raw standing gives what <paramref name="targetSite"/> has actually heard so far
    /// (both positive renown/guild promotions and negative faction infamy wait on couriers outside the bubble).
    /// </summary>
    internal int UnarrivedFactionStandingDelta(int factionId, DaggerfallSiteRecord? targetSite, long currentMinute, int? currentDay = null)
    {
        if (targetSite is null || factionId <= 0 || _wavefronts.Count == 0)
            return 0;

        int unarrived = 0;
        foreach (DaggerfallWordWavefrontSave wf in _wavefronts.Values)
        {
            if (wf.Kind != DaggerfallWordEventKind.FactionStanding || wf.FactionId != factionId || wf.Delta == 0)
                continue;
            if (!EvaluateAt(wf, targetSite, currentMinute, currentDay).HasArrived)
                unarrived += wf.Delta;
        }
        return unarrived;
    }

    /// <summary>
    /// Whether a recorded guild rank promotion for <paramref name="factionId"/> has arrived at <paramref name="targetSite"/>.
    /// Returns true if no promotion wavefront is pending (baseline), or if the latest promotion wavefront has arrived.
    /// </summary>
    internal bool IsGuildRankKnownAt(int factionId, int currentRank, DaggerfallSiteRecord? targetSite, long currentMinute, int? currentDay = null)
    {
        if (targetSite is null || factionId <= 0 || _wavefronts.Count == 0)
            return true;

        DaggerfallWordWavefrontSave? latestRankWavefront = _wavefronts.Values
            .Where(wf => wf.Kind == DaggerfallWordEventKind.FactionStanding
                && wf.FactionId == factionId
                && wf.GuildRank is int rank && rank >= currentRank)
            .OrderByDescending(wf => wf.EmittedAtMinute)
            .FirstOrDefault();

        return latestRankWavefront is null || EvaluateAt(latestRankWavefront, targetSite, currentMinute, currentDay).HasArrived;
    }

    /// <summary>Returns all news/standing wavefronts that have arrived at <paramref name="targetSite"/> (newest first).</summary>
    internal IReadOnlyList<DaggerfallWordWavefrontSave> ArrivedNewsAt(DaggerfallSiteRecord targetSite, long currentMinute, int? currentDay = null)
    {
        ArgumentNullException.ThrowIfNull(targetSite);
        if (_wavefronts.Count == 0)
            return [];

        return [.. _wavefronts.Values
            .Where(wf => wf.Kind is DaggerfallWordEventKind.NewsRumor or DaggerfallWordEventKind.FactionStanding
                && !string.IsNullOrWhiteSpace(wf.Body)
                && EvaluateAt(wf, targetSite, currentMinute, currentDay).HasArrived)
            .OrderByDescending(wf => wf.EmittedAtMinute)
            .ThenBy(wf => wf.EventId, StringComparer.Ordinal)];
    }

    /// <summary>Prunes ancient unintercepted wavefronts older than <see cref="MaxRetainedWavefrontMinutes"/>.</summary>
    internal int PruneExpired(long currentMinute)
    {
        List<string> stale = [.. _wavefronts.Values
            .Where(wf => !wf.CourierIntercepted
                && (wf.SuppressedRegions is null || wf.SuppressedRegions.Length == 0)
                && currentMinute - wf.EmittedAtMinute >= MaxRetainedWavefrontMinutes)
            .Select(wf => wf.EventId)];
        foreach (string id in stale) _wavefronts.Remove(id);
        return stale.Count;
    }

    internal DaggerfallWordSpreadSave Capture() =>
        new([.. _wavefronts.Values.OrderBy(wf => wf.EventId, StringComparer.Ordinal)]);

    internal void Restore(DaggerfallWordSpreadSave? saved)
    {
        _wavefronts.Clear();
        if (saved is null)
            return;
        saved.Validate();
        foreach (DaggerfallWordWavefrontSave wf in saved.Wavefronts)
            _wavefronts[wf.EventId] = wf;
    }
}
