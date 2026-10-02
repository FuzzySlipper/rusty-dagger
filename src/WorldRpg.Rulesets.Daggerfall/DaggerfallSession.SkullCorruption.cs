using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Meaning attached to the copied actor, independent of the source's later lifetime.</summary>
internal sealed record DaggerfallCorruptionOrigin(long ActorId, ulong ItemId)
{
    internal void Validate()
    {
        if (ActorId <= 0 || ItemId == 0) throw new ArgumentException("A corruption copy must name its source actor and item.");
    }
}

internal sealed partial class DaggerfallSession
{
    private DaggerfallInventoryUseResult UseSkullCorruption(UniqueInventoryItem source)
    {
        if (State.PlayerControl.Position is not { } player)
            return new(false, "Skull of Corruption cannot copy without a player position.");
        var nearest = QueryEnemies(DaggerfallActorIdentity.PlayerEntityId, player, Vector3.UnitZ, 12f, -1d,
            definition => definition.Team != "player-ally"
                && definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
            .Where(pair => pair.Distance < 12d)
            .OrderBy(pair => pair.Distance).ThenBy(pair => pair.Target).FirstOrDefault();
        long target = checked((long)nearest.Target);
        if (target == 0 || !State.Actors.TryGet(target, out var original)
            || original.IsDefeated || !DefinitionsByActor.TryGetValue(target, out var definition))
            return new(false, "No monsters nearby.");
        if (State.Quests.ProtectsActor(target))
            return new(false, "Skull of Corruption cannot copy a protected quest target.");
        if (definition.MobileId is not int mobile || !_sites.Projection.Inputs.MobileSprites.ContainsKey(mobile))
            return new(false, "This site cannot display the Skull of Corruption copy.");
        if (!TrySummonPose(source, player, out ActorPose pose, "daggerfall.skull-corruption.v1"))
            return new(false, "This site has no clear ground for the Skull of Corruption copy.");
        // The donor creates a fresh allied mobile of the target's type; it does not duplicate
        // the target's wounds, inventory, quest bindings or native handles.
        long copy = _roster.Spawn(definition.Id.Value, pose, playerAllied: true);
        State.Actors.Get(copy).Actor.Add(new DaggerfallCorruptionOrigin(target,
            State.Inventory.GetDurableItemId(new(source.EntityId)).Value));
        var charged = _itemCondition.Damage(source, 100);
        return new(true, charged.Outcome == DaggerfallItemConditionOutcome.Broken
            ? "Skull of Corruption created an allied copy and broke."
            : "Skull of Corruption created an allied copy.");
    }
}
