using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Transient noncombat projection of a registry-owned person; no fabricated combat mechanics.</summary>
internal sealed class DaggerfallNpcBody(ActorPose pose)
{
    internal ActorPose Pose { get; set; } = pose;
}
internal sealed record DaggerfallNpcView(long Id, WorldPoint Position);

internal sealed partial class DaggerfallSession
{
    private static readonly EntityTypeId QuestNpcType = new("daggerfall.quest-npc");

    /// <summary>Re-entry recreates only the projection; placement receipts and registry identities remain.</summary>
    internal void ReconcileNpcProjection()
    {
        var profile = _sites.Projection.Inputs;
        // Questors without a gameplay actor use this lightweight projection. Static source
        // providers are materialized by DaggerfallActorRoster as real actor entities so dialogue,
        // targeting and site lifetime all observe the same owner; projecting them here would
        // collide with that actor identity.
        var active = State.Npcs.All.Where(npc => _sites.ActiveLocationLoaded && npc.Kind == DaggerfallNpcKind.Questor
            && npc.Profile == profile.ProfileKey && State.Npcs.IsGameplayActive(npc.DurableId)).ToDictionary(npc => npc.DurableId);
        foreach (var entry in State.Actors.Store.Query<DaggerfallNpcBody>())
        {
            long id = checked((long)State.Actors.Entities.IdentityOf(entry.Entity).Value);
            if (active.ContainsKey(id)) continue;
            _appearance.RetireNpc(id);
            State.Actors.Entities.Destroy(ActorsState.Identity(id));
        }
        foreach (var npc in active.Values)
        {
            if (npc.X is not float x || npc.Y is not float y || npc.Z is not float z)
                throw new InvalidOperationException($"Placed NPC {npc.DurableId} has no profile position.");
            ProjectNpc(npc, profile, _sites.ProfileToLocal(new(x, y, z)));
        }

        ReconcileCivilianPopulation();
    }

    private void ProjectNpc(DaggerfallNpc npc, DaggerfallSiteProfile profile, WorldPoint localPosition)
    {
        if (!profile.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out var sprite))
            throw new NotSupportedException($"NPC {npc.DurableId} has no published billboard {npc.Appearance.BillboardArchive}/{npc.Appearance.BillboardRecord} at '{profile.ProfileKey.LogicalId}'.");
        var entities = State.Actors.Entities;
        var identity = ActorsState.Identity(npc.DurableId);
        if (entities.TryResolve(identity, out var existing))
        {
            if (!entities.Store.TryGet<DaggerfallNpcBody>(existing, out var body))
                throw new InvalidOperationException($"NPC {npc.DurableId} is already materialized by a different actor owner.");
            body.Pose = new(localPosition, 0);
            _appearance.AddNpc(npc.DurableId, sprite);
            return;
        }
        var entity = entities.Create(identity, QuestNpcType);
        try
        {
            entities.Store.Add(entity, new DaggerfallNpcBody(new(localPosition, 0)));
            _appearance.AddNpc(npc.DurableId, sprite);
        }
        catch { _appearance.RetireNpc(npc.DurableId); entities.Destroy(identity); throw; }
    }

    private IReadOnlyList<DaggerfallNpcView> ReadNpcViews() => State.Actors.Store.Query<DaggerfallNpcBody>()
        .Select(entry => new DaggerfallNpcView(checked((long)State.Actors.Entities.IdentityOf(entry.Entity).Value), entry.Value.Pose.Position)).ToArray();

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
        // GetSiteMarker/AlignBillboardToGround use the marker as a probe and retain the billboard's pivot above the floor.
        var probe = position;
        if (profile.ProfileKind != DaggerfallWorldProfileKind.Dungeon)
            probe = WorldPoint.From(position.ToVector() + System.Numerics.Vector3.UnitY * (sprite.Size.Y / 2));
        var floor = _grounding.GroundPosition(probe, 4);
        position = WorldPoint.From(floor.ToVector() + System.Numerics.Vector3.UnitY * (sprite.Size.Y * sprite.Pivot.Y));
        if (npc.Kind == DaggerfallNpcKind.Civilian) RelocateQuestActor(id, position);
        else if (State.Actors.TryGet(id, out var actor)) actor.ApplyPose(new(position, actor.HeadingYawRadians));
        else ProjectNpc(npc, profile, position);
        State.Npcs.SetDisplayName(id, person.DisplayName);
        State.Npcs.Place(id, profile.ProfileKey, WorldPoint.From(_sites.LocalToProfile(position.ToVector())));
        return DaggerfallQuestResourceBinding.Actors(id);
    }
}
