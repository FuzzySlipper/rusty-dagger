using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// A dungeon's own population as it loads: its treasure piles, every load, and its marked enemies once a dungeon, by
/// the encounter runtime's record of which dungeons are populated.
/// </summary>
/// <param name="restingPosition">Where a pile placed at a point comes to rest on the floor.</param>
/// <param name="spawn">Spawns an enemy definition at a pose with its marker's spawn distance and passivity (the roster's).</param>
/// <param name="playerLevel">The player's level now.</param>
internal sealed class DaggerfallDungeonPopulation(DaggerfallDefinitions definitions, IRandomService random, DaggerfallUniqueItemAllocator uniqueItems,
    DaggerfallGroundContainers groundContainers, Func<WorldPoint, WorldPoint> restingPosition, DaggerfallEncounterRuntime encounters,
    Action<string, ActorPose, int, bool> spawn, DaggerfallCharacterState character, Func<int> playerLevel)
{
    /// <summary>Places a dungeon profile's treasure piles, and its enemies the first time it loads.</summary>
    internal void Populate(DaggerfallSiteProfile profile)
    {
        if (profile.ProfileKind != DaggerfallWorldProfileKind.Dungeon
            || profile.DungeonPopulation is not { } population
            || population.Treasures.Count == 0 && population.Enemies.Count == 0) return;
        var site = definitions.Locations.Records.Single(value => value.Id == profile.Site);
        var loot = new DaggerfallLootPopulation(definitions, random, uniqueItems);
        string gender = character.Identity.Gender.ToString().ToLowerInvariant();
        foreach (var marker in population.Treasures)
            groundContainers.EnsureTreasure(marker, restingPosition(marker.Position), site.DungeonType, playerLevel(), character.Identity.RaceId, gender, loot);
        if (encounters.DungeonPopulated(profile.ProfileKey.LogicalId)) return;
        foreach (var marker in population.Enemies)
        {
            if (encounters.SelectDungeonEnemy(site, marker, playerLevel()) is not { } definition) continue;
            spawn(definition.Value, new ActorPose(marker.Position, 0), marker.SpawnDistance, marker.Passive);
        }
        encounters.MarkDungeonPopulated(profile.ProfileKey.LogicalId);
    }
}
