using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    DaggerfallQuestResourceBinding? IDaggerfallQuestWorldAdmission.Place(string instanceId, DaggerfallQuestResourceState resource,
        DaggerfallSiteProfile profile, DaggerfallSiteMarker marker)
    {
        var position = _exterior.ProfileToLocal(marker.Position);
        if (resource.SelectedFoe is { } foe)
        {
            if (resource.IsHidden || resource.Binding.ActorIds.Length > 0 && resource.Binding.ActorIds.All(resource.RemovedFoeIds.Contains)) return null;
            if (resource.Binding.ActorIds.Length != 0)
            {
                foreach (long id in resource.Binding.ActorIds.Except(resource.RemovedFoeIds))
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

    /// <summary>Places a quest person: a civilian actor moves, a live actor turns in place, anyone else is projected.</summary>
    private DaggerfallQuestResourceBinding PlaceQuestPerson(string instanceId, DaggerfallQuestResourceState resource,
        DaggerfallSiteProfile profile, WorldPoint position)
    {
        var person = resource.SelectedPerson!;
        var appearance = person.Appearance ?? throw new NotSupportedException($"Quest Person '{resource.Symbol}' has no selected billboard appearance.");
        if (!profile.BillboardSprites.TryGetValue((appearance.BillboardArchive, appearance.BillboardRecord), out var sprite))
            throw new NotSupportedException($"Quest Person '{resource.Symbol}' has no published billboard {appearance.BillboardArchive}/{appearance.BillboardRecord}.");
        long id = resource.Binding.ActorIds.Length == 1 ? resource.Binding.ActorIds[0]
            : person.QuestorId ?? State.Npcs.RegisterStable(DaggerfallNpcKind.Questor, instanceId + "/" + resource.Symbol,
                new(profile.Site!.Value.Region, _site.Require(profile.Site.Value).Name, profile.ProfileKey.LogicalId), appearance, "quest person", ["talk"]);
        var npc = State.Npcs.Require(id);
        // GetSiteMarker/AlignBillboardToGround use the marker as a probe. A person stands at their pose; only a
        // projected questor draws the centred billboard, which keeps its pivot above the floor.
        var probe = position;
        if (profile.ProfileKind != DaggerfallWorldProfileKind.Dungeon)
            probe = WorldPoint.From(position.ToVector() + System.Numerics.Vector3.UnitY * (sprite.Size.Y / 2));
        var floor = _grounding.GroundPosition(probe, 4);
        bool projected = npc.Kind != DaggerfallNpcKind.Civilian && !State.Actors.TryGet(id, out _);
        position = projected ? WorldPoint.From(floor.ToVector() + System.Numerics.Vector3.UnitY * (sprite.Size.Y * sprite.Pivot.Y)) : floor;
        if (npc.Kind == DaggerfallNpcKind.Civilian) RelocateQuestActor(id, position);
        else if (State.Actors.TryGet(id, out var actor)) actor.ApplyPose(new(position, actor.HeadingYawRadians));
        else NpcProjection.Project(npc, profile, position);
        State.Npcs.SetDisplayName(id, person.DisplayName);
        // A live actor owns its own pose; only a projected questor keeps a detached registry placement.
        if (State.Actors.TryGet(id, out _)) State.Npcs.Bind(id, profile.ProfileKey);
        else State.Npcs.Place(id, profile.ProfileKey, WorldPoint.From(_exterior.LocalToProfile(position.ToVector())));
        return DaggerfallQuestResourceBinding.Actors(id);
    }
}
