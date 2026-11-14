using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Marker for the transient noncombat projection of a registry-owned person; no fabricated combat
/// mechanics. Its placement lives in the entity's Engine Transform, so the Engine origin helpers
/// rebase it with every other root.
/// </summary>
internal sealed class DaggerfallNpcBody
{
    internal static WorldPoint Position(EntityStore store, EntityId entity) =>
        WorldPoint.From(store.Get(entity, EngineComponentTypes.Transform).Translation);

    internal static Transform Placement(WorldPoint position) =>
        new(position.ToVector(), System.Numerics.Quaternion.Identity, System.Numerics.Vector3.One);
}

/// <summary>
/// The lightweight projection of questors without a gameplay actor: a body entity at the registry's placement with its
/// billboard, recreated on re-entry while placement receipts and registry identities remain.
/// </summary>
/// <param name="profile">The active site's profile.</param>
/// <param name="appearance">The active site's appearance.</param>
/// <param name="locationLoaded">Whether the exterior's active location (its town) is loaded.</param>
/// <param name="profileToLocal">A point in the active profile's frame in the scene's local frame.</param>
internal sealed class DaggerfallNpcProjection(DaggerfallNpcRegistry npcs, ActorsState actors, Func<DaggerfallSiteProfile> profile,
    Func<DaggerfallSiteAppearance> appearance, Func<bool> locationLoaded, Func<WorldPoint, WorldPoint> profileToLocal)
{
    private static readonly EntityTypeId QuestNpcType = new("daggerfall.quest-npc");

    /// <summary>Projects the active site's gameplay-active questors and retires every other projected body.</summary>
    internal void Reconcile()
    {
        var active = profile();
        // Questors without a gameplay actor use this lightweight projection. Static source
        // providers are materialized by DaggerfallActorRoster as real actor entities so dialogue,
        // targeting and site lifetime all observe the same owner; projecting them here would
        // collide with that actor identity.
        var projected = npcs.All.Where(npc => locationLoaded() && npc.Kind == DaggerfallNpcKind.Questor
            && npc.Profile == active.ProfileKey && npcs.IsGameplayActive(npc.DurableId)).ToDictionary(npc => npc.DurableId);
        foreach (var entry in actors.Store.Query<DaggerfallNpcBody>())
        {
            long id = checked((long)actors.Entities.IdentityOf(entry.Entity).Value);
            if (projected.ContainsKey(id)) continue;
            appearance().RetireNpc(id);
            actors.Entities.Destroy(ActorsState.Identity(id));
        }
        foreach (var npc in projected.Values)
        {
            if (npc.X is not float x || npc.Y is not float y || npc.Z is not float z)
                throw new InvalidOperationException($"Placed NPC {npc.DurableId} has no profile position.");
            Project(npc, active, profileToLocal(new(x, y, z)));
        }
    }

    /// <summary>Projects one person at a scene-local position, moving the body it already has.</summary>
    internal void Project(DaggerfallNpc npc, DaggerfallSiteProfile site, WorldPoint localPosition)
    {
        if (!site.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out var sprite))
            throw new NotSupportedException($"NPC {npc.DurableId} has no published billboard {npc.Appearance.BillboardArchive}/{npc.Appearance.BillboardRecord} at '{site.ProfileKey.LogicalId}'.");
        var entities = actors.Entities;
        var identity = ActorsState.Identity(npc.DurableId);
        if (entities.TryResolve(identity, out var existing))
        {
            if (!entities.Store.Has<DaggerfallNpcBody>(existing))
                throw new InvalidOperationException($"NPC {npc.DurableId} is already materialized by a different actor owner.");
            entities.Store.Set(existing, EngineComponentTypes.Transform, DaggerfallNpcBody.Placement(localPosition));
            appearance().AddNpc(npc.DurableId, sprite);
            return;
        }
        var entity = entities.Create(identity, QuestNpcType);
        try
        {
            entities.Store.Add(entity, new DaggerfallNpcBody());
            entities.Store.Set(entity, EngineComponentTypes.Transform, DaggerfallNpcBody.Placement(localPosition));
            appearance().AddNpc(npc.DurableId, sprite);
        }
        catch { appearance().RetireNpc(npc.DurableId); entities.Destroy(identity); throw; }
    }

    /// <summary>Where every projected person stands, for the site appearance.</summary>
    internal IReadOnlyList<DaggerfallNpcView> Views() => actors.Store.Query<DaggerfallNpcBody>()
        .Select(entry => new DaggerfallNpcView(checked((long)actors.Entities.IdentityOf(entry.Entity).Value),
            DaggerfallNpcBody.Position(actors.Store, entry.Entity))).ToArray();
}
