using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The donor's random encounters as time passes: which minute offers one and in what context, the catch-up over an
/// elapsed interval, queueing a selected encounter at the player and placing it around them on an admitted step.
/// </summary>
/// <param name="encounters">The encounter runtime that selects, keeps and materializes pending encounters.</param>
/// <param name="placement">Where a foe can stand around the player.</param>
/// <param name="playerLevel">The player's level now.</param>
/// <param name="swimming">Whether the player swims now.</param>
/// <param name="onShip">Whether the player is aboard a ship now.</param>
/// <param name="activeProfile">The site profile the player is in.</param>
/// <param name="activeSite">The active profile's site record, if the site is known.</param>
/// <param name="exteriorCell">The world map pixel the player stands in outdoors.</param>
/// <param name="atLocation">Whether the player stands at a location (a town or dungeon's grounds) outdoors.</param>
/// <param name="generation">The latest admitted update's generation, if any.</param>
/// <param name="spawn">Spawns a definition at a pose with a level and returns its durable identity.</param>
internal sealed class DaggerfallWorldEncounters(DaggerfallEncounterRuntime encounters, DaggerfallActorPlacement placement,
    DaggerfallDefinitions definitions, PlayerControlState player, Func<int> playerLevel, Func<bool> swimming, Func<bool> onShip,
    Func<DaggerfallWorldProfileKey> activeProfile, Func<DaggerfallSiteRecord?> activeSite, Func<(int X, int Y)> exteriorCell,
    Func<bool> atLocation, Func<ulong?> generation, Func<string, ActorPose, int, long> spawn)
{
    /// <summary>
    /// Catches up each elapsed minute and stops at its first selected spawn, as the donor does. Ordinary play reaches
    /// this through the single calendar fan-out; placement is the encounter runtime's persisted pending operation.
    /// </summary>
    internal void Advance(long minuteBefore, long minuteAfter)
    {
        for (long minute = minuteBefore + 1; minute <= minuteAfter; minute++)
        {
            DaggerfallEncounterRequest? request = Request(minute, resting: false);
            if (request is not null && Queue(request).Choice.MobileId is not null) break;
        }
    }

    /// <summary>Whether resting through <paramref name="minute"/> draws an encounter that will arrive.</summary>
    internal bool ArrivesWhileResting(long minute) =>
        Request(minute, resting: true) is { } encounter && Queue(encounter).Choice.MobileId is not null;

    /// <summary>
    /// The source encounter context for one elapsed eligible game minute. The donor checks intermittent spawns at
    /// minutes where (minute / 12) % 12 is zero and retries after failed draws.
    /// </summary>
    internal DaggerfallEncounterRequest? Request(long minute, bool resting)
    {
        if (swimming() || onShip()) return null;
        DaggerfallWorldProfileKey profile = activeProfile();
        if ((minute / 12) % 12 != 0 || activeSite() is not { } site) return null;

        if (profile.Kind == DaggerfallWorldProfileKind.Dungeon)
            return resting ? new(DaggerfallEncounterContext.Dungeon, playerLevel(),
                DungeonType: site.DungeonType, EnemyAlert: true) : null;
        if (profile.Kind != DaggerfallWorldProfileKind.Exterior) return null;

        (int cellX, int cellY) = exteriorCell();
        DaggerfallClimateCell climate = definitions.Grids.ClimateAtWorldPixel(cellX, cellY);
        // RandomEncounters.ChooseRandomEnemy has no table for other climates (ocean) and spawns nothing.
        if (climate.Value is not (224 or 225 or 226 or 227 or 228 or 229 or 230 or 231 or 232)) return null;
        long timeOfDay = minute % (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour);
        bool day = timeOfDay is >= 360 and <= 1080;
        bool located = atLocation();
        if (located && day) return null;
        DaggerfallEncounterContext context = located
            ? DaggerfallEncounterContext.LocationNight
            : day ? DaggerfallEncounterContext.WildernessDay : DaggerfallEncounterContext.WildernessNight;
        return new(context, playerLevel(), Climate: climate.Value);
    }

    /// <summary>
    /// Resolves one rest/travel/time encounter at the player pose. The result stays durable but unmaterialized until an
    /// admitted simulation step places it around the player, so a save between the two restores the selected source
    /// result rather than drawing again.
    /// </summary>
    internal DaggerfallEncounterResolution Queue(DaggerfallEncounterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorldPoint pose = player.Position
            ?? throw new InvalidOperationException("Daggerfall cannot select an encounter without a player position.");
        ulong? admitted = generation();
        ulong latest = admitted is > 0 ? admitted.Value : 1UL;
        return encounters.Select(request, latest, activeProfile().LogicalId, new ActorPose(pose, player.YawRadians));
    }

    /// <summary>
    /// Places one selected encounter around the player at the donor's distances, or leaves it pending for the next step
    /// when no floor and clearance were found, as the donor's spawner retries each frame.
    /// </summary>
    internal long? Place(DaggerfallEncounterResolution pending, ulong simulationStep)
    {
        if (player.Position is not WorldPoint standing) return null;
        DaggerfallEncounterContext context = pending.Request.Context;
        if (!placement.TrySpawnPose($"generation:{pending.Generation}:sequence:{pending.Sequence}:step:{simulationStep}",
                "daggerfall.encounter.placement", standing, DaggerfallEncounterPolicy.MinimumSpawnDistance(context),
                DaggerfallEncounterPolicy.MaximumSpawnDistance, out ActorPose pose,
                DaggerfallEncounterPolicy.PlacesOutsideView(context) ? SpawnDirection.BesideView : SpawnDirection.Any))
            return null;
        return spawn(pending.ActorDefinition!, pose, pending.Request.PlayerLevel);
    }
}
