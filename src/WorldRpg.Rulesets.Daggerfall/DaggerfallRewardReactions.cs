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
    private readonly HashSet<long> _experienceAwarded = [];
    private readonly HashSet<long> _lootAwarded = [];

    internal void React(ActorDiedFact fact, FactBuffer<IProductFact> facts)
    {
        if (_awarded.Contains(fact.ActorId) || !actors.TryGetValue(fact.ActorId, out DaggerfallActorDefinition? actor)) return;
        if (actor.Rewards.ExperienceReward > 0 && !_experienceAwarded.Contains(fact.ActorId))
        {
            progression.Award(actor.Rewards.ExperienceReward);
            facts.Append(new ExperienceAwardedFact(fact.ActorId, actor.Rewards.ExperienceReward));
            _experienceAwarded.Add(fact.ActorId);
        }
        if (actor.Rewards.Loot is LootDefinition loot && !_lootAwarded.Contains(fact.ActorId))
        {
            int quantity = checked((int)random.DrawKeyed(new KeyedRngRequest(LootRandomKey.Seed, LootRandomKey.Scope, LootRandomKey.For(fact.OriginatingGeneration, fact.OriginatingSequence, fact.ActorId, loot.TableKey), loot.MinimumQuantity, loot.MaximumQuantity)).Value);
            inventory.Grant(new InventoryGrant("daggerfall.loot", $"daggerfall.loot.a{fact.ActorId}.g{fact.OriginatingGeneration}.s{fact.OriginatingSequence}", new InventoryItemId(loot.ItemId.Value), checked((ulong)quantity)));
            facts.Append(new LootAwardedFact(fact.ActorId, loot.ItemId.Value, quantity, fact.OriginatingSequence));
            _lootAwarded.Add(fact.ActorId);
        }
        _awarded.Add(fact.ActorId);
    }
}

internal static class LootRandomKey
{
    internal const ulong Seed = 0;
    internal const string Scope = "dagger.combat.ai.v1";
    internal static string For(ulong generation, ulong update, long actor, string table) => $"generation:{generation}:step:{update}:loot:{actor}:{table}";
}
