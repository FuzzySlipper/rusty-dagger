using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    DaggerfallQuestResourceBinding? IDaggerfallQuestWorldAdmission.Place(string instanceId, DaggerfallQuestResourceState resource,
        DaggerfallSiteProfile profile, DaggerfallSiteMarker marker)
    {
        var position = _sites.ProfileToLocal(marker.Position);
        if (resource.SelectedFoe is { } foe)
        {
            if (resource.Binding.ActorIds.Length != 0)
            {
                foreach (long id in resource.Binding.ActorIds)
                {
                    RelocateQuestActor(id, position);
                }
                return resource.Binding;
            }
            long[] actors = new long[foe.Count];
            for (int index = 0; index < actors.Length; index++)
                actors[index] = _roster.Spawn(foe.Definition, new(position, 0));
            return DaggerfallQuestResourceBinding.Actors(actors);
        }
        if (resource.SelectedItem is { } item)
        {
            if (resource.Binding.Kind == DaggerfallQuestResourceBindingKind.Item)
            {
                return PlaceBoundQuestItem(resource.Binding, position);
            }
            return _groundContainers.CreateQuestItem(item, position, _uniqueItems);
        }
        if (resource.SelectedPerson is not null)
        {
            if (resource.Binding.ActorIds.Length == 1 && State.Npcs.Require(resource.Binding.ActorIds[0]).Presence == DaggerfallNpcPresence.Removed) return null;
            return PlaceQuestPerson(instanceId, resource, profile, position);
        }
        throw new ArgumentException($"Quest resource '{resource.Symbol}' has no selected physical meaning.");
    }
    /// <summary>Transfers an unloaded actor's retained values between the existing site and roster owners.</summary>
    private void RelocateQuestActor(long id, WorldPoint position)
    {
        if (State.Actors.TryGet(id, out var live))
        {
            live.ApplyPose(new(position, live.HeadingYawRadians));
            return;
        }
        var owners = _sites.Deltas.Where(entry => entry.Value.DynamicActors.Any(actor => actor.EntityId == id)).ToArray();
        if (owners.Length != 1)
            throw new InvalidOperationException($"Quest actor {id} requires one retained site owner, found {owners.Length}.");
        var source = owners[0];
        var saved = source.Value.DynamicActors.Single(actor => actor.EntityId == id);
        var incoming = new DaggerfallSiteRuntimeDelta([], [saved with { X = position.X, Y = position.Y, Z = position.Z }],
            source.Value.ActorInventories.Where(value => value.EntityId == id).ToArray(),
            source.Value.Corpses.Where(value => value.ActorId == id).ToArray(), [],
            source.Value.Effects.Where(value => value.TargetId == id).ToArray());
        try
        {
            _roster.MaterializeRetainedActor(incoming.DynamicActors.Single());
            _persistence.RestoreSiteDelta(incoming);
        }
        catch { _roster.UnloadActor(id); throw; }
        _sites.ReplaceDelta(source.Key, source.Value with
        {
            DynamicActors = source.Value.DynamicActors.Where(value => value.EntityId != id).ToArray(),
            ActorInventories = source.Value.ActorInventories.Where(value => value.EntityId != id).ToArray(),
            Corpses = source.Value.Corpses.Where(value => value.ActorId != id).ToArray(),
            Effects = source.Value.Effects.Where(value => value.TargetId != id).ToArray(),
        });
    }

}
