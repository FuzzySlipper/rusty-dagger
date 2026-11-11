using System.Globalization;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record InventoryItemPresentation(string Key, string Definition, string Label, string Quantity, int Weight, int Value,
    string Details, string? Icon, int? GridSlot, string[] EquippedSlots, string[] CompatibleSlots,
    ItemConditionPresentation? Condition = null, bool Identified = true, bool CanUse = false, bool CanEquip = false, string? EquipRefusal = null, bool CanUseMagic = false,
    string Category = DaggerfallInventoryPresentation.OtherCategory, bool Stackable = false, string? DropRefusal = null,
    ItemStatPresentation[]? Stats = null, ItemComparisonPresentation? Comparison = null)
{
    /// <summary>The tint a refreshed dyed or metal icon is drawn in, or null.</summary>
    internal PaperDollTint? IconTint { get; init; }

    /// <summary>Whether the item is enchanted, so it shimmers with the magic glint wherever it is shown.</summary>
    internal bool Enchanted { get; init; }

    /// <summary>
    /// Whether <see cref="InventoryItemPresentation.Details"/> is the donor's item information text. Its name, fighting
    /// figure, condition, worth and weight are this row's own fields too, so a screen that shows the fields shows
    /// <see cref="Notes"/> in place of the text.
    /// </summary>
    internal bool DetailsStateFacts { get; init; }

    /// <summary>What the information text says that no field of the row does: a book's author, the soul a gem holds.</summary>
    internal string[] Notes { get; init; } = [];

    /// <summary>
    /// What one of the item weighs in kilograms, a gold piece's classic unit included; <see cref="Weight"/> is in the
    /// template's quarter-kilogram units.
    /// </summary>
    internal double WeightKilograms { get; init; }
}
internal sealed record ItemConditionPresentation(int Current, int Maximum, int Percentage, bool Broken)
{
    /// <summary>The share of its condition below which an item is shown as badly worn.</summary>
    internal const int LowPercentage = 25;

    /// <summary>Whether the item is broken or badly worn, so the screens mark it.</summary>
    internal bool Low => Broken || Percentage < LowPercentage;
}
/// <summary>One fighting figure of an item (its damage or its armour), with a score where higher is better.</summary>
internal sealed record ItemStatPresentation(string Label, string Text, double Score);
/// <summary>The equipped item a carried one would be compared with, and its figures.</summary>
internal sealed record ItemComparisonPresentation(string Item, string Label, string Slot, ItemStatPresentation[] Stats);
/// <summary>One equipment slot, with the group the screen lays it out in: armour, clothing, weapons or accessories.</summary>
internal sealed record EquipmentSlotPresentation(string Id, string Label, string? ItemKey, string Group = "accessories");
/// <summary>One tab of the pack: the category an item row names, and its player-facing name.</summary>
internal sealed record InventoryCategoryPresentation(string Id, string Label);
internal sealed record EquipmentChangePresentation(string Cue, int RightHandDelayMilliseconds, int LeftHandDelayMilliseconds);
internal sealed record DaggerfallBankAccountPresentation(int Region, string Gold, string RegionName);
internal sealed record DaggerfallLoanPresentation(string Principal, string Remaining, long DueMinute, bool Defaulted, long DaysRemaining);
/// <summary>
/// One bank operation as the bank offers it now: whether it is available, the bounds of its amount
/// (both zero when it takes none), and why it is not available when it is not.
/// </summary>
internal sealed record DaggerfallBankActionPresentation(string Action, string Label, bool Available, long Minimum, long Maximum, string? Refusal = null)
{
    internal bool TakesAmount => Maximum > 0 || Minimum > 0;
}
internal sealed record DaggerfallBankPresentation(int CurrentRegion, string CurrentBalance, DaggerfallBankAccountPresentation[] Accounts,
    DaggerfallLoanPresentation? Loan, long MaximumNewLoan, string CurrentRegionName = DaggerfallRegionNames.Unnamed,
    DaggerfallBankActionPresentation[]? Actions = null);
internal sealed record InventoryPresentation(string Revision, InventoryItemPresentation[] Items, EquipmentSlotPresentation[] Slots, string Message,
    EquipmentChangePresentation? EquipmentChange = null, DaggerfallEncumbrance? Encumbrance = null, DaggerfallCurrencyTotals? Currency = null,
    DaggerfallBankPresentation? Bank = null, InventoryCategoryPresentation[]? Categories = null)
{
    /// <summary>The item glints in the art drawn, when the publication has them.</summary>
    internal InventoryGlintsPresentation? Glints { get; init; }
}

/// <summary>
/// One of the inventory window's looping glints: its atlas image, the atlas size, each frame's rect in it, how many
/// frames it turns a second, and where the classic frame sits in a frame (fractions, top-down), which the item's slot
/// fills: the whole frame for classic art, a part of it for refreshed art painted past the classic edges.
/// </summary>
internal sealed record InventoryGlintPresentation(string Media, int Width, int Height, IReadOnlyList<(int X, int Y, int Width, int Height)> Frames,
    double FramesPerSecond, DaggerfallArtRect Slot);

/// <summary>
/// The glints item lists draw (DaggerfallInventoryWindow): the magic shimmer over every enchanted item, and the coin glint
/// behind stock the player may steal.
/// </summary>
internal sealed record InventoryGlintsPresentation(InventoryGlintPresentation Magic, InventoryGlintPresentation Coins);

/// <summary>Daggerfall inventory projection and UI-action translation; quantities and equipment remain Engine facts.</summary>
internal sealed class DaggerfallInventoryPresentation
{
    private readonly DaggerfallEquipmentMoves moves;
    private readonly DaggerfallDefinitions definitions;
    private readonly IReadOnlyDictionary<string, string> icons;
    private Func<DaggerfallArtReplacements?>? art;
    private Func<DaggerfallCharacterIdentity>? wearer;
    private readonly DaggerfallEncumbrancePolicy? encumbrance;
    private readonly DaggerfallCurrencyService? currency;
    private DaggerfallItemValuation? valuation;
    private DaggerfallItemInstances? itemInstances;
    private DaggerfallItemOwner? itemOwner;
    private Func<ulong, ulong>? uniqueIdentity;
    private DaggerfallItemConditionService? itemCondition;
    private DaggerfallGroundContainers? ground;
    private Func<WorldRpg.Kit.Controls.WorldPoint?>? groundPosition;
    private DaggerfallInventoryUseService? itemUse;
    private DaggerfallRegionalBankState? bank;
    private Func<ulong>? depositableGold;
    private DaggerfallLoanState? loans;
    private Func<DaggerfallCalendar>? loanCalendar;
    private Func<int>? loanLevel;
    private Func<int?>? currentRegion;
    private Func<DaggerfallItemInstanceMetadata, string?>? canDropQuestItem;
    private Action<DaggerfallItemInstanceMetadata>? questItemDropped;
    internal void BindQuestDrops(Func<DaggerfallItemInstanceMetadata, string?> canDrop, Action<DaggerfallItemInstanceMetadata> dropped)
    { canDropQuestItem = canDrop; questItemDropped = dropped; }
    internal event Action<DaggerfallReadableBook>? BookOpened;
    internal string Message { get; private set; } = "Drag items between the grid and compatible equipment slots.";
    /// <summary>
    /// The equipment change the last inventory action completed, if it made one. Every action clears
    /// it first, so a later drop or use is never reported with an earlier equip's cue.
    /// </summary>
    internal DaggerfallEquipmentChange? LastEquipmentChange { get; private set; }
    internal ulong MetadataRevision => itemInstances?.Revision ?? 0;

    internal DaggerfallInventoryPresentation(
        DaggerfallEquipmentMoves moves,
        DaggerfallDefinitions definitions,
        IReadOnlyDictionary<string, string> icons,
        DaggerfallEncumbrancePolicy? encumbrance = null,
        DaggerfallCurrencyService? currency = null)
    {
        this.moves = moves;
        this.definitions = definitions;
        this.icons = icons;
        this.encumbrance = encumbrance;
        this.currency = currency;
        moves.Changed += change => LastEquipmentChange = change;
    }

    /// <summary>Connects durable item meaning after ordinary inventory composition has completed.</summary>
    internal void UseItemValuation(DaggerfallItemValuation value, DaggerfallItemInstances instances,
        DaggerfallItemOwner owner, Func<ulong, ulong> resolveUniqueIdentity)
    {
        if (valuation is not null) throw new InvalidOperationException("Inventory valuation is already configured.");
        valuation = value ?? throw new ArgumentNullException(nameof(value));
        itemInstances = instances ?? throw new ArgumentNullException(nameof(instances));
        itemOwner = owner?.Validate() ?? throw new ArgumentNullException(nameof(owner));
        uniqueIdentity = resolveUniqueIdentity ?? throw new ArgumentNullException(nameof(resolveUniqueIdentity));
    }

    /// <summary>Connects persisted condition and identification meaning to existing inventory rows.</summary>
    internal void UseItemCondition(DaggerfallItemConditionService condition)
    {
        if (itemCondition is not null) throw new InvalidOperationException("Inventory item condition is already configured.");
        if (itemInstances is null || itemOwner is null || uniqueIdentity is null)
            throw new InvalidOperationException("Inventory item condition requires durable item metadata.");
        itemCondition = condition ?? throw new ArgumentNullException(nameof(condition));
    }

    /// <summary>Connects player drops to the durable, positioned ground-container owner.</summary>
    internal void UseGroundDrops(DaggerfallGroundContainers containers, Func<WorldRpg.Kit.Controls.WorldPoint?> position)
    {
        if (ground is not null) throw new InvalidOperationException("Inventory ground drops are already configured.");
        ground = containers ?? throw new ArgumentNullException(nameof(containers));
        groundPosition = position ?? throw new ArgumentNullException(nameof(position));
    }

    internal void UseItemActions(DaggerfallInventoryUseService use)
    {
        if (itemUse is not null) throw new InvalidOperationException("Inventory item use is already configured.");
        itemUse = use ?? throw new ArgumentNullException(nameof(use));
    }

    internal void UseBank(DaggerfallRegionalBankState state, Func<int?> resolveCurrentRegion, Func<ulong>? resolveDepositableGold = null)
    {
        if (bank is not null) throw new InvalidOperationException("Regional bank presentation is already configured.");
        bank = state ?? throw new ArgumentNullException(nameof(state));
        currentRegion = resolveCurrentRegion ?? throw new ArgumentNullException(nameof(resolveCurrentRegion));
        depositableGold = resolveDepositableGold;
    }

    internal void UseLoans(DaggerfallLoanState state, Func<DaggerfallCalendar> calendar, Func<int> level)
    {
        loans = state ?? throw new ArgumentNullException(nameof(state));
        loanCalendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        loanLevel = level ?? throw new ArgumentNullException(nameof(level));
    }

    internal void ReportBankTransaction(DaggerfallBankTransactionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        LastEquipmentChange = null;
        string detail = outcome.Result switch
        {
            DaggerfallBankTransactionResult.Applied => AppliedBankMessage(outcome),
            DaggerfallBankTransactionResult.InvalidRegion => "Choose a region where you hold an account.",
            DaggerfallBankTransactionResult.SameRegion => "Choose a different destination region.",
            DaggerfallBankTransactionResult.InvalidAmount => "Enter a positive amount.",
            DaggerfallBankTransactionResult.LetterTooSmall => "A letter of credit must be worth at least 100 gold.",
            DaggerfallBankTransactionResult.SourceLimitExceeded => "That amount is larger than the bank can handle at once.",
            DaggerfallBankTransactionResult.InsufficientAccountBalance => "That regional account does not contain enough gold.",
            DaggerfallBankTransactionResult.AccountBalanceLimit => "That account cannot hold any more gold.",
            DaggerfallBankTransactionResult.CurrencyMovementRejected => "The gold or letter of credit could not be taken from your pack.",
            DaggerfallBankTransactionResult.WagonUnavailable => "The wagon is unavailable here or does not hold enough gold.",
            DaggerfallBankTransactionResult.InvalidWagonGoldOwnership => "The gold in your wagon cannot be deposited; no balance was changed.",
            DaggerfallBankTransactionResult.WagonTransferPartiallyApplied => $"Moved {outcome.AmountMoved} gold from the wagon into your pack; the bank deposit did not complete.",
            DaggerfallBankTransactionResult.NoLettersOfCredit => "You have no letters of credit to deposit.",
            DaggerfallBankTransactionResult.InvalidLetterOwnership or DaggerfallBankTransactionResult.InvalidLetterMetadata
                => "The bank cannot accept one of your letters of credit.",
            DaggerfallBankTransactionResult.LedgerMismatch => "The bank's records do not balance; no transaction was made.",
            _ => "The bank transaction was not accepted.",
        };
        Message = detail;
    }

    internal InventoryPresentation Read()
    {
        Rusty.Engine.Mechanics.InventoryView current = moves.ReadInventory();
        EquipmentRead equipped = moves.ReadEquipment();
        var items = current.UniqueItems.Select(item => (Key: UniqueKey(item.Entity.Value), Definition: item.Definition.Value, Quantity: 1UL,
                Slots: equipped.Assignments.Where(assignment => assignment.Item.EntityId == item.Entity.Value).Select(assignment => assignment.Slot.Value).ToArray()))
            .Concat(current.Stacks.Select(stack => (Key: StackKey(stack.Id), Definition: stack.Definition.Value, Quantity: stack.Quantity, Slots: Array.Empty<string>())))
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        moves.ReconcileLayout();
        int? region = bank is not null && currentRegion is not null ? currentRegion() : null;
        InventoryItemPresentation[] rows = Compare([.. items.Select(item =>
            DescribeItem(item.Key, item.Definition, item.Quantity, item.Slots.Length == 0 ? moves.GridPosition(item.Key) : null, item.Slots))]);
        // The art the icons are drawn in is part of what the rows show, so choosing classic or refreshed art redraws them.
        string drawn = art?.Invoke() is null ? "classic" : "refreshed";
        return new InventoryPresentation($"{current.StoreRevision}:{moves.LayoutRevision}:{MetadataRevision}:{bank?.Revision}:{region}:{drawn}", rows,
            definitions.EquipmentSlots.Values.OrderBy(slot => SlotOrder(slot.Id.Value)).Select(slot => SlotPresentation(slot.Id.Value,
                equipped.TryGet(new EquipmentSlotId(slot.Id.Value), out UniqueInventoryItem item) ? UniqueKey(item.EntityId) : null)).ToArray(), Message,
            LastEquipmentChange is { } change ? new(change.Cue.ToString().ToLowerInvariant(), change.Timing.RightHandMilliseconds, change.Timing.LeftHandMilliseconds) : null,
            encumbrance?.Read(), currency?.Read(), ReadBank(), PackCategories) { Glints = ReadGlints() };
    }

    private (NormalizedClassicEffect Magic, NormalizedClassicEffect Coins)? glints;

    /// <summary>Draws the glints from these classic effects, or their refreshed atlases while refreshed art is drawn.</summary>
    internal void UseGlints(NormalizedClassicEffect magic, NormalizedClassicEffect coins) => glints = (magic, coins);

    private InventoryGlintsPresentation? ReadGlints()
    {
        if (glints is not var (magic, coins)) return null;
        DaggerfallArtReplacements? set = art?.Invoke();
        return new(Glint(magic, set), Glint(coins, set));

        static InventoryGlintPresentation Glint(NormalizedClassicEffect effect, DaggerfallArtReplacements? set) =>
            set?.Effects.GetValueOrDefault(effect.MediaId) is { } refreshed
                ? new(DaggerfallArtReplacements.EffectMediaId(effect.MediaId), refreshed.Width, refreshed.Height,
                    [.. refreshed.Frames.Select(frame => ((int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height))], effect.FramesPerSecond, refreshed.Classic)
                : new(effect.MediaId, effect.AtlasWidth, effect.AtlasHeight,
                    [.. effect.Sequence.Select(index => effect.Frames.Single(frame => frame.Id == index)).Select(frame => (frame.X, frame.Y, frame.Width, frame.Height))],
                    effect.FramesPerSecond, new(0, 0, 1, 1));
    }

    /// <summary>
    /// Gives each carried, unworn item with fighting figures the figures of what it would be
    /// compared with: the item worn in the first slot that takes it and holds something.
    /// </summary>
    private InventoryItemPresentation[] Compare(InventoryItemPresentation[] rows)
    {
        Dictionary<string, InventoryItemPresentation> worn = [];
        foreach (InventoryItemPresentation row in rows)
            foreach (string slot in row.EquippedSlots) worn.TryAdd(slot, row);
        return [.. rows.Select(row =>
        {
            if (row.EquippedSlots.Length != 0 || row.Stats is not { Length: > 0 }) return row;
            string? slot = definitions.EquipmentSlots.Keys.Select(id => id.Value).OrderBy(SlotOrder)
                .FirstOrDefault(id => row.CompatibleSlots.Contains(id) && worn.ContainsKey(id));
            if (slot is null) return row;
            InventoryItemPresentation equipped = worn[slot];
            return row with { Comparison = new(equipped.Key, equipped.Label, SlotPresentation(slot, equipped.Key).Label, equipped.Stats ?? []) };
        })];
    }

    /// <summary>An item's fighting figures: a weapon's damage with its material's modifier, armour's or a shield's rating.</summary>
    private ItemStatPresentation[] Stats(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata? metadata)
    {
        string? material = metadata?.Material ?? definition.Weapon?.Material ?? definition.Armor?.Material;
        if (definition.Weapon is { } weapon)
        {
            int modifier = DaggerfallFormulaPolicy.WeaponMaterialDamageModifier(material, DaggerfallFormulaPolicy.ClassicWeaponMaterialModifiers);
            int low = weapon.MinimumDamage + modifier, high = weapon.MaximumDamage + modifier;
            return [new("Damage", $"{low}–{high}", (low + high) / 2d)];
        }
        if (definition.Shield is { } shield) return [new("Armour", shield.Armor.ToString(CultureInfo.InvariantCulture), shield.Armor)];
        if (definition.Armor is not null && material is not null && definitions.ArmorValuesByMaterial.TryGetValue(material, out int rating))
            return [new("Armour", rating.ToString(CultureInfo.InvariantCulture), rating)];
        return [];
    }

    /// <summary>
    /// The pack's tabs, after the donor's inventory window: weapons and armour, magic items (anything
    /// enchanted, and spellbooks), ingredients, and clothing with everything else.
    /// </summary>
    internal const string OtherCategory = "clothing-misc";
    private static readonly InventoryCategoryPresentation[] PackCategories =
        [new("weapons-armor", "Weapons & armour"), new("magic-items", "Magic items"), new(OtherCategory, "Clothing & misc"), new("ingredients", "Ingredients")];
    private const int SpellbookTemplate = 132;

    internal static string Category(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata? metadata)
    {
        if (metadata?.HasEnchantment == true || definition.Template?.Index == SpellbookTemplate) return "magic-items";
        bool weaponOrArmor = definition.Template is { } template
            ? template.Groups.Count > 0 && template.Groups[0] is "Weapons" or "Armor"
            : definition.Weapon is not null || definition.Armor is not null || definition.Shield is not null;
        if (weaponOrArmor) return "weapons-armor";
        return definition.Template?.IsIngredient == true ? "ingredients" : OtherCategory;
    }

    /// <summary>
    /// Where the equipment screen lays each classic slot out, and the name it gives it: worn armour
    /// beside the doll on one side, clothing and the hands on the other, and the paired accessories
    /// below. A slot this table does not name is an accessory under its own name.
    /// </summary>
    private static readonly (string Id, string Group, string Label)[] SlotLayout =
    [
        ("head", "armour", "Helm"), ("right-arm", "armour", "Right pauldron"), ("left-arm", "armour", "Left pauldron"),
        ("chest-armor", "armour", "Cuirass"), ("gloves", "armour", "Gauntlets"), ("legs-armor", "armour", "Greaves"), ("feet", "armour", "Feet"),
        ("cloak1", "clothing", "Cloak"), ("cloak2", "clothing", "Second cloak"), ("chest-clothes", "clothing", "Chest clothing"),
        ("legs-clothes", "clothing", "Leg clothing"), ("right-hand", "weapons", "Right hand"), ("left-hand", "weapons", "Left hand"),
        ("amulet0", "accessories", "Amulet"), ("amulet1", "accessories", "Second amulet"),
        ("bracelet0", "accessories", "Bracelet"), ("bracelet1", "accessories", "Second bracelet"),
        ("ring0", "accessories", "Ring"), ("ring1", "accessories", "Second ring"),
        ("bracer0", "accessories", "Bracer"), ("bracer1", "accessories", "Second bracer"),
        ("mark0", "accessories", "Mark"), ("mark1", "accessories", "Second mark"),
        ("crystal0", "accessories", "Crystal"), ("crystal1", "accessories", "Second crystal"),
    ];

    private static int SlotOrder(string id)
    {
        int index = Array.FindIndex(SlotLayout, entry => entry.Id == id);
        return index < 0 ? SlotLayout.Length : index;
    }

    private static EquipmentSlotPresentation SlotPresentation(string id, string? itemKey)
    {
        int index = Array.FindIndex(SlotLayout, entry => entry.Id == id);
        return index < 0 ? new(id, Label(id), itemKey) : new(id, SlotLayout[index].Label, itemKey, SlotLayout[index].Group);
    }

    /// <summary>Lays the pack out by tab, then by name, from the first cell.</summary>
    internal void Sort()
    {
        LastEquipmentChange = null;
        InventoryPresentation current = Read();
        string[] order = [.. current.Items.Where(item => item.EquippedSlots.Length == 0)
            .OrderBy(item => Array.FindIndex(PackCategories, category => category.Id == item.Category))
            .ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Key)];
        moves.ArrangeGrid(order);
        Message = "Pack sorted.";
    }

    private DaggerfallBankPresentation? ReadBank()
    {
        if (bank is null || currentRegion is null) return null;
        if (currentRegion() is not int region) return null;
        DaggerfallLoanRecord? loan = loans?.Read(region);
        long now = loanCalendar is null ? 0 : DaggerfallLoanPolicy.ClassicMinute(loanCalendar());
        return new(region, bank.BalanceForRegion(region).ToString(CultureInfo.InvariantCulture),
            bank.ReadBalances().Select(account => new DaggerfallBankAccountPresentation(account.Region,
                account.Gold.ToString(CultureInfo.InvariantCulture), RegionName(account.Region))).ToArray(),
            loan is null ? null : new(loan.Principal.ToString(CultureInfo.InvariantCulture), loan.Remaining.ToString(CultureInfo.InvariantCulture),
                loan.DueMinute, loan.Defaulted, loan.Remaining == 0 ? 0 : Math.Max(0, (loan.DueMinute - now) / DaggerfallLoanPolicy.MinutesPerDay)),
            loanLevel is null ? 0 : DaggerfallLoanPolicy.CalculateMaxBankLoan(loanLevel()), RegionName(region), BankActions(region, loan));
    }

    /// <summary>
    /// What each bank operation allows now, from the same owners the operations use: the amount
    /// bounds the screen's steppers keep to and the reason an operation is closed.
    /// </summary>
    private DaggerfallBankActionPresentation[] BankActions(int region, DaggerfallLoanRecord? loan)
    {
        const long most = DaggerfallRegionalBankPolicy.MaximumTransactionAmount;
        static long Bound(ulong value) => value > most ? most : (long)value;
        DaggerfallCurrencyTotals funds = currency?.Read() ?? new(0, 0, 0);
        ulong balance = bank!.BalanceForRegion(region);
        ulong room = DaggerfallRegionalBankPolicy.MaximumAccountBalance - Math.Min(balance, DaggerfallRegionalBankPolicy.MaximumAccountBalance);
        ulong reachable = depositableGold?.Invoke() ?? funds.Gold;
        const string Empty = "This account holds no gold.";

        long deposit = Math.Min(Bound(reachable), Bound(room));
        long withdraw = Bound(balance);
        long letter = LargestLetter(withdraw);
        bool owing = loan is { Remaining: > 0 };
        long levelLoan = loanLevel is null ? 0 : DaggerfallLoanPolicy.CalculateMaxBankLoan(loanLevel());
        long lend = Math.Min(levelLoan, Bound(room));
        string? lendRefusal = loan?.Defaulted == true ? "You defaulted on a loan here; this bank will not lend to you again."
            : owing ? "Repay the loan you hold here before borrowing again."
            : lend < DaggerfallLoanPolicy.MinimumPrincipal ? $"This bank lends no less than {DaggerfallLoanPolicy.MinimumPrincipal} gold, and will not lend you that much."
            : null;
        long remaining = owing ? Bound(loan!.Remaining) : 0;
        ulong carriedFunds = funds.Gold > ulong.MaxValue - funds.LettersOfCredit ? ulong.MaxValue : funds.Gold + funds.LettersOfCredit;
        long repayAccount = Math.Min(remaining, withdraw);
        long repayCarried = Math.Min(remaining, Bound(carriedFunds > ulong.MaxValue - balance ? ulong.MaxValue : carriedFunds + balance));
        return
        [
            new("currency-deposit-gold", "Deposit gold", deposit > 0, deposit > 0 ? 1 : 0, deposit,
                deposit > 0 ? null : reachable == 0 ? "You carry no gold to deposit." : "This account cannot hold any more gold."),
            new("currency-withdraw-gold", "Withdraw gold", withdraw > 0, withdraw > 0 ? 1 : 0, withdraw, withdraw > 0 ? null : Empty),
            new("currency-deposit-letters", "Deposit all letters of credit", funds.LettersOfCredit > 0, 0, 0,
                funds.LettersOfCredit > 0 ? null : "You carry no letters of credit."),
            new("currency-withdraw-letter", "Withdraw a letter of credit", letter > 0, letter > 0 ? DaggerfallRegionalBankPolicy.MinimumLetterOfCredit : 0, letter,
                letter > 0 ? null : $"A letter of credit is worth at least {DaggerfallRegionalBankPolicy.MinimumLetterOfCredit} gold, and the bank keeps {DaggerfallRegionalBankPolicy.LetterCommissionPercent}% more from the account."),
            new("bank-transfer", "Transfer to another region", withdraw > 0, withdraw > 0 ? 1 : 0, withdraw, withdraw > 0 ? null : Empty),
            new("bank-loan-issue", "Borrow into this account", lendRefusal is null, lendRefusal is null ? DaggerfallLoanPolicy.MinimumPrincipal : 0,
                lendRefusal is null ? lend : 0, lendRefusal),
            new("bank-loan-repay-account", "Repay the loan from this account", repayAccount > 0, repayAccount > 0 ? 1 : 0, repayAccount,
                repayAccount > 0 ? null : owing ? Empty : "You hold no loan here."),
            new("bank-loan-repay-carried", "Repay with carried gold and letters, then the account", repayCarried > 0, repayCarried > 0 ? 1 : 0, repayCarried,
                repayCarried > 0 ? null : owing ? "You have no gold to repay with." : "You hold no loan here."),
        ];
    }

    /// <summary>The largest letter of credit an account balance pays for, its commission included, or zero.</summary>
    private static long LargestLetter(long balance)
    {
        long letter = balance * 100 / (100 + DaggerfallRegionalBankPolicy.LetterCommissionPercent);
        while (DaggerfallRegionalBankPolicy.TryLetterDebit(letter + 1, out long debit, out _) && debit <= balance) letter++;
        while (letter >= DaggerfallRegionalBankPolicy.MinimumLetterOfCredit
            && !(DaggerfallRegionalBankPolicy.TryLetterDebit(letter, out long debit, out _) && debit <= balance)) letter--;
        return letter >= DaggerfallRegionalBankPolicy.MinimumLetterOfCredit ? letter : 0;
    }

    private string RegionName(int region) => definitions.BuildingNames.RegionName(region);

    private string AppliedBankMessage(DaggerfallBankTransactionOutcome outcome)
    {
        string source = outcome.SourceRegion is int from ? $"your {RegionName(from)} account" : "your pack";
        string destination = outcome.DestinationRegion is int to ? $"your {RegionName(to)} account" : "your pack";
        string movement = outcome.Kind switch
        {
            DaggerfallBankTransactionKind.DepositGold => $"Deposited {outcome.AmountMoved} gold into {destination}.",
            DaggerfallBankTransactionKind.WithdrawGold => $"Withdrew {outcome.AmountMoved} gold from {source}.",
            DaggerfallBankTransactionKind.DepositLettersOfCredit => $"Deposited letters worth {outcome.AmountMoved} gold into {destination}.",
            DaggerfallBankTransactionKind.WithdrawLetterOfCredit => $"Withdrew a letter worth {outcome.AmountMoved} gold from {source}; fee {outcome.Fee} gold.",
            DaggerfallBankTransactionKind.Transfer => $"Transferred {outcome.AmountMoved} gold from {source} to {destination}.",
            _ => "Bank transaction completed.",
        };
        string balances = outcome.SourceRegion is int sourceRegion && outcome.DestinationRegion is int destinationRegion
            ? $" Balances: {RegionName(sourceRegion)} {outcome.SourceBalance} gold; {RegionName(destinationRegion)} {outcome.DestinationBalance} gold."
            : outcome.DestinationRegion is int depositedRegion
                ? $" {RegionName(depositedRegion)} balance: {outcome.DestinationBalance} gold."
                : outcome.SourceRegion is int withdrawnRegion
                    ? $" {RegionName(withdrawnRegion)} balance: {outcome.SourceBalance} gold."
                    : string.Empty;
        return movement + balances;
    }

    /// <summary>
    /// Translates one semantic UI action into a typed equipment move and renders the outcome. The
    /// gameplay decision lives in <see cref="DaggerfallEquipmentMoves"/>; this only speaks UI keys.
    /// Staleness is handled per item against the fresh read and the coordinators' containment checks,
    /// not by rejecting the whole action on a revision mismatch.
    /// </summary>
    internal void Move(DaggerfallPlayerUiAction action)
    {
        LastEquipmentChange = null;
        InventoryPresentation before = Read();
        InventoryItemPresentation? row = action.Item is null ? null : before.Items.SingleOrDefault(item => item.Key == action.Item);
        if (row is null) { Message = "That item is no longer in your inventory."; return; }
        EquipmentMoveResult result;
        if (action.TargetGrid is int target)
        {
            result = TryParseUnique(row, out UniqueInventoryItem unique)
                ? moves.MoveToGrid(unique, target)
                : TryParseStack(row, out Rusty.Engine.Mechanics.InventoryStackId stack)
                    ? moves.MoveStackToGrid(stack, target)
                    : new EquipmentMoveResult(EquipmentMoveOutcome.UnknownItem);
        }
        else if (action.TargetEquipment is string slot)
        {
            // Stacks cannot take equipment slots; the rejection below keeps the previous wording.
            result = TryParseUnique(row, out UniqueInventoryItem unique)
                ? moves.MoveToSlot(unique, new EquipmentSlotId(slot))
                : new EquipmentMoveResult(EquipmentMoveOutcome.Incompatible, "The item does not fit this equipment slot.");
        }
        else { Message = "Choose an inventory or equipment destination."; return; }
        Message = result.Outcome switch
        {
            EquipmentMoveOutcome.Applied => "Inventory updated.",
            EquipmentMoveOutcome.UnknownItem => "That item is no longer in your inventory.",
            EquipmentMoveOutcome.InvalidDestination => "Choose a valid inventory slot.",
            EquipmentMoveOutcome.Incompatible or EquipmentMoveOutcome.ConflictsNeedClearing or EquipmentMoveOutcome.Rejected
                => $"Cannot place that item there. {result.Detail}",
            _ => $"Cannot place that item there. {result.Detail}",
        };
    }

    /// <summary>Publishes the authoritative current description for a still-contained inventory row.</summary>
    internal void Inspect(DaggerfallPlayerUiAction action)
    {
        LastEquipmentChange = null;
        InventoryPresentation current = Read();
        InventoryItemPresentation? row = action.Item is null ? null : current.Items.SingleOrDefault(item => item.Key == action.Item);
        if (row is null) { Message = "That item is no longer in your inventory."; return; }
        Message = $"{row.Label}: {row.Details}";
    }

    /// <summary>Routes a current item through a named domain operation; rejected or deferred use conserves it.</summary>
    internal void Use(DaggerfallPlayerUiAction action)
    {
        LastEquipmentChange = null;
        if (itemUse is null) throw new InvalidOperationException("Inventory item use is not composed.");
        InventoryPresentation current = Read();
        if (action.Revision != current.Revision) { Message = "Inventory changed. Choose the item again."; return; }
        InventoryItemPresentation? row = action.Item is null ? null : current.Items.SingleOrDefault(item => item.Key == action.Item);
        if (row is null) { Message = "That item is no longer in your inventory."; return; }
        DaggerfallInventoryUseResult result = itemUse.Use(row.Key, moves.ReadInventory().StoreRevision);
        Message = result.Message;
        if (result.OpenedBook is not null) BookOpened?.Invoke(result.OpenedBook);
    }

    /// <summary>Drops one explicit current stack quantity or one unequipped unique item into a durable ground pile.</summary>
    internal void Drop(DaggerfallPlayerUiAction action)
    {
        LastEquipmentChange = null;
        if (ground is null || groundPosition is null) throw new InvalidOperationException("Inventory ground drops are not composed.");
        InventoryPresentation current = Read();
        if (action.Revision != current.Revision) { Message = "Inventory changed. Choose the item again."; return; }
        InventoryItemPresentation? row = action.Item is null ? null : current.Items.SingleOrDefault(item => item.Key == action.Item);
        if (row is null) { Message = "That item is no longer in your inventory."; return; }
        ulong quantity = action.Amount ?? 1;
        InventoryContainerSelection selection;
        if (TryParseUnique(row, out UniqueInventoryItem unique))
        {
            if (quantity != 1) { Message = "A unique item can only be dropped once."; return; }
            if (row.EquippedSlots.Length != 0) { Message = "Unequip that item before dropping it."; return; }
            selection = new(new InventoryItemId(row.Definition), 1, UniqueEntityId: unique.EntityId);
        }
        else if (TryParseStack(row, out Rusty.Engine.Mechanics.InventoryStackId stack))
        {
            if (quantity == 0 || quantity > ulong.Parse(row.Quantity, CultureInfo.InvariantCulture)) { Message = "Choose a quantity still in that stack."; return; }
            selection = new(new InventoryItemId(row.Definition), quantity, Stack: stack);
        }
        else { Message = "That item is no longer in your inventory."; return; }
        WorldRpg.Kit.Controls.WorldPoint? position = groundPosition();
        if (position is null) { Message = "You cannot drop an item without a world position."; return; }
        var metadata = Metadata(row.Key, itemOwner);
        if (metadata is not null && canDropQuestItem?.Invoke(metadata) is { } refusal) { Message = refusal; return; }
        try
        {
            _ = ground.Drop(selection, position.Value, ulong.Parse(current.Revision.Split(':')[0], CultureInfo.InvariantCulture));
            if (metadata is not null) questItemDropped?.Invoke(metadata);
            Message = $"Dropped {row.Label}.";
        }
        catch (Exception rejection) when (rejection is InvalidOperationException or ArgumentException)
        {
            Message = $"You cannot drop {row.Label} here.";
        }
    }

    /// <summary>
    /// The icon an item draws, and the tint a refreshed dyed or metal one is drawn in. A potion of a recipe draws its
    /// recipe's bottle from the published icons; every other item the image its template draws in the inventory, for
    /// the wearer's body (<see cref="DaggerfallPaperDollPolicy.InventoryImage"/>); an item without a template its own
    /// published icon, if any.
    /// </summary>
    internal (string? MediaId, PaperDollTint? Tint) Icon(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata? metadata)
    {
        DaggerfallArtReplacements? set = art?.Invoke();
        bool recipe = definition.Template?.Index == PotionTemplate && metadata?.PotionRecipeKey is not null;
        if (!recipe && wearer?.Invoke() is { } who && DaggerfallPaperDollPolicy.InventoryImage(definitions, definition, metadata, who, set) is { } image)
            return image;
        string? classic = icons.GetValueOrDefault(recipe ? $"potion.{metadata!.PotionRecipeKey}" : definition.Id.Value);
        return (classic is null ? null : set?.Drawn(classic) ?? classic, null);
    }

    /// <summary>
    /// How a ground pile stands with refreshed art: a pile of one item or one stack as that item's own image, in its
    /// metal or dye, as the inventory draws it. Null for classic art, a pile of mixed or no items, or an item whose
    /// image the set has no world picture of; such a pile stands as the classic treasure pile.
    /// </summary>
    internal DaggerfallGroundItemLook? GroundLook(long groundId, Rusty.Engine.Mechanics.InventoryView contents)
    {
        if (art?.Invoke() is not { } set || contents.Stacks.Count + contents.UniqueItems.Count != 1) return null;
        (string key, string id) = contents.Stacks.Count == 1
            ? (StackKey(contents.Stacks.First().Id), contents.Stacks.First().Definition.Value)
            : (UniqueKey(contents.UniqueItems.First().Entity.Value), contents.UniqueItems.First().Definition.Value);
        (string? media, PaperDollTint? tint) = Icon(definitions.RequireItem(new DaggerfallItemId(id)), Metadata(key, DaggerfallItemOwner.Ground(groundId)));
        if (media is null || set.ByMediaId(media) is not { World: { } world } replacement) return null;
        // An icon is sized in classic pixels, a worn item's image in doll units.
        (double height, bool doll) = world.Classic is { } classic ? (classic.Height, false) : (replacement.Doll!.Height, true);
        if (tint is null) return new(world, null, null, 1, height, doll);
        // A tinted image without a world mask would stand in bare steel or undyed cloth, which is not the item.
        return replacement.DyeMask?.World is { } mask ? new(world, mask, tint.Color, tint.Brightness, height, doll) : null;
    }

    /// <summary>The potion template, whose items draw their recipe's bottle.</summary>
    private const int PotionTemplate = 83;

    /// <summary>Draws icons from the refreshed art set the provider returns, or classic ones when it returns none.</summary>
    internal void UseArt(Func<DaggerfallArtReplacements?> provider) => art = provider;

    /// <summary>Draws icons for the body the provider returns: armour and clothing are drawn for the wearer's morphology.</summary>
    internal void UseWearer(Func<DaggerfallCharacterIdentity> provider) => wearer = provider;

    internal InventoryItemPresentation DescribeItem(string key, string itemId, ulong quantity, int? gridSlot = null, string[]? equippedSlots = null,
        DaggerfallItemOwner? owner = null)
    {
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(itemId));
        DaggerfallItemInstanceMetadata? metadata = Metadata(key, owner ?? itemOwner);
        ItemDisplay display = Display(definition, metadata);
        string details = display.Details;
        bool detailsStateFacts = false;
        string[] notes = [];
        if (metadata is not null && itemCondition is not null && metadata.Enchantment is null
            && metadata.MadeEnchantment is null && metadata.Conjuration is null
            && definition.Template?.Groups.Contains("Paintings") != true && definition.Template?.Index != 278)
        {
            int record = definition.Id.Value == "arrow" || definition.Template?.Index == 131 ? 1011
                : definition.Weapon is not null ? 1001
                : definition.Shield is not null || definition.Template?.Index == 107 ? 1014
                : definition.Armor is not null ? 1000
                : metadata.BookId is not null ? 1009
                : metadata.PotionRecipeKey is not null ? 1008
                : metadata.CapturedSoulMobileId is not null || definition.Template?.Index == 275 ? 1004
                : metadata.CreditValue is not null ? 1007 : 1003;
            var context = DaggerfallTextContext.Empty with { Item = DaggerfallTextContexts.Item(definitions,
                definition, metadata, DaggerfallItemNames.BaseName(definitions, definition), quantity, CurrentValue(definition, metadata)) };
            details = definitions.TextPresentation.Resolve(new(DaggerfallTextKind.Resource,
                record.ToString(CultureInfo.InvariantCulture)), context).Text.Trim();
            detailsStateFacts = true;
            notes = record switch
            {
                1009 when context.Item.BookAuthor is { Length: > 0 } author => [$"By {author}"],
                1004 when context.Item.HeldSoul is { Length: > 0 } soul => [$"Holding: {soul}"],
                _ => [],
            };
        }
        else if (display.Facts is { } facts)
        {
            // A magical or conjured item's text repeats its condition and figures; it adds only these.
            detailsStateFacts = true;
            notes = facts;
        }
        // Eligibility belongs to the carried rows only: a loot or container row is taken before it is used.
        bool carried = owner is null;
        string? equipRefusal = carried ? moves.EquipRefusal(definition, key.StartsWith("unique:", StringComparison.Ordinal), metadata?.CurrentCondition) : null;
        string? dropRefusal = !carried ? null
            : equippedSlots is { Length: > 0 } ? "Unequip that item before dropping it."
            : metadata is not null ? canDropQuestItem?.Invoke(metadata) : null;
        (string? icon, PaperDollTint? iconTint) = Icon(definition, metadata);
        return new InventoryItemPresentation(key, itemId, display.Label, quantity.ToString(CultureInfo.InvariantCulture),
            metadata?.WeightClassicUnits is ulong weight ? checked((int)(weight / 100)) : definition.Weight, CurrentValue(definition, metadata), details, icon, gridSlot, equippedSlots ?? [],
            definitions.EquipmentSlots.Keys.Select(slot => slot.Value).Where(slot => DaggerfallEquipmentPolicy.IsCompatible(definitions, definition, slot)).ToArray(),
            display.Condition, display.Identified,
            CanUse: carried && metadata is not null && itemUse?.CanUse(itemId, metadata) == true,
            CanEquip: carried && equipRefusal is null, EquipRefusal: equipRefusal,
            CanUseMagic: carried && metadata is not null && (metadata.HasEnchantment || metadata.PotionRecipeKey is not null)
                && itemUse?.CanUse(itemId, metadata) == true,
            Category: Category(definition, metadata), Stackable: key.StartsWith("stack:", StringComparison.Ordinal), DropRefusal: dropRefusal,
            Stats: Stats(definition, metadata)) { WeightKilograms = DaggerfallEncumbrancePolicy.UnitKilograms(definition, metadata), IconTint = iconTint, Enchanted = metadata?.HasEnchantment == true, DetailsStateFacts = detailsStateFacts, Notes = notes };
    }

    /// <summary>The durable meaning of one equipped item, or null before item meaning is composed.</summary>
    internal DaggerfallItemInstanceMetadata? EquippedMetadata(ulong entity) => Metadata(UniqueKey(entity), itemOwner);

    private DaggerfallItemInstanceMetadata? Metadata(string key, DaggerfallItemOwner? owner)
    {
        if (itemInstances is null) return null;
        DaggerfallItemOwner resolvedOwner = owner?.Validate() ?? throw new InvalidOperationException("Valued inventory rows require a durable owner.");
        return key.StartsWith("unique:", StringComparison.Ordinal)
            ? itemInstances!.RequireUnique(uniqueIdentity!(ParseUniqueEntity(key)))
            : key.StartsWith("stack:", StringComparison.Ordinal)
                ? itemInstances!.RequireStack(resolvedOwner, Rusty.Engine.Mechanics.InventoryStackId.Parse(key["stack:".Length..]))
                : throw new InvalidOperationException($"Inventory item key '{key}' does not name a unique item or stack.");
    }

    private int CurrentValue(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata? metadata) =>
        valuation is null ? definition.Value : valuation.CurrentValue(definition, metadata ?? throw new InvalidOperationException("Valued inventory rows require durable metadata."));

    internal string DescribeCreatedItem(DaggerfallCreatedItem item) => DescribeCreatedItem(definitions, item);

    internal static string DescribeCreatedItem(DaggerfallDefinitions definitions, DaggerfallCreatedItem item) =>
        DaggerfallItemNames.Label(definitions, definitions.RequireItem(new(item.Item.Value)), item.Metadata);

    private ItemDisplay Display(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata? metadata)
    {
        string baseLabel = DaggerfallItemNames.Label(definitions, definition, metadata);
        if (metadata is null || itemCondition is null) return new(baseLabel, Details(definition), null, true);
        if (!string.Equals(metadata.ItemId, definition.Id.Value, StringComparison.Ordinal))
            throw new InvalidOperationException($"Live inventory definition '{definition.Id.Value}' does not match durable metadata '{metadata.ItemId}'.");
        DaggerfallItemCondition condition = itemCondition.Condition(metadata);
        ItemConditionPresentation presentedCondition = new(condition.Current, condition.Maximum, condition.Percentage, condition.IsBroken);
        string conditionDetail = condition.Maximum == 0 ? string.Empty : $"Condition: {condition.Current}/{condition.Maximum} ({condition.Percentage}%); ";
        // What the item says beyond its name, condition and figures, which the row's own fields state.
        List<string> facts = [];
        if (metadata.CapturedSoulMobileId is int soul) { conditionDetail += $"Captured soul: {SoulName(soul)}; "; facts.Add($"Holding: {SoulName(soul)}"); }
        if (metadata.Conjuration is { } conjured)
        {
            string remaining = ConjurationRemaining(conjured.ExpiresAtMinute);
            conditionDetail += $"Conjured: {remaining}; ";
            facts.Add($"Conjured: {remaining}");
        }
        string[]? Facts(string? magic) => magic is null && facts.Count == 0 ? null : [.. facts, .. magic is null ? [] : new[] { magic }];
        if (metadata.PotionRecipeKey is int recipeKey)
        {
            var recipe = definitions.Magic.PotionRecipes[recipeKey];
            bool sheet = definition.Template?.Index == 278;
            string ingredients = string.Join(", ", recipe.Ingredients.Select(value => $"{value.Count} × {definitions.RequireItem(new(value.Item)).Template!.Name}"));
            return new(sheet ? $"Recipe: {recipe.Name}" : $"Potion of {recipe.Name}", sheet ? ingredients : "Drink to apply its effects to yourself.", presentedCondition, true);
        }
        const string Unidentified = "A magical item: identify it to learn its powers.";
        if (metadata.MadeEnchantment is { } made)
        {
            string powers = string.Join(", ", made.Settings.Select(value => definitions.Magic.EnchantmentSettings[value.Key].DisplayName));
            return new(baseLabel, conditionDetail + (metadata.Identified ? powers : "Unidentified magical item"),
                presentedCondition, metadata.Identified) { Facts = Facts(metadata.Identified ? $"Enchantment: {powers}" : Unidentified) };
        }
        if (metadata.Enchantment is null) return new(baseLabel, conditionDetail + Details(definition), presentedCondition, true) { Facts = Facts(null) };
        if (!definitions.Magic.MagicItems.TryGetValue(metadata.Enchantment, out DaggerfallMagicItemDefinition? magic))
            throw new InvalidOperationException($"Item '{metadata.ItemId}' names unpublished magic metadata '{metadata.Enchantment}'.");
        if (!metadata.Identified)
            return new(baseLabel, conditionDetail + "Unidentified magical item", presentedCondition, false) { Facts = Facts(Unidentified) };
        string namedMagic = baseLabel;
        // The donor names each identified enchantment by its setting ("Cast When Used: Fireball",
        // "Enhances Skill: Long Blade"); an artifact power or an unpublished setting keeps its meaning.
        string effects = string.Join(", ", magic.Enchantments.Select(enchantment =>
            definitions.Magic.EnchantmentSettings.TryGetValue($"enchantment.{enchantment.Type}.{enchantment.Param}", out DaggerfallEnchantmentSetting named)
                ? named.DisplayName : Label(enchantment.ParamMeaning)));
        return new(namedMagic, conditionDetail + Details(definition) + $"; Enchantment: {effects}", presentedCondition, true) { Facts = Facts($"Enchantment: {effects}") };
    }

    /// <summary>The creature a soul gem holds, named by the actor the pack publishes for that mobile.</summary>
    private string SoulName(int mobile) =>
        definitions.Actors.Values.FirstOrDefault(actor => actor.MobileId == mobile) is { } actor ? Label(actor.Id.Value) : "an unknown creature";

    /// <summary>How long a conjured item lasts, in game time from now.</summary>
    private string ConjurationRemaining(long expiresAtMinute)
    {
        if (loanCalendar is null) return "fades in time";
        long remaining = expiresAtMinute - DaggerfallLoanPolicy.ClassicMinute(loanCalendar());
        return remaining <= 0 ? "fading now"
            : "fades in " + DaggerfallCalendar.DescribeDuration(checked(remaining * DaggerfallCalendar.SecondsPerMinute));
    }

    /// <summary><paramref name="Details"/> in full, and <see cref="Facts"/>: the part of it beyond the row's own fields, where it is not the classic text.</summary>
    private sealed record ItemDisplay(string Label, string Details, ItemConditionPresentation? Condition, bool Identified)
    {
        internal string[]? Facts { get; init; }
    }

    private static bool TryParseUnique(InventoryItemPresentation row, out UniqueInventoryItem item)
    {
        item = default;
        if (!row.Key.StartsWith("unique:", StringComparison.Ordinal)
            || !ulong.TryParse(row.Key.AsSpan("unique:".Length), System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong entity))
            return false;
        item = new UniqueInventoryItem(entity, new InventoryItemId(row.Definition));
        return true;
    }

    internal static string UniqueKey(ulong entity) => DaggerfallEquipmentMoves.LayoutKey(entity);
    internal static string StackKey(Rusty.Engine.Mechanics.InventoryStackId stack) => DaggerfallEquipmentMoves.LayoutKey(stack);
    private static ulong ParseUniqueEntity(string key) => ulong.TryParse(key.AsSpan("unique:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong entity)
        ? entity
        : throw new InvalidOperationException($"Inventory item key '{key}' does not name a unique item.");
    private static bool TryParseStack(InventoryItemPresentation row, out Rusty.Engine.Mechanics.InventoryStackId stack)
    {
        stack = null!;
        if (!row.Key.StartsWith("stack:", StringComparison.Ordinal)) return false;
        stack = Rusty.Engine.Mechanics.InventoryStackId.Parse(row.Key.Substring("stack:".Length));
        return true;
    }
    internal static string Label(string id) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));
    private static string Details(DaggerfallItemDefinition item) => item.Weapon is { } weapon
        ? $"Damage {weapon.MinimumDamage}–{weapon.MaximumDamage}; {Label(weapon.Material)}; {Label(weapon.Skill)}"
        : item.Armor is { } armor ? $"Armor: {Label(armor.Material)}" : item.Shield is not null ? "Shield" : item.IsFungible ? "Stackable item" : "Equipment";
}

/// <summary>
/// A ground pile of one kind of item standing as that item with refreshed art: its world image, and where it is in
/// a metal or dye, the mask of what <paramref name="Color"/> (a CSS colour) brightened by <paramref name="Brightness"/>
/// recolours. It is sized from its classic image, <paramref name="ClassicHeight"/> tall: in classic pixels for an icon,
/// or for a worn item's image (<paramref name="Doll"/>), in paper-doll units, which are at a body's scale.
/// </summary>
internal sealed record DaggerfallGroundItemLook(DaggerfallArtWorldImage Image, DaggerfallArtWorldImage? Mask, string? Color, double Brightness,
    double ClassicHeight, bool Doll);
