using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private long? SpawnQuestFoe(string operation, DaggerfallQuestFoeSelection foe, bool send)
    {
        if (State.Actors.Player.IsDefeated || State.PlayerControl.Position is not WorldPoint player) return null;
        var location = _sites.ReadQuestLocation();
        if (send && location.ExteriorLocation is null) return null;
        bool wilderness = location.Profile.ProfileKind == DaggerfallWorldProfileKind.Exterior && location.ExteriorLocation is null;
        // Source CreateFoe uses the same floor/clearance admission as the existing summon
        // owner, with its authored near-player ranges for a location versus wilderness.
        if (!TrySpawnPose(operation, "daggerfall.quest.foe-placement", player, wilderness ? _tuning.QuestSpawning.MinimumWildernessDistance : _tuning.QuestSpawning.MinimumFoeDistance,
            wilderness ? _tuning.QuestSpawning.MaximumWildernessDistance : _tuning.QuestSpawning.MaximumFoeDistance, out var pose)) return null;
        long id = _roster.Spawn(foe.Definition, pose);
        _enemyBehavior.MakeHostile(id);
        return id;
    }
}
