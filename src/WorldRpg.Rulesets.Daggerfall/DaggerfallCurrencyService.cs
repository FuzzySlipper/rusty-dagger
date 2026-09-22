using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallCurrencyTotals(ulong Gold, ulong LettersOfCredit, ulong AccountGold);
internal sealed record DaggerfallCurrencySave(ulong AccountGold, ulong NextGoldStack)
{
    internal DaggerfallCurrencySave Validate()
    {
        if (NextGoldStack == 0) throw new ArgumentOutOfRangeException(nameof(NextGoldStack));
        return this;
    }
}

/// <summary>
/// Currency is inventory-backed: gold remains a fungible Engine stack and each letter of credit is
/// a unique Engine item whose metadata carries its redeemable value. Only the bank balance is an
/// account value; no parallel coin or letter quantity is retained here.
/// </summary>
internal sealed class DaggerfallCurrencyService
{
    private const string GoldItem = "gold-piece";
    private const string GoldTemplateItem = "template-276";
    private const string LetterItem = "template-275";
    private const ulong MinimumLetter = 100;
    private readonly DaggerfallDefinitions _definitions;
    private readonly MechanicsInventoryCoordinator _inventory;
    private readonly DaggerfallItemInstances _instances;
    private readonly DaggerfallEncumbrancePolicy _encumbrance;
    private readonly DaggerfallUniqueItemAllocator _unique;
    private ulong _accountGold;
    private ulong _nextGoldStack = 1;

    internal DaggerfallCurrencyService(DaggerfallDefinitions definitions, MechanicsInventoryCoordinator inventory,
        DaggerfallItemInstances instances, DaggerfallEncumbrancePolicy encumbrance, DaggerfallUniqueItemAllocator unique,
        DaggerfallCurrencySave? restored = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _encumbrance = encumbrance ?? throw new ArgumentNullException(nameof(encumbrance));
        _unique = unique ?? throw new ArgumentNullException(nameof(unique));
        if (restored is { } save)
        {
            save.Validate();
            _accountGold = save.AccountGold;
            _nextGoldStack = save.NextGoldStack;
        }
    }

    internal DaggerfallCurrencyTotals Read()
    {
        ulong gold = 0;
        ulong letters = 0;
        foreach (InventoryStack stack in _inventory.Read().Stacks)
            if (IsGold(stack)) gold = checked(gold + stack.Quantity);
        foreach (Rusty.Engine.Mechanics.UniqueInventoryItem item in _inventory.Read().UniqueItems)
            if (item.Definition.Value == LetterItem) letters = checked(letters + LetterValue(item));
        return new(gold, letters, _accountGold);
    }

    internal DaggerfallCurrencySave Capture() => new(_accountGold, _nextGoldStack);

    internal bool DepositGold(ulong amount)
    {
        if (amount == 0 || Read().Gold < amount || ulong.MaxValue - _accountGold < amount) return false;
        ConsumeGold(amount);
        _accountGold = checked(_accountGold + amount);
        return true;
    }

    internal bool WithdrawGold(ulong amount)
    {
        if (amount == 0 || _accountGold < amount || !_encumbrance.CanCarry(_definitions.RequireItem(new DaggerfallItemId(GoldItem)), amount)) return false;
        GoldGrantPlan? plan = PrepareGoldGrant(amount);
        if (plan is null) return false;
        GrantGold(amount, plan);
        _accountGold -= amount;
        return true;
    }

    internal bool DepositLetters()
    {
        (Rusty.Engine.Mechanics.UniqueInventoryItem Item, ulong Value)[] letters = _inventory.Read().UniqueItems
            .Where(item => item.Definition.Value == LetterItem)
            .Select(item => (item, LetterValue(item)))
            .ToArray();
        if (letters.Length == 0) return false;
        ulong value = checked(letters.Aggregate(0UL, (total, letter) => checked(total + letter.Value)));
        if (value == 0 || ulong.MaxValue - _accountGold < value) return false;
        foreach ((Rusty.Engine.Mechanics.UniqueInventoryItem item, _) in letters)
        {
            DurableIdentityReference identity = _inventory.GetDurableItemId(item.Entity);
            _inventory.Destroy(new WorldRpg.Kit.Inventory.UniqueInventoryItem(item.Entity.Value, new InventoryItemId(LetterItem)));
            _inventory.Entities.Destroy(identity);
            _instances.RemoveUnique(identity.Value);
            _unique.Remove(identity);
        }
        _accountGold = checked(_accountGold + value);
        return true;
    }

    internal bool WithdrawLetter(ulong amount)
    {
        // Donor BankManager refuses a LOC below 100 and charges a truncated 1% commission.
        if (amount < MinimumLetter) return false;
        if (amount > ulong.MaxValue - (amount / 100)) return false;
        ulong debit = amount + (amount / 100);
        DaggerfallItemDefinition letter = _definitions.RequireItem(new DaggerfallItemId(LetterItem));
        if (_accountGold < debit || !_encumbrance.CanCarry(letter, 1)) return false;
        DurableIdentityReference identity = _unique.AllocateReference();
        // Shape and validate all product meaning before the Engine publish. The newly allocated
        // identity cannot collide with existing metadata, so registration after a successful
        // GrantAtomic has no fallible cleanup branch that could orphan an Engine item.
        DaggerfallItemInstanceMetadata metadata = DaggerfallItemInstanceMetadata.Default(letter, DaggerfallItemOwner.Player, amount);
        _inventory.GrantAtomic([new InventoryAtomicGrant(new InventoryItemId(LetterItem), UniqueItem: identity)]);
        _instances.RegisterUnique(identity.Value, metadata);
        _accountGold -= debit;
        return true;
    }

    private void ConsumeGold(ulong amount)
    {
        ulong remaining = amount;
        foreach (InventoryStack stack in _inventory.Read().Stacks.Where(IsGold).OrderBy(stack => stack.Id.Value, StringComparer.Ordinal))
        {
            ulong spent = Math.Min(remaining, stack.Quantity);
            InventoryMutationReceipt receipt = _inventory.Consume(new InventoryConsume(stack.Id, spent));
            if (receipt.AfterQuantity == 0) _instances.RemoveStack(DaggerfallItemOwner.Player, stack.Id);
            remaining -= spent;
            if (remaining == 0) return;
        }
        throw new InvalidOperationException("Gold disappeared before its account deposit completed.");
    }

    private void GrantGold(ulong amount, GoldGrantPlan plan)
    {
        ulong remaining = amount;
        foreach (InventoryStack existing in _inventory.Read().Stacks.Where(IsGold).OrderBy(value => value.Id.Value, StringComparer.Ordinal))
        {
            DaggerfallItemDefinition definition = Definition(existing);
            ulong room = definition.MaximumQuantity - existing.Quantity;
            ulong grant = Math.Min(remaining, room);
            if (grant != 0) _inventory.Grant(new InventoryGrant(new InventoryItemId(definition.Id.Value), existing.Id, grant));
            remaining -= grant;
            if (remaining == 0) return;
        }
        DaggerfallItemDefinition gold = plan.NewStackDefinition;
        foreach (InventoryStackId id in plan.NewStacks)
        {
            ulong grant = Math.Min(remaining, gold.MaximumQuantity);
            _inventory.Grant(new InventoryGrant(new InventoryItemId(gold.Id.Value), id, grant));
            _instances.RegisterStack(DaggerfallItemOwner.Player, id, DaggerfallItemInstanceMetadata.Default(gold, DaggerfallItemOwner.Player));
            remaining -= grant;
        }
        if (remaining != 0) throw new InvalidOperationException("The preflighted gold-stack plan did not cover its requested quantity.");
        _nextGoldStack = plan.NextSequence;
    }

    private bool IsGold(InventoryStack stack) => DaggerfallEncumbrancePolicy.IsGold(Definition(stack));
    private DaggerfallItemDefinition Definition(InventoryStack stack) => _definitions.RequireItem(new DaggerfallItemId(stack.Definition.Value));
    private ulong LetterValue(Rusty.Engine.Mechanics.UniqueInventoryItem item)
    {
        ulong identity = _inventory.GetDurableItemId(item.Entity).Value;
        return _instances.RequireUnique(identity).CreditValue
            ?? throw new InvalidOperationException($"Letter of credit '{identity}' has no redeemable amount.");
    }

    /// <summary>Finds every new stack identifier before changing either the account or inventory.</summary>
    private GoldGrantPlan? PrepareGoldGrant(ulong amount)
    {
        ulong remaining = amount;
        DaggerfallItemDefinition? first = null;
        foreach (InventoryStack stack in _inventory.Read().Stacks.Where(IsGold).OrderBy(value => value.Id.Value, StringComparer.Ordinal))
        {
            DaggerfallItemDefinition definition = Definition(stack);
            first ??= definition;
            remaining -= Math.Min(remaining, definition.MaximumQuantity - stack.Quantity);
            if (remaining == 0) return new GoldGrantPlan([], _nextGoldStack, first);
        }

        DaggerfallItemDefinition gold = first ?? _definitions.RequireItem(new DaggerfallItemId(GoldTemplateItem));
        ulong maximum = gold.MaximumQuantity;
        HashSet<string> existing = _inventory.Read().Stacks.Select(stack => stack.Id.Value).ToHashSet(StringComparer.Ordinal);
        List<InventoryStackId> planned = [];
        ulong next = _nextGoldStack;
        while (remaining != 0)
        {
            // Keep a positive representable successor in the save state. If a collision advances
            // to UInt64.MaxValue, no candidate can safely be committed in this operation.
            if (next == ulong.MaxValue) return null;
            InventoryStackId candidate = InventoryStackId.Parse($"daggerfall.currency.gold.{next}");
            next++;
            if (!existing.Add(candidate.Value)) continue;
            planned.Add(candidate);
            remaining -= Math.Min(remaining, maximum);
        }
        return new GoldGrantPlan(planned, next, gold);
    }

    private sealed record GoldGrantPlan(IReadOnlyList<InventoryStackId> NewStacks, ulong NextSequence, DaggerfallItemDefinition NewStackDefinition);
}
