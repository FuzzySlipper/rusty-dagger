using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRegionalBankTests
{
    [Fact]
    public void Account_only_changes_advance_the_projection_revision()
    {
        using Fixture fixture = new();
        DaggerfallCurrencyService currency = fixture.Currency(100);
        DaggerfallRegionalBankState bank = fixture.Bank(currency, fixture.Accounts((0, 100)));

        Assert.Equal(0UL, bank.Revision);
        Assert.True(bank.Transfer(0, 1, 40).Applied);
        Assert.Equal(1UL, bank.Revision);
        Assert.Equal(DaggerfallBankTransactionResult.InsufficientAccountBalance, bank.Transfer(1, 0, 41).Result);
        Assert.Equal(1UL, bank.Revision);
    }

    [Fact]
    public void Gold_deposits_and_withdrawals_move_inventory_and_restore_the_region_partition()
    {
        using Fixture fixture = new();
        fixture.GrantGold(250);
        DaggerfallCurrencyService currency = fixture.Currency();
        DaggerfallRegionalBankState bank = fixture.Bank(currency);

        DaggerfallBankTransactionOutcome deposit = bank.DepositGold(3, 200);
        Assert.True(deposit.Applied);
        Assert.Equal(200UL, deposit.AmountMoved);
        Assert.Equal(200UL, bank.BalanceForRegion(3));
        Assert.Equal(200UL, currency.Capture().AccountGold);
        Assert.Equal(50UL, currency.Read().Gold);

        DaggerfallBankTransactionOutcome withdrawal = bank.WithdrawGold(3, 125);
        Assert.True(withdrawal.Applied);
        Assert.Equal(75UL, bank.BalanceForRegion(3));
        Assert.Equal(75UL, currency.Capture().AccountGold);
        Assert.Equal(175UL, currency.Read().Gold);

        DaggerfallRegionalBankState restored = fixture.Bank(currency, bank.Capture());
        Assert.Equal(75UL, restored.BalanceForRegion(3));
        Assert.Equal(62, restored.ReadBalances().Count);
        Assert.Equal(75UL, restored.Capture().Accounts.Single(account => account.Region == 3).Gold);
    }

    [Fact]
    public void Transfers_reject_invalid_regions_amounts_and_overflow_without_changing_balances()
    {
        using Fixture fixture = new();
        DaggerfallCurrencyService currency = fixture.Currency((ulong)int.MaxValue + 100);
        DaggerfallRegionalBankState bank = fixture.Bank(currency, fixture.Accounts((0, 200), (1, (ulong)int.MaxValue - 100)));

        Assert.Equal(DaggerfallBankTransactionResult.InvalidRegion, bank.Transfer(-1, 1, 1).Result);
        Assert.Equal(DaggerfallBankTransactionResult.InvalidRegion, bank.Transfer(0, 62, 1).Result);
        Assert.Equal(DaggerfallBankTransactionResult.SameRegion, bank.Transfer(0, 0, 1).Result);
        Assert.Equal(DaggerfallBankTransactionResult.InvalidAmount, bank.Transfer(0, 1, 0).Result);
        Assert.Equal(DaggerfallBankTransactionResult.InvalidAmount, bank.Transfer(0, 1, -1).Result);
        Assert.Equal(DaggerfallBankTransactionResult.SourceLimitExceeded, bank.Transfer(0, 1, (long)int.MaxValue + 1).Result);
        Assert.Equal(DaggerfallBankTransactionResult.AccountBalanceLimit, bank.Transfer(0, 1, 101).Result);
        Assert.Equal(200UL, bank.BalanceForRegion(0));
        Assert.Equal((ulong)int.MaxValue - 100, bank.BalanceForRegion(1));

        DaggerfallBankTransactionOutcome moved = bank.Transfer(0, 1, 100);
        Assert.True(moved.Applied);
        Assert.Equal(100UL, bank.BalanceForRegion(0));
        Assert.Equal((ulong)int.MaxValue, bank.BalanceForRegion(1));
        Assert.Equal((ulong)int.MaxValue + 100, currency.Capture().AccountGold);
    }

    [Fact]
    public void Letters_of_credit_use_real_unique_items_and_charge_the_truncated_one_percent_fee()
    {
        using Fixture fixture = new();
        DaggerfallCurrencyService currency = fixture.Currency(1_000);
        DaggerfallRegionalBankState bank = fixture.Bank(currency, fixture.Accounts((0, 1_000)));

        Assert.Equal(DaggerfallBankTransactionResult.LetterTooSmall, bank.WithdrawLetter(0, 99).Result);
        DaggerfallBankTransactionOutcome letter = bank.WithdrawLetter(0, 199);
        Assert.True(letter.Applied);
        Assert.Equal(1UL, letter.Fee);
        Assert.Equal(199UL, letter.AmountMoved);
        Assert.Equal(800UL, bank.BalanceForRegion(0));
        Assert.Equal(800UL, currency.Capture().AccountGold);
        Assert.Equal(199UL, currency.Read().LettersOfCredit);
        Rusty.Engine.Mechanics.UniqueInventoryItem held = Assert.Single(fixture.Inventory.Read().UniqueItems);
        ulong identity = fixture.Inventory.GetDurableItemId(held.Entity).Value;
        Assert.Equal(199UL, fixture.Instances.RequireUnique(identity).CreditValue);

        DaggerfallBankTransactionOutcome deposit = bank.DepositLetters(4);
        Assert.True(deposit.Applied);
        Assert.Equal(199UL, deposit.AmountMoved);
        Assert.Equal(199UL, bank.BalanceForRegion(4));
        Assert.Equal(999UL, currency.Capture().AccountGold);
        Assert.Equal(0UL, currency.Read().LettersOfCredit);
        Assert.Empty(fixture.Inventory.Read().UniqueItems);
        Assert.Contains(identity, fixture.Unique.RemovedEntityIds);
    }

    [Fact]
    public void Letter_deposit_refuses_non_player_metadata_and_keeps_the_item_and_balance()
    {
        using Fixture fixture = new();
        DaggerfallCurrencyService currency = fixture.Currency(500);
        DaggerfallRegionalBankState bank = fixture.Bank(currency, fixture.Accounts((2, 500)));
        fixture.GrantLetter(125);
        Rusty.Engine.Mechanics.UniqueInventoryItem letter = Assert.Single(fixture.Inventory.Read().UniqueItems);
        ulong identity = fixture.Inventory.GetDurableItemId(letter.Entity).Value;
        fixture.Instances.ReplaceUnique(identity, fixture.Instances.RequireUnique(identity) with { Owner = DaggerfallItemOwner.Wagon(77) });

        DaggerfallBankTransactionOutcome refused = bank.DepositLetters(2);

        Assert.Equal(DaggerfallBankTransactionResult.InvalidLetterOwnership, refused.Result);
        Assert.Equal(500UL, bank.BalanceForRegion(2));
        Assert.Equal(500UL, currency.Capture().AccountGold);
        Assert.Equal(identity, fixture.Inventory.GetDurableItemId(Assert.Single(fixture.Inventory.Read().UniqueItems).Entity).Value);
    }

    [Fact]
    public void Repeated_submissions_cannot_mint_gold_and_separate_region_transfers_conserve_value()
    {
        using Fixture fixture = new();
        fixture.GrantGold(100);
        DaggerfallCurrencyService currency = fixture.Currency();
        DaggerfallRegionalBankState bank = fixture.Bank(currency);

        Assert.True(bank.DepositGold(0, 100).Applied);
        Assert.Equal(DaggerfallBankTransactionResult.CurrencyMovementRejected, bank.DepositGold(0, 100).Result);
        Assert.True(bank.Transfer(0, 1, 60).Applied);
        Assert.True(bank.WithdrawGold(1, 40).Applied);
        Assert.Equal(DaggerfallBankTransactionResult.InsufficientAccountBalance, bank.WithdrawGold(1, 40).Result);

        ulong totalBanked = bank.ReadBalances().Aggregate(0UL, (total, account) => checked(total + account.Gold));
        Assert.Equal(100UL, checked(totalBanked + currency.Read().Gold + currency.Read().LettersOfCredit));
        Assert.Equal(100UL, currency.Read().Gold + currency.Capture().AccountGold);
    }

    [Fact]
    public void Gold_deposit_draws_wagon_shortfall_through_transport_owner_and_updates_item_ownership()
    {
        using Fixture fixture = new();
        fixture.AddCart();
        fixture.GrantGold(50);
        DaggerfallCurrencyService currency = fixture.Currency();
        DaggerfallRegionalBankState bank = fixture.Bank(currency);
        DaggerfallWagonStorage wagon = fixture.CreateWagon();
        InventoryStackId first = InventoryStackId.Parse("wagon.gold.first");
        InventoryStackId second = InventoryStackId.Parse("wagon.gold.second");
        fixture.SeedWagonGold(wagon, first, 30);
        fixture.SeedWagonGold(wagon, second, 40);

        DaggerfallBankTransactionOutcome outcome = bank.DepositGold(5, 100, wagon, new DaggerfallTransportAccessContext());

        Assert.True(outcome.Applied);
        Assert.Equal(100UL, bank.BalanceForRegion(5));
        Assert.Equal(100UL, currency.Capture().AccountGold);
        Assert.Equal(0UL, currency.Read().Gold);
        InventoryView wagonContents = Assert.IsType<InventoryView>(wagon.Read());
        Assert.Equal(20UL, Assert.Single(wagonContents.Stacks).Quantity);
        Assert.Equal(second, Assert.Single(wagonContents.Stacks).Id);
        Assert.Equal(DaggerfallItemOwner.Wagon(wagon.Current!.Id), fixture.Instances.RequireStack(DaggerfallItemOwner.Wagon(wagon.Current.Id), second).Owner);
        Assert.False(fixture.Instances.ContainsStack(DaggerfallItemOwner.Wagon(wagon.Current.Id), first));
    }

    [Fact]
    public void Account_limit_and_malformed_or_mismatched_restores_refuse_before_currency_changes()
    {
        using Fixture fixture = new();
        fixture.GrantGold(10);
        DaggerfallCurrencyService currency = fixture.Currency((ulong)int.MaxValue - 5);
        DaggerfallRegionalBankState bank = fixture.Bank(currency, fixture.Accounts((0, (ulong)int.MaxValue - 5)));

        Assert.Equal(DaggerfallBankTransactionResult.AccountBalanceLimit, bank.DepositGold(0, 10).Result);
        Assert.Equal(10UL, currency.Read().Gold);
        Assert.Equal((ulong)int.MaxValue - 5, currency.Capture().AccountGold);

        DaggerfallRegionalBankSave duplicateRegion = new([new DaggerfallBankAccountSave(0, 0), new DaggerfallBankAccountSave(0, 0)]);
        Assert.Throws<ArgumentException>(() => duplicateRegion.Validate());
        Assert.Throws<ArgumentException>(() => new DaggerfallRegionalBankSave([]).Validate());
        Assert.Throws<ArgumentException>(() => fixture.Bank(currency));

        DaggerfallBankAccountSave[] mismatched = fixture.Accounts((0, 1));
        Assert.Throws<ArgumentException>(() => fixture.Bank(currency, mismatched));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly EntityDirectory _entities = new();
        private readonly DaggerfallUniqueItemAllocator _unique = new(1_000);
        private readonly DurableIdentityAllocator _containerIdentities = new(DurableIdentityKind.Container, 100);
        private readonly InventoryStore _store = new();
        private readonly DaggerfallEncumbrancePolicy _encumbrance;
        private readonly StatsComponent _stats = new();
        private readonly DaggerfallDefinitions _definitions;

        internal MechanicsInventoryCoordinator Inventory { get; }
        internal MechanicsInventoryContainerCoordinator Containers { get; }
        internal EntityId Player { get; }
        internal DaggerfallItemInstances Instances { get; } = new();
        internal DaggerfallUniqueItemAllocator Unique => _unique;

        internal Fixture()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json")))
                directory = directory.Parent;
            _definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(directory!.FullName, "content/worldrpg/payloads/daggerfall.base.json")));
            Dictionary<InventoryItemId, ItemDefinition> items = _definitions.Items.Values.Concat(_definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            Player = _entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("test.player"));
            _store.RegisterInventory(new InventoryState(Player, [new InventoryCapacityLimit(DaggerActorFactory.ClassicWeightMetric, ulong.MaxValue)]));
            InventoryComponent component = new(_store, Player);
            _entities.Store.Add(Player, component);
            Inventory = new(component, _entities, items);
            Containers = new(_store, _entities, items);
            _stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value), new Stat(700_000_000, 0, 700_000_000,
                quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero));
            _encumbrance = new(Inventory, _stats);
        }

        internal DaggerfallCurrencyService Currency(ulong accountGold = 0) => new(
            _definitions, Inventory, Instances, _encumbrance, _unique, new DaggerfallCurrencySave(accountGold, 1));

        internal DaggerfallRegionalBankState Bank(DaggerfallCurrencyService currency, DaggerfallRegionalBankSave? restored = null) =>
            new(currency, Inventory, Instances, restored);

        internal DaggerfallRegionalBankState Bank(DaggerfallCurrencyService currency, DaggerfallBankAccountSave[] accounts) =>
            new(currency, Inventory, Instances, new DaggerfallRegionalBankSave(accounts));

        internal DaggerfallWagonStorage CreateWagon()
        {
            DaggerfallWagonStorage wagon = new(Containers, Instances, _definitions, Player, _containerIdentities);
            _ = wagon.EnsureCreated();
            return wagon;
        }

        internal void AddCart()
        {
            DurableIdentityReference identity = _unique.AllocateReference();
            DaggerfallItemDefinition cart = _definitions.RequireItem(new DaggerfallItemId("template-93"));
            Inventory.GrantAtomic([new InventoryAtomicGrant(new InventoryItemId("template-93"), UniqueItem: identity)]);
            Instances.RegisterUnique(identity.Value, DaggerfallItemInstanceMetadata.Default(cart, DaggerfallItemOwner.Player));
        }

        internal void SeedWagonGold(DaggerfallWagonStorage wagon, InventoryStackId stack, ulong quantity)
        {
            DaggerfallWagon owner = wagon.Current ?? throw new InvalidOperationException("Test wagon was not created.");
            Containers.Seed(owner.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), quantity, Stack: stack)]);
            DaggerfallItemDefinition gold = _definitions.RequireItem(new DaggerfallItemId("gold-piece"));
            Instances.RegisterStack(DaggerfallItemOwner.Wagon(owner.Id), stack,
                DaggerfallItemInstanceMetadata.Default(gold, DaggerfallItemOwner.Wagon(owner.Id)));
        }

        internal DaggerfallBankAccountSave[] Accounts(params (int Region, ulong Gold)[] populated)
        {
            Dictionary<int, ulong> values = populated.ToDictionary(item => item.Region, item => item.Gold);
            return Enumerable.Range(0, DaggerfallRegionalBankPolicy.RegionCount)
                .Select(region => new DaggerfallBankAccountSave(region, values.GetValueOrDefault(region))).ToArray();
        }

        internal void GrantGold(ulong amount)
        {
            InventoryStackId stack = InventoryStackId.Parse("test.wallet.gold");
            DaggerfallItemDefinition gold = _definitions.RequireItem(new DaggerfallItemId("gold-piece"));
            Inventory.Grant(new(new("gold-piece"), stack, amount));
            Instances.RegisterStack(DaggerfallItemOwner.Player, stack, DaggerfallItemInstanceMetadata.Default(gold, DaggerfallItemOwner.Player));
        }

        internal void GrantLetter(ulong amount)
        {
            DurableIdentityReference identity = _unique.AllocateReference();
            DaggerfallItemDefinition letter = _definitions.RequireItem(new DaggerfallItemId("template-275"));
            Inventory.GrantAtomic([new InventoryAtomicGrant(new InventoryItemId("template-275"), UniqueItem: identity)]);
            Instances.RegisterUnique(identity.Value, DaggerfallItemInstanceMetadata.Default(letter, DaggerfallItemOwner.Player, amount));
        }

        public void Dispose() => _entities.Dispose();
    }
}
