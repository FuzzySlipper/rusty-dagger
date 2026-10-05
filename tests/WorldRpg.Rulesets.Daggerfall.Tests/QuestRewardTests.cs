using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using Rusty.Engine;
using System.Text.Json;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestRewardTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Reward_is_permanent_before_success_and_popup_then_real_loot_survives_restore_and_rearm(bool stackable, bool gold)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(stackable: stackable, gold: gold, actions: ["give pc _gift_"],
            messages: ["Your reward."], firstMessageId: 1004, rearmPlacement: true);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions);
        ulong? original = null;
        if (!stackable)
        {
            Assert.Equal(DaggerfallQuestItemResult.Changed, f.Session.State.Quests.GrantItem("reward", "gift"));
            original = f.Session.State.Quests.Capture().Instances.Single().Resources.Single(r => r.Symbol == "gift").Binding.UniqueItemIds.Single();
        }
        Advance(f.Session);
        var quest = f.Session.State.Quests.Capture().Instances.Single();
        Assert.True(quest.Succeeded);
        var receipt = quest.Tasks.SelectMany(t => t.OperationState).Single(o => o.Reward is not null).Reward!;
        Assert.NotNull(receipt.GroundContainer);
        Assert.False(receipt.LootOpened);
        Assert.Null(f.Session.OpenLoot);
        var save = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        var ground = save.GroundContainers.Single(g => g.Id == receipt.GroundContainer);
        Assert.All(ground.Inventory.UniqueItems, i => Assert.Null(i.Metadata.QuestId));
        if (original is { } id) Assert.Equal(id, Assert.Single(ground.Inventory.UniqueItems).EntityId);
        Assert.All(ground.Inventory.Stacks, i => Assert.Null(i.Metadata.QuestId));
        DaggerfallSiteProfiles? profiles = null;
        if (!stackable)
        {
            profiles = new([f.Inputs, f.Castle]);
            f.Session.AdmitSiteProfiles(profiles);
            Assert.True(f.Session.TryTransitionTo(f.Castle.ProfileKey));
            var pending = Assert.Single(f.Session.State.Quests.Messages.Deliveries);
            Assert.False(f.Session.State.Quests.DismissRewardMessage("reward", pending.EntryId));
            Assert.Single(f.Session.State.Quests.Messages.Deliveries);
            Assert.False(f.Session.State.Quests.Capture().Instances.Single().Tasks.SelectMany(t => t.OperationState).Single(o => o.Reward is not null).Reward!.LootOpened);
        }
        using var restored = f.Restore(profiles);
        var message = Assert.Single(restored.State.Quests.Messages.Deliveries);
        if (!stackable)
        {
            Assert.False(restored.State.Quests.DismissRewardMessage("reward", message.EntryId));
            Assert.True(restored.TryTransitionTo(f.Inputs.ProfileKey));
        }
        Assert.Equal(1004, message.MessageId);
        restored.Update(new ProductUpdate(OuterUpdate(1), [Ui(JsonSerializer.Serialize(new { action = "quest-dismiss", questInstance = "reward", questDelivery = message.EntryId }))]));
        Assert.NotNull(restored.OpenLoot);
        Assert.False(restored.State.Quests.DismissRewardMessage("reward", message.EntryId));
        Advance(restored); Advance(restored);
        Assert.Empty(restored.State.Quests.Messages.Deliveries);
        Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).GroundContainers.Where(g => g.Id == receipt.GroundContainer));
        restored.State.Quests.Complete("reward", "done");
        Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).GroundContainers.Where(g => g.Id == receipt.GroundContainer));
    }

    [Fact]
    public void Unsupported_resource_diagnoses_before_success_or_notification()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["give pc _enemy_", "end quest"], messages: ["Your reward."], firstMessageId: 1004);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        var quest = Assert.Single(f.Session.State.Quests.Capture().Instances);
        Assert.NotEqual(true, quest.Succeeded);
        Assert.All(quest.Tasks.Single().OperationCompleted, completed => Assert.False(completed));
        Assert.Equal(DaggerfallQuestLifecycle.Active, quest.Lifecycle);
        Assert.Contains("selected Item", quest.Tasks.Single().OperationState.First().UnavailableReason);
        Assert.Empty(f.Session.State.Quests.Messages.Deliveries);
    }

    [Fact]
    public void Give_nothing_completes_once_without_inventory_reward()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["give pc nothing"], messages: ["Completed."], firstMessageId: 1004);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        Assert.True(f.Session.State.Quests.Capture().Instances.Single().Succeeded);
        Assert.Single(f.Session.State.Quests.Messages.Deliveries);
        Assert.Null(f.Session.OpenLoot);
        using var restored = f.Restore(); Advance(restored);
        Assert.Single(restored.State.Quests.Messages.Deliveries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Town_delivery_waits_admitted_time_across_restore_and_does_not_finish_quest(bool silent)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["give pc _gift_ " + (silent ? "silently" : "notify 100")], messages: ["Delivery arrived."]);
        using var f = new QuestGuardSpawnTests.Fixture(true, definitions);
        var session = f.Session;
        session.State.Quests.Advance(session.State.Variables, DaggerfallCalendar.Start with { Hour = 0 });
        Assert.True(session.State.Quests.Capture().Instances.Single().Tasks.Single().OperationState.Single().Reward!.WaitingForTown);
        using var restored = f.Restore();
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start with { Hour = 8 });
        var remaining = restored.State.Quests.Capture().Instances.Single().Tasks.Single().OperationState.Single().Reward!.DelaySeconds;
        Assert.InRange(remaining, 4, 50);
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start with { Hour = 9 }, elapsedSeconds: remaining - 1);
        Assert.Empty(restored.State.Quests.Messages.Deliveries);
        Assert.False(restored.State.Quests.Capture().Instances.Single().Tasks.Single().OperationCompleted.Single());
        using var resumed = f.Restore(restored);
        resumed.State.Quests.Advance(resumed.State.Variables, DaggerfallCalendar.Start with { Hour = 9 }, elapsedSeconds: 1);
        var quest = resumed.State.Quests.Capture().Instances.Single();
        Assert.NotEqual(true, quest.Succeeded);
        Assert.True(quest.Tasks.Single().OperationCompleted.Single());
        Assert.True(resumed.State.Quests.TryGet("guards", out var live));
        Assert.Equal(DaggerfallItemOwner.Player, resumed.State.ItemInstances.RequireUnique(live!.Resources.Single(r => r.Symbol == "gift").Binding.UniqueItemIds.Single()).Owner);
        Assert.Equal(silent ? 0 : 1, resumed.State.Quests.Messages.Deliveries.Count);
        if (!silent) Assert.Equal(DaggerfallQuestMessageDelivery.Letter, resumed.State.Quests.Messages.Deliveries.Single().Delivery);
        var ids = live!.Resources.Single(r => r.Symbol == "gift").Binding.UniqueItemIds;
        resumed.State.Quests.Advance(resumed.State.Variables, DaggerfallCalendar.Start with { Hour = 9 }, elapsedSeconds: 100);
        Assert.Equal(ids, live.Resources.Single(r => r.Symbol == "gift").Binding.UniqueItemIds);
    }

    private static void Advance(DaggerfallSession session) => session.State.Quests.Advance(session.State.Variables, DaggerfallCalendar.Start);
    private static void Start(SanguineRoseSessionTests.Fixture f, DaggerfallDefinitions definitions)
    {
        var site = definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new("reward", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId) })], []));
    }
}
