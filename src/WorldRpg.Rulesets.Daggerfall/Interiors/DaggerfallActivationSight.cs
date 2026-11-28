using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Where a body or a person is sighted for use, from the sprite sizes the active site's inputs carry. A visibility ray
/// to where an actor stands or lies can end on, or under, the ground there and read as occluded from every side, as
/// DaggerfallTargetingPolicy.AimPoint notes for grounded actors, so each is sighted at the middle of its sprite.
/// </summary>
/// <param name="inputs">The active site's inputs: its placed and mobile sprites and its people's billboards.</param>
/// <param name="definitions">The roster's definition of each live actor, which names its mobile.</param>
internal sealed class DaggerfallActivationSight(
    Func<DaggerfallSiteProfile> inputs,
    DaggerfallNpcRegistry npcs,
    ActorsState actors,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions)
{
    /// <summary>Where a corpse is sighted and reached: the middle of its lying sprite above the floor contact.</summary>
    internal WorldPoint Corpse(ActorState actor)
    {
        if (ActorSprite(actor, inputs()) is not { } sprite) return actor.Position;
        float height = (sprite.Corpse ?? sprite).Size.Y;
        return WorldPoint.From(actor.Position.ToVector() + (Vector3.UnitY * (height * .5F)));
    }

    /// <summary>
    /// How far above where a person stands they are sighted for use: the middle of their standing sprite. A placed
    /// person shows their billboard, anyone else their actor sprite; both stand at their pose. A person with no known
    /// sprite keeps their position.
    /// </summary>
    internal double? PersonHeight(long id)
    {
        DaggerfallSiteProfile site = inputs();
        if (npcs.TryGet(id, out DaggerfallNpc? npc)
            && site.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out NormalizedBillboardSprite? billboard))
            return billboard.Size.Y * .5d;
        return actors.TryGet(id, out ActorState? actor) && ActorSprite(actor, site) is { } sprite ? sprite.Size.Y * .5d : null;
    }

    /// <summary>
    /// Standing body boxes for every live site actor, keyed by durable identity (matching NPC activation query
    /// identities so Engine perception ignores a target's own body while nearer actors block targets behind them).
    /// </summary>
    internal ReadOnlyMemory<SpatialEntityCollider> ActorOccluders()
    {
        DaggerfallSiteProfile site = inputs();
        List<SpatialEntityCollider> colliders = [];
        foreach (ActorState actor in actors.All)
        {
            if (actor.IsDefeated || actor.DurableId == DaggerfallActorIdentity.PlayerEntityId) continue;
            (float halfWidth, float height) = BodyExtent(site, actor);
            Vector3 feet = actor.Position.ToVector();
            Vector3 reach = new(halfWidth, 0f, halfWidth);
            Vector3 top = new(halfWidth, height, halfWidth);
            colliders.Add(new SpatialEntityCollider(checked((ulong)actor.DurableId), feet - reach, feet + top, 0, 0, true, false, false));
        }
        return colliders.ToArray();
    }

    private (float HalfWidth, float Height) BodyExtent(DaggerfallSiteProfile site, ActorState actor)
    {
        // Keep the canonical contact half-width (0.3 m) so a nearby actor's box stays outside the player's eye
        // and never occludes targets in other directions, while matching the sprite's vertical height so small
        // creatures (rats) do not block standing people behind them.
        if (npcs.TryGet(actor.DurableId, out DaggerfallNpc? npc)
            && site.BillboardSprites.TryGetValue((npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord), out NormalizedBillboardSprite? billboard))
            return (DaggerfallContactBody.Envelope.HalfWidth, MathF.Max(.1f, billboard.Size.Y));
        if (ActorSprite(actor, site) is { } sprite)
            return (DaggerfallContactBody.Envelope.HalfWidth, MathF.Max(.1f, sprite.Size.Y));
        return (DaggerfallContactBody.Envelope.HalfWidth, DaggerfallContactBody.Envelope.Height);
    }

    private NormalizedActorSprite? ActorSprite(ActorState actor, DaggerfallSiteProfile site) =>
        site.ActorSprites.TryGetValue(actor.DurableId, out NormalizedActorSprite? placed)
            ? placed
            : definitions.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? definition)
                && definition.MobileId is int mobileId && site.MobileSprites.TryGetValue(mobileId, out NormalizedActorSprite? mobile)
                    ? mobile.ForGender(actor.Actor.TryGet<DaggerfallActorGender>(out var gender) ? gender.Value : "unspecified")
                    : null;
}
