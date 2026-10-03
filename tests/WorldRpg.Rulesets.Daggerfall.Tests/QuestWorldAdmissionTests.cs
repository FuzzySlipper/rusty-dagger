using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestWorldAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Queued_Item_and_Foe_placement_uses_real_owners_once_and_retains_identity_after_encoded_restore(bool stackable)
    {
        var definitions = Definitions(stackable);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var session = fixture.Session;
        var inputs = fixture.Inputs;
        var site = definitions.Locations.Records.Single(value => value.Id == inputs.Site);
        var binding = DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with
            { PlaceSelection = new(inputs.ProfileKind, site.MapId, null, 0) };
        var instance = session.State.Quests.Start(new("world-test", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", binding)], []));
        string item = instance.Resources.Single(value => value.SelectedItem is not null).Symbol;
        string foe = instance.Resources.Single(value => value.SelectedFoe is not null).Symbol;
        session.State.Quests.RequestPlacement(instance.InstanceId, "place-item", item, "location");
        session.State.Quests.RequestPlacement(instance.InstanceId, "place-foe", foe, "location");
        var before = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.All(before.Quests.Instances.Single().Placements, operation => Assert.Null(operation.Applied));
        int actorCount = session.State.Actors.All.Count();
        int itemCount = session.State.ItemInstances.UniqueItems.Count();
        int stackCount = session.State.ItemInstances.StackItems.Count();
        using (var pending = fixture.Restore())
        {
            Assert.All(pending.State.Quests.Capture().Instances.Single().Placements, operation => Assert.Null(operation.Applied));
            pending.State.Quests.AdmitPlacements(inputs, pending);
            pending.State.Quests.AdmitPlacements(inputs, pending);
            Assert.Equal(actorCount + 1, pending.State.Actors.All.Count());
            Assert.All(pending.State.Quests.Capture().Instances.Single().Placements, operation => Assert.NotNull(operation.Applied));
        }
        session.State.Quests.AdmitPlacements(inputs, session);
        Assert.True(session.State.Quests.TryGet(instance.InstanceId, out var placed));
        var foeBinding = placed!.Resources.Single(value => value.Symbol == foe).Binding;
        var itemBinding = placed.Resources.Single(value => value.Symbol == item).Binding;
        Assert.Equal(actorCount + 1, session.State.Actors.All.Count());
        Assert.Equal(itemCount + (stackable ? 0 : 1), session.State.ItemInstances.UniqueItems.Count());
        Assert.Equal(stackCount + (stackable ? 1 : 0), session.State.ItemInstances.StackItems.Count());
        Assert.True(session.State.Actors.TryGet(foeBinding.ActorIds.Single(), out _));
        if (stackable) Assert.Equal("ground", itemBinding.Stacks.Single().Owner.Scope);
        else Assert.Equal("ground", session.State.ItemInstances.RequireUnique(itemBinding.UniqueItemIds.Single()).Owner.Scope);
        Assert.All(placed.Placements, operation => Assert.Equal(inputs.QuestMarkers.Single().Id, operation.Applied!.MarkerId));
        session.State.Quests.AdmitPlacements(inputs, session);
        Assert.Equal(actorCount + 1, session.State.Actors.All.Count());
        Assert.Equal(itemCount + (stackable ? 0 : 1), session.State.ItemInstances.UniqueItems.Count());
        Assert.Equal(stackCount + (stackable ? 1 : 0), session.State.ItemInstances.StackItems.Count());
        using var restored = fixture.Restore();
        restored.State.Quests.AdmitPlacements(inputs, restored);
        Assert.Equal(actorCount + 1, restored.State.Actors.All.Count());
        Assert.Equal(itemCount + (stackable ? 0 : 1), restored.State.ItemInstances.UniqueItems.Count());
        Assert.Equal(stackCount + (stackable ? 1 : 0), restored.State.ItemInstances.StackItems.Count());
        Assert.True(restored.State.Quests.TryGet(instance.InstanceId, out var saved));
        Assert.Equal(foeBinding.ActorIds, saved!.Resources.Single(value => value.Symbol == foe).Binding.ActorIds);
        Assert.Equal(itemBinding.UniqueItemIds, saved.Resources.Single(value => value.Symbol == item).Binding.UniqueItemIds);
        Assert.Equal(itemBinding.Stacks, saved.Resources.Single(value => value.Symbol == item).Binding.Stacks);
    }

    [Fact]
    public void Ended_quest_discards_unadmitted_operations_and_conflicting_ids_report_the_collision()
    {
        var definitions = Definitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var inputs = fixture.Inputs;
        var site = definitions.Locations.Records.Single(value => value.Id == inputs.Site);
        var instance = fixture.Session.State.Quests.Start(new("ending", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with
                { PlaceSelection = new(inputs.ProfileKind, site.MapId, null, 0) })], []));
        string item = instance.Resources.Single(value => value.SelectedItem is not null).Symbol;
        string foe = instance.Resources.Single(value => value.SelectedFoe is not null).Symbol;
        var quests = fixture.Session.State.Quests;
        quests.RequestPlacement(instance.InstanceId, "operation", item, "location");
        quests.RequestPlacement(instance.InstanceId, "operation", item, "location");
        Assert.Contains("different operation", Assert.Throws<ArgumentException>(() =>
            quests.RequestPlacement(instance.InstanceId, "operation", foe, "location")).Message);
        Assert.Empty(quests.Complete(instance.InstanceId, "done").Placements);
        int actorCount = fixture.Session.State.Actors.All.Count();
        quests.AdmitPlacements(inputs, fixture.Session);
        Assert.Equal(actorCount, fixture.Session.State.Actors.All.Count());
    }

    [Fact]
    public void Current_save_rejects_missing_queue_and_receipts_without_world_bindings_or_destination_profiles()
    {
        var definitions = Definitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var instance = Start(fixture, definitions);
        var quests = fixture.Session.State.Quests;
        string foe = instance.Resources.Single(value => value.SelectedFoe is not null).Symbol;
        quests.RequestPlacement(instance.InstanceId, "foe", foe, "location");
        var saved = quests.Capture();
        var encoded = JsonNode.Parse(JsonSerializer.Serialize(saved, DaggerfallSaveJsonContext.Default.DaggerfallQuestInstancesSave))!;
        encoded["Instances"]![0]!.AsObject().Remove("Placements");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(encoded.ToJsonString(), DaggerfallSaveJsonContext.Default.DaggerfallQuestInstancesSave));
        var pending = saved.Instances.Single();
        var noProfile = pending with { Resources = pending.Resources.Select(value => value.Symbol == "location"
            ? value with { Binding = value.Binding with { PlaceSelection = null } } : value).ToArray() };
        Assert.Contains("destination profile", Assert.Throws<ArgumentException>(() => noProfile.ValidateShape()).Message);
        var forgedReceipt = pending with { Placements = [pending.Placements.Single() with
            { Applied = new(fixture.Inputs.ProfileKey, fixture.Inputs.QuestMarkers.Single().Id) }] };
        Assert.Contains("world binding", Assert.Throws<ArgumentException>(() => forgedReceipt.ValidateShape()).Message);
        quests.AdmitPlacements(fixture.Inputs, fixture.Session);
        quests.Capture().Instances.Single().ValidateShape();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void Initial_marker_fallback_ignores_static_index_when_preferred_pool_is_absent_or_any(int preferenceValue)
    {
        var definitions = Definitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var instance = Start(fixture, definitions);
        string item = instance.Resources.Single(value => value.SelectedItem is not null).Symbol;
        Assert.Equal(DaggerfallSiteMarkerKind.QuestSpawn, fixture.Inputs.QuestMarkers.Single().Kind);
        fixture.Session.State.Quests.RequestPlacement(instance.InstanceId, "item", item, "location", markerIndex: 99, preference: (DaggerfallQuestMarkerPreference)preferenceValue);
        fixture.Session.State.Quests.AdmitPlacements(fixture.Inputs, fixture.Session);
        Assert.Equal(fixture.Inputs.QuestMarkers.Single().Id,
            fixture.Session.State.Quests.Capture().Instances.Single().Placements.Single().Applied!.MarkerId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Failed_ground_creation_retires_allocated_identities_before_or_after_seed(bool stackable, bool failAfterSeed)
    {
        var definitions = Definitions(stackable);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var state = fixture.Session.State;
        var instance = Start(fixture, definitions);
        var created = instance.Resources.Single(value => value.SelectedItem is not null).SelectedItem!;
        var identities = new WorldRpg.Kit.World.DurableIdentityAllocator(WorldRpg.Kit.World.DurableIdentityKind.Container, 9000000);
        var unique = new DaggerfallUniqueItemAllocator(90000000);
        var itemDefinitions = new Dictionary<WorldRpg.Kit.Inventory.InventoryItemId, Rusty.Engine.Mechanics.ItemDefinition>();
        if (failAfterSeed)
        {
            itemDefinitions.Add(created.Item, new(Rusty.Engine.Mechanics.ItemDefinitionId.Parse(created.Item.Value),
                stackable ? Rusty.Engine.Mechanics.ItemKind.Fungible : Rusty.Engine.Mechanics.ItemKind.Unique,
                maximumQuantity: stackable ? ulong.MaxValue : 1));
            created = created with { Metadata = created.Metadata with { CurrentCondition = -1 } };
        }
        var coordinator = new WorldRpg.Kit.Inventory.MechanicsInventoryContainerCoordinator(state.InventoryStore,
            state.Actors.Entities, itemDefinitions);
        var ground = new WorldRpg.Rulesets.Daggerfall.Modules.Loot.DaggerfallGroundContainers(coordinator, state.ItemInstances,
            state.Actors.Player.Actor.Entity, identities, fixture.Inputs.ProfileKey);
        var before = state.InventoryStore.View(state.Actors.Player.Actor.Entity);
        Assert.ThrowsAny<Exception>(() => ground.CreateQuestItem(created, default, unique));
        Assert.Empty(ground.Persisted);
        Assert.Empty(identities.ReservedIdentities(WorldRpg.Kit.World.DurableIdentityKind.Container));
        Assert.Empty(unique.ReservedEntityIds);
        Assert.False(state.Actors.Entities.TryResolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, 9000000), out _));
        Assert.Equal(before.UniqueItems.Count, state.InventoryStore.View(state.Actors.Player.Actor.Entity).UniqueItems.Count);
        Assert.Equal(before.Stacks.Count, state.InventoryStore.View(state.Actors.Player.Actor.Entity).Stacks.Count);
    }

    [Fact]
    public void Bound_stack_tracks_partial_transfer_split_merge_and_consumption_through_encoded_restore()
    {
        var definitions = Definitions(stackable: true, gold: true);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var session = fixture.Session;
        var state = session.State;
        var started = Start(fixture, definitions);
        var selected = started.Resources.Single(value => value.SelectedItem is not null);
        session.State.Quests.RequestPlacement(started.InstanceId, "item", selected.Symbol, "location");
        session.State.Quests.AdmitPlacements(fixture.Inputs, session);
        DaggerfallQuestResourceBinding Binding() => state.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == selected.Symbol).Binding;
        var sourceBinding = Binding().Stacks.Single();
        var groundOwner = new DaggerfallItemOwner(sourceBinding.Owner.Scope, sourceBinding.Owner.Id);
        var source = Rusty.Engine.Mechanics.InventoryStackId.Parse(sourceBinding.StackId);
        var groundEntity = state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, checked((ulong)groundOwner.Id)));
        var groundInventory = state.Containers.Read(groundEntity);
        Assert.True(groundInventory.Stacks.Single(value => value.Id == source).Quantity > 2);
        var taken = Rusty.Engine.Mechanics.InventoryStackId.Parse("quest.taken");
        state.Containers.Transfer(groundEntity, state.Actors.Player.Actor.Entity, new(selected.SelectedItem!.Item, 2, source, taken));
        state.ItemInstances.TransferStack(groundOwner, DaggerfallItemOwner.Player, source, taken, sourceWasExhausted: false);
        Assert.Equal(2, Binding().Stacks.Length);
        using (var moved = fixture.Restore())
            Assert.Equal(Binding().Stacks, moved.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == selected.Symbol).Binding.Stacks);
        var split = Rusty.Engine.Mechanics.InventoryStackId.Parse("quest.split");
        state.ItemInstances.SplitStack(DaggerfallItemOwner.Player, state.Inventory, taken, split, 1);
        Assert.Equal(3, Binding().Stacks.Length);
        using (var divided = fixture.Restore())
            Assert.Equal(3, divided.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == selected.Symbol).Binding.Stacks.Length);
        state.ItemInstances.MergeStacks(DaggerfallItemOwner.Player, state.Inventory, split, taken);
        Assert.Equal(2, Binding().Stacks.Length);
        state.Inventory.Consume(new(taken, 2));
        state.ItemInstances.RemoveStack(DaggerfallItemOwner.Player, taken);
        Assert.Equal(sourceBinding, Binding().Stacks.Single());
        var remaining = state.Containers.Read(groundEntity).Stacks.Single(value => value.Id == source).Quantity;
        state.Actors.Store.Get<Rusty.Engine.Mechanics.InventoryComponent>(groundEntity).Consume(source, remaining);
        state.ItemInstances.RemoveStack(groundOwner, source);
        Assert.Equal(DaggerfallQuestResourceBindingKind.Item, Binding().Kind);
        Assert.Empty(Binding().Stacks);
        using var consumed = fixture.Restore();
        consumed.State.Quests.AdmitPlacements(fixture.Inputs, consumed);
        var resource = consumed.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == selected.Symbol);
        Assert.Empty(resource.Binding.Stacks);
        Assert.NotNull(resource.Text); // Actual selected meaning remains available to ending text.
        Assert.DoesNotContain(consumed.State.ItemInstances.StackItems, value => value.Metadata.QuestId == started.InstanceId);
    }

    [Fact]
    public void Bound_unique_item_retains_issued_identity_after_transfer_and_consumption_without_respawning()
    {
        var definitions = Definitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var session = fixture.Session;
        var state = session.State;
        var started = Start(fixture, definitions);
        var resource = started.Resources.Single(value => value.SelectedItem is not null);
        state.Quests.RequestPlacement(started.InstanceId, "item", resource.Symbol, "location");
        state.Quests.AdmitPlacements(fixture.Inputs, session);
        ulong id = state.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == resource.Symbol).Binding.UniqueItemIds.Single();
        var metadata = state.ItemInstances.RequireUnique(id);
        var ground = state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, checked((ulong)metadata.Owner.Id)));
        state.Containers.Transfer(ground, state.Actors.Player.Actor.Entity, new(resource.SelectedItem!.Item, 1, UniqueEntityId: state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Item, id)).Value));
        state.ItemInstances.MoveUnique(id, DaggerfallItemOwner.Player);
        using (var moved = fixture.Restore())
        {
            Assert.Equal(DaggerfallItemOwner.Player, moved.State.ItemInstances.RequireUnique(id).Owner);
            Assert.Contains(moved.State.Inventory.Read().UniqueItems, value => moved.State.Actors.Entities.IdentityOf(value.Entity).Value == id);
        }
        var carried = state.Inventory.Read().UniqueItems.Single(value => state.Actors.Entities.IdentityOf(value.Entity).Value == id);
        state.Inventory.Destroy(new(carried.Entity.Value, new(carried.Definition.Value)));
        state.ItemInstances.RemoveUnique(id);
        state.Actors.Entities.Destroy(new(WorldRpg.Kit.World.DurableIdentityKind.Item, id));
        session.RemoveUniqueItemIdentity(id);
        using var consumed = fixture.Restore();
        consumed.State.Quests.AdmitPlacements(fixture.Inputs, consumed);
        var bound = consumed.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == resource.Symbol);
        Assert.Equal(id, bound.Binding.UniqueItemIds.Single());
        Assert.False(consumed.State.ItemInstances.ContainsUnique(id));
        Assert.NotNull(bound.Text);
        Assert.Equal(WorldRpg.Kit.World.DurableIdentityClassification.Removed,
            consumed.State.Npcs.Identities!.Classify(new(WorldRpg.Kit.World.DurableIdentityKind.Item, id)));
        var saved = DaggerfallSavePayload.Read(consumed.CaptureSave());
        var quest = saved.Quests.Instances.Single();
        var forged = saved with { Quests = saved.Quests with { Instances = [quest with { Resources = quest.Resources.Select(value => value.Symbol == resource.Symbol
            ? value with { Binding = DaggerfallQuestResourceBinding.UniqueItem(ulong.MaxValue) } : value).ToArray() }] } };
        Assert.Throws<ArgumentException>(() => forged.ResolveRestore(definitions, fixture.Inputs, null));
    }

    [Fact]
    public void Bound_stack_in_actor_inventory_survives_site_unload_inactive_save_and_return()
    {
        var definitions = Definitions(stackable: true);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var session = fixture.Session;
        var state = session.State;
        var started = Start(fixture, definitions);
        var resource = started.Resources.Single(value => value.SelectedItem is not null);
        state.Quests.RequestPlacement(started.InstanceId, "item", resource.Symbol, "location");
        state.Quests.AdmitPlacements(fixture.Inputs, session);
        var bound = state.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == resource.Symbol).Binding.Stacks.Single();
        var sourceOwner = new DaggerfallItemOwner(bound.Owner.Scope, bound.Owner.Id);
        var groundEntity = state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, checked((ulong)sourceOwner.Id)));
        var source = Rusty.Engine.Mechanics.InventoryStackId.Parse(bound.StackId);
        var destination = Rusty.Engine.Mechanics.InventoryStackId.Parse("quest.actor-held");
        const long actorId = 2000;
        ulong quantity = state.Containers.Read(groundEntity).Stacks.Single(value => value.Id == source).Quantity;
        state.Containers.Transfer(groundEntity, state.Actors.Get(actorId).Actor.Entity, new(resource.SelectedItem!.Item, quantity, source, destination));
        state.ItemInstances.TransferStack(sourceOwner, DaggerfallItemOwner.Actor(actorId), source, destination, sourceWasExhausted: true);
        var profiles = new DaggerfallSiteProfiles([fixture.Inputs, fixture.Castle]);
        session.AdmitSiteProfiles(profiles);
        Assert.True(session.TryTransitionTo(fixture.Castle.ProfileKey));
        Assert.False(state.ItemInstances.ContainsStack(DaggerfallItemOwner.Actor(actorId), destination));
        var retained = state.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == resource.Symbol).Binding.Stacks.Single();
        Assert.Equal(new("actor", actorId), retained.Owner);
        using var inactive = fixture.Restore(profiles);
        Assert.Equal(retained, inactive.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == resource.Symbol).Binding.Stacks.Single());
        Assert.True(inactive.TryTransitionTo(fixture.Inputs.ProfileKey));
        var inventory = inactive.State.ActorInventories.InventoryFor(actorId)!;
        Assert.Equal(quantity, inventory.Read().Stacks.Single(value => value.Id == destination).Quantity);
        Assert.Equal(DaggerfallItemOwner.Actor(actorId), inactive.State.ItemInstances.RequireStack(DaggerfallItemOwner.Actor(actorId), destination).Owner);
        inventory.Consume(new(destination, quantity));
        inactive.State.ItemInstances.RemoveStack(DaggerfallItemOwner.Actor(actorId), destination);
        Assert.Empty(inactive.State.Quests.Capture().Instances.Single().Resources.Single(value => value.Symbol == resource.Symbol).Binding.Stacks);
        var saved = DaggerfallSavePayload.Read(inactive.CaptureSave());
        _ = saved.ResolveRestore(definitions, fixture.Inputs, profiles);
    }

    [Fact]
    public void Queued_foe_relocation_reuses_the_inactive_actor_inventory_and_stats_once_across_both_profiles()
    {
        var definitions = Definitions(stackable: true, secondPlace: true);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var session = fixture.Session;
        var state = session.State;
        var castle = WithMarker(fixture.Castle);
        var profiles = new DaggerfallSiteProfiles([fixture.Inputs, castle]);
        session.AdmitSiteProfiles(profiles);
        var origin = definitions.Locations.Records.Single(value => value.Id == fixture.Inputs.Site);
        var destination = definitions.Locations.Records.Single(value => value.Id == castle.Site);
        var started = state.Quests.Start(new("relocation", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(origin.Region, origin.Index)) with
                { PlaceSelection = new(fixture.Inputs.ProfileKind, origin.MapId, null, 0) }),
             new("destination", DaggerfallQuestResourceBinding.Place(new(destination.Region, destination.Index)) with
                { PlaceSelection = new(castle.ProfileKind, destination.MapId, null, 0) })], []));
        var foe = started.Resources.Single(value => value.SelectedFoe is not null);
        var item = started.Resources.Single(value => value.SelectedItem is not null);
        state.Quests.RequestPlacement(started.InstanceId, "foe", foe.Symbol, "location");
        state.Quests.RequestPlacement(started.InstanceId, "item", item.Symbol, "location");
        state.Quests.AdmitPlacements(fixture.Inputs, session);
        var admitted = state.Quests.Capture().Instances.Single();
        long id = admitted.Resources.Single(value => value.Symbol == foe.Symbol).Binding.ActorIds.Single();
        var actor = state.Actors.Get(id);
        actor.Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).SetCurrent(3, clamp: true);
        double health = actor.Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current;
        var stack = admitted.Resources.Single(value => value.Symbol == item.Symbol).Binding.Stacks.Single();
        var source = new DaggerfallItemOwner(stack.Owner.Scope, stack.Owner.Id);
        var owner = state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, checked((ulong)source.Id)));
        var sourceStack = Rusty.Engine.Mechanics.InventoryStackId.Parse(stack.StackId);
        var carried = Rusty.Engine.Mechanics.InventoryStackId.Parse("relocated.quest-stack");
        ulong quantity = state.Containers.Read(owner).Stacks.Single(value => value.Id == sourceStack).Quantity;
        state.Containers.Transfer(owner, actor.Actor.Entity, new(item.SelectedItem!.Item, quantity, sourceStack, carried));
        state.ItemInstances.TransferStack(source, DaggerfallItemOwner.Actor(id), sourceStack, carried, sourceWasExhausted: true);
        state.Quests.RequestPlacement(started.InstanceId, "relocate", foe.Symbol, "destination");
        Assert.True(session.TryTransitionTo(castle.ProfileKey));
        Assert.False(state.Actors.TryGet(id, out _));
        using var restored = fixture.Restore(profiles);
        var pending = restored.State.Quests.Capture().Instances.Single().Placements.Single(value => value.Id == "relocate");
        Assert.Null(pending.Applied);
        restored.State.Quests.AdmitPlacements(castle, restored);
        restored.State.Quests.AdmitPlacements(castle, restored);
        var moved = restored.State.Actors.Get(id);
        Assert.Equal(health, moved.Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current);
        Assert.Equal(castle.QuestMarkers.Single().Position, moved.Position);
        Assert.Equal(quantity, restored.State.ActorInventories.InventoryFor(id)!.Read().Stacks.Single(value => value.Id == carried).Quantity);
        var after = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Single(after.DynamicActors, value => value.EntityId == id);
        Assert.DoesNotContain(after.SiteDeltas.SelectMany(value => value.DynamicActors), value => value.EntityId == id);
        Assert.True(restored.TryTransitionTo(fixture.Inputs.ProfileKey));
        Assert.False(restored.State.Actors.TryGet(id, out _));
        using var inactive = DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition with { Profiles = profiles }, restored.CaptureSave());
        Assert.True(inactive.TryTransitionTo(castle.ProfileKey));
        inactive.State.Quests.AdmitPlacements(castle, inactive);
        Assert.Equal(health, inactive.State.Actors.Get(id).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current);
        Assert.Equal(quantity, inactive.State.ActorInventories.InventoryFor(id)!.Read().Stacks.Single(value => value.Id == carried).Quantity);
        Assert.Single(DaggerfallSavePayload.Read(inactive.CaptureSave()).DynamicActors, value => value.EntityId == id);
    }

    [Theory]
    [InlineData(false, "complete")]
    [InlineData(true, "complete")]
    [InlineData(false, "fail")]
    [InlineData(true, "fail")]
    [InlineData(false, "end")]
    [InlineData(true, "end")]
    public void Terminal_quest_clears_world_queue_and_only_removes_still_quest_items_carried_by_player(bool stackable, string ending)
    {
        var definitions = Definitions(stackable: stackable, gold: stackable, endSource: ending == "end");
        using var fixture = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0006", definitions: definitions, prepareInputs: WithMarker);
        var session = fixture.Session;
        var state = session.State;
        var started = Start(fixture, definitions);
        var item = started.Resources.Single(value => value.SelectedItem is not null);
        var foe = started.Resources.Single(value => value.SelectedFoe is not null);
        state.Quests.RequestPlacement(started.InstanceId, "item", item.Symbol, "location");
        state.Quests.RequestPlacement(started.InstanceId, "foe", foe.Symbol, "location");
        state.Quests.AdmitPlacements(fixture.Inputs, session);
        var admitted = state.Quests.Capture().Instances.Single();
        long actorId = admitted.Resources.Single(value => value.Symbol == foe.Symbol).Binding.ActorIds.Single();
        var physical = admitted.Resources.Single(value => value.Symbol == item.Symbol).Binding;
        var permanentIds = state.ItemInstances.UniqueItems.Where(value => value.Value.Owner == DaggerfallItemOwner.Player
            && value.Key != fixture.Source).Select(value => value.Key).ToArray();
        // Exercise the same Engine-owned equipment removal as ordinary item expiry.
        Assert.Equal(EquipmentMoveOutcome.Applied,
            session.EquipmentMoves.MoveToSlot(fixture.Item, new("right-hand")).Outcome);
        state.ItemInstances.ReplaceUnique(fixture.Source, state.ItemInstances.RequireUnique(fixture.Source) with
            { QuestId = started.InstanceId, QuestItemSymbol = item.Symbol });
        ulong carriedUnique = 0;
        var carriedStack = Rusty.Engine.Mechanics.InventoryStackId.Parse("terminal.player");
        var actorStack = Rusty.Engine.Mechanics.InventoryStackId.Parse("terminal.actor");
        if (stackable)
        {
            var stack = physical.Stacks.Single();
            var source = new DaggerfallItemOwner(stack.Owner.Scope, stack.Owner.Id);
            var ground = state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, checked((ulong)source.Id)));
            var sourceStack = Rusty.Engine.Mechanics.InventoryStackId.Parse(stack.StackId);
            state.Containers.Transfer(ground, state.Actors.Player.Actor.Entity, new(item.SelectedItem!.Item, 2, sourceStack, carriedStack));
            state.ItemInstances.TransferStack(source, DaggerfallItemOwner.Player, sourceStack, carriedStack, sourceWasExhausted: false);
            state.Containers.Transfer(ground, state.Actors.Get(actorId).Actor.Entity, new(item.SelectedItem.Item, 3, sourceStack, actorStack));
            state.ItemInstances.TransferStack(source, DaggerfallItemOwner.Actor(actorId), sourceStack, actorStack, sourceWasExhausted: false);
        }
        else
        {
            carriedUnique = physical.UniqueItemIds.Single();
            var metadata = state.ItemInstances.RequireUnique(carriedUnique);
            var ground = state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, checked((ulong)metadata.Owner.Id)));
            state.Containers.Transfer(ground, state.Actors.Player.Actor.Entity, new(item.SelectedItem!.Item, 1,
                UniqueEntityId: state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Item, carriedUnique)).Value));
            state.ItemInstances.MoveUnique(carriedUnique, DaggerfallItemOwner.Player);
        }
        state.Quests.RequestPlacement(started.InstanceId, "pending-at-end", foe.Symbol, "location");
        if (ending == "complete") state.Quests.Complete(started.InstanceId, "done");
        else if (ending == "fail") state.Quests.Fail(started.InstanceId, "failed");
        else
        {
            // The real task runner owns end-quest's two admitted passes and tombstone cleanup.
            for (int pass = 0; pass < 3; pass++) state.Quests.Advance(state.Variables, DaggerfallCalendar.Start);
        }
        var terminal = state.Quests.Capture().Instances.Single();
        Assert.NotEqual(DaggerfallQuestLifecycle.Active, terminal.Lifecycle);
        Assert.Empty(terminal.Placements);
        Assert.NotNull(terminal.Resources.Single(value => value.Symbol == item.Symbol).Text);
        Assert.Equal(actorId, terminal.Resources.Single(value => value.Symbol == foe.Symbol).Binding.ActorIds.Single());
        Assert.True(state.Actors.TryGet(actorId, out _));
        Assert.False(state.ItemInstances.ContainsUnique(fixture.Source));
        Assert.DoesNotContain(state.Equipment.Read().Assignments, value => value.Item.EntityId == fixture.Item.EntityId);
        Assert.DoesNotContain(state.Inventory.Read().UniqueItems, value => state.Actors.Entities.IdentityOf(value.Entity).Value == fixture.Source);
        Assert.All(permanentIds, id => Assert.True(state.ItemInstances.ContainsUnique(id)));
        if (stackable)
        {
            Assert.DoesNotContain(state.Inventory.Read().Stacks, value => value.Id == carriedStack);
            Assert.Equal((ulong)3, state.ActorInventories.InventoryFor(actorId)!.Read().Stacks.Single(value => value.Id == actorStack).Quantity);
            var remaining = terminal.Resources.Single(value => value.Symbol == item.Symbol).Binding.Stacks;
            Assert.Equal(2, remaining.Length);
            Assert.Contains(remaining, value => value.Owner.Scope == "ground");
            Assert.Contains(remaining, value => value.Owner == new DaggerfallItemOwnerSave("actor", actorId));
        }
        else
        {
            Assert.False(state.ItemInstances.ContainsUnique(carriedUnique));
            Assert.Equal(WorldRpg.Kit.World.DurableIdentityClassification.Removed,
                state.Npcs.Identities!.Classify(new(WorldRpg.Kit.World.DurableIdentityKind.Item, carriedUnique)));
        }
        using var restored = fixture.Restore();
        restored.State.Quests.AdmitPlacements(fixture.Inputs, restored);
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.Empty(restored.State.Quests.Capture().Instances.Single().Placements);
        Assert.False(restored.State.ItemInstances.ContainsUnique(fixture.Source));
        Assert.True(restored.State.Actors.TryGet(actorId, out _));
        Assert.All(permanentIds, id => Assert.True(restored.State.ItemInstances.ContainsUnique(id)));
    }

    [Fact]
    public void Quest_item_made_permanent_and_uncollected_world_items_survive_terminal_cleanup()
    {
        var definitions = Definitions(secondItem: true);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var session = fixture.Session;
        var state = session.State;
        var started = Start(fixture, definitions);
        var resources = started.Resources.Where(value => value.SelectedItem is not null).ToArray();
        Assert.Equal(2, resources.Length);
        foreach (var resource in resources) state.Quests.RequestPlacement(started.InstanceId, resource.Symbol, resource.Symbol, "location");
        state.Quests.AdmitPlacements(fixture.Inputs, session);
        var placed = state.Quests.Capture().Instances.Single();
        var first = placed.Resources.Single(value => value.Symbol == resources[0].Symbol);
        ulong carried = first.Binding.UniqueItemIds.Single();
        ulong left = placed.Resources.Single(value => value.Symbol == resources[1].Symbol).Binding.UniqueItemIds.Single();
        var owner = state.ItemInstances.RequireUnique(carried).Owner;
        var ground = state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, checked((ulong)owner.Id)));
        state.Containers.Transfer(ground, state.Actors.Player.Actor.Entity, new(first.SelectedItem!.Item, 1,
            UniqueEntityId: state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Item, carried)).Value));
        state.ItemInstances.MoveUnique(carried, DaggerfallItemOwner.Player);
        state.Quests.MakeItemPermanent(started.InstanceId, first.Symbol);
        state.Quests.Complete(started.InstanceId, "reward retained");
        Assert.Null(state.ItemInstances.RequireUnique(carried).QuestId);
        Assert.Equal(DaggerfallItemOwner.Player, state.ItemInstances.RequireUnique(carried).Owner);
        Assert.Equal("ground", state.ItemInstances.RequireUnique(left).Owner.Scope);
        Assert.Empty(state.Quests.Capture().Instances.Single().Placements);
        using var restored = fixture.Restore();
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.True(restored.State.ItemInstances.ContainsUnique(carried));
        Assert.Null(restored.State.ItemInstances.RequireUnique(carried).QuestId);
        Assert.Equal("ground", restored.State.ItemInstances.RequireUnique(left).Owner.Scope);
    }

    [Fact]
    public void Parent_cleanup_clears_an_applied_child_queue_even_when_encoded_restore_orders_child_first()
    {
        var definitions = Definitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var state = fixture.Session.State;
        var site = definitions.Locations.Records.Single(value => value.Id == fixture.Inputs.Site);
        DaggerfallQuestInstanceSave Instance(string id, string? parent = null) => new(id, "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with
                { PlaceSelection = new(fixture.Inputs.ProfileKind, site.MapId, null, 0) })], []) { ParentInstanceId = parent };
        state.Quests.Start(Instance("z-parent"));
        var child = state.Quests.Start(Instance("a-child", "z-parent"));
        var resource = child.Resources.Single(value => value.SelectedItem is not null);
        state.Quests.RequestPlacement(child.InstanceId, "child-item", resource.Symbol, "location");
        state.Quests.AdmitPlacements(fixture.Inputs, fixture.Session);
        ulong id = state.Quests.Capture().Instances.Single(value => value.InstanceId == child.InstanceId)
            .Resources.Single(value => value.Symbol == resource.Symbol).Binding.UniqueItemIds.Single();
        var owner = state.ItemInstances.RequireUnique(id).Owner;
        var ground = state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, checked((ulong)owner.Id)));
        state.Containers.Transfer(ground, state.Actors.Player.Actor.Entity, new(resource.SelectedItem!.Item, 1,
            UniqueEntityId: state.Actors.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Item, id)).Value));
        state.ItemInstances.MoveUnique(id, DaggerfallItemOwner.Player);
        using var restored = fixture.Restore();
        Assert.Equal("a-child", restored.State.Quests.Capture().Instances[0].InstanceId);
        restored.State.Quests.Complete("z-parent", "parent ended");
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        var ended = restored.State.Quests.Capture().Instances.Single(value => value.InstanceId == child.InstanceId);
        Assert.NotEqual(DaggerfallQuestLifecycle.Active, ended.Lifecycle);
        Assert.Empty(ended.Placements);
        Assert.False(restored.State.ItemInstances.ContainsUnique(id));
    }

    private static DaggerfallQuestInstanceSave Start(SanguineRoseSessionTests.Fixture fixture, DaggerfallDefinitions definitions)
    {
        var inputs = fixture.Inputs;
        var site = definitions.Locations.Records.Single(value => value.Id == inputs.Site);
        return fixture.Session.State.Quests.Start(new("world-test", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with
                { PlaceSelection = new(inputs.ProfileKind, site.MapId, null, 0) })], []));
    }

    internal static DaggerfallDefinitions Definitions(bool stackable = false, bool gold = false, bool secondPlace = false, bool endSource = false, bool secondItem = false,
        string[]? actions = null, bool person = false, bool atHome = false, bool rearmPlacement = false)
    {
        var root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        var declarations = root["questSources"]!["resources"]!["declarations"]!.AsArray();
        var foe = declarations.First(value => value!["kind"]!.GetValue<string>() == "foe"
            && value["targetSourceSpelling"]!.GetValue<string>() == "Giant_rat")!.DeepClone();
        var item = gold ? declarations.First(value => value!["targetCanonicalId"]?.GetValue<string>() == "gold")!.DeepClone()
            : declarations.Single(value => value!["sourceFile"]!.GetValue<string>() == (stackable ? "R0C11Y28.txt" : "S0000502.txt")
            && value["symbol"]!["canonicalId"]!.GetValue<string>() == (stackable ? "i.09" : "reward"))!.DeepClone();
        var place = declarations.First(value => value!["kind"]!.GetValue<string>() == "place")!.DeepClone();
        foreach (var row in new[] { foe, item, place })
        { row["sourceFile"] = "world-test.txt"; row["quest"] = "world-test"; declarations.Add(row); }
        place["symbol"]!["canonicalId"] = "location"; place["symbol"]!["sourceSpelling"] = "_location_";
        item["symbol"]!["canonicalId"] = "gift"; item["symbol"]!["sourceSpelling"] = "_gift_";
        foe["symbol"]!["canonicalId"] = "enemy"; foe["symbol"]!["sourceSpelling"] = "_enemy_";
        if (person)
        {
            var npc = declarations.First(value => value!["kind"]!.GetValue<string>() == "person" && value["person"]!["named"] is not null && value["person"]!["atHome"]!.GetValue<bool>())!.DeepClone();
            npc["quest"] = "world-test"; npc["sourceFile"] = "world-test.txt";
            npc["symbol"]!["canonicalId"] = "person"; npc["symbol"]!["sourceSpelling"] = "_person_";
            npc["person"]!["atHome"] = atHome; npc["person"]!["gender"] = "female";
            declarations.Add(npc);
        }
        if (secondItem)
        {
            var extraItem = item.DeepClone();
            extraItem["symbol"]!["canonicalId"] = "other-item"; extraItem["symbol"]!["sourceSpelling"] = "_other-item_";
            declarations.Add(extraItem);
        }
        if (secondPlace)
        {
            var destination = place.DeepClone();
            destination["symbol"]!["canonicalId"] = "destination"; destination["symbol"]!["sourceSpelling"] = "_destination_";
            declarations.Add(destination);
        }
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"world-test","displayName":"","sourceFile":"world-test.txt","disposition":"compiled","messages":[],"blocks":[],"diagnostics":[]}
            """));
        if (endSource)
            root["questSources"]!["quests"]!.AsArray().Last()!["blocks"] = JsonNode.Parse("""
                [{"kind":"headless","firstLine":1,"lines":["end quest"],"global":null}]
                """);
        if (actions is not null)
            root["questSources"]!["quests"]!.AsArray().Last()!["blocks"] = new JsonArray(new JsonObject
                { ["kind"] = "headless", ["firstLine"] = 1, ["lines"] = JsonSerializer.SerializeToNode(actions), ["global"] = null });
        if (rearmPlacement)
        {
            var blocks = root["questSources"]!["quests"]!.AsArray().Last()!["blocks"]!.AsArray();
            blocks.Insert(0, JsonNode.Parse("""
                {"kind":"task","firstLine":100,"lines":["until _stop_ performed:","start task headless.1"],"global":null}
                """));
            blocks.Insert(0, JsonNode.Parse("""
                {"kind":"variable","firstLine":99,"lines":["variable _stop_"],"global":null}
                """));
        }
        return DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    internal static DaggerfallSiteProfile WithMarker(DaggerfallSiteProfile source)
    {
        var site = TestPayload.Definitions.Locations.Records.Single(value => value.Id == source.Site);
        var blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
        var block = site.DungeonBlocks.First(value => blocks.QuestMarkers.TryGetValue(new(value.SourceKey, null), out var markers)
            && markers.Any(marker => marker.Kind == DaggerfallSiteMarkerKind.QuestSpawn));
        var marker = blocks.QuestMarkers[new(block.SourceKey, null)].First(value => value.Kind == DaggerfallSiteMarkerKind.QuestSpawn)
            with { BlockX = block.X, BlockZ = block.Z };
        return new(source.Project, source.SpatialArtifact,
        source.StaticMesh, source.WorldAppearance, source.InitialLook, source.Materials, source.ActorSprites, source.MobileSprites,
        source.Audio, source.ClassicPresentation, source.Site, source.Doors, source.ProfileKind, source.ProfileKey.LogicalId,
        source.Portals, source.Anchors.Values.ToArray(), source.Lights, source.GroundContainerSprite, source.DungeonMap,
        source.DungeonActions, source.DungeonActionModels, source.InteriorBuilding, source.Music, source.AudioBundle,
        [marker], source.BillboardSprites);
    }
}
