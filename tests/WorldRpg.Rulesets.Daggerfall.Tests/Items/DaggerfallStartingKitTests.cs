using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A new character's starting kit over a player's own pack and worn set, with no session.</summary>
public sealed class DaggerfallStartingKitTests
{
    [Fact]
    public void The_kit_is_asked_in_its_order_and_its_clothes_are_worn_by_their_keys()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        var (choices, career) = Committed();
        string[] keys = [.. DaggerfallStartingKit.Requests(definitions, choices, career).Select(request => request.Key)];

        Assert.Equal(["new-game.spellbook", "new-game.shirt", "new-game.pants"], keys[..3]);
        int weapons = definitions.NewGame.Careers.Single(value => value.Career == career.Id).Items.Count();
        Assert.Equal(Enumerable.Range(0, weapons).Select(index => $"new-game.weapon.{index}"), keys[3..(3 + weapons)]);
        Assert.Equal(definitions.NewGame.Gold > 0, keys.Contains("new-game.gold"));
        Assert.Equal(choices.Background!.StartingGrants.Count(), keys.Count(key => key.StartsWith("new-game.background.", StringComparison.Ordinal)));
        Assert.Equal("chest-clothes", DaggerfallStartingKit.Slot("new-game.shirt"));
        Assert.Equal("legs-clothes", DaggerfallStartingKit.Slot("new-game.pants"));
        Assert.Null(DaggerfallStartingKit.Slot("new-game.weapon.0"));
    }

    [Fact]
    public void A_granted_kit_hands_its_gold_to_the_purse_packs_the_rest_wears_the_clothes_and_a_clear_empties_the_pack()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        var (choices, career) = Committed();
        using ActorsState actors = new();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        PlayerActorState actor = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId(player.Id.Value),
            TestSessions.HealthyMechanics(), "health", DaggerfallActorFactory.PlayerCapabilities);
        InventoryStore store = new();
        store.RegisterInventory(new InventoryState(actor.Actor.Entity, [new InventoryCapacityLimit(DaggerfallEncumbrancePolicy.ClassicWeightMetric, ulong.MaxValue)]));
        store.RegisterEquipment(new EquipmentState(actor.Actor.Entity));
        InventoryComponent pack = new(store, actor.Actor.Entity);
        EquipmentComponent worn = new(store, actor.Actor.Entity);
        actor.Actor.Add(pack);
        actor.Actor.Add(worn);
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerfallActorFactory.ToManagedItem);
        Dictionary<WorldRpg.Kit.Inventory.EquipmentSlotId, EquipmentSlotDefinition> slots = definitions.EquipmentSlots.Values
            .ToDictionary(slot => new WorldRpg.Kit.Inventory.EquipmentSlotId(slot.Id.Value), DaggerfallActorFactory.ToManagedSlot);
        MechanicsInventoryCoordinator inventory = new(pack, actors.Entities, items);
        MechanicsEquipmentCoordinator equipment = new(pack, worn, actors.Entities, items, slots);
        DaggerfallItemInstances instances = new();
        List<ulong> purse = [];

        DaggerfallStartingKit.Make(definitions, RandomMinimum.Create(), choices, career).Grant(inventory, instances,
            new DaggerfallUniqueItemAllocator(DaggerfallUniqueItemAllocator.DefaultFirstEntityId), actors.Entities,
            new DaggerfallEquipmentMoves(inventory, equipment, definitions, itemInstances: instances), gold => { purse.Add(gold); return true; });

        ulong backgroundGold = choices.Background!.StartingGrants.Where(grant => DaggerfallEncumbrancePolicy.IsGold(definitions.RequireItem(new DaggerfallItemId(grant.ItemId))))
            .Aggregate(0UL, (total, grant) => checked(total + grant.Quantity));
        Assert.Equal((ulong)definitions.NewGame.Gold + backgroundGold, purse.Aggregate(0UL, (total, gold) => checked(total + gold)));
        Assert.DoesNotContain(inventory.Read().Stacks, stack => DaggerfallEncumbrancePolicy.IsGold(definitions.RequireItem(new DaggerfallItemId(stack.Definition.Value))));
        Assert.Equal(["chest-clothes", "legs-clothes"], equipment.Read().Assignments.Select(assignment => assignment.Slot.Value).Order());
        Assert.NotEmpty(instances.UniqueItems);

        DaggerfallStartingKit.ClearLoadout(inventory, equipment, instances, actors.Entities);
        Assert.Empty(inventory.Read().UniqueItems);
        Assert.Empty(inventory.Read().Stacks);
        Assert.Empty(equipment.Read().Assignments);
        Assert.Empty(instances.UniqueItems);
    }

    /// <summary>A quick-started character's committed choices and career.</summary>
    private static (DaggerfallCharacterCreationChoices Choices, DaggerfallCareerDefinition Career) Committed()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCharacterState character = new(definitions, new DaggerfallMechanicsState().CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00"))), player);
        DaggerfallCharacterCreation creation = new(character, definitions, RandomMinimum.Create());
        creation.QuickStart(new DaggerfallCharacterSheetFields("Aubk-i", "khajiit", "female", 3, 1, "class08",
            null, null, null, null, null, null, null, null, null));
        return (creation.NewGameChoices(newGameBegun: false), character.Career);
    }
}
