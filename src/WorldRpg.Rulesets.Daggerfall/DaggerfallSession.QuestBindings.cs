using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    DaggerfallQuestResourceBinding IDaggerfallQuestWorldAdmission.Place(string instanceId, DaggerfallQuestResourceState resource,
        DaggerfallSiteProfile profile, DaggerfallSiteMarker marker)
    {
        var position = _sites.ProfileToLocal(marker.Position);
        if (resource.SelectedFoe is { } foe)
        {
            if (resource.Binding.ActorIds.Length != 0)
            {
                foreach (long id in resource.Binding.ActorIds)
                {
                    if (!State.Actors.TryGet(id, out var actor))
                        throw new NotSupportedException($"Quest foe {id} must be admitted before it can be relocated.");
                    actor.ApplyPose(new(position, actor.Pose.HeadingYawRadians));
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
                _groundContainers.RelocateQuestItem(resource.Binding, position);
                return resource.Binding;
            }
            return _groundContainers.CreateQuestItem(item, position, _uniqueItems);
        }
        if (resource.SelectedPerson is not null) return PlaceQuestPerson(instanceId, resource, profile, position);
        throw new ArgumentException($"Quest resource '{resource.Symbol}' has no selected physical meaning.");
    }
}
