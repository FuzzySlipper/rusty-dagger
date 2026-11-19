using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// A quest's foes as actors: the team each fights for and whether it is held back (inputs to the team rule in
/// <see cref="DaggerfallActorPlacement.SelectTarget"/>), the quest's command over all enemies, spawning a foe near the
/// player, and a foe's kill or removal, live or kept in an unloaded site.
/// </summary>
/// <remarks>A quest's kill is harm like any other: the session turns it into facts (killedByQuest). The roster's spawning and
/// removal, enemy behaviour's hostility, retiring a detached actor and killing a foe an unloaded site keeps (the list
/// KillRetainedQuestFoe) are handed in; where the player is changes with the site, so it is read as it is asked.</remarks>
internal sealed class DaggerfallQuestFoes(DaggerfallQuestInstances quests, ActorsState actors, PlayerControlState playerControl,
    DaggerfallTravelState travel, IReadOnlyDictionary<long, DaggerfallActorDefinition> actorDefinitions, DaggerfallActorPlacement placement,
    DaggerfallVitalityConsequences vitality, DaggerfallQuestSpawningTuning spawning,
    Func<DaggerfallQuestLocationRead> questLocation, Func<string, ActorPose, long> spawn, Action<long> removeQuestActor,
    Action<long, bool> makeHostile, Action<DamageResult> killedByQuest, Func<long, bool> retireDetached,
    Func<long, bool> killRetained)
{
    /// <summary>Whether a quest holds this foe back from acting.</summary>
    internal bool Restrained(long id) => quests.IsFoeRestrained(id);
    /// <summary>The team a foe fights for: its definition's, unless a quest made it an ally or changed it.</summary>
    internal string? Team(long id) => actorDefinitions.TryGetValue(id, out var definition)
        ? quests.FoeTeam(id, definition.Team) : null;

    /// <summary>Removes every monster and enemy class, or makes them all hostile, as a quest commands.</summary>
    internal void CommandAllEnemies(bool clear)
    {
        foreach (long id in actorDefinitions.Where(pair => pair.Value.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
            .Select(pair => pair.Key).ToArray())
        {
            if (clear) removeQuestActor(id);
            else makeHostile(id, true);
        }
    }

    /// <summary>Spawns a quest foe near the player and makes it hostile; null while travelling, defeated or with no clear pose.</summary>
    internal long? Spawn(string operation, DaggerfallQuestFoeSelection foe, bool send)
    {
        // DFU advances travel's clock before the next quest update. Calendar slices here still
        // process deadlines, but must not populate either end of a journey at intermediate hours.
        if (travel.IsExecuting) return null;
        if (actors.Player.IsDefeated || playerControl.Position is not WorldPoint player) return null;
        var location = questLocation();
        if (send && location.ExteriorLocation is null) return null;
        bool wilderness = location.Profile.ProfileKind == DaggerfallWorldProfileKind.Exterior && location.ExteriorLocation is null;
        // Source CreateFoe uses the same floor/clearance admission as the existing summon
        // owner, with its authored near-player ranges for a location versus wilderness.
        if (!placement.TrySpawnPose(operation, "daggerfall.quest.foe-placement", player, wilderness ? spawning.MinimumWildernessDistance : spawning.MinimumFoeDistance,
            wilderness ? spawning.MaximumWildernessDistance : spawning.MaximumFoeDistance, out var pose)) return null;
        long id = spawn(foe.Definition, pose);
        makeHostile(id, false);
        return id;
    }

    /// <summary>Kills or removes a quest foe, live or kept in an unloaded site; false while the foe is not yet placed.</summary>
    internal bool Command(long id, bool remove)
    {
        if (actors.TryGet(id, out var actor))
        {
            if (remove) removeQuestActor(id);
            else if (!actor.IsDefeated) killedByQuest(vitality.ResolveQuestDeath(actor.Actor));
            return true;
        }
        return remove ? retireDetached(id) : killRetained(id);
    }
}
