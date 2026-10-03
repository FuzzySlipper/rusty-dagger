using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Loot;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using Rusty.Engine;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestPlacementActionTests
{
    [Theory]
    [InlineData("create npc at _location_", DaggerfallQuestTaskOperationKind.ReservePlace, null, DaggerfallQuestMarkerPreference.Default)]
    [InlineData("place foe _enemy_ at _location_", DaggerfallQuestTaskOperationKind.PlaceFoe, null, DaggerfallQuestMarkerPreference.Default)]
    [InlineData("place foe _enemy_ at _location_ marker 2", DaggerfallQuestTaskOperationKind.PlaceFoe, 2, DaggerfallQuestMarkerPreference.Default)]
    [InlineData("place item _gift_ at _location_", DaggerfallQuestTaskOperationKind.PlaceItem, null, DaggerfallQuestMarkerPreference.Default)]
    [InlineData("place item _gift_ at _location_ marker 3", DaggerfallQuestTaskOperationKind.PlaceItem, 3, DaggerfallQuestMarkerPreference.Default)]
    [InlineData("place item _gift_ at _location_ questmarker 4", DaggerfallQuestTaskOperationKind.PlaceItem, 4, DaggerfallQuestMarkerPreference.QuestSpawn)]
    [InlineData("place item _gift_ at _location_ anymarker", DaggerfallQuestTaskOperationKind.PlaceItem, null, DaggerfallQuestMarkerPreference.Any)]
    [InlineData("place npc _person_ at _location_", DaggerfallQuestTaskOperationKind.PlaceNpc, null, DaggerfallQuestMarkerPreference.Default)]
    [InlineData("place npc _person_ at _location_ marker 5", DaggerfallQuestTaskOperationKind.PlaceNpc, 5, DaggerfallQuestMarkerPreference.Default)]
    internal void Retained_source_variants_compile_to_named_operations(string line, DaggerfallQuestTaskOperationKind kind, int? marker, DaggerfallQuestMarkerPreference preference)
    {
        var source = new DaggerfallQuestSourceDefinition("placement", "", "placement.txt", DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [line], null)], []);
        var operation = Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(source).Tasks).Operations);
        Assert.Equal(kind, operation.Kind);
        Assert.Equal(marker, operation.MarkerIndex);
        Assert.Equal(preference, operation.MarkerPreference);
        Assert.Equal("location", operation.Targets.Last());
        Assert.Empty(DaggerfallQuestTaskCompiler.Assess(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_update_places_queued_foe_and_taken_item_once_with_current_identity_after_restore(bool stackable)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(stackable: stackable,
            actions: ["create npc at _location_", "place foe _enemy_ at _location_ marker 0", "place item _gift_ at _location_ anymarker"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var state = f.Session.State;
        var started = Start(f, definitions, f.Inputs);
        state.Quests.GrantItem(started.InstanceId, "gift");
        state.Quests.TakeItem(started.InstanceId, "gift");
        var itemBinding = state.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "gift").Binding;
        state.Quests.Advance(state.Variables, DaggerfallCalendar.Start);
        Assert.All(state.Quests.Capture().Instances.Single().Placements, value => Assert.Null(value.Applied));
        int actors = state.Actors.All.Count();
        using (var pending = f.Restore())
        {
            pending.State.Quests.AdmitPlacements(f.Inputs, pending);
            pending.State.Quests.AdmitPlacements(f.Inputs, pending);
            Assert.Equal(actors + 1, pending.State.Actors.All.Count());
        }
        f.Update();
        f.Update();
        var placed = state.Quests.Capture().Instances.Single();
        Assert.Equal(2, placed.Placements.Length);
        Assert.All(placed.Placements, value => Assert.NotNull(value.Applied));
        Assert.Equal(actors + 1, state.Actors.All.Count());
        var item = placed.Resources.Single(value => value.Symbol == "gift").Binding;
        if (stackable)
        {
            Assert.Equal(itemBinding.Stacks.Single().StackId, item.Stacks.Single().StackId);
            Assert.Equal("ground", item.Stacks.Single().Owner.Scope);
        }
        else
        {
            Assert.Equal(itemBinding.UniqueItemIds, item.UniqueItemIds);
            Assert.Equal("ground", state.ItemInstances.RequireUnique(item.UniqueItemIds.Single()).Owner.Scope);
        }
        using var restored = f.Restore();
        restored.State.Quests.AdmitPlacements(f.Inputs, restored);
        Assert.Equal(actors + 1, restored.State.Actors.All.Count());
        restored.State.Quests.Complete(started.InstanceId, "done");
        Assert.Empty(restored.State.Quests.Capture().Instances.Single().Placements);
        if (stackable) Assert.Equal(item.Stacks.Single().StackId, Assert.Single(restored.State.ItemInstances.StackItems, value => value.Metadata.QuestId == started.InstanceId).Stack.Value);
        else Assert.Equal("ground", restored.State.ItemInstances.RequireUnique(item.UniqueItemIds.Single()).Owner.Scope);
    }

    [Fact]
    public void Normal_source_placement_before_destination_visit_survives_restore_and_actual_transition()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place foe _enemy_ at _location_", "place item _gift_ at _location_ questmarker 0"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var castle = QuestWorldAdmissionTests.WithMarker(f.Castle);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, castle]);
        f.Session.AdmitSiteProfiles(profiles);
        Start(f, definitions, castle);
        f.Update();
        Assert.All(f.Session.State.Quests.Capture().Instances.Single().Placements, value => Assert.Null(value.Applied));
        using var pending = f.Restore(profiles);
        Assert.True(pending.TryTransitionTo(castle.ProfileKey));
        pending.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.All(pending.State.Quests.Capture().Instances.Single().Placements, value => Assert.NotNull(value.Applied));
        var ids = pending.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "enemy").Binding.ActorIds;
        Assert.True(pending.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.True(pending.TryTransitionTo(castle.ProfileKey));
        Assert.Equal(ids, pending.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "enemy").Binding.ActorIds);
    }

    [Fact]
    public void Missing_or_wrong_resource_and_unreserved_place_report_without_an_applied_operation()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place foe _gift_ at _location_"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions, f.Inputs);
        Assert.Contains("required resource kind", Assert.Throws<ArgumentException>(() => f.Update()).Message);
        Assert.Empty(f.Session.State.Quests.Capture().Instances.Single().Placements);
    }

    [Fact]
    public void Source_npc_placement_unhides_and_projects_the_selected_person_with_saved_identity()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(person: true, actions: ["place npc _person_ at _location_ marker 0"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var started = Start(f, definitions, f.Inputs);
        var person = started.Resources.Single(value => value.Symbol == "person");
        f.Session.State.Quests.SetResource(started.InstanceId, person with { IsHidden = true });
        f.Update();
        var placed = f.Session.State.Quests.Capture().Instances.Single();
        var binding = placed.Resources.Single(value => value.Symbol == "person");
        Assert.False(binding.IsHidden);
        long id = binding.Binding.ActorIds.Single();
        Assert.Equal(DaggerfallNpcPresence.Active, f.Session.State.Npcs.Require(id).Presence);
        Assert.Equal(f.Inputs.ProfileKey, f.Session.State.Npcs.Require(id).Profile);
        Assert.True(f.Session.State.Actors.Entities.TryResolve(WorldRpg.Kit.Actors.ActorsState.Identity(id), out _));
        f.Update();
        using var restored = f.Restore();
        Assert.Equal(id, restored.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "person").Binding.ActorIds.Single());
        Assert.Equal(DaggerfallNpcPresence.Active, restored.State.Npcs.Require(id).Presence);
        Assert.Single(restored.State.Quests.Capture().Instances.Single().Placements);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Placement_reclaims_the_same_bound_item_from_inactive_actor_or_corpse_without_moving_or_duplicating_the_actor(bool stackable, bool corpse, bool mixed)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(stackable: stackable, actions: ["place item _gift_ at _location_ questmarker 0"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var castle = QuestWorldAdmissionTests.WithMarker(f.Castle);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, castle]);
        f.Session.AdmitSiteProfiles(profiles);
        var started = Start(f, definitions, castle);
        var state = f.Session.State;
        if (mixed)
        {
            var selected = started.Resources.Single(value => value.Symbol == "gift");
            state.Quests.SetResource(started.InstanceId, selected with { SelectedItem = selected.SelectedItem! with { Quantity = 2 } });
        }
        state.Quests.GrantItem(started.InstanceId, "gift");
        var resource = state.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "gift");
        var actor = state.Actors.Get(f.Enemy);
        if (corpse)
        {
            state.Kit.Rules.RegisterAction(f.Session.DefinitionsByActor[1].ActionId!, new DefeatingHit());
            actor.Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).SetCurrent(1);
            f.Session.ResolveExplicitMelee(new(1, f.Enemy, 1, 10000, .125));
            f.Update();
            Assert.True(actor.IsDefeated);
        }
        var actorOwner = corpse ? actor.Actor.Get<CorpseLootComponent>().Owner : actor.Actor.Entity;
        var itemOwner = corpse ? DaggerfallItemOwner.Corpse(f.Enemy) : DaggerfallItemOwner.Actor(f.Enemy);
        ulong originalQuantity = 0;
        if (stackable)
        {
            var id = Rusty.Engine.Mechanics.InventoryStackId.Parse(resource.Binding.Stacks.Single().StackId);
            originalQuantity = state.Inventory.Read().Stacks.Single(value => value.Id == id).Quantity;
            ulong quantity = mixed ? originalQuantity - 1 : originalQuantity;
            state.Containers.Transfer(state.Actors.Player.Actor.Entity, actorOwner, new(resource.SelectedItem!.Item, quantity, Stack: id, DestinationStack: id));
            state.ItemInstances.TransferStack(DaggerfallItemOwner.Player, itemOwner, id, id, !mixed);
        }
        else
        {
            ulong id = resource.Binding.UniqueItemIds.Single();
            var entity = state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Item, id));
            state.Containers.Transfer(state.Actors.Player.Actor.Entity, actorOwner, new(resource.SelectedItem!.Item, 1, UniqueEntityId: entity.Value));
            state.ItemInstances.MoveUnique(id, itemOwner);
        }
        f.Update();
        Assert.True(f.Session.TryTransitionTo(castle.ProfileKey));
        using var inactive = f.Restore(profiles);
        Assert.False(inactive.State.Actors.TryGet(f.Enemy, out _));
        inactive.Update(new ProductUpdate(OuterUpdate(1), []));
        var item = inactive.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "gift").Binding;
        Assert.NotNull(inactive.State.Quests.Capture().Instances.Single().Placements.Single().Applied);
        if (stackable)
        {
            Assert.Equal(resource.Binding.Stacks.Single().StackId, item.Stacks.Single().StackId);
            Assert.Equal("ground", item.Stacks.Single().Owner.Scope);
            var groundOwner = inactive.State.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container,
                checked((ulong)item.Stacks.Single().Owner.Id)));
            Assert.Equal(originalQuantity, inactive.State.Containers.Read(groundOwner).Stacks.Single(value => value.Id.Value == item.Stacks.Single().StackId).Quantity);
        }
        else
        {
            Assert.Equal(resource.Binding.UniqueItemIds, item.UniqueItemIds);
            Assert.Equal("ground", inactive.State.ItemInstances.RequireUnique(item.UniqueItemIds.Single()).Owner.Scope);
        }
        Assert.False(inactive.State.Actors.TryGet(f.Enemy, out _));
        Assert.True(inactive.TryTransitionTo(f.Inputs.ProfileKey));
        var restoredActor = inactive.State.Actors.Get(f.Enemy);
        var inventory = corpse ? inactive.State.Containers.Read(restoredActor.Actor.Get<CorpseLootComponent>().Owner)
            : inactive.State.ActorInventories.InventoryFor(f.Enemy)!.Read();
        if (stackable) Assert.DoesNotContain(inventory.Stacks, value => value.Id.Value == item.Stacks.Single().StackId);
        else Assert.DoesNotContain(inventory.UniqueItems, value => value.Entity.Value == inactive.State.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Item, item.UniqueItemIds.Single())).Value);
    }

    [Fact]
    public void Individual_at_home_skip_keeps_active_quest_save_valid_and_does_not_add_a_source_placement()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(person: true, atHome: true, actions: ["place npc _person_ at _location_ marker 0"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        Start(f, definitions, f.Inputs);
        f.Update();
        var saved = f.Session.State.Quests.Capture().Instances.Single();
        Assert.Null(saved.Outcome);
        Assert.All(saved.Placements, value => Assert.True(value.AutomaticHome));
        using var restored = f.Restore();
        Assert.Equal(DaggerfallQuestLifecycle.Active, restored.State.Quests.Capture().Instances.Single().Lifecycle);
    }

    [Fact]
    public void Explicitly_rearmed_source_npc_placement_reveals_hidden_but_keeps_removed_person_absent()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(person: true, rearmPlacement: true, actions: ["place npc _person_ at _location_ marker 0", "clear headless.1"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var started = Start(f, definitions, f.Inputs);
        f.Update();
        var saved = f.Session.State.Quests.Capture();
        var quest = saved.Instances.Single();
        long id = quest.Resources.Single(value => value.Symbol == "person").Binding.ActorIds.Single();
        f.Session.State.Npcs.SetPresence(id, DaggerfallNpcPresence.Hidden);
        f.Update();
        Assert.Equal(DaggerfallNpcPresence.Active, f.Session.State.Npcs.Require(id).Presence);
        f.Session.State.Npcs.SetPresence(id, DaggerfallNpcPresence.Removed);
        f.Session.ReconcileNpcProjection();
        f.Update();
        Assert.Equal(DaggerfallNpcPresence.Removed, f.Session.State.Npcs.Require(id).Presence);
        Assert.False(f.Session.State.Actors.Entities.TryResolve(WorldRpg.Kit.Actors.ActorsState.Identity(id), out _));
    }

    [Theory]
    [InlineData("place item _gift_ at _location_")]
    [InlineData("place item _gift_ at _location_ anymarker")]
    public void Rearmed_no_index_source_placement_retains_actual_marker_through_pending_encoded_restore(string action)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(rearmPlacement: true, actions: [action, "clear headless.1"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        Start(f, definitions, f.Inputs);
        f.Update();
        var applied = f.Session.State.Quests.Capture().Instances.Single().Placements.Single();
        Assert.NotNull(applied.Applied);
        f.Session.State.Quests.Advance(f.Session.State.Variables, DaggerfallCalendar.Start);
        var pending = f.Session.State.Quests.Capture().Instances.Single().Placements.Single();
        Assert.True(pending.PendingReapplication);
        Assert.Equal(applied.Applied, pending.Applied);
        using var restored = f.Restore();
        restored.Update(new ProductUpdate(OuterUpdate(1), []));
        var repeated = restored.State.Quests.Capture().Instances.Single().Placements.Single();
        Assert.False(repeated.PendingReapplication);
        Assert.Equal(applied.Applied, repeated.Applied);
        Assert.Equal(f.Session.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "gift").Binding.UniqueItemIds,
            restored.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == "gift").Binding.UniqueItemIds);
    }

    [Fact]
    public void Retained_equipped_enchanted_item_rehoming_clears_held_cast_and_all_detached_source_effects()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["place item _gift_ at _location_ anymarker"]);
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0035", definitions: definitions);
        var castle = QuestWorldAdmissionTests.WithMarker(f.Castle);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, castle]);
        f.Session.AdmitSiteProfiles(profiles);
        var started = Start(f, definitions, castle);
        var state = f.Session.State;
        var resource = started.Resources.Single(value => value.Symbol == "gift");
        var metadata = state.ItemInstances.RequireUnique(f.Source) with { QuestId = started.InstanceId, QuestItemSymbol = "gift" };
        state.ItemInstances.ReplaceUnique(f.Source, metadata);
        state.Quests.SetResource(started.InstanceId, resource with
        {
            Binding = DaggerfallQuestResourceBinding.UniqueItem(f.Source),
            SelectedItem = resource.SelectedItem! with { Item = new(f.Item.Definition.Value), Metadata = metadata }
        });
        var actor = state.Actors.Get(f.Enemy);
        state.Containers.Transfer(state.Actors.Player.Actor.Entity, actor.Actor.Entity,
            new(f.Item.Definition, 1, UniqueEntityId: f.Item.EntityId));
        state.ItemInstances.MoveUnique(f.Source, DaggerfallItemOwner.Actor(f.Enemy));
        var equipment = state.ActorInventories.EquipmentFor(f.Enemy);
        foreach (var item in equipment.Read().Assignments.Select(value => value.Item).Distinct()) equipment.Unequip(item);
        var definition = definitions.RequireItem(new(f.Item.Definition.Value));
        var slot = definitions.EquipmentSlots.Values.First(value => value.AllowedClassifications.Intersect(definition.Equipment!.Classifications).Any());
        equipment.Equip(f.Item, [new(slot.Id.Value)]);
        f.Update();
        Assert.NotNull(state.ItemInstances.RequireUnique(f.Source).HeldCast);
        Assert.Contains(state.Effects.Capture(), effect => effect.ItemId == f.Source);
        Assert.True(f.Session.TryTransitionTo(castle.ProfileKey));
        var saved = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        Assert.Contains(saved.SiteDeltas.SelectMany(value => value.Effects), effect => effect.ItemId == f.Source);
        using var inactive = f.Restore(profiles);
        inactive.Update(new ProductUpdate(OuterUpdate(1), []));
        var moved = inactive.State.ItemInstances.RequireUnique(f.Source);
        Assert.Equal("ground", moved.Owner.Scope);
        Assert.Null(moved.HeldCast);
        var after = DaggerfallSavePayload.Read(inactive.CaptureSave());
        Assert.DoesNotContain(after.SiteDeltas.SelectMany(value => value.Effects), effect => effect.ItemId == f.Source);
        Assert.DoesNotContain(after.ActiveEffects, effect => effect.ItemId == f.Source);
        Assert.True(inactive.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.DoesNotContain(inactive.State.Effects.Capture(), effect => effect.ItemId == f.Source);
        using var restored = DaggerfallSession.Restore(f.Engine.Context, f.Composition with { Profiles = profiles }, inactive.CaptureSave());
        Assert.Null(restored.State.ItemInstances.RequireUnique(f.Source).HeldCast);
    }

    private sealed class DefeatingHit : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = true;
        public void Damage(DamageEvent value) => value.Damage = 1;
    }

    private static DaggerfallQuestInstanceSave Start(SanguineRoseSessionTests.Fixture f, DaggerfallDefinitions definitions, DaggerfallSiteProfile profile)
    {
        var site = definitions.Locations.Records.Single(value => value.Id == profile.Site);
        return f.Session.State.Quests.Start(new("placement", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(profile.ProfileKind, site.MapId, null, 0) })], []));
    }
}
