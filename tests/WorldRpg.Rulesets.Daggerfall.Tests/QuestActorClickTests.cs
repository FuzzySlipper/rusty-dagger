using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestActorClickTests
{
    [Theory]
    [InlineData("grab", true)]
    [InlineData("talk", true)]
    [InlineData("steal", true)]
    [InlineData("bash", false)]
    [InlineData("info", false)]
    public void Admitted_muted_npc_click_runs_first_source_task_consumes_flag_and_retains_latch_after_save(string mode, bool activatesQuest)
    {
        var definitions = QuestNpcOverlayTests.Definitions(["mute npc _contact_"], taskBlocks:
            [["_first_ task:", "clicked npc _contact_ say 100"], ["_second_ task:", "clicked npc _contact_ say 100"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = QuestNpcOverlayTests.Giver(f, definitions); QuestNpcOverlayTests.Start(f, "click", id); f.Update();
        Assert.DoesNotContain(f.Session.Dialogue.NpcTargets(), v => v.Identity.Value == (ulong)id);
        f.Submit(new { action = "activation-mode", mode });
        AimActivationAt(f.Session, id);
        f.Session.Update(new ProductUpdate(OuterUpdate(50), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.Equal(mode, f.Session.ActivationView.Mode);
        if (!activatesQuest)
        {
            Assert.False(Task(f.Session, "first").IsSet);
            Assert.Empty(f.Session.State.Quests.Messages.Deliveries);
            return;
        }
        Assert.True(f.Session.ActivationView.Applied, f.Session.ActivationView.Message);
        Assert.True(Task(f.Session, "first").IsSet); Assert.False(Task(f.Session, "second").IsSet);
        Assert.False(f.Session.State.Quests.Capture().Instances.Single().Resources.Single().HasPlayerClicked);
        Assert.Single(f.Session.State.Quests.Messages.Deliveries);
        using var restored = f.Restore();
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.True(Task(restored, "first").IsSet); Assert.False(Task(restored, "second").IsSet);
        Assert.Single(restored.State.Quests.Messages.Deliveries);
        Assert.False(restored.State.Quests.ActorClicked(long.MaxValue));
    }

    [Fact]
    public void Latched_quest_still_consumes_repeated_interaction_without_repeating_popup()
    {
        var definitions = QuestNpcOverlayTests.Definitions([], taskBlocks: [["_click_ task:", "clicked npc _contact_ say 100"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = QuestNpcOverlayTests.Giver(f, definitions); QuestNpcOverlayTests.Start(f, "latched", id);
        Assert.True(f.Session.State.Quests.ActorClicked(id)); f.Update();
        Assert.True(f.Session.State.Quests.ActorClicked(id)); f.Update();
        Assert.Single(f.Session.State.Quests.Messages.Deliveries);
        Assert.False(f.Session.State.Quests.Capture().Instances.Single().Resources.Single().HasPlayerClicked);
    }

    [Fact]
    public void Npc_click_is_not_claimed_by_a_foe_only_trigger()
    {
        var definitions = QuestNpcOverlayTests.Definitions([], taskBlocks:
            [["_wrong_ task:", "clicked foe _contact_ say 100"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = QuestNpcOverlayTests.Giver(f, definitions);
        QuestNpcOverlayTests.Start(f, "wrong-kind", id);
        Assert.False(f.Session.State.Quests.ActorClicked(id));
        f.Update();
        Assert.False(Task(f.Session, "wrong").IsSet);
        Assert.Empty(f.Session.State.Quests.Messages.Deliveries);
        Assert.False(f.Session.State.Quests.Capture().Instances.Single().Resources.Single().HasPlayerClicked);
    }

    [Fact]
    public void Source_rearm_waits_for_next_actual_click_and_gold_fallback_does_not_debit()
    {
        var definitions = QuestNpcOverlayTests.Definitions([], taskBlocks:
            [["_paid_ task:", "clicked _contact_ and at least 10 gold otherwise do _poor_", "clear _paid_"], ["_poor_ task:", "say 100"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = QuestNpcOverlayTests.Giver(f, definitions); QuestNpcOverlayTests.Start(f, "paid", id);
        ulong initial = f.Session.State.Currency.Read().Gold;
        Assert.True(initial >= 10);
        f.Session.State.Quests.ActorClicked(id); f.Update();
        Assert.Equal(initial - 10, f.Session.State.Currency.Read().Gold);
        f.Update(); Assert.Equal(initial - 10, f.Session.State.Currency.Read().Gold);
        using var restored = f.Restore();
        restored.State.Quests.ActorClicked(id); restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.Equal(initial - 20, restored.State.Currency.Read().Gold);
        Assert.True(restored.State.Currency.TrySpendGold(restored.State.Currency.Read().Gold, []));
        restored.State.Quests.ActorClicked(id); restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.Equal(0UL, restored.State.Currency.Read().Gold);
        Assert.True(Task(restored, "poor").IsSet); Assert.False(Task(restored, "paid").IsSet);
        Assert.Single(restored.State.Quests.Messages.Deliveries);
        Assert.False(restored.State.Quests.Capture().Instances.Single().Resources.Single().HasPlayerClicked);
    }

    [Fact]
    public void Ordinary_foe_target_click_does_not_fire_npc_trigger_for_the_same_symbol()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place foe _enemy_ at _location_"], taskBlocks:
            [["_npc_ task:", "clicked npc _enemy_ say 100"], ["_foe_ task:", "clicked foe _enemy_ say 100"]], messages: ["The creature speaks."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var site = definitions.Locations.Records.Single(v => v.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new("foe", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId) })], []));
        f.Update();
        long id = f.Session.State.Quests.Capture().Instances.Single().Resources.Single(v => v.SelectedFoe is not null).Binding.ActorIds.Single();
        var actor = f.Session.State.Actors.Get(id);
        actor.ApplyPose(new(new WorldPoint(0, 2, -1), 0));
        var senses = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>();
        senses.SetForcedHostile(false); senses.MagicallyPacified = true; senses.HasEncounteredPlayer = true;
        AimActivationAt(f.Session, id);
        f.Session.Update(new ProductUpdate(OuterUpdate(50), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.True(f.Session.ActivationView.Applied, f.Session.ActivationView.Message);
        Assert.False(Task(f.Session, "npc").IsSet); Assert.True(Task(f.Session, "foe").IsSet);
        Assert.Single(f.Session.State.Quests.Messages.Deliveries);
    }

    private static DaggerfallQuestTaskState Task(DaggerfallSession session, string symbol) => session.State.Quests.Capture().Instances.Single().Tasks.Single(v => v.Symbol == symbol);
}
