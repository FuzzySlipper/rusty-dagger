using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private bool ApplyQuestFoeCommand(long id, bool remove)
    {
        if (State.Actors.TryGet(id, out var actor))
        {
            if (remove) _roster.RemoveQuestActor(id);
            else if (!actor.IsDefeated) AppendDamage(_vitality.ResolveQuestDeath(actor.Actor), DaggerfallDamageCause.Quest, 0);
            return true;
        }
        if (remove) return RetireDetachedActor(id);
        var owners = _sites.Deltas.Where(entry => entry.Value.DynamicActors.Any(value => value.EntityId == id)).ToArray();
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
            var materialized = _roster.MaterializeRetainedActor(saved, projectAppearance: false);
            _persistence.RestoreSiteDelta(incoming);
            if (!materialized.IsDefeated) AppendDamage(_vitality.ResolveQuestDeath(materialized.Actor), DaggerfallDamageCause.Quest, 0);
            DeliverFacts(); // Complete actual death/corpse contributions before the capture boundary.
            var updated = _persistence.CaptureDynamicActorDelta(id, saved.Definition);
            _sites.ReplaceDelta(source.Key, source.Value with
            {
                DynamicActors = source.Value.DynamicActors.Select(value => value.EntityId == id ? updated.DynamicActors.Single() : value).ToArray(),
                ActorInventories = [.. source.Value.ActorInventories.Where(value => value.EntityId != id), .. updated.ActorInventories],
                Corpses = [.. source.Value.Corpses.Where(value => value.ActorId != id), .. updated.Corpses],
                Effects = [.. source.Value.Effects.Where(value => value.TargetId != id), .. updated.Effects],
            });
            return true;
        }
        finally { _roster.UnloadActor(id); }
    }
}
