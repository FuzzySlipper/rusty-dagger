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
        Assert.Equal(DaggerfallQuestItemResult.Changed, state.Quests.TakeItem("items", "gift"));
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
    public void Letter_and_give_notification_source_references_resolve_without_publishing_and_missing_text_diagnoses()
    {
        using var f = Fixture();
        var state = f.Session.State;
        Start(state);
        Assert.Equal(1010, state.Quests.ItemUsedMessage("items", "gift"));
        var program = DaggerfallQuestTaskCompiler.Compile(new("items", "", "items.txt", DaggerfallQuestDisposition.Compiled, [],
            [new("headless", 1, ["give pc _gift_ notify 1010"], null)], []));
        var operation = program.Tasks.Single().Operations.Single();
        Assert.Equal("gift", operation.Targets.Single());
        Assert.Equal(DaggerfallQuestTaskOperationKind.Unsupported, operation.Kind); // #8133 still owns execution.
        Assert.Equal(1010, state.Quests.ItemGrantNotification("items", operation));
        Assert.Throws<ArgumentException>(() => state.Quests.ItemGrantNotification("items", operation with { MessageAlias = "99999" }));
        Assert.Empty(state.Quests.Messages.Capture().Deliveries);
    }

    private static SanguineRoseSessionTests.Fixture Fixture(bool stackable = false, bool gold = false, bool actions = false) => new(definitions: Definitions(stackable, gold, actions));
    private static void Start(DaggerfallState state) => state.Quests.Start(new("items", "items.txt", "items", DaggerfallQuestLifecycle.Active, null, [], []));
    private static DaggerfallQuestResourceState Item(DaggerfallState state) => state.Quests.All.Single().Resources.Single();
    private static DaggerfallDefinitions Definitions(bool stackable, bool gold, bool actions)
    {
        var root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
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
        return DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
