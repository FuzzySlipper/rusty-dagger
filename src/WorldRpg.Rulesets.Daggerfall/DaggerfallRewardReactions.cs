using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Daggerfall-owned reward policy for defeated authored actors.</summary>
internal sealed class DaggerfallRewardReactions(MechanicsInventoryCoordinator inventory, ProgressionState progression, IRandomService random, IReadOnlyDictionary<long, DaggerfallActorDefinition> actors)
{
    private readonly HashSet<long> _awarded = [];

    internal void React(ActorDiedFact fact, FactBuffer<IProductFact> facts)
    {
        if (!_awarded.Add(fact.ActorId) || !actors.TryGetValue(fact.ActorId, out DaggerfallActorDefinition? actor)) return;
        if (actor.Rewards.ExperienceReward > 0)
        {
            progression.Award(actor.Rewards.ExperienceReward);
            facts.Append(new ExperienceAwardedFact(fact.ActorId, actor.Rewards.ExperienceReward));
        }
        if (actor.Rewards.Loot is not LootDefinition loot) return;
        int quantity = checked((int)random.DrawKeyed(new KeyedRngRequest(LootRandomKey.Seed, LootRandomKey.Scope, LootRandomKey.For(fact.OriginatingSequence, fact.ActorId, loot.TableKey), loot.MinimumQuantity, loot.MaximumQuantity)).Value);
        inventory.Grant(new InventoryGrant("daggerfall.loot", $"actor:{fact.ActorId}:step:{fact.OriginatingSequence}", new InventoryItemId(loot.ItemId), checked((ulong)quantity)));
        facts.Append(new LootAwardedFact(fact.ActorId, loot.ItemId, quantity, fact.OriginatingSequence));
    }
}

internal static class LootRandomKey
{
    internal const ulong Seed = 0xDA66E2UL;
    internal const string Scope = "dagger.combat";
    internal static string For(ulong update, long actor, string table) => $"step:{update}:loot:{actor}:{table}";
}
