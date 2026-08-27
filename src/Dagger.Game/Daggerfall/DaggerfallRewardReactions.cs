using Rusty.Engine;
using RustyDagger.Game.Daggerfall.Content;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Inventory;
using RustyDagger.Game.Modules.Progression;

namespace RustyDagger.Game.Daggerfall;

/// <summary>Daggerfall-owned reward policy for defeated authored actors.</summary>
internal sealed class DaggerfallRewardReactions(InventoryState inventory, ProgressionState progression, IRandomService random, IReadOnlyDictionary<long, DaggerfallActorDefinition> actors)
{
    private readonly HashSet<long> _awarded = [];

    internal void React(ActorDiedFact fact, ProductFactBuffer facts)
    {
        if (!_awarded.Add(fact.ActorId) || !actors.TryGetValue(fact.ActorId, out DaggerfallActorDefinition? actor)) return;
        if (actor.Rewards.ExperienceReward > 0)
        {
            progression.Award(actor.Rewards.ExperienceReward);
            facts.Append(new ExperienceAwardedFact(fact.ActorId, actor.Rewards.ExperienceReward));
        }
        if (actor.Rewards.Loot is not LootDefinition loot) return;
        int quantity = checked((int)random.DrawKeyed(new KeyedRngRequest(LootRandomKey.Seed, LootRandomKey.Scope, LootRandomKey.For(fact.OriginatingSequence, fact.ActorId, loot.TableKey), loot.MinimumQuantity, loot.MaximumQuantity)).Value);
        inventory.Add(new ItemStack(loot.ItemId, quantity));
        facts.Append(new LootAwardedFact(fact.ActorId, loot.ItemId, quantity, fact.OriginatingSequence));
    }
}

internal static class LootRandomKey
{
    internal const ulong Seed = 0xDA66E2UL;
    internal const string Scope = "dagger.combat";
    internal static string For(ulong update, long actor, string table) => $"step:{update}:loot:{actor}:{table}";
}
