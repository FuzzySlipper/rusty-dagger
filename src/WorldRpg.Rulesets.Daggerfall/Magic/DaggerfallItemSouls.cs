using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// A creature to place near a point: drawn under the operation and random scope between the two distances, of the
/// definition whose sprites are the mobile's.
/// </summary>
internal sealed record DaggerfallSpawnRequest(string Operation, string RandomScope, WorldPoint Origin, float MinimumDistance,
    float MaximumDistance, DaggerfallActorDefinition Definition, int Mobile);

/// <summary>
/// The souls items hold: Azura's Star takes the soul of what the player kills while it is worn and releases it on
/// use, and a broken item's bound soul escapes as a hostile creature where its holder stands.
/// </summary>
/// <param name="definitionOf">A site actor's definition, when it has one.</param>
/// <param name="holderPosition">Where an item's holder stands, when that is the player or a live actor.</param>
/// <param name="spawnHostile">
/// Places a hostile creature of the definition (its mobile) between the two distances of the point, drawn under the
/// operation and random scope, when the site can show it and has clear ground; false otherwise.
/// </param>
internal sealed class DaggerfallItemSouls(DaggerfallSoulGems gems, DaggerfallDefinitions definitions, DaggerfallItemInstances instances,
    DaggerfallHeldEnchantments held, DaggerfallEffectLifecycle effects, FactBuffer<IProductFact> facts, PresentationState presentation,
    Func<long, DaggerfallActorDefinition?> definitionOf, Func<DaggerfallItemOwner, WorldPoint?> holderPosition,
    Func<DaggerfallSpawnRequest, bool> spawnHostile)
{
    internal DaggerfallSoulGems Gems => gems;

    internal DaggerfallInventoryUseResult UseAzurasStar(ulong id)
    {
        int? released = gems.ReleaseStar(id);
        return released is { } mobile ? new(true, $"Released {definitions.Actors.Values.First(actor => actor.Kind == DaggerfallActorKinds.Monster && actor.MobileId == mobile).Id.Value} from Azura's Star.")
            : new(false, "Azura's Star has no soul to release.");
    }

    internal void ReleaseBoundSoul(ulong itemId)
    {
        if (!instances.ContainsUnique(itemId)) return;
        var item = instances.RequireUnique(itemId);
        if (!item.BoundSoulReleasePending || item.BoundSoulReleased) return;
        if (!definitions.Magic.TryEnchantments(item, out var payloads)) return;
        var soul = payloads.SingleOrDefault(value => value.Type == 15);
        if (soul is null) return;
        var definition = definitions.Actors.Values.FirstOrDefault(actor => actor.Kind == DaggerfallActorKinds.Monster && actor.MobileId == soul.Param);
        if (definition is null) throw new InvalidOperationException($"Bound soul {soul.Param} has no admitted actor definition.");
        if (holderPosition(item.Owner) is not { } position
            || !spawnHostile(new($"soul:{itemId}", "daggerfall.soul-bound.v1", position, 4f, 20f, definition, soul.Param))) return;
        instances.ReplaceUnique(itemId, item with { BoundSoulReleasePending = false, BoundSoulReleased = true });
        presentation.SetOutcome($"The bound {definition.Id.Value} escaped from the broken item.");
    }

    internal void ReleasePendingBoundSouls()
    {
        foreach (ulong itemId in instances.UniqueItems.Where(value => value.Value.BoundSoulReleasePending).Select(value => value.Key).ToArray())
            ReleaseBoundSoul(itemId);
    }

    internal void CaptureHeldSoul(ActorDiedFact death)
    {
        if (death.ActorId == DaggerfallActorIdentity.PlayerEntityId
            || death.KillerId != DaggerfallActorIdentity.PlayerEntityId
            || !held.AzurasStarEquipped
            || DaggerfallItemSoulEffects.WasSoulCaptured(effects, death.ActorId)) return;
        int? mobile = definitionOf(death.ActorId) is { Kind: DaggerfallActorKinds.Monster } actor ? actor.MobileId : null;
        var result = gems.CaptureStar(mobile);
        facts.Append(new AzurasStarCaptureFact(death.ActorId, result.ItemId, result.Outcome));
    }
}
