using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A quest's items without the session: the player with a real Kit pack and equipment, item instances, the quest
/// custody over them, the ground piles of an exterior site, and the quests of <c>world-test.txt</c>
/// (<see cref="QuestWorldAdmissionTests.Definitions"/>), with its gift declared.
/// </summary>
internal sealed class QuestItemStage : IDisposable
{
    internal QuestItemStage()
    {
        DaggerfallActorDefinition player = Definitions.RequireActor(new DaggerfallActorId("player"));
        PlayerActorState actor = Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId(player.Id.Value),
            new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, Definitions.Catalogs.RequireCareer("class00"))),
            "health", DaggerActorFactory.PlayerCapabilities);
        PlayerEntity = actor.Actor.Entity;
        Store.RegisterInventory(new InventoryState(PlayerEntity, [new InventoryCapacityLimit(DaggerfallEncumbrancePolicy.ClassicWeightMetric, ulong.MaxValue)]));
        Store.RegisterEquipment(new EquipmentState(PlayerEntity));
        InventoryComponent pack = new(Store, PlayerEntity);
        EquipmentComponent worn = new(Store, PlayerEntity);
        actor.Actor.Add(pack);
        actor.Actor.Add(worn);
        Dictionary<InventoryItemId, ItemDefinition> items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
        Dictionary<WorldRpg.Kit.Inventory.EquipmentSlotId, EquipmentSlotDefinition> slots = Definitions.EquipmentSlots.Values
            .ToDictionary(slot => new WorldRpg.Kit.Inventory.EquipmentSlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
        MechanicsInventoryCoordinator inventory = new(pack, Actors.Entities, items);
        MechanicsEquipmentCoordinator equipment = new(pack, worn, Actors.Entities, items, slots);
        Containers = new(Store, Actors.Entities, items);
        Quests = new(Definitions, RandomMinimum.Create());
        QuestItems = new(Quests, Actors.Entities, Store, Containers, equipment, Instances, _ => true, () => { }, Identities, UniqueItems,
            owner => owner == DaggerfallItemOwner.Player ? PlayerEntity : throw new InvalidOperationException($"No owner {owner} in this stage."),
            _ => throw new InvalidOperationException("No unique item is destroyed in this stage."),
            (_, _) => throw new InvalidOperationException("No stack is consumed in this stage."),
            new DaggerfallEquipmentMoves(inventory, equipment, Definitions, itemInstances: Instances));
        Quests.BindItems(QuestItems);
        Site = Definitions.Locations.Records.First(site => site.Exterior is not null);
        Ground = new(Containers, Instances, PlayerEntity, Identities, DaggerfallWorldProfileIds.Exterior(Site.Id));
    }

    internal DaggerfallDefinitions Definitions { get; } = QuestWorldAdmissionTests.Definitions();
    internal ActorsState Actors { get; } = new();
    internal EntityId PlayerEntity { get; }
    internal InventoryStore Store { get; } = new();
    internal MechanicsInventoryContainerCoordinator Containers { get; }
    internal DaggerfallItemInstances Instances { get; } = new();
    internal DurableIdentityAllocator Identities { get; } = new(DurableIdentityKind.Container, 100);
    internal DaggerfallUniqueItemAllocator UniqueItems { get; } = new(DaggerfallUniqueItemAllocator.DefaultFirstEntityId);
    internal DaggerfallQuestInstances Quests { get; }
    internal DaggerfallQuestItems QuestItems { get; }
    internal DaggerfallSiteRecord Site { get; }
    internal DaggerfallGroundContainers Ground { get; }
    internal PlayerControlState Player { get; } = new(null, 0f, 0f);

    /// <summary>Starts <c>world-test.txt</c> with its gift selected as a created item of <paramref name="template"/>, still unplaced.</summary>
    internal DaggerfallQuestRuntimeInstance StartWithGift(int template = 132)
    {
        DaggerfallCreatedItem gift = new DaggerfallItemFactory(Definitions, RandomMinimum.Create())
            .Create(new("MiscItems", "quest.gift", DaggerfallItemOwner.Player, TemplateIndex: template, QuestId: "items", QuestSymbol: "gift"));
        DaggerfallQuestInstanceSave started = Quests.Start(new("items", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(Site.Id.Region, Site.Id.Index))),
             new("gift", DaggerfallQuestResourceBinding.Pending()) { SelectedItem = gift }], []));
        return new(started, DaggerfallQuestTaskCompiler.Compile(Definitions.QuestSources.Resolve(started.SourceFile)));
    }

    public void Dispose() => Actors.Dispose();
}
