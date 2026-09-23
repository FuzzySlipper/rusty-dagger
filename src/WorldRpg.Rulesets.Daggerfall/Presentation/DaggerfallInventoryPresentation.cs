using System.Globalization;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record InventoryItemPresentation(string Key, string Definition, string Label, string Quantity, int Weight, int Value,
    string Details, string? Icon, int? GridSlot, string[] EquippedSlots, string[] CompatibleSlots,
    ItemConditionPresentation? Condition = null, bool Identified = true);
internal sealed record ItemConditionPresentation(int Current, int Maximum, int Percentage, bool Broken);
internal sealed record EquipmentSlotPresentation(string Id, string Label, string? ItemKey);
internal sealed record EquipmentChangePresentation(string Cue, int RightHandDelayMilliseconds, int LeftHandDelayMilliseconds);
internal sealed record InventoryPresentation(string Revision, InventoryItemPresentation[] Items, EquipmentSlotPresentation[] Slots, string Message,
    EquipmentChangePresentation? EquipmentChange = null, DaggerfallEncumbrance? Encumbrance = null, DaggerfallCurrencyTotals? Currency = null);

/// <summary>Daggerfall inventory projection and UI-action translation; quantities and equipment remain Engine facts.</summary>
internal sealed class DaggerfallInventoryPresentation
{
    private readonly DaggerfallEquipmentMoves moves;
    private readonly DaggerfallDefinitions definitions;
    private readonly IReadOnlyDictionary<string, string> icons;
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
    internal event Action<DaggerfallReadableBook>? BookOpened;
    internal string Message { get; private set; } = "Drag items between the grid and compatible equipment slots.";
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

    internal InventoryPresentation Read()
    {
        Rusty.Engine.Mechanics.InventoryView current = moves.ReadInventory();
        EquipmentRead equipped = moves.ReadEquipment();
        var items = current.UniqueItems.Select(item => (Key: UniqueKey(item.Entity.Value), Definition: item.Definition.Value, Quantity: 1UL,
                Slots: equipped.Assignments.Where(assignment => assignment.Item.EntityId == item.Entity.Value).Select(assignment => assignment.Slot.Value).ToArray()))
            .Concat(current.Stacks.Select(stack => (Key: StackKey(stack.Id), Definition: stack.Definition.Value, Quantity: stack.Quantity, Slots: Array.Empty<string>())))
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        moves.ReconcileLayout();
        return new InventoryPresentation($"{current.StoreRevision}:{moves.LayoutRevision}:{MetadataRevision}", items.Select(item =>
            DescribeItem(item.Key, item.Definition, item.Quantity, item.Slots.Length == 0 ? moves.GridPosition(item.Key) : null, item.Slots)).ToArray(),
            definitions.EquipmentSlots.Values.Select(slot => new EquipmentSlotPresentation(slot.Id.Value, Label(slot.Id.Value),
                equipped.TryGet(new EquipmentSlotId(slot.Id.Value), out UniqueInventoryItem item) ? UniqueKey(item.EntityId) : null)).ToArray(), Message,
            LastEquipmentChange is { } change ? new(change.Cue.ToString().ToLowerInvariant(), change.Timing.RightHandMilliseconds, change.Timing.LeftHandMilliseconds) : null,
            encumbrance?.Read(), currency?.Read());
    }

    /// <summary>
    /// Translates one semantic UI action into a typed equipment move and renders the outcome. The
    /// gameplay decision lives in <see cref="DaggerfallEquipmentMoves"/>; this only speaks UI keys.
    /// Staleness is handled per item against the fresh read and the coordinators' containment checks,
    /// not by rejecting the whole action on a revision mismatch.
    /// </summary>
    internal void Move(DaggerfallPlayerUiAction action)
    {
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
        InventoryPresentation current = Read();
        InventoryItemPresentation? row = action.Item is null ? null : current.Items.SingleOrDefault(item => item.Key == action.Item);
        if (row is null) { Message = "That item is no longer in your inventory."; return; }
        Message = $"{row.Label}: {row.Details}";
    }

    /// <summary>Routes a current item through a named domain operation; rejected or deferred use conserves it.</summary>
    internal void Use(DaggerfallPlayerUiAction action)
    {
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
        try
        {
            _ = ground.Drop(selection, position.Value, ulong.Parse(current.Revision.Split(':')[0], CultureInfo.InvariantCulture));
            Message = $"Dropped {row.Label}.";
        }
        catch (Exception rejection) when (rejection is InvalidOperationException or ArgumentException)
        {
            Message = $"Cannot drop that item. {rejection.Message}";
        }
    }

    internal InventoryItemPresentation DescribeItem(string key, string itemId, ulong quantity, int? gridSlot = null, string[]? equippedSlots = null,
        DaggerfallItemOwner? owner = null)
    {
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(itemId));
        DaggerfallItemInstanceMetadata? metadata = Metadata(key, owner ?? itemOwner);
        ItemDisplay display = Display(definition, metadata);
        return new InventoryItemPresentation(key, itemId, display.Label, quantity.ToString(CultureInfo.InvariantCulture),
            definition.Weight, CurrentValue(definition, metadata), display.Details, icons.GetValueOrDefault(itemId), gridSlot, equippedSlots ?? [],
            definitions.EquipmentSlots.Keys.Select(slot => slot.Value).Where(slot => DaggerfallEquipmentPolicy.IsCompatible(definitions, definition, slot)).ToArray(),
            display.Condition, display.Identified);
    }

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

    private ItemDisplay Display(DaggerfallItemDefinition definition, DaggerfallItemInstanceMetadata? metadata)
    {
        string baseLabel = definition.Template?.Name ?? Label(definition.Id.Value);
        if (metadata is null || itemCondition is null) return new(baseLabel, Details(definition), null, true);
        if (!string.Equals(metadata.ItemId, definition.Id.Value, StringComparison.Ordinal))
            throw new InvalidOperationException($"Live inventory definition '{definition.Id.Value}' does not match durable metadata '{metadata.ItemId}'.");
        DaggerfallItemCondition condition = itemCondition.Condition(metadata);
        ItemConditionPresentation presentedCondition = new(condition.Current, condition.Maximum, condition.Percentage, condition.IsBroken);
        string conditionDetail = condition.Maximum == 0 ? string.Empty : $"Condition: {condition.Current}/{condition.Maximum} ({condition.Percentage}%); ";
        if (metadata.Enchantment is null) return new(baseLabel, conditionDetail + Details(definition), presentedCondition, true);
        if (!definitions.Magic.MagicItems.TryGetValue(metadata.Enchantment, out DaggerfallMagicItemDefinition? magic))
            throw new InvalidOperationException($"Item '{metadata.ItemId}' names unpublished magic metadata '{metadata.Enchantment}'.");
        if (!metadata.Identified)
            return new(baseLabel, conditionDetail + "Unidentified magical item", presentedCondition, false);
        string namedMagic = magic.Name.Replace("%it", baseLabel, StringComparison.Ordinal);
        string effects = string.Join(", ", magic.Enchantments.Select(enchantment => Label(enchantment.ParamMeaning)));
        return new(namedMagic, conditionDetail + Details(definition) + $"; Enchantment: {effects}", presentedCondition, true);
    }

    private sealed record ItemDisplay(string Label, string Details, ItemConditionPresentation? Condition, bool Identified);

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
