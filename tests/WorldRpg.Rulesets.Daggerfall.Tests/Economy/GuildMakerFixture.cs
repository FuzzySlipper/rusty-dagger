using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.World;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The guild makers' inputs built over real Kit inventories, without the session: the player's pack
/// and equipment, a wagon, the item instances, a guild's provider and the paid service transaction.
/// </summary>
internal sealed class GuildMakerFixture : IDisposable
{
    private readonly DaggerfallNpcSite _site = new(0, "Daggerfall", "guild");
    private readonly IRandomService _random = RandomMinimum.Create();

    internal EntityDirectory Entities { get; } = new();
    internal DaggerfallDefinitions Definitions { get; } = TestPayload.Definitions;
    internal DaggerfallItemInstances Instances { get; } = new();
    internal DaggerfallUniqueItemAllocator UniqueItems { get; } = new(5_000);
    internal DaggerfallNpcRegistry Npcs { get; } = new();
    internal DaggerfallSocialState Social { get; }
    internal MechanicsInventoryCoordinator Inventory { get; }
    internal MechanicsEquipmentCoordinator Equipment { get; }
    internal MechanicsInventoryContainerCoordinator Containers { get; }
    internal DaggerfallWagonStorage Wagon { get; }
    internal DaggerfallCurrencyService Currency { get; }
    internal DaggerfallServiceTransactions Services { get; }
    internal DaggerfallConcreteGuildServiceRuntime GuildServices { get; }
    internal DaggerfallItemConditionService Conditions { get; }
    internal DaggerfallCalendar Calendar { get; } = DaggerfallCalendar.Start;

    internal GuildMakerFixture()
    {
        var items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
        var slots = Definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
        InventoryStore world = new();
        EntityId player = Entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("test.player"));
        world.RegisterInventory(new InventoryState(player, [new InventoryCapacityLimit(DaggerActorFactory.ClassicWeightMetric, ulong.MaxValue)]));
        world.RegisterEquipment(new EquipmentState(player));
        InventoryComponent pack = new(world, player);
        EquipmentComponent worn = new(world, player);
        Entities.Store.Add(player, pack);
        Entities.Store.Add(player, worn);
        Inventory = new(pack, Entities, items);
        Equipment = new(pack, worn, Entities, items, slots);
        Containers = new(world, Entities, items);
        Wagon = new(Containers, Instances, Definitions, player, new DurableIdentityAllocator(DurableIdentityKind.Container, 1),
            DaggerfallTuning.Defaults.Transport);

        DaggerfallActorDefinition actor = Definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(actor,
            DaggerfallPlayerVitals.Initial(actor.Stats, Definitions.Catalogs.RequireCareer("class00")));
        Social = new(Definitions.Factions);
        Currency = new(Definitions, Inventory, Instances, new DaggerfallEncumbrancePolicy(Inventory, stats), UniqueItems);
        Services = new(Npcs, Social, Inventory, Instances, Currency, UniqueItems, () => Calendar, () => _site);
        GuildServices = new(new DaggerfallGuildMembershipPolicy(Social, _ => 100, DaggerfallConcreteGuildCatalog.AllMembershipPolicies),
            Npcs, Services);
        DaggerfallEquipmentMoves moves = new(Inventory, Equipment, Definitions, itemInstances: Instances);
        Conditions = new(Definitions, Instances, moves, Inventory, () => new DaggerfallSoulGems(Inventory, Instances, Definitions.Magic, UniqueItems));
    }

    internal DaggerfallItemMaker ItemMaker() => new(Definitions, Npcs, GuildServices, Services, Inventory, Instances, Conditions,
        new DaggerfallSoulGems(Inventory, Instances, Definitions.Magic, UniqueItems), UniqueItems, () => Calendar);

    internal DaggerfallPotionMaking PotionMaker() => new(Definitions, Npcs, GuildServices, Inventory, Containers, Wagon, Instances,
        _random, () => Calendar);

    /// <summary>A guild member of <paramref name="rank"/> talking to a provider of <paramref name="service"/>.</summary>
    internal DaggerfallServiceProvider Provider(string service, int guild, int providerFaction, int rank)
    {
        Social.JoinGuild(guild, 0);
        for (int step = 0; step < rank; step++) Social.PromoteGuild(guild, 0);
        Npcs.Restore([new DaggerfallNpc(501, DaggerfallNpcKind.Static, "maker." + service, _site,
            new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, providerFaction), "maker", ["talk", service],
            DaggerfallNpcPresence.Active, null, null, null)]);
        return new(501, _site, service);
    }

    internal void GrantGold(ulong amount)
    {
        DaggerfallItemFactory factory = new(Definitions, _random);
        factory.Materialize(factory.Create(new("Currency", "maker.gold", DaggerfallItemOwner.Player, Quantity: amount, TemplateIndex: 276)),
            Inventory, Instances, InventoryStackId.Parse("maker.gold"));
    }

    internal (UniqueItem Item, ulong Id) Weapon()
    {
        DaggerfallItemDefinition definition = Definitions.RequireItem(new("template-121-daedric"));
        DurableIdentityReference id = UniqueItems.AllocateReference();
        UniqueItem item = Equipment.Materialize(id, new(definition.Id.Value));
        Instances.RegisterDefaultUnique(id.Value, definition, DaggerfallItemOwner.Player);
        return (item, id.Value);
    }

    /// <summary>One ingredient of <paramref name="template"/> in the pack, or in the wagon.</summary>
    internal void Ingredient(int template, bool inWagon = false)
    {
        DaggerfallItemOwner owner = inWagon ? DaggerfallItemOwner.Wagon(Wagon.EnsureCreated().Id) : DaggerfallItemOwner.Player;
        DaggerfallItemTemplateDefinition definition = Definitions.ItemTemplateCatalog.Templates[template];
        string key = $"ingredient.{owner.Scope}.{template}";
        DaggerfallCreatedItem item = new DaggerfallItemFactory(Definitions, _random)
            .Create(new(definition.Groups[0], key, owner, Quantity: 1, TemplateIndex: template));
        InventoryStackId stack = InventoryStackId.Parse(key);
        Containers.Seed(inWagon ? Wagon.Current!.Owner : Inventory.Component.Owner, [new(item.Item, 1, Stack: stack)]);
        Instances.RegisterStack(owner, stack, item.Metadata);
    }

    public void Dispose() => Entities.Dispose();
}
