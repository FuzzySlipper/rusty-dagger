using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The guild owners' inputs without the session, over a <see cref="SocietyStage"/>: the guild membership and service
/// runtimes on its social state, the service transactions with a player purse, the dialogue revision, and the
/// admission and providers with provider availability, teleport offers and knightly house awards answered here.
/// </summary>
internal sealed class GuildStage : IDisposable
{
    internal GuildStage()
    {
        DaggerfallDefinitions definitions = Society.Definitions;
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
        InventoryStore store = new();
        EntityId owner = _entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("test.player"));
        store.RegisterInventory(new InventoryState(owner, [new InventoryCapacityLimit(DaggerfallEncumbrancePolicy.ClassicWeightMetric, ulong.MaxValue)]));
        InventoryComponent component = new(store, owner);
        _entities.Store.Add(owner, component);
        Inventory = new(component, _entities, items);
        DaggerfallCurrencyService currency = new(definitions, Inventory, Society.ItemInstances,
            new DaggerfallEncumbrancePolicy(Inventory, Society.Actors.Player.Stats), _unique);
        DaggerfallServiceTransactions transactions = new(Society.Npcs, Society.Social, Inventory, Society.ItemInstances, currency, _unique,
            () => DaggerfallCalendar.Start, () => Society.NpcSite);
        DaggerfallGuildMembershipPolicy policy = new(Society.Social, Society.SkillUses.PermanentSkillValue, DaggerfallConcreteGuildCatalog.AllMembershipPolicies);
        Membership = new(policy);
        Services = new(policy, Society.Npcs, transactions.ProviderAvailable);
        Admission = new(Membership, Society.Social, Society.SkillUses, Society.Crime, () => Revision);
        Providers = new(Services, Membership, new DaggerfallKnightlyOrderClaimRuntime(Services, new DaggerfallKnightlyOrderClaimState(null), Society.Random.Service),
            Admission, Society.Actors, Society.Effects, Society.Character, definitions, Society.Random.Service, _unique, Inventory, Society.ItemInstances,
            () => Society.Calendar, () => false,
            provider => Availability,
            provider => { Teleports.Add(provider); return new(true, DaggerfallGuildProviderDenial.None, "Select a destination."); },
            (faction, day, _) => { Houses.Add((faction, day)); return new(true, DaggerfallGuildProviderDenial.None, "A house."); });
    }

    internal SocietyStage Society { get; } = new();
    internal MechanicsInventoryCoordinator Inventory { get; }
    internal DaggerfallConcreteGuildMembershipRuntime Membership { get; }
    internal DaggerfallConcreteGuildServiceRuntime Services { get; }
    internal DaggerfallGuildAdmission Admission { get; }
    internal DaggerfallGuildProviders Providers { get; }
    internal string? Revision { get; set; } = "page:1";
    internal DaggerfallServiceDenial Availability { get; set; }
    internal List<DaggerfallServiceProvider> Teleports { get; } = [];
    internal List<(int Faction, int Day)> Houses { get; } = [];

    /// <summary>A guild hall of this faction, as the interior the player stands in.</summary>
    internal static DaggerfallInteriorBuilding Hall(int faction, int buildingType = 11) => new(0, 0, new DaggerfallRmbBuildingId("hall", 0), buildingType, faction);

    /// <summary>A guild NPC of this source faction at the stage's site, offering these services.</summary>
    internal DaggerfallNpc Provider(int faction, params string[] services)
    {
        DaggerfallNpc npc = new(501, DaggerfallNpcKind.Static, "provider", Society.NpcSite,
            new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, faction), "provider", services,
            DaggerfallNpcPresence.Active, null, null, null);
        Society.Npcs.Restore([npc]);
        return npc;
    }

    private readonly EntityDirectory _entities = new();
    private readonly DaggerfallUniqueItemAllocator _unique = new(1_000);

    public void Dispose()
    {
        _entities.Dispose();
        Society.Dispose();
    }
}
