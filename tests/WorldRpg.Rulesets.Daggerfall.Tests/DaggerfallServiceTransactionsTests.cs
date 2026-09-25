using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallServiceTransactionsTests
{
    [Fact]
    public void Commit_revalidates_changed_funds_rank_and_item_then_duplicate_submit_preserves_truth()
    {
        using Fixture f = new();
        DaggerfallServiceRequest funds = f.Request("funds");
        DaggerfallServiceQuote fundsQuote = Assert.IsType<DaggerfallServiceQuote>(f.Services.Quote(funds, f.MemberOnly, new(60)).Quote);
        f.Inventory.Consume(new InventoryConsume(InventoryStackId.Parse("coins"), 50));
        Assert.Equal(DaggerfallServiceDenial.InsufficientFunds, f.Services.Commit(fundsQuote).Denial);
        Assert.Equal(50UL, f.Currency.Read().Gold);

        f.GrantGold(100);
        DaggerfallServiceQuote rankQuote = Assert.IsType<DaggerfallServiceQuote>(f.Services.Quote(f.Request("rank"), f.MemberOnly, new(10)).Quote);
        _ = f.Social.DemoteGuild(15, 2);
        Assert.Equal(DaggerfallServiceDenial.InsufficientRank, f.Services.Commit(rankQuote).Denial);
        Assert.Equal(150UL, f.Currency.Read().Gold);
        _ = f.Social.PromoteGuild(15, 3);

        DaggerfallServiceRequest item = f.Request("item") with { Item = new(101, "iron-longsword", 1) };
        DaggerfallServiceQuote itemQuote = Assert.IsType<DaggerfallServiceQuote>(f.Services.Quote(item, f.MemberOnly, new(10)).Quote);
        f.Instances.ReplaceUnique(101, f.Instances.RequireUnique(101) with { CurrentCondition = 0 });
        Assert.Equal(DaggerfallServiceDenial.ItemChanged, f.Services.Commit(itemQuote).Denial);
        Assert.Equal(150UL, f.Currency.Read().Gold);

        f.Instances.ReplaceUnique(101, f.Instances.RequireUnique(101) with { CurrentCondition = 4 });
        DaggerfallServiceQuote deletedItemQuote = Assert.IsType<DaggerfallServiceQuote>(f.Services.Quote(
            f.Request("deleted-item") with { Item = new(101, "iron-longsword", 1) }, f.MemberOnly, new(10)).Quote);
        Rusty.Engine.Mechanics.UniqueInventoryItem held = Assert.Single(f.Inventory.Read().UniqueItems,
            candidate => f.Inventory.GetDurableItemId(candidate.Entity).Value == 101);
        f.Inventory.Destroy(new WorldRpg.Kit.Inventory.UniqueInventoryItem(held.Entity.Value, new InventoryItemId(held.Definition.Value)));
        Assert.Equal(DaggerfallServiceDenial.ItemChanged, f.Services.Commit(deletedItemQuote).Denial);
        Assert.Equal(150UL, f.Currency.Read().Gold);

        DaggerfallServiceQuote accepted = Assert.IsType<DaggerfallServiceQuote>(f.Services.Quote(f.Request("accepted"), f.MemberOnly, new(10)).Quote);
        Assert.True(f.Services.Commit(accepted).Accepted);
        Assert.Equal(140UL, f.Currency.Read().Gold);
        Assert.Equal(DaggerfallServiceDenial.AlreadySubmitted, f.Services.Commit(accepted).Denial);
        Assert.Equal(140UL, f.Currency.Read().Gold);
    }

    [Fact]
    public void Commit_publishes_payment_grant_and_pending_work_as_one_actual_service_result()
    {
        using Fixture f = new();
        DaggerfallServiceRequest request = f.Request("repair-101");
        InventoryStackId stack = InventoryStackId.Parse("service.award");
        // A service stack grant must use a definition that the Engine admits as fungible.
        // Template 287 is a unique map; template 277 is the stackable book template.
        DaggerfallItemDefinition award = f.Definitions.RequireItem(new DaggerfallItemId("template-277"));
        DaggerfallServiceGrant grant = new(new InventoryAtomicGrant(new InventoryItemId(award.Id.Value), 2, Stack: stack),
            DaggerfallItemInstanceMetadata.Default(award, DaggerfallItemOwner.Player));
        DaggerfallServiceQueuedWork work = new(request.Id, request.Provider.Service, request.Provider, 360);

        DaggerfallServiceQuote quote = Assert.IsType<DaggerfallServiceQuote>(f.Services.Quote(request, f.MemberOnly, new(25), [grant], work).Quote);
        DaggerfallServiceOutcome outcome = f.Services.Commit(quote);

        Assert.True(outcome.Accepted);
        Assert.Equal(75UL, f.Currency.Read().Gold);
        Assert.Contains(f.Inventory.Read().Stacks, value => value.Id == stack && value.Quantity == 2);
        Assert.Equal(DaggerfallItemOwner.Player, f.Instances.RequireStack(DaggerfallItemOwner.Player, stack).Owner);
        Assert.Equal(work, Assert.Single(f.Services.Capture().Pending));
        DaggerfallServiceTransactions restored = new(f.Npcs, f.Social, f.Inventory, f.Instances, f.Currency, f.Unique,
            () => f.Calendar, () => f.CurrentSite, f.Services.Capture());
        Assert.Equal(work, Assert.Single(restored.Pending));
        Assert.Equal(DaggerfallServiceDenial.AlreadySubmitted, restored.Commit(quote).Denial);
        Assert.Equal(75UL, f.Currency.Read().Gold);
    }

    [Fact]
    public void Commit_admits_only_allocator_issued_unique_grants_and_preserves_their_restorable_ledger_identity()
    {
        using Fixture f = new();
        DaggerfallItemDefinition sword = f.Definitions.RequireItem(new DaggerfallItemId("iron-longsword"));
        DurableIdentityReference granted = f.Unique.AllocateReference();
        DaggerfallServiceGrant grant = new(new InventoryAtomicGrant(new InventoryItemId(sword.Id.Value), UniqueItem: granted),
            DaggerfallItemInstanceMetadata.Default(sword, DaggerfallItemOwner.Player));

        DaggerfallServiceQuote quote = Assert.IsType<DaggerfallServiceQuote>(f.Services.Quote(
            f.Request("unique-award"), f.MemberOnly, new(10), [grant]).Quote);
        Assert.True(f.Services.Commit(quote).Accepted);
        Assert.Contains(f.Inventory.Read().UniqueItems,
            item => f.Inventory.GetDurableItemId(item.Entity) == granted && item.Definition.Value == sword.Id.Value);
        Assert.Equal(DaggerfallItemOwner.Player, f.Instances.RequireUnique(granted.Value).Owner);

        DaggerfallUniqueItemAllocator restoredLedger = DaggerfallUniqueItemAllocator.Restore(f.Unique.CaptureState());
        DaggerfallServiceTransactions restored = new(f.Npcs, f.Social, f.Inventory, f.Instances, f.Currency, restoredLedger,
            () => f.Calendar, () => f.CurrentSite);
        DaggerfallServiceGrant forged = grant with
        {
            Grant = new InventoryAtomicGrant(new InventoryItemId(sword.Id.Value), UniqueItem: new(DurableIdentityKind.Item, granted.Value + 100)),
        };
        Assert.Equal(DaggerfallServiceDenial.GrantUnavailable,
            restored.Quote(f.Request("forged-unique"), f.MemberOnly, new(1), [forged]).Outcome.Denial);
    }

    [Fact]
    public void Quote_requires_live_funds_and_site_and_pending_work_only_completes_at_its_provider_after_due_time()
    {
        using Fixture f = new();
        Assert.Equal(DaggerfallServiceDenial.InsufficientFunds,
            f.Services.Quote(f.Request("too-expensive"), f.MemberOnly, new(101)).Outcome.Denial);

        f.CurrentSite = new DaggerfallNpcSite(0, "Wayrest", string.Empty);
        Assert.Equal(DaggerfallServiceDenial.SiteChanged,
            f.Services.Quote(f.Request("away"), f.MemberOnly, new(1)).Outcome.Denial);
        f.CurrentSite = f.Provider.Site;

        DaggerfallServiceRequest request = f.Request("ready-later");
        DaggerfallServiceQueuedWork work = new(request.Id, request.Provider.Service, request.Provider, 700);
        DaggerfallServiceQuote quote = Assert.IsType<DaggerfallServiceQuote>(f.Services.Quote(request, f.MemberOnly, new(0), queuedWork: work).Quote);
        Assert.True(f.Services.Commit(quote).Accepted);
        Assert.Equal(DaggerfallServiceDenial.WorkNotReady, f.Services.CompletePending(request.Id).Denial);
        f.Calendar = new(405, 5, 0, 11, 40, 0);
        f.CurrentSite = new DaggerfallNpcSite(0, "Wayrest", string.Empty);
        Assert.Equal(DaggerfallServiceDenial.WorkUnavailable, f.Services.CompletePending(request.Id).Denial);
        f.CurrentSite = f.Provider.Site;
        Assert.True(f.Services.CompletePending(request.Id).Accepted);
        Assert.Empty(f.Services.Pending);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly EntityDirectory Entities = new();
        internal readonly DaggerfallDefinitions Definitions;
        internal readonly MechanicsInventoryCoordinator Inventory;
        internal readonly DaggerfallItemInstances Instances = new();
        internal readonly DaggerfallUniqueItemAllocator Unique = new(1_000);
        internal readonly DaggerfallEncumbrancePolicy Load;
        internal readonly DaggerfallCurrencyService Currency;
        internal readonly DaggerfallSocialState Social;
        internal readonly DaggerfallNpcRegistry Npcs = new();
        internal readonly DaggerfallServiceTransactions Services;
        internal DaggerfallCalendar Calendar = new(405, 5, 0, 10, 0, 0);
        internal DaggerfallNpcSite CurrentSite;
        internal readonly DaggerfallServiceEligibility MemberOnly = new(true, 1, 15, 8, 18);
        private readonly DaggerfallServiceProvider _provider = new(501, new DaggerfallNpcSite(0, "Daggerfall", "smith"), "repair");
        internal DaggerfallServiceProvider Provider => _provider;

        internal Fixture()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
            Definitions = TestPayload.Definitions;
            Dictionary<InventoryItemId, ItemDefinition> items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            InventoryStore store = new();
            EntityId owner = Entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("test.player"));
            store.RegisterInventory(new InventoryState(owner, [new InventoryCapacityLimit(DaggerActorFactory.ClassicWeightMetric, ulong.MaxValue)]));
            InventoryComponent component = new(store, owner);
            Entities.Store.Add(owner, component);
            Inventory = new(component, Entities, items);
            StatsComponent stats = new();
            stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value), new Stat(100, 0, 100, quantum: 1,
                rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero));
            Load = new(Inventory, stats);
            Currency = new(Definitions, Inventory, Instances, Load, Unique);
            Social = new(Definitions.Factions);
            _ = Social.JoinGuild(15, 0);
            _ = Social.PromoteGuild(15, 1);
            Npcs.Restore([new DaggerfallNpc(501, DaggerfallNpcKind.Static, "smith", _provider.Site,
                new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, 15), "smith", ["repair"], DaggerfallNpcPresence.Active, null, null, null)]);
            CurrentSite = _provider.Site;
            Services = new(Npcs, Social, Inventory, Instances, Currency, Unique, () => Calendar, () => CurrentSite);
            GrantGold(100);
            Inventory.GrantAtomic([new InventoryAtomicGrant(new InventoryItemId("iron-longsword"), UniqueItem: new(DurableIdentityKind.Item, 101))]);
            Instances.RegisterUnique(101, new DaggerfallItemInstanceMetadata("iron-longsword", "iron", 0, 4, 4,
                true, false, null, null, null, DaggerfallItemOwner.Player));
        }

        internal DaggerfallServiceRequest Request(string id) => new(id, _provider);

        internal void GrantGold(ulong amount)
        {
            DaggerfallItemDefinition gold = Definitions.RequireItem(new DaggerfallItemId("template-276"));
            InventoryStackId stack = InventoryStackId.Parse("coins");
            Inventory.Grant(new InventoryGrant(new InventoryItemId(gold.Id.Value), stack, amount));
        }

        public void Dispose() => Entities.Dispose();
    }
}
