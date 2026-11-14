using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Persistence;
using System.Reflection;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallItemConditionServiceTests
{
    [Fact]
    public void Broken_equipped_item_survives_actual_session_reload_then_repairs_and_reequips_with_its_durable_identity()
    {
        using var fixture = new ConditionSessionFixture();
        DaggerfallSession source = fixture.Session;
        source.State.Character.BeginChoices();
        source.State.Character.ReplacePending(source.State.Character.ReadCreation().Current with
        {
            CareerId = "class16",
            CustomCareer = null,
            Background = null,
        });
        source.State.Character.CommitChoices();
        WorldRpg.Kit.Inventory.EquipmentAssignment equipped = source.State.Equipment.Read().Assignments.First();
        UniqueItem item = equipped.Item;
        ulong durable = source.State.Inventory.GetDurableItemId(new EntityId(item.EntityId)).Value;

        Assert.Equal(DaggerfallItemConditionOutcome.Broken, source.ItemCondition.Damage(item, int.MaxValue).Outcome);
        RulesetSavePayload save = source.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(save);
        UniqueItem restoredItem = restored.State.Inventory.Read().UniqueItems.Single(candidate =>
            restored.State.Inventory.GetDurableItemId(candidate.Entity).Value == durable) is { } candidate
                ? new UniqueItem(candidate.Entity.Value, new InventoryItemId(candidate.Definition.Value)) : throw new InvalidOperationException();

        Assert.Equal(0, restored.State.ItemInstances.RequireUnique(durable).CurrentCondition);
        Assert.DoesNotContain(restored.State.Equipment.Read().Assignments, assignment => assignment.Item.EntityId == restoredItem.EntityId);
        Assert.Equal(DaggerfallItemConditionOutcome.Repaired, restored.ItemCondition.Repair(restoredItem).Outcome);
        EquipmentMoveResult reequipped = restored.EquipmentMoves.MoveToSlot(restoredItem, new SlotId(equipped.Slot.Value));
        Assert.True(reequipped.Outcome == EquipmentMoveOutcome.Applied, reequipped.Detail);
        Assert.Contains(restored.State.Equipment.Read().Assignments, assignment => assignment.Item.EntityId == restoredItem.EntityId);
        Assert.Equal(durable, restored.State.Inventory.GetDurableItemId(new EntityId(restoredItem.EntityId)).Value);
    }

    [Fact]
    public void Identifying_an_unidentified_made_enchantment_succeeds()
    {
        // A made enchantment names its settings rather than a published magic template, so identifying it
        // discloses what the settings do without demanding a template it does not have.
        using Fixture f = new();
        UniqueItem sword = f.CreatePlainWeapon(407, 115, "daedric");
        string settingKey = TestPayload.Definitions.Magic.EnchantmentSettings.Values.Single(candidate => candidate.Type == 7 && candidate.Param == 0).Key;
        f.Enchanting.Enchant(sword, settingKey);
        f.Instances.ReplaceUnique(407, f.Instances.RequireUnique(407) with { Identified = false });

        DaggerfallItemConditionResult result = f.Service.Identify(sword);

        Assert.Equal(DaggerfallItemConditionOutcome.Identified, result.Outcome);
        Assert.True(f.Instances.RequireUnique(407).Identified);
    }

    [Fact]
    public void A_live_setting_key_in_the_magic_item_field_is_refused_by_condition_operations()
    {
        // Item-maker settings travel only as a made enchantment, so a setting key that reaches the
        // magic-item field by any path is malformed metadata here as it is in save validation.
        using Fixture f = new();
        UniqueItem sword = f.CreatePlainWeapon(408, 115, "daedric");
        string settingKey = TestPayload.Definitions.Magic.EnchantmentSettings.Values.Single(candidate => candidate.Type == 7 && candidate.Param == 0).Key;
        f.Instances.ReplaceUnique(408, f.Instances.RequireUnique(408) with { Enchantment = settingKey, Identified = false });

        Assert.Throws<InvalidOperationException>(() => f.Service.Identify(sword));
        Assert.Throws<InvalidOperationException>(() => f.Service.Damage(sword, 1));
        Assert.False(f.Instances.RequireUnique(408).Identified);
    }

    [Fact]
    public void A_saved_setting_key_is_not_magic_item_metadata_and_is_refused()
    {
        // An item maker's settings are saved as a made enchantment; the magic-item field names only a
        // published magic item, so a setting key there is malformed current data.
        using var fixture = new ConditionSessionFixture();
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(fixture.Session.CaptureSave());
        DaggerfallUniqueSave target = saved.Inventory.UniqueItems.First();
        string settingKey = TestPayload.Definitions.Magic.EnchantmentSettings.Values.Single(candidate => candidate.Type == 7 && candidate.Param == 0).Key;
        DaggerfallSavePayload enchanted = saved with
        {
            Inventory = saved.Inventory with
            {
                UniqueItems = saved.Inventory.UniqueItems.Select(item => item.EntityId == target.EntityId
                    ? item with { Metadata = item.Metadata with { Enchantment = settingKey, Identified = true } }
                    : item).ToArray(),
            },
        };

        Assert.Throws<ArgumentException>(() => fixture.Restore(DaggerfallSavePayload.Encode(enchanted)));
    }

    [Fact]
    public void Restore_rejects_unknown_or_incompatible_enchantment_metadata_before_materializing_items()
    {
        using var fixture = new ConditionSessionFixture();
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(fixture.Session.CaptureSave());
        DaggerfallUniqueSave unknownTarget = saved.Inventory.UniqueItems.First();
        DaggerfallSavePayload unknown = saved with
        {
            Inventory = saved.Inventory with
            {
                UniqueItems = saved.Inventory.UniqueItems.Select(item => item.EntityId == unknownTarget.EntityId
                    ? item with { Metadata = item.Metadata with { Enchantment = "magic-item.not-published", Identified = false } }
                    : item).ToArray(),
            },
        };
        const string magicKey = "magic-item.0010";
        DaggerfallUniqueSave incompatibleTarget = saved.Inventory.UniqueItems.First(item =>
            !fixture.Definitions.TryResolveItem(new DaggerfallItemId(DaggerfallMagicItemIds.For(item.ItemId, magicKey)), out _));
        DaggerfallSavePayload incompatible = saved with
        {
            Inventory = saved.Inventory with
            {
                UniqueItems = saved.Inventory.UniqueItems.Select(item => item.EntityId == incompatibleTarget.EntityId
                    ? item with { Metadata = item.Metadata with { Enchantment = magicKey, Identified = false } }
                    : item).ToArray(),
            },
        };

        Assert.Throws<ArgumentException>(() => fixture.Restore(DaggerfallSavePayload.Encode(unknown)));
        Assert.Throws<ArgumentException>(() => fixture.Restore(DaggerfallSavePayload.Encode(incompatible)));
    }

    [Fact]
    public void Breaking_an_equipped_item_clamps_condition_unequips_once_and_repair_restores_eligibility()
    {
        using Fixture f = new();
        UniqueItem sword = f.Materialize(101, "iron-longsword", condition: 2, maximumCondition: 2);
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(sword, new SlotId("right-hand")).Outcome);
        List<DaggerfallEquipmentChange> changes = [];
        f.Moves.Changed += changes.Add;

        DaggerfallItemConditionResult damaged = f.Service.Damage(sword, 1);
        DaggerfallItemConditionResult broken = f.Service.Damage(sword, 9);

        Assert.Equal((DaggerfallItemConditionOutcome.Damaged, 1, 50), (damaged.Outcome, damaged.Metadata.CurrentCondition,
            f.Service.Condition(damaged.Metadata).Percentage));
        Assert.Equal(DaggerfallItemConditionOutcome.Broken, broken.Outcome);
        DaggerfallEquipmentChange change = Assert.Single(changes);
        Assert.Equal(broken.EquipmentChange, change);
        Assert.Equal([sword], change.Removed);
        Assert.DoesNotContain(f.Equipment.Read().Assignments, assignment => assignment.Item == sword);
        Assert.Equal(DaggerfallItemConditionOutcome.AlreadyBroken, f.Service.Damage(sword, 1).Outcome);
        Assert.Single(changes);

        Assert.Equal(DaggerfallItemConditionOutcome.Repaired, f.Service.Repair(sword).Outcome);
        Assert.Equal((2, 100), (f.Service.Read(101).Current, f.Service.Read(101).Percentage));
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Moves.MoveToSlot(sword, new SlotId("right-hand")).Outcome);
    }

    [Fact]
    public void Refuel_adds_only_the_supplied_condition_units_and_clamps_at_the_lantern_capacity()
    {
        using Fixture f = new();
        UniqueItem lantern = f.Materialize(151, "template-248", condition: 60, maximumCondition: 100);

        DaggerfallItemConditionResult first = f.Service.Refuel(lantern, 25);
        DaggerfallItemConditionResult capped = f.Service.Refuel(lantern, 25);

        Assert.Equal((DaggerfallItemConditionOutcome.Repaired, 85), (first.Outcome, first.Metadata.CurrentCondition));
        Assert.Equal((DaggerfallItemConditionOutcome.Repaired, 100), (capped.Outcome, capped.Metadata.CurrentCondition));
        Assert.Equal(DaggerfallItemConditionOutcome.AlreadyRepaired, f.Service.Refuel(lantern, 1).Outcome);
    }

    [Fact]
    public void Using_oil_refuels_a_lantern_and_consumes_exactly_one_real_oil_stack_unit()
    {
        using Fixture f = new();
        UniqueItem lantern = f.Materialize(161, "template-248", condition: 60, maximumCondition: 100);
        DaggerfallItemDefinition oilDefinition = f.Definitions.RequireItem(new DaggerfallItemId("template-252"));
        InventoryStackId oil = InventoryStackId.Parse("condition.oil");
        f.Inventory.Grant(new InventoryGrant(new InventoryItemId(oilDefinition.Id.Value), oil, 2));
        f.Instances.RegisterDefaultStack(DaggerfallItemOwner.Player, f.Inventory.Read().Stacks.Single(stack => stack.Id == oil), oilDefinition);
        DaggerfallInventoryUseService use = new(f.Inventory, f.Definitions, f.Instances,
            new DaggerfallUniqueItemAllocator(1_000), _ => throw new InvalidOperationException("Lantern oil reveals no location."), f.Service);

        DaggerfallInventoryUseResult result = use.Use($"stack:{oil.Value}", f.Inventory.Read().StoreRevision);

        Assert.True(result.Applied, result.Message);
        Assert.Equal(61, f.Service.Read(161).Current);
        Assert.Equal(1UL, f.Inventory.Read().Stacks.Single(stack => stack.Id == oil).Quantity);
        Assert.Equal(lantern.EntityId, f.Inventory.Read().UniqueItems.Single().Entity.Value);
        _ = f.Inventory.Destroy(lantern);
        DaggerfallInventoryUseResult missingLantern = use.Use($"stack:{oil.Value}", f.Inventory.Read().StoreRevision);
        Assert.False(missingLantern.Applied);
        Assert.Equal("You need a lantern to use this oil.", missingLantern.Message);
        Assert.Equal(1UL, f.Inventory.Read().Stacks.Single(stack => stack.Id == oil).Quantity);
    }

    [Fact]
    public void Identification_persists_through_transfer_and_reload_while_unknown_magic_remains_distinct()
    {
        using Fixture f = new();
        const string magicKey = "magic-item.0010";
        DaggerfallMagicItemDefinition magic = f.Definitions.Magic.MagicItems[magicKey];
        string magicId = DaggerfallMagicItemIds.For("template-113-iron", magicKey);
        _ = f.Definitions.RequireItem(new DaggerfallItemId(magicId));
        UniqueItem magicItem = f.Materialize(202, magicId, magic.Uses, magic.Uses, identified: false, enchantment: magicKey);

        InventoryItemPresentation unknown = Assert.Single(f.Presentation.Read().Items);
        DaggerfallItemConditionResult identified = f.Service.Identify(magicItem);
        InventoryItemPresentation known = Assert.Single(f.Presentation.Read().Items);
        Assert.Equal(DaggerfallItemConditionOutcome.AlreadyIdentified, f.Service.Identify(magicItem).Outcome);
        f.Instances.MoveUnique(202, DaggerfallItemOwner.Corpse(17));
        DaggerfallItemInstanceMetadata moved = f.Instances.RequireUnique(202);
        DaggerfallItemInstanceMetadata restored = DaggerfallItemInstanceMetadata.Restore(moved.ItemId, moved.Capture());

        Assert.Equal(DaggerfallItemConditionOutcome.Identified, identified.Outcome);
        Assert.False(unknown.Identified);
        Assert.Equal(f.Definitions.RequireItem(new DaggerfallItemId(magicId)).Template!.Name, unknown.Label);
        Assert.DoesNotContain(magic.Enchantments[0].ParamMeaning, unknown.Details, StringComparison.OrdinalIgnoreCase);
        Assert.True(known.Identified);
        // The identified enchantment is named by its setting, as the donor names it.
        Assert.Contains("Enchantment: Cast when used: Silence", known.Details, StringComparison.Ordinal);
        // The tooltip states condition and figures from the row's own fields, so a magical item adds only its magic.
        Assert.True(unknown.DetailsStateFacts);
        Assert.Equal(["A magical item: identify it to learn its powers."], unknown.Notes);
        Assert.True(known.DetailsStateFacts);
        Assert.Equal(["Enchantment: Cast when used: Silence"], known.Notes);
        Assert.NotNull(known.Condition);
        Assert.True(restored.Identified);
        Assert.Equal(magicKey, restored.Enchantment);
        Assert.Equal(DaggerfallItemOwner.Corpse(17), restored.Owner);
    }

    [Fact]
    public void Remote_or_stale_items_are_refused_without_mutating_player_item_meaning()
    {
        using Fixture f = new();
        UniqueItem sword = f.CreatePlainSword(303);
        DaggerfallItemInstanceMetadata before = f.Instances.RequireUnique(303);
        f.Instances.MoveUnique(303, DaggerfallItemOwner.Corpse(17));

        Assert.Throws<InvalidOperationException>(() => f.Service.Damage(sword, 1));
        Assert.Throws<InvalidOperationException>(() => f.Service.Repair(sword));
        Assert.Throws<InvalidOperationException>(() => f.Service.Identify(sword));
        Assert.Throws<InvalidOperationException>(() => f.Enchanting.Enchant(sword, "not-published"));
        Assert.Equal(before with { Owner = DaggerfallItemOwner.Corpse(17) }, f.Instances.RequireUnique(303));
        Assert.Throws<InvalidOperationException>(() => f.Service.Repair(new UniqueItem(999_999, new InventoryItemId("iron-longsword"))));
    }

    [Fact]
    public void Condition_percentage_has_the_donor_zero_maximum_and_truncating_rules()
    {
        Assert.Equal(100, DaggerfallFormulaPolicy.ConditionPercentage(0, 0));
        Assert.Equal(66, DaggerfallFormulaPolicy.ConditionPercentage(2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallFormulaPolicy.ConditionPercentage(4, 3));
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly EntityDirectory Entities = new();
        internal readonly MechanicsInventoryCoordinator Inventory;
        internal readonly MechanicsEquipmentCoordinator Equipment;
        internal readonly DaggerfallDefinitions Definitions;
        internal readonly DaggerfallItemInstances Instances = new();
        internal readonly DaggerfallEquipmentMoves Moves;
        internal readonly DaggerfallItemConditionService Service;
        internal readonly DaggerfallItemEnchanting Enchanting;
        internal readonly DaggerfallInventoryPresentation Presentation;

        internal Fixture(DaggerfallDefinitions? definitions = null)
        {
            Definitions = definitions ?? TestPayload.Definitions;
            var items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values).ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            var slots = Definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
            InventoryStore world = new();
            EntityId owner = Entities.Create(new(DurableIdentityKind.Actor, 1), new("test.player"));
            world.RegisterInventory(new InventoryState(owner));
            world.RegisterEquipment(new EquipmentState(owner));
            InventoryComponent inventory = new(world, owner);
            EquipmentComponent equipment = new(world, owner);
            Entities.Store.Add(owner, inventory);
            Entities.Store.Add(owner, equipment);
            Inventory = new(inventory, Entities, items);
            Equipment = new(inventory, equipment, Entities, items, slots);
            Moves = new(Inventory, Equipment, Definitions, itemInstances: Instances);
            Service = new(Definitions, Instances, Moves);
            Enchanting = new(Definitions, Instances, Moves, Service);
            Presentation = new(Moves, Definitions, new Dictionary<string, string>());
            Presentation.UseItemValuation(new DaggerfallItemValuation(Definitions), Instances, DaggerfallItemOwner.Player,
                entity => Entities.IdentityOf(new EntityId(entity)).Value);
            Presentation.UseItemCondition(Service);
        }

        internal UniqueItem Materialize(ulong durable, string itemId, int condition, int maximumCondition,
            bool identified = true, string? enchantment = null)
        {
            UniqueItem item = Equipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, durable), new InventoryItemId(itemId));
            Instances.RegisterUnique(durable, new DaggerfallItemInstanceMetadata(itemId, "iron", 0, condition, maximumCondition,
                identified, Stolen: false, QuestId: null, QuestItemSymbol: null, enchantment, DaggerfallItemOwner.Player));
            return item;
        }

        internal UniqueItem CreatePlainSword(ulong durable)
            => CreatePlainWeapon(durable, 113, "iron");

        internal UniqueItem CreatePlainWeapon(ulong durable, int templateIndex, string material)
        {
            IRandomService random = DispatchProxy.Create<IRandomService, UnusedRandom>();
            DaggerfallCreatedItem created = new DaggerfallItemFactory(Definitions, random).Create(new(
                "Weapons", "condition.sword", DaggerfallItemOwner.Player, TemplateIndex: templateIndex, Material: material, Variant: 0));
            new DaggerfallItemFactory(Definitions, random).Materialize(created, Inventory, Instances,
                unique: new DurableIdentityReference(DurableIdentityKind.Item, durable));
            Rusty.Engine.Mechanics.UniqueInventoryItem item = Inventory.Read().UniqueItems.Single(item =>
                item.Entity.Value == Entities.Resolve(new(DurableIdentityKind.Item, durable)).Value);
            return new UniqueItem(item.Entity.Value, new InventoryItemId(item.Definition.Value));
        }

        public void Dispose() => Entities.Dispose();
    }

    private class UnusedRandom : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected random draw: {targetMethod?.Name}");
    }
}
