using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using Xunit;
using EngineItemDefinition = Rusty.Engine.Mechanics.ItemDefinition;
using EngineItemDefinitionId = Rusty.Engine.Mechanics.ItemDefinitionId;
using KitInventoryItemId = WorldRpg.Kit.Inventory.InventoryItemId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRewardReactionTests
{
    [Fact]
    public void Failed_loot_grant_does_not_award_xp_and_retry_is_exactly_once()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition thief = definitions.RequireActor(new DaggerfallActorId("thief"));
        Dictionary<long, DaggerfallActorDefinition> actors = new() { [9000] = thief };
        EntityId owner = new(DaggerfallActorIdentity.PlayerEntityId);
        InventoryWorld world = new();
        world.RegisterInventory(new InventoryState(owner));
        world.RegisterEquipment(new EquipmentState(owner));
        Dictionary<KitInventoryItemId, EngineItemDefinition> managed = new()
        {
            [new KitInventoryItemId("gold-piece")] = ToManaged(definitions.Items[new DaggerfallItemId("gold-piece")]),
        };
        MechanicsInventoryCoordinator inventory = new(world, owner, managed);
        ProgressionState progression = new();
        DaggerfallRewardReactions reactions = new(
            inventory,
            progression,
            RandomMinimums(),
            actors,
            definitions,
            new DaggerfallUniqueItemAllocator(1000));
        ActorDiedFact death = new(9000, DaggerfallActorIdentity.PlayerEntityId, 5, 2, 3);
        FactBuffer<IProductFact> facts = new();

        // Table T's first supported weapon is intentionally absent. The
        // candidate fails before publication, so progression remains untouched.
        Assert.Throws<InvalidOperationException>(() => reactions.React(death, facts));
        Assert.Equal(0, progression.Experience);
        Assert.Empty(inventory.Read().Stacks);
        Assert.Empty(inventory.Read().UniqueItems);

        foreach (DaggerfallItemDefinition item in definitions.Items.Values.Where(item => item.Weapon is not null || item.Armor is not null || item.Shield is not null))
            managed.TryAdd(new KitInventoryItemId(item.Id.Value), ToManaged(item));

        reactions.React(death, facts);
        reactions.React(death, facts);
        Assert.Equal(50, progression.Experience);
        Assert.Equal(1, progression.Level);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Single(delivered.OfType<ExperienceAwardedFact>());
        Assert.NotEmpty(delivered.OfType<LootAwardedFact>());
    }

    [Fact]
    public void Death_by_non_player_does_not_award_player_rewards()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition thief = definitions.RequireActor(new DaggerfallActorId("thief"));
        EntityId owner = new(DaggerfallActorIdentity.PlayerEntityId);
        InventoryWorld world = new();
        world.RegisterInventory(new InventoryState(owner));
        world.RegisterEquipment(new EquipmentState(owner));
        MechanicsInventoryCoordinator inventory = new(world, owner, new Dictionary<KitInventoryItemId, EngineItemDefinition>());
        ProgressionState progression = new();
        DaggerfallRewardReactions reactions = new(
            inventory,
            progression,
            RandomMinimums(),
            new Dictionary<long, DaggerfallActorDefinition> { [9000] = thief },
            definitions,
            new DaggerfallUniqueItemAllocator(1000));

        reactions.React(new ActorDiedFact(9000, 777, 5, 2, 3), new FactBuffer<IProductFact>());

        Assert.Equal(0, progression.Experience);
        Assert.Empty(inventory.Read().Stacks);
    }

    private static IRandomService RandomMinimums()
    {
        IRandomService service = DispatchProxy.Create<IRandomService, RandomMinimumProxy>();
        return service;
    }

    private class RandomMinimumProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

    private static EngineItemDefinition ToManaged(DaggerfallItemDefinition item)
    {
        ItemEquipmentPolicy? equipment = item.Equipment is null
            ? null
            : new ItemEquipmentPolicy(
                item.Equipment.RequiredSlots,
                item.Equipment.ExclusiveGroup is { } group ? EquipmentExclusivityId.Parse(group) : null);
        return new EngineItemDefinition(
            EngineItemDefinitionId.Parse(item.Id.Value),
            item.IsFungible ? ItemKind.Fungible : ItemKind.Unique,
            item.MaximumQuantity,
            item.Equipment?.Classifications.Select(ItemClassificationId.Parse),
            null,
            equipment);
    }

    private static DaggerfallDefinitions LoadDefinitions() => DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
