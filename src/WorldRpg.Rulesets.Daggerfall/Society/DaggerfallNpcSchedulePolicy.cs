using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Coarse placement resolved by <see cref="DaggerfallNpcSchedulePolicy"/>.</summary>
internal enum DaggerfallSchedulePlacement
{
    ExteriorStreet,
    InteriorBuilding,
    ShelteredInHome,
}

/// <summary>Specific interior building role when <see cref="DaggerfallSchedulePlacement.InteriorBuilding"/> applies.</summary>
internal enum DaggerfallScheduleBuildingTarget
{
    None,
    HomePost,
    Tavern,
    Temple,
}

/// <summary>Elapsed in-game minutes since the onset of each weather-sheltering class.</summary>
internal readonly record struct DaggerfallWeatherShelterElapsed(int PrecipElapsedMinutes, int SevereElapsedMinutes)
{
    internal static DaggerfallWeatherShelterElapsed Settled { get; } = new(int.MaxValue, int.MaxValue);
}

/// <summary>
/// Tracks live weather-sheltering onset separately for precipitating weather (<c>Rain</c>/<c>Thunder</c>/<c>Snow</c>)
/// and severe weather (<c>Thunder</c>/<c>Snow</c>) so escalating from sustained rain to a thunderstorm preserves
/// already-sheltered rain-sensitive walkers while still staggering rain-tolerant roles (<c>underworld-night</c>).
/// </summary>
internal sealed class DaggerfallWeatherShelterClock
{
    private const long SettledSentinel = long.MinValue;
    private DaggerfallWeatherKind? _observed;
    private long? _precipStartedMinute;
    private long? _severeStartedMinute;

    internal static bool IsPrecipitating(DaggerfallWeatherKind weather) =>
        weather is DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder or DaggerfallWeatherKind.Snow;

    internal static bool IsSevere(DaggerfallWeatherKind weather) =>
        weather is DaggerfallWeatherKind.Thunder or DaggerfallWeatherKind.Snow;

    internal void Reset()
    {
        _observed = null;
        _precipStartedMinute = null;
        _severeStartedMinute = null;
    }

    internal DaggerfallWeatherShelterElapsed Observe(DaggerfallWeatherKind current, long currentMinute)
    {
        bool currentPrecip = IsPrecipitating(current);
        bool currentSevere = IsSevere(current);

        if (_observed is null)
        {
            // Initial load, site arrival, or post-time-jump reconciliation settles immediately.
            _observed = current;
            _precipStartedMinute = currentPrecip ? SettledSentinel : null;
            _severeStartedMinute = currentSevere ? SettledSentinel : null;
            return new(
                currentPrecip ? int.MaxValue : 0,
                currentSevere ? int.MaxValue : 0);
        }

        bool prevPrecip = IsPrecipitating(_observed.Value);
        bool prevSevere = IsSevere(_observed.Value);

        if (!currentPrecip)
            _precipStartedMinute = null;
        else if (!prevPrecip)
            _precipStartedMinute = currentMinute;

        if (!currentSevere)
            _severeStartedMinute = null;
        else if (!prevSevere)
            _severeStartedMinute = currentMinute;

        _observed = current;
        return new(
            Elapsed(_precipStartedMinute, currentMinute),
            Elapsed(_severeStartedMinute, currentMinute));
    }

    private static int Elapsed(long? startedMinute, long currentMinute) => startedMinute switch
    {
        null => 0,
        SettledSentinel => int.MaxValue,
        long started => (int)Math.Clamp(currentMinute - started, 0L, int.MaxValue),
    };
}

/// <summary>Deterministic schedule and weather placement outcome for one NPC slot at one instant.</summary>
internal sealed record DaggerfallScheduleDecision(
    DaggerfallSchedulePlacement Placement,
    DaggerfallScheduleBuildingTarget BuildingTarget,
    string ScheduleId,
    string Reason,
    int JitterMinutes,
    int StormDelayMinutes,
    bool Pinned);

/// <summary>
/// Pure, deterministic NPC daily schedule and weather-sheltering resolver (#11085 / #11100).
/// Computes <c>(WorldSocialSeed, SiteId, SlotKey, Profile, MinuteOfDay, Weather) -> DaggerfallScheduleDecision</c>
/// with zero background simulation and zero save bytes for untouched NPCs.
/// Reads shop/building opening hours directly from <see cref="DaggerfallBuildingEntryPolicy"/> (single source of truth).
/// </summary>
internal static class DaggerfallNpcSchedulePolicy
{
    internal const string StreetWalkerScheduleId = "street-walker";
    internal const string CityGuardScheduleId = "city-guard";
    internal const string UnderworldNightScheduleId = "underworld-night";
    internal const string AlwaysStationedScheduleId = "always-stationed";
    internal const string ShopkeeperScheduleId = "shopkeeper";
    internal const string GuildMemberScheduleId = "guild-member";
    internal const string TavernStaffScheduleId = "tavern-staff";
    internal const string CourtNobleScheduleId = "court-noble";

    internal const int MinutesPerDay = DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour;

    /// <summary>Minimum exterior population pool size before seeding the late-night underworld pulse.</summary>
    internal const int MinimumTownPopulationForUnderworldPulse = 8;

    /// <summary>1-in-12 non-guard exterior slots (~8.3%) carry the late-night underworld pulse in populated towns.</summary>
    internal const ulong UnderworldPulseDivisor = 12UL;

    /// <summary>
    /// Deterministically resolves the schedule profile ID for an exterior source-population placement:
    /// city guards take <c>city-guard</c>, a small seeded slice of non-guard placements in towns takes
    /// <c>underworld-night</c>, and ordinary civilians take <c>street-walker</c>.
    /// </summary>
    internal static string ResolveExteriorScheduleId(
        ulong worldSocialSeed,
        DaggerfallSiteId siteId,
        string slotKey,
        bool isGuard,
        int totalPopulationCount = 0)
    {
        if (isGuard)
            return CityGuardScheduleId;
        if (totalPopulationCount >= MinimumTownPopulationForUnderworldPulse)
        {
            ulong pulseHash = StableHash.NonZeroFnv1a64(
                $"daggerfall.schedule.underworld:{worldSocialSeed}:{siteId.Region}:{siteId.Index}:{slotKey}");
            if (pulseHash % UnderworldPulseDivisor == 0UL)
                return UnderworldNightScheduleId;
        }
        return StreetWalkerScheduleId;
    }

    /// <summary>
    /// Evaluates one deterministic NPC slot's placement at <paramref name="minuteOfDay"/> and <paramref name="weather"/>.
    /// </summary>
    internal static DaggerfallScheduleDecision Evaluate(
        ulong worldSocialSeed,
        DaggerfallSiteId siteId,
        string slotKey,
        DaggerfallActorScheduleProfile profile,
        int minuteOfDay,
        DaggerfallWeatherKind weather,
        int? buildingType = null,
        int holidayId = 0,
        int weatherElapsedMinutes = int.MaxValue,
        bool isActiveQuestor = false,
        bool isOfficeHolder = false,
        bool isWithParty = false,
        bool isOnOverlandBand = false,
        int? severeWeatherElapsedMinutes = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);

        int normalizedMinute = NormalizeMinute(minuteOfDay);
        (int jitter, int stormDelay, int tavernRoll) = SlotRolls(worldSocialSeed, siteId, slotKey, profile);

        // 1. Custody & post-pinning exclusions: active party/overland members, active questors, and office holders
        //    are never pulled away by ambient schedule or weather transitions.
        if (isWithParty)
            return new(DaggerfallSchedulePlacement.ShelteredInHome, DaggerfallScheduleBuildingTarget.None,
                profile.Id, "with-party", jitter, stormDelay, Pinned: true);
        if (isOnOverlandBand)
            return new(DaggerfallSchedulePlacement.ShelteredInHome, DaggerfallScheduleBuildingTarget.None,
                profile.Id, "on-overland-band", jitter, stormDelay, Pinned: true);
        if (isActiveQuestor)
            return PinnedAtPost(profile, "active-questor", jitter, stormDelay);
        if (isOfficeHolder)
            return PinnedAtPost(profile, "office-holder", jitter, stormDelay);

        if (profile.PrimaryMode == DaggerfallSchedulePrimaryMode.AlwaysStationed)
            return new(DaggerfallSchedulePlacement.InteriorBuilding, DaggerfallScheduleBuildingTarget.HomePost,
                profile.Id, "always-stationed", jitter, stormDelay, Pinned: true);

        // 2. Interior-bound profiles (shopkeepers, guild members, court nobles):
        //    Read open hours directly from DaggerfallBuildingEntryPolicy (single source of truth).
        if (profile.PrimaryMode == DaggerfallSchedulePrimaryMode.InteriorBuilding)
            return EvaluateInteriorProfile(profile, normalizedMinute, buildingType, holidayId, jitter, stormDelay, tavernRoll);

        // 3. Exterior-street profiles (street-walker, city-guard, underworld-night):
        return EvaluateExteriorProfile(
            profile, normalizedMinute, weather, weatherElapsedMinutes,
            severeWeatherElapsedMinutes ?? weatherElapsedMinutes, jitter, stormDelay, tavernRoll);
    }

    /// <summary>Evaluates one spawner spec's authored schedule profile from <paramref name="catalog"/>.</summary>
    internal static DaggerfallScheduleDecision EvaluateSpec(
        ulong worldSocialSeed,
        DaggerfallSiteId siteId,
        string slotKey,
        DaggerfallActorSpawnerSpec spec,
        DaggerfallActorCatalog catalog,
        int hour,
        int minute,
        DaggerfallWeatherKind weather,
        int? buildingType = null,
        int holidayId = 0,
        int weatherElapsedMinutes = int.MaxValue,
        bool isActiveQuestor = false,
        bool isOfficeHolder = false,
        bool isWithParty = false,
        bool isOnOverlandBand = false,
        int? severeWeatherElapsedMinutes = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(catalog);
        string scheduleId = spec.Schedule ?? AlwaysStationedScheduleId;
        if (!catalog.Schedules.TryGetValue(scheduleId, out DaggerfallActorScheduleProfile? profile))
            throw new KeyNotFoundException($"Actor schedule profile '{scheduleId}' was not found in the actor catalog.");
        return Evaluate(
            worldSocialSeed,
            siteId,
            slotKey,
            profile,
            checked(hour * DaggerfallCalendar.MinutesPerHour + minute),
            weather,
            buildingType,
            holidayId,
            weatherElapsedMinutes,
            isActiveQuestor,
            isOfficeHolder,
            isWithParty,
            isOnOverlandBand,
            severeWeatherElapsedMinutes);
    }

    /// <summary>Evaluates an exterior source-population placement against the active schedule catalog.</summary>
    internal static DaggerfallScheduleDecision EvaluateExteriorPlacement(
        ulong worldSocialSeed,
        DaggerfallSiteId siteId,
        string slotKey,
        bool isGuard,
        int totalPopulationCount,
        DaggerfallActorCatalog catalog,
        int minuteOfDay,
        DaggerfallWeatherKind weather,
        int weatherElapsedMinutes = int.MaxValue,
        bool isActiveQuestor = false,
        bool isOfficeHolder = false,
        bool isWithParty = false,
        bool isOnOverlandBand = false,
        int? severeWeatherElapsedMinutes = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string scheduleId = ResolveExteriorScheduleId(worldSocialSeed, siteId, slotKey, isGuard, totalPopulationCount);
        if (!catalog.Schedules.TryGetValue(scheduleId, out DaggerfallActorScheduleProfile? profile))
            profile = FallbackExteriorProfile(scheduleId);
        return Evaluate(
            worldSocialSeed,
            siteId,
            slotKey,
            profile,
            minuteOfDay,
            weather,
            buildingType: null,
            holidayId: 0,
            weatherElapsedMinutes: weatherElapsedMinutes,
            isActiveQuestor: isActiveQuestor,
            isOfficeHolder: isOfficeHolder,
            isWithParty: isWithParty,
            isOnOverlandBand: isOnOverlandBand,
            severeWeatherElapsedMinutes: severeWeatherElapsedMinutes);
    }

    private static DaggerfallScheduleDecision EvaluateInteriorProfile(
        DaggerfallActorScheduleProfile profile,
        int minuteOfDay,
        int? buildingType,
        int holidayId,
        int jitter,
        int stormDelay,
        int tavernRoll)
    {
        int hour = minuteOfDay / DaggerfallCalendar.MinutesPerHour;
        bool isOpen;
        if (profile.UsesBuildingOpeningHours
            && buildingType is int bt
            && DaggerfallBuildingEntryPolicy.TryGetOpeningHours(bt, out _, out _))
        {
            bool closedForHoliday = holidayId == DaggerfallBuildingEntryPolicy.SunsRestHoliday
                && DaggerfallNpcServiceFacts.IsShop(bt);
            isOpen = !closedForHoliday && DaggerfallBuildingEntryPolicy.IsOpenHour(bt, hour);
        }
        else
        {
            isOpen = IsInMinuteWindow(minuteOfDay, profile.ActiveStartMinute, profile.ActiveEndMinute);
        }

        // Shopkeepers and hall staff remain 100% pinned to their post during open hours.
        if (isOpen)
        {
            return new(
                DaggerfallSchedulePlacement.InteriorBuilding,
                DaggerfallScheduleBuildingTarget.HomePost,
                profile.Id,
                "open-hours",
                jitter,
                stormDelay,
                Pinned: false);
        }

        // Off-hours evening tavern mixer (19:30-22:30 +/- slot jitter) for closed-shop proprietors,
        // off-duty court clerks/nobles, and guild members.
        if (IsInEveningTavernWindow(profile, minuteOfDay, jitter) && tavernRoll < profile.EveningTavernSharePercent)
        {
            return new(
                DaggerfallSchedulePlacement.InteriorBuilding,
                DaggerfallScheduleBuildingTarget.Tavern,
                profile.Id,
                "evening-tavern",
                jitter,
                stormDelay,
                Pinned: false);
        }

        return new(
            DaggerfallSchedulePlacement.ShelteredInHome,
            DaggerfallScheduleBuildingTarget.None,
            profile.Id,
            "after-hours",
            jitter,
            stormDelay,
            Pinned: false);
    }

    private static DaggerfallScheduleDecision EvaluateExteriorProfile(
        DaggerfallActorScheduleProfile profile,
        int minuteOfDay,
        DaggerfallWeatherKind weather,
        int precipElapsedMinutes,
        int severeElapsedMinutes,
        int jitter,
        int stormDelay,
        int tavernRoll)
    {
        // Full-day duty profiles (e.g. city-guard) remain outdoors in all hours and weather.
        if (profile.ActiveStartMinute == 0 && profile.ActiveEndMinute >= MinutesPerDay
            && !profile.SheltersInRain && !profile.SheltersInStorm)
        {
            return new(
                DaggerfallSchedulePlacement.ExteriorStreet,
                DaggerfallScheduleBuildingTarget.None,
                profile.Id,
                "on-duty",
                jitter,
                stormDelay,
                Pinned: false);
        }

        // Staggered weather sheltering: rain-sensitive roles use precipElapsedMinutes (so Rain -> Thunder keeps
        // already-sheltered walkers indoors), while rain-tolerant severe-only roles (underworld-night) use
        // severeElapsedMinutes so escalating from sustained Rain to Thunder/Snow still staggers their shelter.
        bool severeWeather = weather is DaggerfallWeatherKind.Thunder or DaggerfallWeatherKind.Snow;
        bool rainWeather = weather == DaggerfallWeatherKind.Rain;
        bool weatherShelters = (profile.SheltersInStorm && severeWeather) || (profile.SheltersInRain && rainWeather);
        int relevantElapsed = profile.SheltersInRain ? precipElapsedMinutes : severeElapsedMinutes;
        if (weatherShelters && relevantElapsed >= stormDelay)
        {
            if (profile.EveningTavernSharePercent > 0 && tavernRoll < profile.EveningTavernSharePercent)
            {
                return new(
                    DaggerfallSchedulePlacement.InteriorBuilding,
                    DaggerfallScheduleBuildingTarget.Tavern,
                    profile.Id,
                    "storm-shelter-tavern",
                    jitter,
                    stormDelay,
                    Pinned: false);
            }
            return new(
                DaggerfallSchedulePlacement.ShelteredInHome,
                DaggerfallScheduleBuildingTarget.None,
                profile.Id,
                "storm-shelter-home",
                jitter,
                stormDelay,
                Pinned: false);
        }

        // Late-night wrapping window (e.g. underworld-night 22:00-05:00 +/- jitter).
        if (profile.ActiveEndMinute < profile.ActiveStartMinute)
        {
            int start = NormalizeMinute(profile.ActiveStartMinute + jitter);
            int end = NormalizeMinute(profile.ActiveEndMinute + jitter);
            if (IsInMinuteWindow(minuteOfDay, start, end))
            {
                return new(
                    DaggerfallSchedulePlacement.ExteriorStreet,
                    DaggerfallScheduleBuildingTarget.None,
                    profile.Id,
                    profile.UnderworldNightPulse ? "underworld-night" : "night-patrol",
                    jitter,
                    stormDelay,
                    Pinned: false);
            }
            return new(
                DaggerfallSchedulePlacement.ShelteredInHome,
                DaggerfallScheduleBuildingTarget.None,
                profile.Id,
                "daytime-shelter",
                jitter,
                stormDelay,
                Pinned: false);
        }

        // Standard daytime window (e.g. street-walker 06:00-18:00 +/- 35m jitter).
        int openMin = Math.Clamp(profile.ActiveStartMinute + jitter, 0, MinutesPerDay - 1);
        int closeMin = Math.Clamp(profile.ActiveEndMinute + jitter, openMin + 1, MinutesPerDay);
        if (minuteOfDay >= openMin && minuteOfDay < closeMin)
        {
            return new(
                DaggerfallSchedulePlacement.ExteriorStreet,
                DaggerfallScheduleBuildingTarget.None,
                profile.Id,
                weatherShelters ? "storm-linger" : "daytime-street",
                jitter,
                stormDelay,
                Pinned: false);
        }

        // Evening social window (18:00-22:00 +/- jitter): deterministic slice visits a tavern; remainder heads home.
        if (IsInEveningTavernWindow(profile, minuteOfDay, jitter) && tavernRoll < profile.EveningTavernSharePercent)
        {
            return new(
                DaggerfallSchedulePlacement.InteriorBuilding,
                DaggerfallScheduleBuildingTarget.Tavern,
                profile.Id,
                "evening-tavern",
                jitter,
                stormDelay,
                Pinned: false);
        }

        return new(
            DaggerfallSchedulePlacement.ShelteredInHome,
            DaggerfallScheduleBuildingTarget.None,
            profile.Id,
            "night-shelter",
            jitter,
            stormDelay,
            Pinned: false);
    }

    private static DaggerfallScheduleDecision PinnedAtPost(
        DaggerfallActorScheduleProfile profile,
        string reason,
        int jitter,
        int stormDelay) =>
        profile.PrimaryMode == DaggerfallSchedulePrimaryMode.ExteriorStreet
            ? new(DaggerfallSchedulePlacement.ExteriorStreet, DaggerfallScheduleBuildingTarget.None, profile.Id, reason, jitter, stormDelay, Pinned: true)
            : new(DaggerfallSchedulePlacement.InteriorBuilding, DaggerfallScheduleBuildingTarget.HomePost, profile.Id, reason, jitter, stormDelay, Pinned: true);

    private static bool IsInEveningTavernWindow(DaggerfallActorScheduleProfile profile, int minuteOfDay, int jitter)
    {
        if (profile.EveningTavernStartMinute is not int rawStart
            || profile.EveningTavernEndMinute is not int rawEnd
            || profile.EveningTavernSharePercent <= 0)
            return false;
        int start = Math.Clamp(rawStart + jitter, 0, MinutesPerDay - 1);
        int end = Math.Clamp(rawEnd + jitter, 1, MinutesPerDay);
        return IsInMinuteWindow(minuteOfDay, start, end);
    }

    private static bool IsInMinuteWindow(int minuteOfDay, int startMinute, int endMinute)
    {
        if (startMinute == 0 && endMinute >= MinutesPerDay)
            return true;
        if (startMinute < endMinute)
            return minuteOfDay >= startMinute && minuteOfDay < endMinute;
        return minuteOfDay >= startMinute || minuteOfDay < endMinute;
    }

    private static int NormalizeMinute(int minuteOfDay)
    {
        int m = minuteOfDay % MinutesPerDay;
        return m < 0 ? m + MinutesPerDay : m;
    }

    private static (int JitterMinutes, int StormDelayMinutes, int TavernRoll) SlotRolls(
        ulong worldSocialSeed,
        DaggerfallSiteId siteId,
        string slotKey,
        DaggerfallActorScheduleProfile profile)
    {
        ulong hash = StableHash.NonZeroFnv1a64(
            $"daggerfall.schedule:{worldSocialSeed}:{siteId.Region}:{siteId.Index}:{slotKey}");
        int jitter = profile.JitterMinutes > 0
            ? (int)((hash & 0xFFFFUL) % (ulong)(2 * profile.JitterMinutes + 1)) - profile.JitterMinutes
            : 0;
        int stormSpan = Math.Max(0, profile.StormDelayMaxMinutes - profile.StormDelayMinMinutes);
        int stormDelay = stormSpan > 0
            ? profile.StormDelayMinMinutes + (int)(((hash >> 16) & 0xFFFFUL) % (ulong)(stormSpan + 1))
            : profile.StormDelayMinMinutes;
        int tavernRoll = (int)(((hash >> 32) & 0xFFFFUL) % 100UL);
        return (jitter, stormDelay, tavernRoll);
    }

    private static DaggerfallActorScheduleProfile FallbackExteriorProfile(string scheduleId) => scheduleId switch
    {
        CityGuardScheduleId => new(CityGuardScheduleId, "City Guard", "", DaggerfallSchedulePrimaryMode.ExteriorStreet,
            false, 0, MinutesPerDay, 0, null, null, 0, false, false, 0, 0, false),
        UnderworldNightScheduleId => new(UnderworldNightScheduleId, "Underworld Night", "", DaggerfallSchedulePrimaryMode.ExteriorStreet,
            false, 1320, 300, 20, null, null, 0, false, true, 6, 10, true),
        _ => new(StreetWalkerScheduleId, "Street Walker", "", DaggerfallSchedulePrimaryMode.ExteriorStreet,
            false, 360, 1080, 35, 1080, 1320, 40, true, true, 2, 10, false),
    };
}
