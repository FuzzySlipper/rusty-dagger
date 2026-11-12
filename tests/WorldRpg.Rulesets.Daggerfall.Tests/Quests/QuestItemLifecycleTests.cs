using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestItemLifecycleTests
{
    [Fact]
    public void Take_failure_distinguishes_equivalent_template_and_has_no_partial_mutation()
    {
        using var f = Fixture();
        var state = f.Session.State;
        Start(state);
        var resource = Item(state);
        var id = f.Session.UniqueItemAllocator.AllocateReference();
        state.Equipment.Materialize(id, resource.SelectedItem!.Item);
        state.ItemInstances.RegisterUnique(id.Value, resource.SelectedItem.Metadata with { QuestId = null, QuestItemSymbol = null });
        var before = state.Inventory.Read();
        Assert.False(state.Quests.HasItem("items", "gift"));
        Assert.Equal(DaggerfallQuestItemResult.NotCarried, state.Quests.TakeItem("items", "gift"));
        Assert.Equal(before.StoreRevision, state.Inventory.Read().StoreRevision);
        Assert.Empty(state.QuestItems.Custody);
        Assert.Null(state.ItemInstances.RequireUnique(id.Value).QuestId);
    }

    [Fact]
    public void Canonical_unique_reoffer_preserves_identity_current_values_and_equipment_cleanup_across_encoded_save()
    {
        using var f = Fixture();
        var state = f.Session.State;
        Start(state);
        Assert.Equal(DaggerfallQuestItemResult.Changed, state.Quests.GrantItem("items", "gift"));
        ulong id = Item(state).Binding.UniqueItemIds.Single();
        var metadata = state.ItemInstances.RequireUnique(id);
        state.ItemInstances.ReplaceUnique(id, metadata with { CurrentCondition = metadata.MaximumCondition - 1, Stolen = true });
        var runtime = state.Actors.Entities.Resolve(new(DurableIdentityKind.Item, id));
        foreach (var assignment in state.Equipment.Read().Assignments.Where(value => value.Slot.Value == "right-hand").ToArray()) state.Equipment.Unequip(assignment.Item);
        state.Equipment.Equip(new(runtime.Value, Item(state).SelectedItem!.Item), [new("right-hand")]);
        DaggerfallEquipmentChange? notification = null;
        f.Session.EquipmentMoves.Changed += value => notification = value;
        Assert.Equal(DaggerfallQuestItemResult.Changed, state.Quests.TakeItem("items", "gift"));
        Assert.Equal(runtime.Value, Assert.Single(Assert.IsType<DaggerfallEquipmentChange>(notification).Removed).EntityId);
        Assert.False(state.Quests.HasItem("items", "gift"));
        Assert.DoesNotContain(state.Equipment.Read().Assignments, assignment => assignment.Item.EntityId == runtime.Value);
        Assert.Equal("quest", state.ItemInstances.RequireUnique(id).Owner.Scope);
        using var restored = f.Restore();
        Assert.Equal(id, Item(restored.State).Binding.UniqueItemIds.Single());
        Assert.Equal(DaggerfallQuestItemResult.Changed, restored.State.Quests.GrantItem("items", "gift"));
        var current = restored.State.ItemInstances.RequireUnique(id);
        Assert.Equal(DaggerfallItemOwner.Player, current.Owner);
        Assert.Equal(metadata.MaximumCondition - 1, current.CurrentCondition);
        Assert.True(current.Stolen);
        Assert.Equal(metadata.Material, current.Material);
        Assert.Equal(metadata.Enchantment, current.Enchantment);
    }

    [Fact]
    public void Taking_equipped_item_spell_source_uses_the_complete_equipment_refresh_owner()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0035", definitions: Definitions(false, false, false));
        var state = f.Session.State;
        Start(state);
        var metadata = state.ItemInstances.RequireUnique(f.Source) with { QuestId = "items", QuestItemSymbol = "gift" };
        state.ItemInstances.ReplaceUnique(f.Source, metadata);
        var definition = f.Composition.Definitions.RequireItem(new(metadata.ItemId));
        var original = Item(state);
        state.Quests.SetResource("items", original with
        {
            Binding = DaggerfallQuestResourceBinding.UniqueItem(f.Source),
            SelectedItem = new(definition.Template!.Index, new(metadata.ItemId), false, 1, metadata),
        });
        var slot = f.Composition.Definitions.EquipmentSlots.Values.First(value => value.AllowedClassifications.Intersect(definition.Equipment!.Classifications).Any());
        Assert.Equal(EquipmentMoveOutcome.Applied, f.Session.EquipmentMoves.MoveToSlot(f.Item, new(slot.Id.Value)).Outcome);
        Assert.Contains(state.Effects.Active, value => value.Context.Item?.Value == f.Source);
        Assert.Equal(DaggerfallQuestItemResult.Changed, state.Quests.TakeItem("items", "gift"));
        Assert.DoesNotContain(state.Effects.Active, value => value.Context.Item?.Value == f.Source);
        Assert.Null(state.ItemInstances.RequireUnique(f.Source).HeldCast);
        using var restored = f.Restore();
        Assert.DoesNotContain(restored.State.Effects.Active, value => value.Context.Item?.Value == f.Source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Permanent_prototype_and_player_copy_clear_linkage_and_survive_restore_and_terminal_cleanup(bool beforeGrant)
    {
        using var f = Fixture();
        var state = f.Session.State;
        Start(state);
        if (beforeGrant) state.Quests.MakeItemPermanent("items", "gift");
        state.Quests.GrantItem("items", "gift");
        if (!beforeGrant) state.Quests.MakeItemPermanent("items", "gift");
        ulong id = Item(state).Binding.UniqueItemIds.Single();
        Assert.False(state.Quests.HasItem("items", "gift"));
        Assert.Null(Item(state).SelectedItem!.Metadata.QuestId);
        using var restored = f.Restore();
        Assert.Null(restored.State.ItemInstances.RequireUnique(id).QuestId);
        Assert.Equal(DaggerfallQuestItemResult.AlreadyCarried, restored.State.Quests.GrantItem("items", "gift"));
        restored.State.Quests.Complete("items", "finished");
        Assert.Equal(DaggerfallItemOwner.Player, restored.State.ItemInstances.RequireUnique(id).Owner);
        Assert.Empty(restored.State.QuestItems.Custody);
    }

    [Fact]
    public void All_carried_clones_are_released_but_only_canonical_unique_is_reoffered()
    {
        using var f = Fixture();
        var state = f.Session.State;
        Start(state);
        state.Quests.GrantItem("items", "gift");
        ulong primary = Item(state).Binding.UniqueItemIds.Single();
        var copy = f.Session.UniqueItemAllocator.AllocateReference();
        state.Equipment.Materialize(copy, Item(state).SelectedItem!.Item);
        state.ItemInstances.RegisterUnique(copy.Value, state.ItemInstances.RequireUnique(primary));
        state.Quests.GrantItem("items", "gift");
        Assert.False(state.ItemInstances.ContainsUnique(copy.Value));
        Assert.True(state.ItemInstances.ContainsUnique(primary));
        Assert.True(state.Quests.HasItem("items", "gift"));
        Assert.Single(state.ItemInstances.UniqueItems, value => value.Value.QuestId == "items");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Permanent_taken_item_survives_terminal_custody_retirement_and_encoded_restore(bool stackable)
    {
        using var f = Fixture(stackable: stackable);
        var state = f.Session.State;
        Start(state);
        state.Quests.GrantItem("items", "gift");
        var original = Item(state).Binding;
        state.Quests.TakeItem("items", "gift");
        state.Quests.MakeItemPermanent("items", "gift");
        using (var pending = f.Restore())
        {
            pending.State.Quests.Complete("items", "permanent reward");
            Assert.Empty(pending.State.QuestItems.Custody);
            if (stackable) Assert.Null(pending.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, InventoryStackId.Parse(original.Stacks.Single().StackId)).QuestId);
            else Assert.Equal(DaggerfallItemOwner.Player, pending.State.ItemInstances.RequireUnique(original.UniqueItemIds.Single()).Owner);
        }
        state.Quests.Complete("items", "permanent reward");
        using var restored = f.Restore();
        Assert.Empty(restored.State.QuestItems.Custody);
        if (stackable) Assert.Null(restored.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, InventoryStackId.Parse(original.Stacks.Single().StackId)).QuestId);
        else
        {
            var retained = restored.State.ItemInstances.RequireUnique(original.UniqueItemIds.Single());
            Assert.Equal(DaggerfallItemOwner.Player, retained.Owner);
            Assert.Null(retained.QuestId);
        }
    }

    [Fact]
    public void Split_stack_reoffer_keeps_canonical_current_quantity_instead_of_adding_copies()
    {
        using var f = Fixture(stackable: true);
        var state = f.Session.State;
        Start(state);
        state.Quests.GrantItem("items", "gift");
        var primary = InventoryStackId.Parse(Item(state).Binding.Stacks.Single().StackId);
        state.Inventory.Grant(new(Item(state).SelectedItem!.Item, primary, 4));
        ulong quantity = state.Inventory.Read().Stacks.Single(value => value.Id == primary).Quantity;
        Assert.True(quantity > 1);
        var split = InventoryStackId.Parse("quest.split");
        state.ItemInstances.SplitStack(DaggerfallItemOwner.Player, state.Inventory, primary, split, 1);
        state.Quests.TakeItem("items", "gift");
        Assert.False(state.ItemInstances.ContainsStack(DaggerfallItemOwner.Player, split));
        using var restored = f.Restore();
        restored.State.Quests.GrantItem("items", "gift");
        var current = Assert.Single(restored.State.Inventory.Read().Stacks, value => value.Id == primary);
        Assert.Equal(quantity - 1, current.Quantity);
        Assert.Single(Item(restored.State).Binding.Stacks);
        restored.State.Quests.Complete("items", "done");
        Assert.DoesNotContain(restored.State.Inventory.Read().Stacks, value => value.Id == primary);
        Assert.Empty(restored.State.QuestItems.Custody);
    }

    [Fact]
    public void Gold_uses_real_currency_without_quest_item_linkage()
    {
        using var f = Fixture(gold: true);
        var state = f.Session.State;
        Start(state);
        ulong before = state.Currency.Read().Gold;
        ulong amount = Item(state).SelectedItem!.Quantity;
        state.Quests.GrantItem("items", "gift");
        Assert.Equal(before + amount, state.Currency.Read().Gold);
        Assert.False(state.Quests.HasItem("items", "gift"));
        Assert.Equal(DaggerfallQuestItemResult.NotCarried, state.Quests.TakeItem("items", "gift"));
        Assert.Empty(state.QuestItems.Custody);
        using var restored = f.Restore();
        Assert.Equal(before + amount, restored.State.Currency.Read().Gold);
    }

    [Fact]
    public void Real_compiled_actions_grant_show_message_and_have_rearms_after_current_save()
    {
        using var f = Fixture(actions: true);
        var state = f.Session.State;
        Start(state);
        state.Quests.Advance(state.Variables, DaggerfallCalendar.Start);
        Assert.True(state.Quests.HasItem("items", "gift"));
        var saved = state.Quests.All.Single();
        Assert.True(saved.Tasks.Single(value => value.Symbol == "seen").IsSet);
        Assert.Single(state.Quests.Messages.Capture().Deliveries);
        ulong id = Item(state).Binding.UniqueItemIds.Single();
        using var restored = f.Restore();
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.Equal(id, Item(restored.State).Binding.UniqueItemIds.Single());
        Assert.Single(restored.State.ItemInstances.UniqueItems, value => value.Value.QuestId == "items");
        Assert.Single(restored.State.Quests.Messages.Capture().Deliveries);
    }

    [Fact]
    public void Letter_source_reference_resolves_and_give_notification_compiles_without_publishing()
    {
        using var f = Fixture();
        var state = f.Session.State;
        Start(state);
        Assert.Equal(1010, state.Quests.ItemUsedMessage("items", "gift"));
        var program = DaggerfallQuestTaskCompiler.Compile(new("items", "", "items.txt", DaggerfallQuestDisposition.Compiled, [],
            [new("headless", 1, ["give pc _gift_ notify 1010"], null)], []));
        var operation = program.Tasks.Single().Operations.Single();
        Assert.Equal("gift", operation.Targets.Single());
        Assert.Equal(DaggerfallQuestTaskOperationKind.GivePc, operation.Kind);
        Assert.Equal(1010, operation.MessageId);
        Assert.Empty(state.Quests.Messages.Capture().Deliveries);
    }

    [Fact]
    public void Terminal_custody_cleanup_retires_the_actual_inventory_owner_before_entity_destruction()
    {
        using var f = Fixture();
        var state = f.Session.State;
        Start(state);
        state.Quests.GrantItem("items", "gift");
        state.Quests.TakeItem("items", "gift");
        var custody = Assert.Single(state.QuestItems.Custody);
        Assert.True(state.InventoryStore.TryGetInventory(custody.Owner, out _));
        state.Quests.Complete("items", "done");
        Assert.False(state.InventoryStore.TryGetInventory(custody.Owner, out _));
        Assert.False(state.Actors.Entities.TryResolve(new(DurableIdentityKind.Container, checked((ulong)custody.Id)), out _));
        using var restored = f.Restore();
        Assert.Empty(restored.State.QuestItems.Custody);
    }

    [Fact]
    public void Failed_first_grant_leaves_no_seeded_item_binding_or_custody_owner()
    {
        using var f = Fixture(stackable: true);
        var state = f.Session.State;
        Start(state);
        var original = Item(state);
        ulong maximum = f.Composition.Definitions.RequireItem(new(original.SelectedItem!.Item.Value)).MaximumQuantity;
        state.Quests.SetResource("items", original with { SelectedItem = original.SelectedItem with { Quantity = maximum + 1 } });
        var before = state.Inventory.Read();
        Assert.Throws<MechanicsException>(() => state.Quests.GrantItem("items", "gift"));
        Assert.Equal(before.Stacks, state.Inventory.Read().Stacks);
        Assert.Equal(before.UniqueItems, state.Inventory.Read().UniqueItems);
        Assert.Equal(DaggerfallQuestResourceBindingKind.Pending, Item(state).Binding.Kind);
        Assert.Empty(state.QuestItems.Custody);
        Assert.DoesNotContain(state.ItemInstances.StackItems, value => value.Metadata.QuestId == "items");
        state.Quests.SetResource("items", original);
        Assert.Equal(DaggerfallQuestItemResult.Changed, state.Quests.GrantItem("items", "gift"));
    }

    [Fact]
    public void Engine_rejected_take_returns_branchable_result_without_partial_player_or_metadata_changes()
    {
        using var f = Fixture(stackable: true);
        var state = f.Session.State;
        Start(state);
        state.Quests.GrantItem("items", "gift");
        var primary = InventoryStackId.Parse(Item(state).Binding.Stacks.Single().StackId);
        state.Quests.TakeItem("items", "gift");
        var custody = Assert.Single(state.QuestItems.Custody);
        state.Quests.GrantItem("items", "gift");
        var prototype = Item(state).SelectedItem!;
        ulong maximum = f.Composition.Definitions.RequireItem(new(prototype.Item.Value)).MaximumQuantity;
        state.Containers.Seed(custody.Owner, [new(prototype.Item, maximum, Stack: primary)]);
        state.ItemInstances.RegisterStack(DaggerfallItemOwner.Quest(custody.Id), primary, prototype.Metadata);
        var before = state.Inventory.Read();
        Assert.Equal(DaggerfallQuestItemResult.Unavailable, state.Quests.TakeItem("items", "gift"));
        Assert.Equal(before.Stacks, state.Inventory.Read().Stacks);
        Assert.True(state.Quests.HasItem("items", "gift"));
        Assert.Equal(DaggerfallItemOwner.Player, state.ItemInstances.RequireStack(DaggerfallItemOwner.Player, primary).Owner);
        Assert.Equal(maximum, state.Containers.Read(custody.Owner).Stacks.Single().Quantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pending_legal_clones_can_be_taken_into_one_canonical_custody_binding_and_reoffered(bool stackable)
    {
        using var f = Fixture(stackable: stackable);
        var state = f.Session.State;
        Start(state);
        var prototype = Item(state).SelectedItem!;
        ulong first = 0;
        var stack = InventoryStackId.Parse("pending.copy.1");
        if (stackable)
        {
            state.Inventory.Grant(new(prototype.Item, stack, prototype.Quantity));
            state.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, stack, prototype.Metadata);
        }
        else
        {
            var id = f.Session.UniqueItemAllocator.AllocateReference();
            first = id.Value;
            state.Equipment.Materialize(id, prototype.Item);
            state.ItemInstances.RegisterUnique(id.Value, prototype.Metadata);
        }
        Assert.Equal(DaggerfallQuestResourceBindingKind.Pending, Item(state).Binding.Kind);
        using (var pending = f.Restore()) Assert.True(pending.State.Quests.HasItem("items", "gift"));
        Assert.Equal(DaggerfallQuestItemResult.Changed, state.Quests.TakeItem("items", "gift"));
        Assert.Equal(DaggerfallQuestResourceBindingKind.Item, Item(state).Binding.Kind);
        using var restored = f.Restore();
        restored.State.Quests.GrantItem("items", "gift");
        if (stackable) Assert.Equal(stack.Value, Item(restored.State).Binding.Stacks.Single().StackId);
        else Assert.Equal(first, Item(restored.State).Binding.UniqueItemIds.Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Encoded_save_rejects_bound_items_whose_actual_metadata_lost_quest_provenance(bool stackable)
    {
        using var f = Fixture(stackable: stackable);
        var state = f.Session.State;
        Start(state);
        state.Quests.GrantItem("items", "gift");
        var saved = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        var inventory = saved.Inventory;
        if (stackable)
        {
            string id = Item(state).Binding.Stacks.Single().StackId;
            inventory = inventory with { Stacks = inventory.Stacks.Select(value => value.StackId == id
                ? value with { Metadata = value.Metadata with { QuestId = null, QuestItemSymbol = null } } : value).ToArray() };
        }
        else
        {
            ulong id = Item(state).Binding.UniqueItemIds.Single();
            inventory = inventory with { UniqueItems = inventory.UniqueItems.Select(value => value.EntityId == id
                ? value with { Metadata = value.Metadata with { QuestId = null, QuestItemSymbol = null } } : value).ToArray() };
        }
        var payload = DaggerfallSavePayload.Encode(saved with { Inventory = inventory });
        Assert.Contains("provenance", Assert.Throws<ArgumentException>(() =>
            DaggerfallSession.Restore(f.Engine.Context, f.Composition, payload)).Message);
    }

    [Fact]
    public void Item_retained_outside_player_survives_ordinary_quest_runtime_retirement()
    {
        using var f = Fixture();
        var state = f.Session.State;
        Start(state);
        state.Quests.GrantItem("items", "gift");
        ulong id = Item(state).Binding.UniqueItemIds.Single();
        var metadata = state.ItemInstances.RequireUnique(id);
        f.Update();
        f.Submit(new { action = "inventory-drop", revision = f.Engine.PublishedNested("inventory", "revision"),
            item = $"unique:{state.Actors.Entities.Resolve(new(DurableIdentityKind.Item, id)).Value}", amount = 1 });
        var ground = state.ItemInstances.RequireUnique(id).Owner;
        Assert.Equal("ground", ground.Scope);
        state.Quests.Complete("items", "done");
        state.Quests.Advance(state.Variables, DaggerfallCalendar.Start);
        state.Quests.Advance(state.Variables, DaggerfallCalendar.Start.Advance(8 * 24 * 60 * 60, out _));
        Assert.Empty(state.Quests.All);
        using var restored = f.Restore();
        Assert.Equal(ground, restored.State.ItemInstances.RequireUnique(id).Owner);
        Assert.Equal("items", restored.State.ItemInstances.RequireUnique(id).QuestId);
    }

    private static SanguineRoseSessionTests.Fixture Fixture(bool stackable = false, bool gold = false, bool actions = false) => new(definitions: Definitions(stackable, gold, actions));
    private static void Start(DaggerfallState state) => state.Quests.Start(new("items", "items.txt", "items", DaggerfallQuestLifecycle.Active, null, [], []));
    private static DaggerfallQuestResourceState Item(DaggerfallState state) => state.Quests.All.Single().Resources.Single();
    private static DaggerfallDefinitions Definitions(bool stackable, bool gold, bool actions)
    {
        var root = TestPayload.Sections("questSources");
        var declarations = root["questSources"]!["resources"]!["declarations"]!.AsArray();
        var item = gold ? declarations.First(value => value!["targetCanonicalId"]?.GetValue<string>() == "gold")!.DeepClone()
            : declarations.Single(value => value!["sourceFile"]!.GetValue<string>() == (stackable ? "R0C11Y28.txt" : "S0000502.txt")
                && value["symbol"]!["canonicalId"]!.GetValue<string>() == (stackable ? "i.09" : "reward"))!.DeepClone();
        item["sourceFile"] = "items.txt"; item["quest"] = "items";
        item["symbol"]!["canonicalId"] = "gift"; item["symbol"]!["sourceSpelling"] = "_gift_";
        item["item"]!["usedMessage"] = "1010";
        declarations.Add(item);
        if (!gold)
        {
            item["item"]!["class"] = 3;
            item["item"]!["template"] = stackable ? 131 : 116;
        }
        var source = JsonNode.Parse("""
            {"name":"items","displayName":"","sourceFile":"items.txt","disposition":"compiled","messages":[{"id":1010,"firstLine":1,"lines":["Here is the item."]}],"blocks":[],"diagnostics":[]}
            """)!;
        if (actions) source["blocks"] = JsonNode.Parse("""
            [{"kind":"headless","firstLine":2,"lines":["get item _gift_ saying 1010","have _gift_ set _seen_"],"global":null},{"kind":"variable","firstLine":4,"lines":["variable _seen_"],"global":null}]
            """);
        root["questSources"]!["quests"]!.AsArray().Add(source);
        return TestPayload.WithQuestSections(root.AsObject());
    }
}
