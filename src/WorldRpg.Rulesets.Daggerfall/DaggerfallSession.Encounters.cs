using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    // Ordinary play reaches this through the single calendar fan-out. The donor catches up each
    // elapsed minute and stops at its first selected spawn; placement remains the encounter owner's
    // existing ground/clearance-tested, persisted pending operation.
    private void AdvanceWorldEncounters(long minuteBefore, long minuteAfter)
    {
        for (long minute = minuteBefore + 1; minute <= minuteAfter; minute++)
        {
            DaggerfallEncounterRequest? request = WorldEncounterRequest(minute, resting: false);
            if (request is not null && QueueEncounter(request).Choice.MobileId is not null) break;
        }
    }

    /// <summary>
    /// Builds the source encounter context for each elapsed eligible game minute. The donor checks
    /// intermittent spawns at minutes where (minute / 12) % 12 is zero and retries after failed draws.
    /// </summary>
    private DaggerfallEncounterRequest? WorldEncounterRequest(long minute, bool resting)
    {
        if (State.Swimming.IsSwimming || State.Transport.OnShip) return null;
        if ((minute / 12) % 12 != 0 || !_site.TryFind(_activeProfileKey.Site, out DaggerfallSiteRecord site)) return null;

        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon)
            return resting ? new(DaggerfallEncounterContext.Dungeon, State.Progression.Level,
                DungeonType: site.DungeonType, EnemyAlert: true) : null;
        if (_activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior) return null;

        DaggerfallExteriorCellId cell = _sites.CurrentExteriorCell();
        DaggerfallClimateCell climate = _definitions.Grids.ClimateAtWorldPixel(cell.X, cell.Y);
        if (climate.Value is not (224 or 225 or 226 or 227 or 228 or 229 or 230 or 231 or 232))
            throw new InvalidOperationException($"Exterior cell {cell.X}/{cell.Y} has no encounter climate.");
        long timeOfDay = minute % (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour);
        bool day = timeOfDay is >= 360 and <= 1080;
        bool atLocation = site.Exterior is { } exterior
            && cell == new DaggerfallExteriorCellId(exterior.MapPixelX, exterior.MapPixelY);
        if (atLocation && day) return null;
        DaggerfallEncounterContext context = atLocation
            ? DaggerfallEncounterContext.LocationNight
            : day ? DaggerfallEncounterContext.WildernessDay : DaggerfallEncounterContext.WildernessNight;
        return new(context, State.Progression.Level, Climate: climate.Value);
    }

}
