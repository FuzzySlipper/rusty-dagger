using Rusty.Engine;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using System.Numerics;
using System.Text.Json;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestItemInteractionTests
{
    [Theory]
    [InlineData("clicked item _gift_")]
    [InlineData("clicked item _gift_ say 100")]
    [InlineData("clicked item _gift_ say QuestComplete")]
    [InlineData("toting _gift_ and _person_ clicked")]
    [InlineData("toting _gift_ and _person_ clicked saying 100")]
    [InlineData("_gift_ used do _used_")]
    [InlineData("_gift_ used saying 100 do _used_")]
    [InlineData("give item _gift_ to _enemy_")]
    [InlineData("pay 10 money do _paid_ otherwise do _unpaid_")]
    [InlineData("pay 10 gold do _paid_ otherwise do _unpaid_")]
    public void Source_forms_compile(string action)
    {
        var source = new DaggerfallQuestSourceDefinition("items", "", "items.txt", DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [action], null)], []);
        Assert.NotEqual(DaggerfallQuestTaskOperationKind.Unsupported, Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(source).Tasks).Operations).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Atomic_payment_records_paid_or_unpaid_before_branch_and_never_repeats_on_restore(bool enough)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["pay 10 gold do _paid_ otherwise do _unpaid_"],
            taskBlocks: [["_paid_ task:"], ["_unpaid_ task:"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var currency = f.Session.State.Currency;
        Assert.True(currency.TrySpendGold(currency.Read().Gold, []));
        Assert.True(currency.ReceiveGold(enough ? 10UL : 9UL));
        Start(f, definitions); Advance(f.Session);
        Assert.Equal(enough ? 0UL : 9UL, currency.Read().Gold);
        var quest = Quest(f.Session);
        Assert.True(quest.Tasks.Single(t => t.Symbol == (enough ? "paid" : "unpaid")).IsSet);
        Assert.False(quest.Tasks.Single(t => t.Symbol == (enough ? "unpaid" : "paid")).IsSet);
        Assert.Equal(enough ? "paid" : "unpaid", quest.Tasks.First().OperationState.Single().PaymentBranch);
        using var restored = f.Restore(); Advance(restored);
        Assert.Equal(enough ? 0UL : 9UL, restored.State.Currency.Read().Gold);
    }

    [Fact]
    public void Accepted_foe_gift_reaches_current_and_future_real_inventories_once_across_restore()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["give item _gift_ to _enemy_", "create foe _enemy_ every 0 minutes 1 times with 100% success"], foeCount: 2);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        var first = Quest(f.Session).Resources.Single(r => r.SelectedFoe is not null).Binding.ActorIds.Single();
        Assert.Single(f.Session.State.ItemInstances.UniqueItems.Where(item => item.Value.QuestId == "interactions" && item.Value.Owner == DaggerfallItemOwner.Actor(first)));
        using var restored = f.Restore(); Advance(restored); Advance(restored);
        var ids = Quest(restored).Resources.Single(r => r.SelectedFoe is not null).Binding.ActorIds;
        Assert.Equal(2, ids.Length);
        foreach (long id in ids)
        {
            var item = Assert.Single(restored.State.ItemInstances.UniqueItems.Where(item => item.Value.QuestId == "interactions" && item.Value.Owner == DaggerfallItemOwner.Actor(id)));
            Assert.Contains(restored.State.Containers.Read(restored.State.Actors.Get(id).Actor.Entity).UniqueItems,
                actual => restored.State.Inventory.GetDurableItemId(actual.Entity).Value == item.Key);
        }
        Assert.Equal(ids.Order(), Quest(restored).Tasks.First().OperationState.First().ItemTransfer!.Recipients.Order());
        Advance(restored);
        Assert.Equal(2, restored.State.ItemInstances.UniqueItems.Count(item => item.Value.QuestId == "interactions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Permanent_queued_gift_restores_and_later_foes_receive_permanent_copies(bool beforeQueue)
    {
        string[] actions = beforeQueue
            ? ["make _gift_ permanent", "give item _gift_ to _enemy_", "create foe _enemy_ every 0 minutes 1 times with 100% success"]
            : ["give item _gift_ to _enemy_", "make _gift_ permanent", "create foe _enemy_ every 0 minutes 1 times with 100% success"];
        var definitions = QuestWorldAdmissionTests.Definitions(actions: actions, foeCount: 2);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        using var restored = f.Restore(); Advance(restored);
        var ids = Quest(restored).Resources.Single(r => r.SelectedFoe is not null).Binding.ActorIds;
        Assert.Equal(2, ids.Length);
        foreach (long id in ids)
            Assert.Null(Assert.Single(restored.State.ItemInstances.UniqueItems.Where(item => item.Value.Owner == DaggerfallItemOwner.Actor(id))).Value.QuestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_inventory_use_only_triggers_the_bound_quest_item_and_restores_without_replay(bool equip)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["get item _gift_", "_gift_ used saying 100 do _used_"], messages: ["Used."], taskBlocks: [["_used_ task:"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); f.Update();
        f.Use();
        Assert.False(Quest(f.Session).Tasks.Single(t => t.Symbol == "used").IsSet);
        if (equip)
        {
            ulong id = Quest(f.Session).Resources.Single(r => r.Symbol == "gift").Binding.UniqueItemIds.Single();
            var entity = f.Session.State.Actors.Entities.Resolve(new(DurableIdentityKind.Item, id));
            var actual = f.Session.State.Inventory.Read().UniqueItems.Single(item => item.Entity == entity);
            var definition = definitions.RequireItem(new(actual.Definition.Value));
            var slot = definitions.EquipmentSlots.Values.First(value => value.AllowedClassifications.Intersect(definition.Equipment!.Classifications).Any());
            f.Submit(new { action = "inventory-move", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{entity.Value}", targetEquipment = slot.Id.Value });
            Assert.Contains(f.Session.State.Equipment.Read().Assignments, value => value.Item.EntityId == entity.Value);
        }
        else UseGift(f);
        f.Update();
        Assert.True(Quest(f.Session).Tasks.Single(t => t.Symbol == "used").IsSet);
        Assert.Single(f.Session.State.Quests.Messages.Deliveries);
        using var restored = f.Restore(); Advance(restored); Advance(restored);
        Assert.Single(restored.State.Quests.Messages.Deliveries);
        Assert.True(Quest(restored).Tasks.Single(t => t.Symbol == "used").IsSet);
    }

    [Fact]
    public void Actual_letter_use_retains_source_delivery_once_through_save_and_can_be_reopened()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["get item _gift_"], messages: ["A retained letter."], itemUsedMessage: "100");
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); f.Update(); UseGift(f); f.Update(); UseGift(f);
        var letter = Assert.Single(f.Session.State.Quests.Messages.Deliveries);
        Assert.Equal(DaggerfallQuestMessageDelivery.Letter, letter.Delivery);
        using var restored = f.Restore(); Advance(restored);
        Assert.Equal(letter, Assert.Single(restored.State.Quests.Messages.Deliveries));
        Assert.True(f.Session.State.Quests.DismissRewardMessage("interactions", letter.EntryId));
        UseGift(f);
        Assert.True(Assert.Single(f.Session.State.Quests.Messages.Deliveries).Id > letter.Id);
    }

    [Fact]
    public void Watched_potion_keeps_normal_consumption_and_declared_use_message()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(potion: true, actions: ["get item _gift_", "_gift_ used do _used_"],
            messages: ["A retained use message."], itemUsedMessage: "100", taskBlocks: [["_used_ task:"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); f.Update();
        var stack = Quest(f.Session).Resources.Single(r => r.Symbol == "gift").Binding.Stacks.Single();
        f.Submit(new { action = "inventory-use", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"stack:{stack.StackId}" });
        f.Update();
        Assert.DoesNotContain(f.Session.State.Inventory.Read().Stacks, item => item.Id.Value == stack.StackId);
        Assert.True(Quest(f.Session).Tasks.Single(t => t.Symbol == "used").IsSet);
        Assert.Equal(DaggerfallQuestMessageDelivery.Letter, Assert.Single(f.Session.State.Quests.Messages.Deliveries).Delivery);
        using var restored = f.Restore(); Advance(restored);
        Assert.Single(restored.State.Quests.Messages.Deliveries);
        Assert.DoesNotContain(restored.State.Inventory.Read().Stacks, item => item.Id.Value == stack.StackId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Money_can_redeem_carried_credit_but_gold_cannot(bool goldOnly)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: [$"pay 10 {(goldOnly ? "gold" : "money")} do _paid_ otherwise do _unpaid_"],
            taskBlocks: [["_paid_ task:"], ["_unpaid_ task:"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var currency = f.Session.State.Currency;
        Assert.True(currency.TrySpendGold(currency.Read().Gold, []));
        Assert.True(currency.ReceiveGold(3));
        Assert.True(currency.TryCreditAccount(101));
        Assert.True(currency.WithdrawLetter(100));
        Start(f, definitions); Advance(f.Session);
        Assert.Equal(3UL, currency.Read().Gold);
        Assert.Equal(goldOnly ? 100UL : 90UL, currency.Read().LettersOfCredit);
        Assert.True(Quest(f.Session).Tasks.Single(t => t.Symbol == (goldOnly ? "unpaid" : "paid")).IsSet);
        using var restored = f.Restore(); Advance(restored);
        Assert.Equal(currency.Read(), restored.State.Currency.Read());
    }

    [Fact]
    public void Actual_npc_handoff_takes_bound_item_then_transfers_same_identity_before_later_click_tasks()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(person: true, personQuestor: true, actions: ["get item _gift_"],
            taskBlocks: [["_handoff_ task:", "toting _gift_ and _person_ clicked saying 100", "give item _gift_ to _person_"],
                ["_ordinary_ task:", "clicked npc _person_ say 101"]], messages: ["Thank you.", "Ordinary talk."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        long npc = QuestNpcOverlayTests.Giver(f, definitions, gender: "Female");
        Start(f, definitions, npc); Advance(f.Session);
        var quest = Quest(f.Session);
        ulong item = quest.Resources.Single(r => r.Symbol == "gift").Binding.UniqueItemIds.Single();
        Assert.False(f.Session.State.Quests.ActorClicked(f.Enemy)); Advance(f.Session);
        Assert.True(f.Session.State.Quests.HasItem("interactions", "gift"));
        f.Session.State.Actors.Get(npc).ApplyPose(new(new(0, 0, -2), 0));
        AimActivationAt(f.Session, npc);
        f.Perception.Responder = request => Receipt(request.Observers.ToArray().SelectMany(o => request.Targets.ToArray()
            .Select(t => new PerceptionPair(o.Entity, t.Entity, Vector3.Distance(o.Origin, t.Center), 1, PerceptionPairKind.Visible, 1))).ToArray());
        f.Submit(new { action = "loot" });
        Advance(f.Session);
        Assert.Equal(DaggerfallItemOwner.Actor(npc), f.Session.State.ItemInstances.RequireUnique(item).Owner);
        Assert.True(Quest(f.Session).Tasks.Single(t => t.Symbol == "handoff").IsSet);
        Assert.False(Quest(f.Session).Tasks.Single(t => t.Symbol == "ordinary").IsSet);
        Assert.Equal(100, Assert.Single(f.Session.State.Quests.Messages.Deliveries).MessageId);
        using var restored = f.Restore(); Advance(restored);
        Assert.Equal(DaggerfallItemOwner.Actor(npc), restored.State.ItemInstances.RequireUnique(item).Owner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_corpse_loot_selection_triggers_only_the_bound_item_click_once(bool giveAfterDeath)
    {
        string[] actions = giveAfterDeath ? ["create foe _enemy_ every 0 minutes 1 times with 100% success"]
            : ["create foe _enemy_ every 0 minutes 1 times with 100% success", "give item _gift_ to _enemy_"];
        string[][] blocks = giveAfterDeath ? [["_clicked_ task:", "clicked item _gift_ say 100"], ["_dead_ task:", "killed _enemy_", "give item _gift_ to _enemy_"]]
            : [["_clicked_ task:", "clicked item _gift_ say 100"]];
        var definitions = QuestWorldAdmissionTests.Definitions(actions: actions, taskBlocks: blocks, messages: ["You found it."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        long foe = Quest(f.Session).Resources.Single(r => r.Symbol == "enemy").Binding.ActorIds.Single();
        f.Session.State.Kit.Rules.RegisterAction(f.Session.DefinitionsByActor[1].ActionId!, new Kill());
        f.Session.ResolveExplicitMelee(new(1, foe, 1, 10000, .125));
        Assert.True(f.Session.Corpses.ContainsKey(foe));
        Advance(f.Session);
        ulong gift = f.Session.State.ItemInstances.UniqueItems.Single(i => i.Value.QuestId == "interactions").Key;
        Assert.Equal(DaggerfallItemOwner.Corpse(foe), f.Session.State.ItemInstances.RequireUnique(gift).Owner);
        f.Session.State.Actors.Get(foe).ApplyPose(new(new(0, 0, -2), 0));
        AimActivationAt(f.Session, foe);
        f.Perception.Responder = request => Receipt(request.Observers.ToArray().SelectMany(o => request.Targets.ToArray()
            .Select(t => new PerceptionPair(o.Entity, t.Entity, Vector3.Distance(o.Origin, t.Center), 1, PerceptionPairKind.Visible, 1))).ToArray());
        f.Submit(new { action = "loot" });
        var loot = Assert.IsType<WorldRpg.Rulesets.Daggerfall.Presentation.LootPresentation>(f.Session.OpenLoot);
        ulong entity = f.Session.State.Actors.Entities.Resolve(new(DurableIdentityKind.Item, gift)).Value;
        f.Submit(new { action = "loot-take", container = loot.Container, revision = loot.Revision, item = $"unique:{entity}" });
        Advance(f.Session);
        Assert.Equal(DaggerfallItemOwner.Player, f.Session.State.ItemInstances.RequireUnique(gift).Owner);
        Assert.True(Quest(f.Session).Tasks.Single(t => t.Symbol == "clicked").IsSet);
        Assert.Equal(100, Assert.Single(f.Session.State.Quests.Messages.Deliveries).MessageId);
        using var restored = f.Restore(); Advance(restored);
        Assert.Single(restored.State.Quests.Messages.Deliveries);
    }

    private sealed class Kill : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = true;
        public void Damage(DamageEvent value) => value.Damage = 10000;
    }

    private static void UseGift(SanguineRoseSessionTests.Fixture f)
    {
        ulong id = Quest(f.Session).Resources.Single(r => r.Symbol == "gift").Binding.UniqueItemIds.Single();
        ulong entity = f.Session.State.Actors.Entities.Resolve(new(DurableIdentityKind.Item, id)).Value;
        f.Submit(new { action = "inventory-use", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{entity}" });
    }
    private static DaggerfallQuestInstanceSave Quest(DaggerfallSession s) => s.State.Quests.Capture().Instances.Single();
    private static void Advance(DaggerfallSession s) => s.State.Quests.Advance(s.State.Variables, DaggerfallCalendar.Start);
    private static void Start(SanguineRoseSessionTests.Fixture f, DaggerfallDefinitions definitions, long? questor = null)
    {
        var site = definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new("interactions", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId) })], []) { QuestorId = questor });
    }
}
