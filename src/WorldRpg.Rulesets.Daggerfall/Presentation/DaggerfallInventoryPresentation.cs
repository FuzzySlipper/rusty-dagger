using System.Globalization;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record InventoryItemPresentation(string Key, string Definition, string Label, string Quantity, int Weight, int Value,
    string Details, string? Icon, int? GridSlot, string[] EquippedSlots, string[] CompatibleSlots);
internal sealed record EquipmentSlotPresentation(string Id, string Label, string? ItemKey);
internal sealed record EquipmentChangePresentation(string Cue, int RightHandDelayMilliseconds, int LeftHandDelayMilliseconds);
internal sealed record InventoryPresentation(string Revision, InventoryItemPresentation[] Items, EquipmentSlotPresentation[] Slots, string Message,
    EquipmentChangePresentation? EquipmentChange = null);

/// <summary>Daggerfall inventory projection and UI-action translation; quantities and equipment remain Engine facts.</summary>
internal sealed class DaggerfallInventoryPresentation
{
    private readonly DaggerfallEquipmentMoves moves;
    private readonly DaggerfallDefinitions definitions;
    private readonly IReadOnlyDictionary<string, string> icons;
    private DaggerfallItemValuation? valuation;
    private DaggerfallItemInstances? itemInstances;
    private DaggerfallItemOwner? itemOwner;
    private Func<ulong, ulong>? uniqueIdentity;
    internal string Message { get; private set; } = "Drag items between the grid and compatible equipment slots.";
    internal DaggerfallEquipmentChange? LastEquipmentChange { get; private set; }

    internal DaggerfallInventoryPresentation(
        DaggerfallEquipmentMoves moves,
        DaggerfallDefinitions definitions,
        IReadOnlyDictionary<string, string> icons)
    {
        this.moves = moves;
        this.definitions = definitions;
        this.icons = icons;
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

    internal InventoryPresentation Read()
    {
        Rusty.Engine.Mechanics.InventoryView current = moves.ReadInventory();
        EquipmentRead equipped = moves.ReadEquipment();
        var items = current.UniqueItems.Select(item => (Key: UniqueKey(item.Entity.Value), Definition: item.Definition.Value, Quantity: 1UL,
                Slots: equipped.Assignments.Where(assignment => assignment.Item.EntityId == item.Entity.Value).Select(assignment => assignment.Slot.Value).ToArray()))
            .Concat(current.Stacks.Select(stack => (Key: StackKey(stack.Id), Definition: stack.Definition.Value, Quantity: stack.Quantity, Slots: Array.Empty<string>())))
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        moves.ReconcileLayout();
        return new InventoryPresentation($"{current.StoreRevision}:{moves.LayoutRevision}", items.Select(item =>
            DescribeItem(item.Key, item.Definition, item.Quantity, item.Slots.Length == 0 ? moves.GridPosition(item.Key) : null, item.Slots)).ToArray(),
            definitions.EquipmentSlots.Values.Select(slot => new EquipmentSlotPresentation(slot.Id.Value, Label(slot.Id.Value),
                equipped.TryGet(new EquipmentSlotId(slot.Id.Value), out UniqueInventoryItem item) ? UniqueKey(item.EntityId) : null)).ToArray(), Message,
            LastEquipmentChange is { } change ? new(change.Cue.ToString().ToLowerInvariant(), change.Timing.RightHandMilliseconds, change.Timing.LeftHandMilliseconds) : null);
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

    internal InventoryItemPresentation DescribeItem(string key, string itemId, ulong quantity, int? gridSlot = null, string[]? equippedSlots = null)
    {
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId(itemId));
        return new InventoryItemPresentation(key, itemId, definition.Template?.Name ?? Label(itemId), quantity.ToString(CultureInfo.InvariantCulture),
            definition.Weight, CurrentValue(key, definition), Details(definition), icons.GetValueOrDefault(itemId), gridSlot, equippedSlots ?? [],
            definitions.EquipmentSlots.Keys.Select(slot => slot.Value).Where(slot => DaggerfallEquipmentPolicy.IsCompatible(definitions, definition, slot)).ToArray());
    }

    private int CurrentValue(string key, DaggerfallItemDefinition definition)
    {
        if (valuation is null) return definition.Value;
        DaggerfallItemInstanceMetadata metadata = key.StartsWith("unique:", StringComparison.Ordinal)
            ? itemInstances!.RequireUnique(uniqueIdentity!(ParseUniqueEntity(key)))
            : key.StartsWith("stack:", StringComparison.Ordinal)
                ? itemInstances!.RequireStack(itemOwner!, Rusty.Engine.Mechanics.InventoryStackId.Parse(key["stack:".Length..]))
                : throw new InvalidOperationException($"Inventory item key '{key}' does not name a unique item or stack.");
        return valuation.CurrentValue(definition, metadata);
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
