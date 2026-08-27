using Rusty.Engine;
using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.Inventory;

namespace RustyDagger.Game.Modules.Loot;

internal sealed class LootReaction(InventoryState inventory, IRandomService random)
{
    private readonly HashSet<long> _awardedActors = [];

    internal void React(ActorDiedFact fact, DaggerfallState state, ProductFactBuffer facts)
    {
        if (_awardedActors.Contains(fact.ActorId) || !state.Actors.TryGet(fact.ActorId, out ActorState actor) || !actor.IsDead || actor.Definition.Loot is not { } loot) return;
        _awardedActors.Add(fact.ActorId);
        int quantity = DrawLoot(loot.MinimumQuantity, loot.MaximumQuantity, LootRandomKey.For(fact.OriginatingSequence, actor.EntityId, loot.TableKey));
        inventory.Add(new ItemStack(loot.ItemId, quantity));
        facts.Append(new LootAwardedFact(actor.EntityId, loot.ItemId, quantity, fact.OriginatingSequence));
    }

    private int DrawLoot(int minimum, int maximum, string key) => checked((int)random.DrawKeyed(new KeyedRngRequest(LootRandomKey.Seed, LootRandomKey.Scope, key, minimum, maximum)).Value);
}

internal static class LootRandomKey
{
    internal const ulong Seed = 0xDA66E2UL;
    internal const string Scope = "dagger.combat";
    internal static string For(ulong update, long actor, string table) => $"step:{update}:loot:{actor}:{table}";
}
