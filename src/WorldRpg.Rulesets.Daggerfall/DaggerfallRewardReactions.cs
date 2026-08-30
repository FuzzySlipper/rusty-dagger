using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Daggerfall-owned reward policy for defeated authored actors.</summary>
internal sealed class DaggerfallRewardReactions(MechanicsInventoryCoordinator inventory, ProgressionState progression, IRandomService random, IReadOnlyDictionary<long, DaggerfallActorDefinition> actors, DaggerfallDefinitions definitions, DaggerfallUniqueItemAllocator uniqueItems)
{
    private readonly HashSet<long> _awarded = [];
    private readonly HashSet<long> _experienceAwarded = [];
    private readonly HashSet<long> _lootAwarded = [];

    internal void React(ActorDiedFact fact, FactBuffer<IProductFact> facts)
    {
        if (fact.KillerId != DaggerfallActorIdentity.PlayerEntityId) return;
        if (_awarded.Contains(fact.ActorId) || !actors.TryGetValue(fact.ActorId, out DaggerfallActorDefinition? actor)) return;

        int nextExperience = checked(progression.Experience + Math.Max(0, actor.Rewards.ExperienceReward));
        int nextLevel = checked(1 + DaggerfallFormulaPolicy.ExperimentalXpLevel(nextExperience, DaggerfallFormulaPolicy.Experimental));
        DaggerfallLootResult? loot = null;
        if (actor.LootTableKey is { } tableKey && !_lootAwarded.Contains(fact.ActorId))
        {
            loot = DaggerfallLootPolicy.Generate(
                definitions,
                tableKey,
                nextLevel,
                (id, minimum, maximum) => checked((int)random.DrawKeyed(new KeyedRngRequest(
                    LootRandomKey.Seed,
                    LootRandomKey.Scope,
                    LootRandomKey.For(fact.OriginatingGeneration, fact.OriginatingSequence, fact.ActorId, id),
                    minimum,
                    maximum)).Value));
            List<InventoryAtomicGrant> grants = [];
            foreach (DaggerfallLootDrop drop in loot.Drops)
            {
                DaggerfallItemDefinition item = definitions.Items[new DaggerfallItemId(drop.ItemId)];
                grants.Add(item.IsFungible
                    ? new InventoryAtomicGrant(new InventoryItemId(drop.ItemId), checked((ulong)drop.Quantity))
                    : new InventoryAtomicGrant(
                        new InventoryItemId(drop.ItemId),
                        Identity: $"daggerfall.loot.a{fact.ActorId}.g{fact.OriginatingGeneration}.s{fact.OriginatingSequence}.{grants.Count}",
                        EntityId: uniqueItems.Allocate()));
            }
            if (grants.Count > 0) inventory.GrantAtomic(grants);
            foreach (DaggerfallLootDrop drop in loot.Drops)
                facts.Append(new LootAwardedFact(fact.ActorId, drop.ItemId, drop.Quantity, fact.OriginatingSequence));
            _lootAwarded.Add(fact.ActorId);
        }
        if (actor.Rewards.ExperienceReward > 0 && !_experienceAwarded.Contains(fact.ActorId))
        {
            progression.Award(actor.Rewards.ExperienceReward);
            progression.AdvanceTo(nextExperience, nextLevel);
            facts.Append(new ExperienceAwardedFact(fact.ActorId, actor.Rewards.ExperienceReward));
            _experienceAwarded.Add(fact.ActorId);
        }
        _awarded.Add(fact.ActorId);
    }
}

internal static class LootRandomKey
{
    internal const ulong Seed = 0;
    internal const string Scope = "dagger.combat.ai.v1";
    internal static string For(ulong generation, ulong update, long actor, string roll) => $"generation:{generation}:step:{update}:loot:{actor}:{roll}";
}

/// <summary>Session-owned monotonic allocator for generated unique loot entities.</summary>
internal sealed class DaggerfallUniqueItemAllocator(ulong firstEntityId)
{
    internal const ulong DefaultFirstEntityId = 1_000_000_000_000UL;
    private ulong _next = firstEntityId;

    internal ulong Allocate()
    {
        if (_next == 0 || _next == ulong.MaxValue) throw new InvalidOperationException("The Daggerfall loot entity range is exhausted.");
        return _next++;
    }
}
