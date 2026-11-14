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
/// A quest's foes as actors: the team each fights for and whom it pursues, the quest's command over all enemies, spawning a
/// foe near the player, and a foe's kill or removal, live or kept in an unloaded site.
/// </summary>
/// <remarks>A quest's kill is harm like any other: the session turns it into facts (killedByQuest) and delivers them before
/// a retained foe is captured again (deliverFacts). Retiring a detached actor and a site delta's capture and restore are the
/// session's, handed in.</remarks>
internal sealed class DaggerfallQuestFoes(DaggerfallQuestInstances quests, ActorsState actors, PlayerControlState playerControl,
    DaggerfallTravelState travel, DaggerfallActorRoster roster, DaggerfallActorPlacement placement, DaggerfallEnemyBehaviorModule enemyBehavior,
    DaggerfallVitalityConsequences vitality, DaggerfallSiteCrossing sites, DaggerfallQuestSpawningTuning spawning, DaggerfallEnemyBehaviorTuning enemyBehaviorTuning,
    Action<DamageResult> killedByQuest, Action deliverFacts, Func<long, bool> retireDetached,
    Action<DaggerfallSiteRuntimeDelta> restoreSiteDelta, Func<long, string, DaggerfallSiteRuntimeDelta> captureActorDelta)
{
    /// <summary>Whether a quest holds this foe back from acting.</summary>
    internal bool Restrained(long id) => quests.IsFoeRestrained(id);
    /// <summary>The team a foe fights for: its definition's, unless a quest made it an ally or changed it.</summary>
    internal string? Team(long id) => roster.Definitions.TryGetValue(id, out var definition)
        ? quests.FoeTeam(id, definition.Team) : null;

    /// <summary>Removes every monster and enemy class, or makes them all hostile, as a quest commands.</summary>
    internal void CommandAllEnemies(bool clear)
    {
        foreach (long id in roster.Definitions.Where(pair => pair.Value.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
            .Select(pair => pair.Key).ToArray())
        {
            if (clear) roster.RemoveQuestActor(id);
            else enemyBehavior.MakeHostile(id, includePlayerAllies: true);
        }
    }

    /// <summary>The target an enemy pursues: the player, or a foe across a quest's infighting.</summary>
    internal PursuitTarget? SelectTarget(long observerId)
    {
        if (!actors.TryGet(observerId, out var observer) || observer.IsDefeated || quests.IsFoeRestrained(observerId)) return null;
        if (Team(observerId) == "player-ally")
            return quests.AllowsFoeInfighting(observerId) ? placement.SelectAllyTarget(observerId) : null;
        var player = playerControl.Position;
        PursuitTarget? selected = player is { } position ? new(DaggerfallActorIdentity.PlayerEntityId, position) : null;
        if (!quests.AllowsFoeInfighting(observerId) || !quests.HasInfightingFoes) return selected;
        double best = player is { } target ? Vector3.Distance(observer.Position.ToVector(), target.ToVector()) : double.PositiveInfinity;
        Vector3 forward = ActorHeading.Forward(observer.HeadingYawRadians);
        string? team = Team(observerId);
        foreach (var pair in placement.QueryEnemies(observerId, observer.Position, forward,
            Math.Max(enemyBehaviorTuning.DetectionDistance, DaggerfallPerceptionQueryDefaults.SightRadius),
            DaggerfallPerceptionQueryDefaults.MinimumFacingCosine, _ => true)
            .Where(pair => pair.Kind == PerceptionPairKind.Visible).OrderBy(pair => pair.Distance).ThenBy(pair => pair.Target))
        {
            long id = checked((long)pair.Target);
            // Classic quest foes are excluded from AI fights unless the source explicitly opts in.
            // Other actors keep their established player pursuit unless participating in that opt-in.
            if (quests.FoeRelationsFor(observerId)?.Infighting != true && quests.FoeRelationsFor(id)?.Infighting != true) continue;
            if (!quests.AllowsFoeInfighting(id) || Team(id) == team
                || Team(id) == "player-ally" && enemyBehavior.IsPacified(observerId) || pair.Distance >= best) continue;
            selected = new(id, actors.Get(id).Position); best = pair.Distance;
        }
        return selected;
    }

    /// <summary>Spawns a quest foe near the player and makes it hostile; null while travelling, defeated or with no clear pose.</summary>
    internal long? Spawn(string operation, DaggerfallQuestFoeSelection foe, bool send)
    {
        // DFU advances travel's clock before the next quest update. Calendar slices here still
        // process deadlines, but must not populate either end of a journey at intermediate hours.
        if (travel.IsExecuting) return null;
        if (actors.Player.IsDefeated || playerControl.Position is not WorldPoint player) return null;
        var location = sites.ReadQuestLocation();
        if (send && location.ExteriorLocation is null) return null;
        bool wilderness = location.Profile.ProfileKind == DaggerfallWorldProfileKind.Exterior && location.ExteriorLocation is null;
        // Source CreateFoe uses the same floor/clearance admission as the existing summon
        // owner, with its authored near-player ranges for a location versus wilderness.
        if (!placement.TrySpawnPose(operation, "daggerfall.quest.foe-placement", player, wilderness ? spawning.MinimumWildernessDistance : spawning.MinimumFoeDistance,
            wilderness ? spawning.MaximumWildernessDistance : spawning.MaximumFoeDistance, out var pose)) return null;
        long id = roster.Spawn(foe.Definition, pose);
        enemyBehavior.MakeHostile(id);
        return id;
    }

    /// <summary>Kills or removes a quest foe, live or kept in an unloaded site's delta; false while the foe is not yet placed.</summary>
    internal bool Command(long id, bool remove)
    {
        if (actors.TryGet(id, out var actor))
        {
            if (remove) roster.RemoveQuestActor(id);
            else if (!actor.IsDefeated) killedByQuest(vitality.ResolveQuestDeath(actor.Actor));
            return true;
        }
        if (remove) return retireDetached(id);
        var owners = sites.Deltas.Where(entry => entry.Value.DynamicActors.Any(value => value.EntityId == id)).ToArray();
        if (owners.Length == 0) return false; // A selected but not yet placed foe retains its command intent.
        if (owners.Length != 1) throw new InvalidOperationException($"Quest foe {id} has more than one retained site owner.");
        var source = owners[0];
        var saved = source.Value.DynamicActors.Single(value => value.EntityId == id);
        var incoming = new DaggerfallSiteRuntimeDelta([], [saved],
            source.Value.ActorInventories.Where(value => value.EntityId == id).ToArray(),
            source.Value.Corpses.Where(value => value.ActorId == id).ToArray(), [],
            source.Value.Effects.Where(value => value.TargetId == id).ToArray());
        try
        {
            var materialized = roster.MaterializeRetainedActor(saved, projectAppearance: false);
            restoreSiteDelta(incoming);
            if (!materialized.IsDefeated) killedByQuest(vitality.ResolveQuestDeath(materialized.Actor));
            deliverFacts(); // Complete actual death/corpse contributions before the capture boundary.
            var updated = captureActorDelta(id, saved.Definition);
            sites.ReplaceDelta(source.Key, source.Value with
            {
                DynamicActors = source.Value.DynamicActors.Select(value => value.EntityId == id ? updated.DynamicActors.Single() : value).ToArray(),
                ActorInventories = [.. source.Value.ActorInventories.Where(value => value.EntityId != id), .. updated.ActorInventories],
                Corpses = [.. source.Value.Corpses.Where(value => value.ActorId != id), .. updated.Corpses],
                Effects = [.. source.Value.Effects.Where(value => value.TargetId != id), .. updated.Effects],
            });
            return true;
        }
        finally { roster.UnloadActor(id); }
    }
}
