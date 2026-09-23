using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallEncumbranceCurrencyTests
{
    [Fact]
    public void Classic_coin_units_preserve_coin_weight_and_strength_boundary()
    {
        using Fixture fixture = new(strength: 1);
        DaggerfallItemDefinition gold = fixture.Definitions.RequireItem(new DaggerfallItemId("gold-piece"));
        fixture.Inventory.Grant(new(new("gold-piece"), InventoryStackId.Parse("coins"), 400));

        Assert.Equal(1, fixture.Load.WeightClassicUnits(gold));
        Assert.Equal(400, fixture.Load.Read().CurrentClassicUnits);
        Assert.Equal(400, fixture.Load.Read().MaximumClassicUnits);
        Assert.True(fixture.Load.Read().CanMove);

        fixture.Inventory.Grant(new(new("gold-piece"), InventoryStackId.Parse("coins"), 1));
        Assert.False(fixture.Load.Read().CanMove);
        Assert.False(fixture.Load.CanCarry(gold, 1));
    }

    [Fact]
    public void Gold_and_letters_conserve_inventory_and_account_values()
    {
        using Fixture fixture = new(strength: 100);
        DaggerfallCurrencyService currency = new(fixture.Definitions, fixture.Inventory, fixture.Instances, fixture.Load,
            fixture.Unique, new DaggerfallCurrencySave(200, 1));

        Assert.True(currency.WithdrawGold(100));
        Assert.Equal(100UL, currency.Read().Gold);
        Assert.True(currency.DepositGold(100));
        Assert.Equal(200UL, currency.Read().AccountGold);

        Assert.True(currency.WithdrawLetter(100));
        Assert.Equal(99UL, currency.Read().AccountGold); // 100 plus the donor's truncated 1% commission.
        Assert.Equal(100UL, currency.Read().LettersOfCredit);
        Rusty.Engine.Mechanics.UniqueInventoryItem letter = Assert.Single(fixture.Inventory.Read().UniqueItems);
        DaggerfallItemInstanceMetadata metadata = fixture.Instances.RequireUnique(fixture.Inventory.GetDurableItemId(letter.Entity).Value);
        Assert.Equal(100UL, metadata.CreditValue);
        Assert.Equal(100UL, DaggerfallItemInstanceMetadata.Restore("template-275", metadata.Capture()).CreditValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallItemInstanceMetadata.Restore("template-275", metadata.Capture() with { CreditValue = null }));
        ulong letterIdentity = fixture.Inventory.GetDurableItemId(letter.Entity).Value;
        Assert.True(currency.DepositLetters());
        Assert.Equal(199UL, currency.Read().AccountGold);
        Assert.Equal(0UL, currency.Read().LettersOfCredit);
        Assert.Empty(fixture.Inventory.Read().UniqueItems);
        Assert.Contains(letterIdentity, fixture.Unique.RemovedEntityIds);
        Assert.Contains(fixture.Unique.CaptureState().Kinds.Single(kind => kind.Kind == DurableIdentityKind.Item).Removed, id => id == letterIdentity);
        Assert.Equal(new DaggerfallCurrencySave(199, 2), currency.Capture());
    }

    [Fact]
    public void Letter_withdrawal_at_capacity_refuses_before_creating_an_engine_unique_item()
    {
        using Fixture fixture = new(strength: 1);
        fixture.Inventory.Grant(new(new("gold-piece"), InventoryStackId.Parse("capacity.coins"), 400));
        DaggerfallCurrencyService currency = new(fixture.Definitions, fixture.Inventory, fixture.Instances, fixture.Load,
            fixture.Unique, new DaggerfallCurrencySave(200, 1));

        Assert.False(currency.WithdrawLetter(100));
        Assert.Equal(200UL, currency.Read().AccountGold);
        Assert.Empty(fixture.Inventory.Read().UniqueItems);
        Assert.Equal(1_000UL, fixture.Unique.NextEntityId);
    }

    [Fact]
    public void Currency_account_overflow_refuses_before_consuming_canonical_coins_or_letters()
    {
        using Fixture fixture = new(strength: 100);
        fixture.Inventory.Grant(new(new("template-276"), InventoryStackId.Parse("template.coins"), 25));
        DaggerfallCurrencyService fullAccount = new(fixture.Definitions, fixture.Inventory, fixture.Instances, fixture.Load,
            fixture.Unique, new DaggerfallCurrencySave(ulong.MaxValue, 1));

        Assert.Equal(25UL, fullAccount.Read().Gold);
        Assert.False(fullAccount.DepositGold(25));
        Assert.Equal(25UL, fullAccount.Read().Gold);
        Assert.Equal(ulong.MaxValue, fullAccount.Read().AccountGold);

        DaggerfallCurrencyService letterIssuer = new(fixture.Definitions, fixture.Inventory, fixture.Instances, fixture.Load,
            fixture.Unique, new DaggerfallCurrencySave(200, 2));
        Assert.True(letterIssuer.WithdrawLetter(100));
        Rusty.Engine.Mechanics.UniqueInventoryItem letter = Assert.Single(fixture.Inventory.Read().UniqueItems);
        ulong identity = fixture.Inventory.GetDurableItemId(letter.Entity).Value;

        Assert.False(fullAccount.DepositLetters());
        Assert.Equal(ulong.MaxValue, fullAccount.Read().AccountGold);
        Assert.Equal(identity, fixture.Inventory.GetDurableItemId(Assert.Single(fixture.Inventory.Read().UniqueItems).Entity).Value);
        Assert.Equal(100UL, fixture.Instances.RequireUnique(identity).CreditValue);
    }

    [Fact]
    public void Gold_uses_template_identity_and_never_overfills_an_existing_stack()
    {
        using Fixture fixture = new(strength: 700_000_000, maximumStrength: 700_000_000);
        DaggerfallItemDefinition templateGold = fixture.Definitions.RequireItem(new DaggerfallItemId("template-276"));
        fixture.Inventory.Grant(new(new("template-276"), InventoryStackId.Parse("full.coins"), templateGold.MaximumQuantity));
        DaggerfallCurrencyService currency = new(fixture.Definitions, fixture.Inventory, fixture.Instances, fixture.Load,
            fixture.Unique, new DaggerfallCurrencySave(1, 1));

        Assert.True(currency.WithdrawGold(1));
        Assert.Equal(templateGold.MaximumQuantity, fixture.Inventory.Read().Stacks.Single(stack => stack.Id.Value == "full.coins").Quantity);
        Assert.Equal(templateGold.MaximumQuantity + 1, currency.Read().Gold);
        Assert.Equal(1UL, fixture.Inventory.Read().Stacks.Single(stack => stack.Id.Value == "daggerfall.currency.gold.1").Quantity);
        Assert.Equal(0UL, currency.Read().AccountGold);
    }

    [Fact]
    public void Gold_stack_identifier_collision_at_the_numeric_boundary_refuses_without_mutation()
    {
        using Fixture fixture = new(strength: 700_000_000, maximumStrength: 700_000_000);
        DaggerfallItemDefinition gold = fixture.Definitions.RequireItem(new DaggerfallItemId("template-276"));
        InventoryStackId occupied = InventoryStackId.Parse($"daggerfall.currency.gold.{ulong.MaxValue - 1}");
        fixture.Inventory.Grant(new(new("template-276"), occupied, gold.MaximumQuantity));
        DaggerfallCurrencyService currency = new(fixture.Definitions, fixture.Inventory, fixture.Instances, fixture.Load,
            fixture.Unique, new DaggerfallCurrencySave(1, ulong.MaxValue - 1));

        Assert.False(currency.WithdrawGold(1));
        Assert.Equal(1UL, currency.Read().AccountGold);
        Assert.Equal(gold.MaximumQuantity, fixture.Inventory.Read().Stacks.Single(stack => stack.Id == occupied).Quantity);
        Assert.Equal(ulong.MaxValue - 1, currency.Capture().NextGoldStack);
    }

    [Fact]
    public void Remote_owners_are_excluded_and_large_coin_quantities_do_not_overflow_load_math()
    {
        using Fixture fixture = new(strength: 1);
        fixture.Inventory.Grant(new(new("gold-piece"), InventoryStackId.Parse("coins"), 1_000_000_000));

        Assert.Equal(1_000_000_000L, fixture.Load.Read().CurrentClassicUnits);
        Assert.False(fixture.Load.Read().CanMove);
        Assert.Equal(0L, fixture.Load.WeightForOwner(DaggerfallItemOwner.Wagon(1), fixture.Inventory.Read()));
        Assert.Equal(0L, fixture.Load.WeightForOwner(DaggerfallItemOwner.Actor(42), fixture.Inventory.Read()));
    }

    [Fact]
    public void Imported_stackability_matches_the_donor_item_formula_categories()
    {
        using Fixture fixture = new(strength: 1);
        Assert.True(fixture.Definitions.ItemTemplateCatalog.Templates[83].Stackable);  // potion bottle
        Assert.True(fixture.Definitions.ItemTemplateCatalog.Templates[131].Stackable); // arrow
        Assert.True(fixture.Definitions.ItemTemplateCatalog.Templates[252].Stackable); // oil
        Assert.True(fixture.Definitions.ItemTemplateCatalog.Templates[276].Stackable); // gold
        Assert.False(fixture.Definitions.ItemTemplateCatalog.Templates[275].Stackable); // letter of credit
    }

    [Fact]
    public void Classic_non_encumbering_transport_maps_and_arrows_remain_zero_cost_in_the_engine_metric()
    {
        using Fixture fixture = new(strength: 1);
        Assert.Equal(0L, fixture.Load.WeightClassicUnits(fixture.Definitions.RequireItem(new DaggerfallItemId("arrow"))));
        Assert.Equal(0L, fixture.Load.WeightClassicUnits(fixture.Definitions.RequireItem(new DaggerfallItemId("template-131"))));
        Assert.Equal(0L, fixture.Load.WeightClassicUnits(fixture.Definitions.RequireItem(new DaggerfallItemId("template-93"))));
        Assert.Equal(0L, fixture.Load.WeightClassicUnits(fixture.Definitions.RequireItem(new DaggerfallItemId("template-287"))));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly EntityDirectory Entities = new();
        internal readonly DaggerfallDefinitions Definitions;
        internal readonly MechanicsInventoryCoordinator Inventory;
        internal readonly DaggerfallItemInstances Instances = new();
        internal readonly DaggerfallUniqueItemAllocator Unique = new(1_000);
        internal readonly DaggerfallEncumbrancePolicy Load;

        internal Fixture(int strength, int maximumStrength = 10_000)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) directory = directory.Parent;
            Definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(directory!.FullName, "content/worldrpg/payloads/daggerfall.base.json")));
            Dictionary<InventoryItemId, ItemDefinition> items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            InventoryStore store = new();
            EntityId owner = Entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("test.player"));
            store.RegisterInventory(new InventoryState(owner, [new InventoryCapacityLimit(DaggerActorFactory.ClassicWeightMetric, ulong.MaxValue)]));
            InventoryComponent component = new(store, owner);
            Entities.Store.Add(owner, component);
            Inventory = new(component, Entities, items);
            StatsComponent stats = new();
            stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value), new Stat(strength, 0, maximumStrength, quantum: 1,
                rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero));
            Load = new(Inventory, stats);
        }

        public void Dispose() => Entities.Dispose();
    }
}
