using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Where a quest's resources stand once their site is loaded: foes spawned at their marker or moved there, items created
/// or placed again, people moved, turned or projected there, and a retained quest actor brought out of an unloaded site.
/// </summary>
/// <remarks>The exterior's frame, the floor under a point, the roster's spawning, the ground piles, the questor projection and
/// bringing an actor out of an unloaded site (the list BringOutRetainedQuestActor) are handed in.</remarks>
internal sealed class DaggerfallQuestPlacement(ActorsState actors, DaggerfallNpcRegistry npcs, DaggerfallSiteContext directory,
    Func<WorldPoint, WorldPoint> profileToLocal, Func<Vector3, Vector3> localToProfile, Func<WorldPoint, double, WorldPoint> groundPosition,
    Func<string, ActorPose, long> spawn, Func<DaggerfallCreatedItem, WorldPoint, DaggerfallPlacedItem> createItem,
    Func<DaggerfallQuestResourceBinding, WorldPoint, DaggerfallQuestResourceBinding> placeBoundItem,
    Action<DaggerfallNpc, DaggerfallSiteProfile, WorldPoint> project, Action<long, WorldPoint> bringOutRetained) : IDaggerfallQuestWorldAdmission
{
    /// <summary>Places a quest resource at a marker: its foes spawned or moved there, its item created or placed, its person placed.</summary>
    public DaggerfallQuestResourceBinding? Place(string instanceId, DaggerfallQuestResourceState resource,
        DaggerfallSiteProfile profile, DaggerfallSiteMarker marker)
    {
        var position = profileToLocal(marker.Position);
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
                actors[index] = spawn(foe.Definition, new(position, 0));
            return DaggerfallQuestResourceBinding.Actors(actors);
        }
        if (resource.SelectedItem is { } item)
        {
            if (resource.Binding.Kind == DaggerfallQuestResourceBindingKind.Item)
            {
                return placeBoundItem(resource.Binding, position);
            }
            return DaggerfallQuestResourceBinding.Placed(createItem(item, position));
        }
        if (resource.SelectedPerson is not null)
        {
            if (resource.Binding.ActorIds.Length == 1 && npcs.Require(resource.Binding.ActorIds[0]).Presence == DaggerfallNpcPresence.Removed) return null;
            return PlaceQuestPerson(instanceId, resource, profile, position);
        }
        throw new ArgumentException($"Quest resource '{resource.Symbol}' has no selected physical meaning.");
    }
    /// <summary>Moves a quest actor to a position: a live one is posed there, one an unloaded site keeps is brought out.</summary>
    private void RelocateQuestActor(long id, WorldPoint position)
    {
        if (actors.TryGet(id, out var live))
        {
            live.ApplyPose(new(position, live.HeadingYawRadians));
            return;
        }
        bringOutRetained(id, position);
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
            : person.QuestorId ?? npcs.RegisterStable(DaggerfallNpcKind.Questor, instanceId + "/" + resource.Symbol,
                new(profile.Site!.Value.Region, directory.Require(profile.Site.Value).Name, profile.ProfileKey.LogicalId), appearance, "quest person", ["talk"]);
        var npc = npcs.Require(id);
        // GetSiteMarker/AlignBillboardToGround use the marker as a probe. A person stands at their pose; only a
        // projected questor draws the centred billboard, which keeps its pivot above the floor.
        var probe = position;
        if (profile.ProfileKind != DaggerfallWorldProfileKind.Dungeon)
            probe = WorldPoint.From(position.ToVector() + System.Numerics.Vector3.UnitY * (sprite.Size.Y / 2));
        var floor = groundPosition(probe, 4);
        bool projected = npc.Kind != DaggerfallNpcKind.Civilian && !actors.TryGet(id, out _);
        position = projected ? WorldPoint.From(floor.ToVector() + System.Numerics.Vector3.UnitY * (sprite.Size.Y * sprite.Pivot.Y)) : floor;
        if (npc.Kind == DaggerfallNpcKind.Civilian) RelocateQuestActor(id, position);
        else if (actors.TryGet(id, out var actor)) actor.ApplyPose(new(position, actor.HeadingYawRadians));
        else project(npc, profile, position);
        npcs.SetDisplayName(id, person.DisplayName);
        // A live actor owns its own pose; only a projected questor keeps a detached registry placement.
        if (actors.TryGet(id, out _)) npcs.Bind(id, profile.ProfileKey);
        else npcs.Place(id, profile.ProfileKey, WorldPoint.From(localToProfile(position.ToVector())));
        return DaggerfallQuestResourceBinding.Actors(id);
    }
}
