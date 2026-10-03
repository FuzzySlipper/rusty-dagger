using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestFoeLifecycleTests
{
    [Theory]
    [InlineData("injured _quest-foe_", DaggerfallQuestTaskOperationKind.InjuredFoe, null)]
    [InlineData("injured _quest-foe_ saying 100", DaggerfallQuestTaskOperationKind.InjuredFoe, null)]
    [InlineData("kill foe _quest-foe_", DaggerfallQuestTaskOperationKind.KillFoe, null)]
    [InlineData("killed _quest-foe_", DaggerfallQuestTaskOperationKind.KilledFoe, 1)]
    [InlineData("killed 0 _quest-foe_", DaggerfallQuestTaskOperationKind.KilledFoe, 1)]
    [InlineData("killed 2 _quest-foe_", DaggerfallQuestTaskOperationKind.KilledFoe, 2)]
    [InlineData("killed 2 _quest-foe_ saying 100", DaggerfallQuestTaskOperationKind.KilledFoe, 2)]
    [InlineData("remove foe _quest-foe_", DaggerfallQuestTaskOperationKind.RemoveFoe, null)]
    internal void Retained_source_variants_preserve_named_meaning(string action, DaggerfallQuestTaskOperationKind kind, int? minimum)
    {
        var source = new DaggerfallQuestSourceDefinition("foe", "", "foe.txt", DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [action], null)], []);
        var op = Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(source).Tasks).Operations);
        Assert.Equal(kind, op.Kind); Assert.Equal(minimum, op.Step); Assert.Equal("quest-foe", op.Targets.Single());
        Assert.Empty(DaggerfallQuestTaskCompiler.Assess(source));
    }

    [Fact]
    public void Queued_kill_uses_actual_accepted_death_and_corpse_once_and_survives_current_encoded_restore()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place foe _enemy_ at _location_", "kill foe _enemy_"],
            taskBlocks: [["_dead_ task:", "killed _enemy_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        Start(f, definitions); f.Update(); f.Update();
        var resource = Resource(f.Session);
        long id = Assert.Single(resource.Binding.ActorIds);
        Assert.True(f.Session.State.Actors.Get(id).IsDefeated);
        Assert.False(resource.FoeInjured); Assert.Equal(id, Assert.Single(resource.DefeatedFoeIds));
        Assert.True(f.Session.State.Actors.Get(id).Actor.TryGet<CorpseLootComponent>(out _));
        Assert.True(f.Session.State.Quests.Capture().Instances.Single().Tasks.Single(value => value.Symbol == "dead").IsSet);
        using var restored = f.Restore();
        restored.Update(new ProductUpdate(TestSessions.OuterUpdate(1), []));
        Assert.Equal(id, Assert.Single(Resource(restored).DefeatedFoeIds));
        Assert.True(restored.State.Actors.Get(id).IsDefeated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Physical_damage_records_surviving_injury_but_one_shot_records_only_death(bool terminal)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place foe _enemy_ at _location_"],
            taskBlocks: [["_injury_ task:", "injured _enemy_"], ["_dead_ task:", "killed _enemy_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        Start(f, definitions); f.Update();
        long id = Resource(f.Session).Binding.ActorIds.Single();
        var actor = f.Session.State.Actors.Get(id);
        var health = actor.Stats.GetTrack(TrackId.Parse("health"));
        Assert.True(health.Maximum.Value > 1);
        health.SetCurrent(terminal ? 1 : health.Maximum.Value);
        f.Session.State.Kit.Rules.RegisterAction(f.Session.DefinitionsByActor[1].ActionId!, new OneDamage());
        f.Session.ResolveExplicitMelee(new(1, id, 1, 10000, .125));
        Assert.Equal(!terminal, Resource(f.Session).FoeInjured);
        Assert.Equal(terminal ? 1 : 0, Resource(f.Session).DefeatedFoeIds.Length);
        f.Update();
        var tasks = f.Session.State.Quests.Capture().Instances.Single().Tasks;
        Assert.Equal(!terminal, tasks.Single(value => value.Symbol == "injury").IsSet);
        Assert.Equal(terminal, tasks.Single(value => value.Symbol == "dead").IsSet);
        using var restored = f.Restore();
        Assert.Equal(Resource(f.Session).FoeInjured, Resource(restored).FoeInjured);
        Assert.Equal(Resource(f.Session).DefeatedFoeIds, Resource(restored).DefeatedFoeIds);
    }

    [Fact]
    public void Removal_before_placement_creates_no_actor_death_or_corpse()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place foe _enemy_ at _location_", "remove foe _enemy_"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        int before = f.Session.State.Actors.All.Count();
        Start(f, definitions); f.Update();
        Assert.True(Resource(f.Session).IsHidden); Assert.Empty(Resource(f.Session).Binding.ActorIds);
        Assert.Empty(Resource(f.Session).DefeatedFoeIds); Assert.Empty(f.Session.State.Quests.Capture().Instances.Single().Placements);
        Assert.Equal(before, f.Session.State.Actors.All.Count());
        using var restored = f.Restore(); Assert.True(Resource(restored).IsHidden);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Actual_loaded_or_retained_command_preserves_death_or_removal_lifetime_after_save_and_both_site_returns(bool offSite, bool remove)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place foe _enemy_ at _location_"],
            taskBlocks: [["_command_ task:", remove ? "remove foe _enemy_" : "kill foe _enemy_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, f.Castle]);
        f.Session.AdmitSiteProfiles(profiles);
        Start(f, definitions); f.Update();
        long id = Resource(f.Session).Binding.ActorIds.Single();
        var sourceActor = f.Session.State.Actors.Get(id).Actor.Entity;
        if (offSite) Assert.True(f.Session.TryTransitionTo(f.Castle.ProfileKey));
        var save = f.Session.State.Quests.Capture();
        f.Session.State.Quests.Restore(save with { Instances = [save.Instances.Single() with
            { Tasks = save.Instances.Single().Tasks.Select(value => value.Symbol == "command" ? value with { IsSet = true } : value).ToArray() }] });
        f.Update();
        var resource = Resource(f.Session);
        Assert.Equal(remove ? 0 : 1, resource.DefeatedFoeIds.Length);
        Assert.Equal(remove ? 1 : 0, resource.RemovedFoeIds.Length);
        if (remove)
        {
            Assert.False(f.Session.State.Actors.TryGet(id, out _));
            if (!offSite) Assert.False(f.Session.State.InventoryStore.TryGetInventory(sourceActor, out _));
            var captured = DaggerfallSavePayload.Read(f.Session.CaptureSave());
            Assert.DoesNotContain(captured.ActorInventories.Concat(captured.SiteDeltas.SelectMany(value => value.ActorInventories)), value => value.EntityId == id);
            Assert.DoesNotContain(captured.Corpses.Concat(captured.SiteDeltas.SelectMany(value => value.Corpses)), value => value.ActorId == id);
        }
        if (offSite) Assert.False(f.Session.State.Actors.TryGet(id, out _));
        using var restored = f.Restore(profiles);
        if (offSite) Assert.True(restored.TryTransitionTo(f.Inputs.ProfileKey));
        if (remove) Assert.False(restored.State.Actors.TryGet(id, out _));
        else
        {
            Assert.True(restored.State.Actors.Get(id).IsDefeated);
            Assert.True(restored.State.Actors.Get(id).Actor.TryGet<CorpseLootComponent>(out _));
        }
        Assert.True(restored.TryTransitionTo(f.Castle.ProfileKey));
        Assert.True(restored.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.Equal(resource.DefeatedFoeIds, Resource(restored).DefeatedFoeIds);
        Assert.Equal(resource.RemovedFoeIds, Resource(restored).RemovedFoeIds);
        if (remove) Assert.False(restored.State.Actors.TryGet(id, out _));
    }

    [Fact]
    public void Kill_count_threshold_requires_two_actual_group_deaths_and_does_not_repeat_after_restore()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place foe _enemy_ at _location_", "kill foe _enemy_"],
            taskBlocks: [["_two_ task:", "killed 2 _enemy_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        Start(f, definitions);
        var resource = Resource(f.Session);
        f.Session.State.Quests.SetResource("foes", resource with { SelectedFoe = resource.SelectedFoe! with { Count = 2 } });
        f.Update(); f.Update();
        Assert.Equal(2, Resource(f.Session).Binding.ActorIds.Length);
        Assert.Equal(2, Resource(f.Session).DefeatedFoeIds.Length);
        Assert.True(f.Session.State.Quests.Capture().Instances.Single().Tasks.Single(value => value.Symbol == "two").IsSet);
        using var restored = f.Restore();
        restored.Update(new ProductUpdate(TestSessions.OuterUpdate(1), []));
        Assert.Equal(2, Resource(restored).DefeatedFoeIds.Length);
    }

    [Fact]
    public void Actual_injury_saying_is_delivered_once_and_current_task_completion_survives_encoded_restore()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place foe _enemy_ at _location_"],
            taskBlocks: [["_injury_ task:", "injured _enemy_ saying 100"]], messages: ["The foe is wounded."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        Start(f, definitions); f.Update();
        long id = Resource(f.Session).Binding.ActorIds.Single();
        f.Session.State.Kit.Rules.RegisterAction(f.Session.DefinitionsByActor[1].ActionId!, new OneDamage());
        f.Session.ResolveExplicitMelee(new(1, id, 1, 10000, .125));
        f.Update(); f.Update();
        Assert.Single(f.Session.State.Quests.Capture().Messages.Deliveries, value => value.MessageId == 100);
        using var restored = f.Restore();
        restored.Update(new ProductUpdate(TestSessions.OuterUpdate(1), []));
        Assert.Single(restored.State.Quests.Capture().Messages.Deliveries, value => value.MessageId == 100);
    }

    [Fact]
    public void Wrong_resource_and_malformed_current_lifecycle_state_report_the_actual_problem()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["kill foe _gift_"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions);
        Assert.Contains("requires selected Foe", Assert.Throws<ArgumentException>(() => f.Update()).Message);
        var save = f.Session.State.Quests.Capture().Instances.Single();
        var resource = save.Resources.Single(value => value.Symbol == "enemy");
        var malformed = save with { Resources = save.Resources.Select(value => value.Symbol == "enemy"
            ? resource with { DefeatedFoeIds = [long.MaxValue] } : value).ToArray() };
        Assert.Contains("incompatible foe lifecycle", Assert.Throws<ArgumentException>(malformed.ValidateShape).Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Removal_retires_actual_owned_unique_or_stack_and_quest_binding_when_loaded_or_detached(bool stackable, bool offSite)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(stackable: stackable, actions: ["place foe _enemy_ at _location_"],
            taskBlocks: [["_command_ task:", "remove foe _enemy_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, f.Castle]); f.Session.AdmitSiteProfiles(profiles);
        Start(f, definitions); f.Update();
        var state = f.Session.State;
        long actorId = Resource(f.Session).Binding.ActorIds.Single();
        state.Quests.GrantItem("foes", "gift");
        var item = state.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "gift");
        var owner = state.Actors.Get(actorId).Actor.Entity;
        if (stackable)
        {
            var id = InventoryStackId.Parse(item.Binding.Stacks.Single().StackId);
            ulong quantity = state.Inventory.Read().Stacks.Single(value => value.Id == id).Quantity;
            state.Containers.Transfer(state.Actors.Player.Actor.Entity, owner, new(item.SelectedItem!.Item, quantity, Stack: id, DestinationStack: id));
            state.ItemInstances.TransferStack(DaggerfallItemOwner.Player, DaggerfallItemOwner.Actor(actorId), id, id, true);
        }
        else
        {
            ulong id = item.Binding.UniqueItemIds.Single();
            var entity = state.Actors.Entities.Resolve(new(DurableIdentityKind.Item, id));
            state.Containers.Transfer(state.Actors.Player.Actor.Entity, owner, new(item.SelectedItem!.Item, 1, UniqueEntityId: entity.Value));
            state.ItemInstances.MoveUnique(id, DaggerfallItemOwner.Actor(actorId));
        }
        if (offSite) Assert.True(f.Session.TryTransitionTo(f.Castle.ProfileKey));
        var save = state.Quests.Capture();
        state.Quests.Restore(save with { Instances = [save.Instances.Single() with { Tasks = save.Instances.Single().Tasks.Select(value => value.Symbol == "command" ? value with { IsSet = true } : value).ToArray() }] });
        f.Update();
        Assert.False(state.Actors.TryGet(actorId, out _));
        if (stackable) Assert.Empty(state.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "gift").Binding.Stacks);
        else Assert.False(state.ItemInstances.ContainsUnique(item.Binding.UniqueItemIds.Single()));
        using var restored = f.Restore(profiles);
        if (offSite) Assert.True(restored.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.False(restored.State.Actors.TryGet(actorId, out _));
        if (stackable) Assert.Empty(restored.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "gift").Binding.Stacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_removal_overrides_same_pass_commanded_death_for_loaded_and_retained_foe(bool offSite)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["kill foe _enemy_", "remove foe _enemy_"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, f.Castle]); f.Session.AdmitSiteProfiles(profiles);
        Start(f, definitions);
        f.Session.State.Quests.RequestPlacement("foes", "initial", "enemy", "location");
        f.Session.State.Quests.AdmitPlacements(f.Inputs, f.Session);
        long id = Resource(f.Session).Binding.ActorIds.Single();
        if (offSite) Assert.True(f.Session.TryTransitionTo(f.Castle.ProfileKey));
        f.Update();
        Assert.Equal(id, Assert.Single(Resource(f.Session).RemovedFoeIds));
        Assert.Empty(Resource(f.Session).DefeatedFoeIds);
        Assert.False(Resource(f.Session).FoeInjured);
        var save = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        Assert.DoesNotContain(save.Corpses.Concat(save.SiteDeltas.SelectMany(value => value.Corpses)), value => value.ActorId == id);
        using var restored = f.Restore(profiles);
        Assert.Empty(Resource(restored).DefeatedFoeIds);
    }

    private sealed class OneDamage : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = true;
        public void Damage(DamageEvent value) => value.Damage = 1;
    }
    private static DaggerfallQuestResourceState Resource(DaggerfallSession session) => session.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "enemy");
    private static void Start(SanguineRoseSessionTests.Fixture f, DaggerfallDefinitions definitions)
    {
        var site = definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new("foes", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId, null, 0) })], []));
    }
}
