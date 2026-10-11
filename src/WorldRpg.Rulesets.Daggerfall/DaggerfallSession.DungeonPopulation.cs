using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private void PopulateDungeon(DaggerfallSiteProfile profile)
    {
        if (profile.ProfileKind != DaggerfallWorldProfileKind.Dungeon
            || profile.DungeonPopulation is not { } population
            || population.Treasures.Count == 0 && population.Enemies.Count == 0) return;
        var site = _definitions.Locations.Records.Single(value => value.Id == profile.Site);
        var loot = new DaggerfallLootPopulation(_definitions, _random, _uniqueItems);
        string gender = State.Character.Identity.Gender.ToString().ToLowerInvariant();
        foreach (var marker in population.Treasures)
            _groundContainers.EnsureTreasure(marker, site.DungeonType, State.Progression.Level, State.Character.Identity.RaceId, gender, loot);
        if (_encounters.DungeonPopulated(profile.ProfileKey.LogicalId)) return;
        foreach (var marker in population.Enemies)
        {
            if (_encounters.SelectDungeonEnemy(site, marker, State.Progression.Level) is not { } definition) continue;
            _roster.Spawn(definition.Value, new ActorPose(marker.Position, 0),
                spawnDistance: marker.SpawnDistance, passive: marker.Passive);
        }
        _encounters.MarkDungeonPopulated(profile.ProfileKey.LogicalId);
    }
}
