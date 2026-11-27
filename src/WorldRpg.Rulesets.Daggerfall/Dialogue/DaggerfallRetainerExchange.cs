using System.Globalization;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using KitSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One item row in the retainer gear exchange screen (retainer or player side).</summary>
internal sealed record DaggerfallRetainerItemView(
    string Key,
    string Definition,
    string Label,
    ulong Quantity,
    int Weight,
    int Value,
    double WeightKilograms,
    int CurrentCondition,
    int MaximumCondition,
    bool Identified,
    bool Enchanted,
    string? Icon,
    PaperDollTint? IconTint,
    DaggerfallItemInstanceMetadata? IconMetadata,
    string[] EquippedSlots,
    string[] EquippedSlotLabels,
    string[] CompatibleSlots,
    bool CanEquip,
    bool CanUnequip,
    string? EquipRefusal,
    string? TransferRefusal);

/// <summary>Live retainer gear exchange projection shown on a hired party member's person screen.</summary>
internal sealed record DaggerfallRetainerView(
    string Revision,
    long ActorId,
    string Name,
    string Role,
    string Result,
    DaggerfallRetainerItemView[] RetainerItems,
    DaggerfallRetainerItemView[] PlayerItems);

/// <summary>
/// Canonical item transfer and equipment management with hired retainers (companions and wagon drivers).
/// Every transfer routes through Kit's <see cref="MechanicsInventoryContainerCoordinator"/> and
/// <see cref="DaggerfallItemInstances"/>, and every equip/unequip routes through Kit's
/// <see cref="MechanicsEquipmentCoordinator"/> over the retainer's live Engine components.
/// </summary>
internal sealed class DaggerfallRetainerExchange
{
    private const string UnequipPlayerBeforeGiving = "Unequip that item before giving it.";
    private const string UnequipRetainerBeforeTaking = "Unequip before taking.";
    private const string EncumbranceRefusal = "You cannot carry any more.";
    private const string BrokenItemRefusal = "That item is broken.";

    private readonly ActorsState _actors;
    private readonly DaggerfallActorInventories _actorInventories;
    private readonly MechanicsInventoryContainerCoordinator _containers;
    private readonly MechanicsInventoryCoordinator _playerInventory;
    private readonly MechanicsEquipmentCoordinator _playerEquipment;
    private readonly DaggerfallItemInstances _itemInstances;
    private readonly DaggerfallQuestItemExits _exits;
    private readonly DaggerfallEncumbrancePolicy _encumbrance;
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallPartyHireService _partyHire;
    private readonly Func<DaggerfallDialogueService?> _dialogue;
    private readonly DaggerfallItemValuation? _valuation;

    private long? _lastActorId;
    private string _lastResult = string.Empty;

    internal DaggerfallRetainerExchange(
        ActorsState actors,
        DaggerfallActorInventories actorInventories,
        MechanicsInventoryContainerCoordinator containers,
        MechanicsInventoryCoordinator playerInventory,
        MechanicsEquipmentCoordinator playerEquipment,
        DaggerfallItemInstances itemInstances,
        DaggerfallQuestItemExits exits,
        DaggerfallEncumbrancePolicy encumbrance,
        DaggerfallDefinitions definitions,
        DaggerfallPartyHireService partyHire,
        Func<DaggerfallDialogueService?> dialogue,
        DaggerfallItemValuation? valuation = null)
    {
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _actorInventories = actorInventories ?? throw new ArgumentNullException(nameof(actorInventories));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _playerInventory = playerInventory ?? throw new ArgumentNullException(nameof(playerInventory));
        _playerEquipment = playerEquipment ?? throw new ArgumentNullException(nameof(playerEquipment));
        _itemInstances = itemInstances ?? throw new ArgumentNullException(nameof(itemInstances));
        _exits = exits ?? throw new ArgumentNullException(nameof(exits));
        _encumbrance = encumbrance ?? throw new ArgumentNullException(nameof(encumbrance));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _partyHire = partyHire ?? throw new ArgumentNullException(nameof(partyHire));
        _dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
        _valuation = valuation;
    }

    /// <summary>Reads the active retainer exchange view for the current conversation target, or null when not talking to a hired retainer.</summary>
    internal DaggerfallRetainerView? Read()
    {
        if (!TryResolveActiveRetainer(out DaggerfallNpc? npc, out ActorState? live, out ActorPartyRole role))
        {
            _lastActorId = null;
            _lastResult = string.Empty;
            return null;
        }
        if (_lastActorId != npc!.DurableId)
        {
            _lastActorId = npc.DurableId;
            _lastResult = string.Empty;
        }

        MechanicsInventoryCoordinator? retainerInvCoord = _actorInventories.InventoryFor(npc.DurableId);
        if (retainerInvCoord is null)
            return null;
        MechanicsEquipmentCoordinator retainerEquipCoord = _actorInventories.EquipmentFor(npc.DurableId);

        InventoryView playerInv = _playerInventory.Read();
        EquipmentRead playerEquip = _playerEquipment.Read();
        InventoryView retainerInv = retainerInvCoord.Read();
        EquipmentRead retainerEquip = retainerEquipCoord.Read();
        string revision = RevisionOf(playerInv, retainerInv);
        DaggerfallItemOwner retainerOwner = DaggerfallItemOwner.Actor(npc.DurableId);

        DaggerfallRetainerItemView[] retainerItems = BuildRetainerRows(retainerInv, retainerEquip, retainerOwner);
        DaggerfallRetainerItemView[] playerItems = BuildPlayerRows(playerInv, playerEquip);
        string name = npc.DisplayName ?? npc.Role;
        string roleLabel = role == ActorPartyRole.Driver ? "Wagon driver" : "Companion";
        return new(revision, npc.DurableId, name, roleLabel, _lastResult, retainerItems, playerItems);
    }

    /// <summary>Transfers a player pack item or stack quantity to the active retainer.</summary>
    internal string Give(string? revision, string? itemKey, ulong? amount)
    {
        if (!TryResolveActiveRetainer(out DaggerfallNpc? npc, out ActorState? retainerLive, out _))
            return Record("That retainer is no longer available.");
        MechanicsInventoryCoordinator? retainerInvCoord = _actorInventories.InventoryFor(npc!.DurableId);
        if (retainerInvCoord is null)
            return Record("That retainer is no longer available.");

        InventoryView playerInv = _playerInventory.Read();
        InventoryView retainerInv = retainerInvCoord.Read();
        if (!string.Equals(revision, RevisionOf(playerInv, retainerInv), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(itemKey)
            || !DaggerfallContainerSelection.TryParse(playerInv, itemKey, amount, out InventoryContainerSelection? selection))
            return Record("Inventory changed. Choose the item again.");

        if (selection!.UniqueEntityId is ulong uniqueEntity
            && _playerEquipment.Read().Assignments.Any(a => a.Item.EntityId == uniqueEntity))
            return Record(UnequipPlayerBeforeGiving);

        string label = DescribeLabel(selection, DaggerfallItemOwner.Player);
        try
        {
            _exits.RequireMayLeave(selection, _containers.Entities);
            DaggerfallItemOwner retainerOwner = DaggerfallItemOwner.Actor(npc.DurableId);
            InventoryContainerSelection prepared = PrepareStackTransfer(
                selection, DaggerfallItemOwner.Player, retainerOwner, retainerLive!.Actor.Entity,
                $"daggerfall.actor.{npc.DurableId}");
            InventoryContainerTransferReceipt receipt = _containers.Transfer(_actors.Player.Actor.Entity, retainerLive.Actor.Entity, prepared);
            _itemInstances.ApplyTransfer(receipt, _containers.Read(_actors.Player.Actor.Entity), _containers.Entities,
                DaggerfallItemOwner.Player, retainerOwner);
            string name = npc.DisplayName ?? npc.Role;
            return Record(selection.Quantity > 1
                ? $"Gave {selection.Quantity} × {label} to {name}."
                : $"Gave {label} to {name}.");
        }
        catch (DaggerfallItemExitRefusedException refused)
        {
            return Record(refused.Message);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or MechanicsException)
        {
            return Record("That item cannot be given right now.");
        }
    }

    /// <summary>Transfers an unequipped item or stack quantity from the active retainer to the player's pack.</summary>
    internal string Take(string? revision, string? itemKey, ulong? amount)
    {
        if (!TryResolveActiveRetainer(out DaggerfallNpc? npc, out ActorState? retainerLive, out _))
            return Record("That retainer is no longer available.");
        MechanicsInventoryCoordinator? retainerInvCoord = _actorInventories.InventoryFor(npc!.DurableId);
        if (retainerInvCoord is null)
            return Record("That retainer is no longer available.");

        InventoryView playerInv = _playerInventory.Read();
        InventoryView retainerInv = retainerInvCoord.Read();
        if (!string.Equals(revision, RevisionOf(playerInv, retainerInv), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(itemKey)
            || !DaggerfallContainerSelection.TryParse(retainerInv, itemKey, amount, out InventoryContainerSelection? selection))
            return Record("Inventory changed. Choose the item again.");

        MechanicsEquipmentCoordinator retainerEquip = _actorInventories.EquipmentFor(npc.DurableId);
        if (selection!.UniqueEntityId is ulong uniqueEntity
            && retainerEquip.Read().Assignments.Any(a => a.Item.EntityId == uniqueEntity))
            return Record(UnequipRetainerBeforeTaking);

        DaggerfallItemOwner retainerOwner = DaggerfallItemOwner.Actor(npc.DurableId);
        DaggerfallItemDefinition def = _definitions.RequireItem(new DaggerfallItemId(selection.Item.Value));
        DaggerfallItemInstanceMetadata? meta = ResolveMetadata(selection, retainerOwner);
        if (!_encumbrance.CanCarry(def, selection.Quantity, meta))
            return Record(EncumbranceRefusal);

        string label = DescribeLabel(selection, retainerOwner);
        try
        {
            InventoryContainerSelection prepared = PrepareStackTransfer(
                selection, retainerOwner, DaggerfallItemOwner.Player, _actors.Player.Actor.Entity,
                $"daggerfall.player.retainer.{npc.DurableId}");
            InventoryContainerTransferReceipt receipt = _containers.Transfer(retainerLive!.Actor.Entity, _actors.Player.Actor.Entity, prepared);
            _itemInstances.ApplyTransfer(receipt, _containers.Read(retainerLive.Actor.Entity), _containers.Entities,
                retainerOwner, DaggerfallItemOwner.Player);
            string name = npc.DisplayName ?? npc.Role;
            return Record(selection.Quantity > 1
                ? $"Took {selection.Quantity} × {label} from {name}."
                : $"Took {label} from {name}.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException or MechanicsException)
        {
            return Record("That item cannot be taken right now.");
        }
    }

    /// <summary>Equips a carried unique item on the active retainer through Kit's equipment owner.</summary>
    internal string Equip(string? revision, string? itemKey, string? targetSlot = null)
    {
        if (!TryResolveActiveRetainer(out DaggerfallNpc? npc, out _, out _))
            return Record("That retainer is no longer available.");
        MechanicsInventoryCoordinator? retainerInvCoord = _actorInventories.InventoryFor(npc!.DurableId);
        if (retainerInvCoord is null)
            return Record("That retainer is no longer available.");

        InventoryView playerInv = _playerInventory.Read();
        InventoryView retainerInv = retainerInvCoord.Read();
        if (!string.Equals(revision, RevisionOf(playerInv, retainerInv), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(itemKey)
            || !DaggerfallContainerSelection.TryParse(retainerInv, itemKey, 1, out InventoryContainerSelection? selection)
            || selection!.UniqueEntityId is not ulong uniqueEntity)
            return Record("Inventory changed. Choose the item again.");

        DaggerfallItemDefinition definition = _definitions.RequireItem(new DaggerfallItemId(selection.Item.Value));
        DaggerfallItemOwner retainerOwner = DaggerfallItemOwner.Actor(npc.DurableId);
        DaggerfallItemInstanceMetadata? meta = ResolveMetadata(selection, retainerOwner);
        if (meta is { CurrentCondition: < 1 })
            return Record(BrokenItemRefusal);

        string[] compatible = CompatibleSlotsFor(definition);
        if (compatible.Length == 0)
            return Record("This item cannot be equipped.");

        MechanicsEquipmentCoordinator equipment = _actorInventories.EquipmentFor(npc.DurableId);
        EquipmentRead before = equipment.Read();

        string chosenSlot;
        if (!string.IsNullOrWhiteSpace(targetSlot))
        {
            if (!compatible.Contains(targetSlot, StringComparer.Ordinal))
                return Record("That item does not fit that equipment slot.");
            chosenSlot = targetSlot;
        }
        else
        {
            chosenSlot = compatible.FirstOrDefault(slot => !before.TryGet(new KitSlotId(slot), out _)) ?? compatible[0];
        }

        KitUniqueItem item = new(uniqueEntity, new InventoryItemId(selection.Item.Value));
        IReadOnlyList<KitSlotId> targetSlots = DaggerfallEquipmentPolicy.TargetSlots(definition, new KitSlotId(chosenSlot));
        IReadOnlyList<KitUniqueItem> conflicts = DaggerfallEquipmentPolicy.ConflictingEquipped(_definitions, before, item, definition, targetSlots);
        try
        {
            if (before.Assignments.Any(a => a.Item.EntityId == uniqueEntity) || conflicts.Count > 1)
                equipment.Reassign(item, targetSlots, conflicts);
            else if (conflicts.Count == 1)
                equipment.Swap(conflicts[0], item, targetSlots);
            else
                equipment.Equip(item, targetSlots);
            string name = npc.DisplayName ?? npc.Role;
            string label = DescribeLabel(selection, retainerOwner);
            return Record($"{name} equipped {label}.");
        }
        catch (Exception error) when (error is MechanicsException or ArgumentException or InvalidOperationException)
        {
            return Record("That item cannot be equipped right now.");
        }
    }

    /// <summary>Unequips an equipped item from the active retainer through Kit's equipment owner.</summary>
    internal string Unequip(string? revision, string? itemKey)
    {
        if (!TryResolveActiveRetainer(out DaggerfallNpc? npc, out _, out _))
            return Record("That retainer is no longer available.");
        MechanicsInventoryCoordinator? retainerInvCoord = _actorInventories.InventoryFor(npc!.DurableId);
        if (retainerInvCoord is null)
            return Record("That retainer is no longer available.");

        InventoryView playerInv = _playerInventory.Read();
        InventoryView retainerInv = retainerInvCoord.Read();
        if (!string.Equals(revision, RevisionOf(playerInv, retainerInv), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(itemKey)
            || !DaggerfallContainerSelection.TryParse(retainerInv, itemKey, 1, out InventoryContainerSelection? selection)
            || selection!.UniqueEntityId is not ulong uniqueEntity)
            return Record("Inventory changed. Choose the item again.");

        MechanicsEquipmentCoordinator equipment = _actorInventories.EquipmentFor(npc.DurableId);
        if (!equipment.Read().Assignments.Any(a => a.Item.EntityId == uniqueEntity))
            return Record("That item is not equipped.");

        KitUniqueItem item = new(uniqueEntity, new InventoryItemId(selection.Item.Value));
        try
        {
            equipment.Unequip(item);
            string name = npc.DisplayName ?? npc.Role;
            string label = DescribeLabel(selection, DaggerfallItemOwner.Actor(npc.DurableId));
            return Record($"{name} unequipped {label}.");
        }
        catch (Exception error) when (error is MechanicsException or ArgumentException or InvalidOperationException)
        {
            return Record("That item cannot be unequipped right now.");
        }
    }

    private bool TryResolveActiveRetainer(out DaggerfallNpc? npc, out ActorState? live, out ActorPartyRole role)
    {
        npc = _dialogue()?.CurrentNpc();
        live = null;
        role = default;
        if (npc is null)
            return false;
        if (!_partyHire.TryGetStagedHiredRole(npc.DurableId, out role))
            return false;
        if (!_actors.TryGet(npc.DurableId, out live) || live.IsDefeated)
            return false;
        return true;
    }

    private string RevisionOf(InventoryView playerInv, InventoryView retainerInv) =>
        string.Create(CultureInfo.InvariantCulture, $"{playerInv.StoreRevision}:{retainerInv.StoreRevision}:{_itemInstances.Revision}");

    private string Record(string message)
    {
        _lastResult = message;
        return message;
    }

    private DaggerfallRetainerItemView[] BuildRetainerRows(InventoryView inv, EquipmentRead equip, DaggerfallItemOwner owner)
    {
        List<DaggerfallRetainerItemView> rows = [];
        foreach (Rusty.Engine.Mechanics.UniqueInventoryItem unique in inv.UniqueItems.OrderBy(i => i.Entity.Value))
        {
            string key = $"unique:{unique.Entity.Value.ToString(CultureInfo.InvariantCulture)}";
            DaggerfallItemDefinition def = _definitions.RequireItem(new DaggerfallItemId(unique.Definition.Value));
            DaggerfallItemInstanceMetadata? meta = TryReadUniqueMetadata(unique.Entity);
            string[] equipped = [.. equip.Assignments
                .Where(a => a.Item.EntityId == unique.Entity.Value)
                .Select(a => a.Slot.Value)
                .OrderBy(s => s, StringComparer.Ordinal)];
            string[] compatible = CompatibleSlotsFor(def);
            bool broken = meta is { CurrentCondition: < 1 };
            bool canUnequip = equipped.Length > 0;
            bool canEquip = !canUnequip && compatible.Length > 0 && !broken;
            string? equipRefusal = !canUnequip && compatible.Length > 0 && broken ? BrokenItemRefusal : null;
            string? transferRefusal = canUnequip
                ? UnequipRetainerBeforeTaking
                : !_encumbrance.CanCarry(def, 1, meta) ? EncumbranceRefusal : null;
            rows.Add(BuildRow(key, def, 1, meta, equipped, compatible, canEquip, canUnequip, equipRefusal, transferRefusal));
        }
        foreach (InventoryStack stack in inv.Stacks.OrderBy(s => s.Id.Value, StringComparer.Ordinal))
        {
            string key = $"stack:{stack.Id.Value}";
            DaggerfallItemDefinition def = _definitions.RequireItem(new DaggerfallItemId(stack.Definition.Value));
            DaggerfallItemInstanceMetadata? meta = TryReadStackMetadata(owner, stack.Id);
            string? transferRefusal = !_encumbrance.CanCarry(def, 1, meta) ? EncumbranceRefusal : null;
            rows.Add(BuildRow(key, def, stack.Quantity, meta, [], [], canEquip: false, canUnequip: false, equipRefusal: null, transferRefusal));
        }
        return [.. rows.OrderByDescending(r => r.EquippedSlots.Length > 0).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Key, StringComparer.Ordinal)];
    }

    private DaggerfallRetainerItemView[] BuildPlayerRows(InventoryView inv, EquipmentRead equip)
    {
        List<DaggerfallRetainerItemView> rows = [];
        foreach (Rusty.Engine.Mechanics.UniqueInventoryItem unique in inv.UniqueItems.OrderBy(i => i.Entity.Value))
        {
            string key = $"unique:{unique.Entity.Value.ToString(CultureInfo.InvariantCulture)}";
            DaggerfallItemDefinition def = _definitions.RequireItem(new DaggerfallItemId(unique.Definition.Value));
            DaggerfallItemInstanceMetadata? meta = TryReadUniqueMetadata(unique.Entity);
            string[] equipped = [.. equip.Assignments
                .Where(a => a.Item.EntityId == unique.Entity.Value)
                .Select(a => a.Slot.Value)
                .OrderBy(s => s, StringComparer.Ordinal)];
            string? transferRefusal = equipped.Length > 0
                ? UnequipPlayerBeforeGiving
                : meta is not null ? _exits.Refusal(meta) : null;
            rows.Add(BuildRow(key, def, 1, meta, equipped, CompatibleSlotsFor(def), canEquip: false, canUnequip: false, equipRefusal: null, transferRefusal));
        }
        foreach (InventoryStack stack in inv.Stacks.OrderBy(s => s.Id.Value, StringComparer.Ordinal))
        {
            string key = $"stack:{stack.Id.Value}";
            DaggerfallItemDefinition def = _definitions.RequireItem(new DaggerfallItemId(stack.Definition.Value));
            DaggerfallItemInstanceMetadata? meta = TryReadStackMetadata(DaggerfallItemOwner.Player, stack.Id);
            string? transferRefusal = meta is not null ? _exits.Refusal(meta) : null;
            rows.Add(BuildRow(key, def, stack.Quantity, meta, [], [], canEquip: false, canUnequip: false, equipRefusal: null, transferRefusal));
        }
        return [.. rows.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Key, StringComparer.Ordinal)];
    }

    private DaggerfallRetainerItemView BuildRow(
        string key,
        DaggerfallItemDefinition def,
        ulong quantity,
        DaggerfallItemInstanceMetadata? meta,
        string[] equippedSlots,
        string[] compatibleSlots,
        bool canEquip,
        bool canUnequip,
        string? equipRefusal,
        string? transferRefusal)
    {
        string label = DaggerfallItemNames.Label(_definitions, def, meta);
        int weight = meta?.WeightClassicUnits is ulong w ? checked((int)(w / 100)) : def.Weight;
        int value = _valuation is not null && meta is not null ? _valuation.CurrentValue(def, meta) : def.Value;
        double weightKg = DaggerfallEncumbrancePolicy.UnitKilograms(def, meta);
        int curCond = meta?.CurrentCondition ?? 0;
        int maxCond = meta?.MaximumCondition ?? 0;
        bool identified = meta?.Identified ?? true;
        bool enchanted = meta?.HasEnchantment == true;
        string[] slotLabels = [.. equippedSlots.Select(FormatSlotLabel)];
        return new(
            key,
            def.Id.Value,
            label,
            quantity,
            weight,
            value,
            weightKg,
            curCond,
            maxCond,
            identified,
            enchanted,
            Icon: null,
            IconTint: null,
            IconMetadata: meta,
            equippedSlots,
            slotLabels,
            compatibleSlots,
            canEquip,
            canUnequip,
            equipRefusal,
            transferRefusal);
    }

    private string[] CompatibleSlotsFor(DaggerfallItemDefinition definition) =>
    [
        .. _definitions.EquipmentSlots.Keys
            .Select(slot => slot.Value)
            .Where(slot => DaggerfallEquipmentPolicy.IsCompatible(_definitions, definition, slot))
            .OrderBy(slot => slot, StringComparer.Ordinal),
    ];

    private InventoryContainerSelection PrepareStackTransfer(
        InventoryContainerSelection selection,
        DaggerfallItemOwner sourceOwner,
        DaggerfallItemOwner destinationOwner,
        EntityId destinationEntity,
        string stackPrefix)
    {
        if (selection.UniqueEntityId is not null)
            return selection;
        InventoryStackId sourceStack = selection.Stack
            ?? throw new ArgumentException("A fungible retainer transfer requires a stack identity.", nameof(selection));
        InventoryView destinationView = _containers.Read(destinationEntity);
        DaggerfallItemInstanceMetadata sourceMeta = _itemInstances.RequireStack(sourceOwner, sourceStack);
        InventoryStackId? compatible = destinationView.Stacks
            .OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Where(stack => StringComparer.Ordinal.Equals(stack.Definition.Value, selection.Item.Value)
                && TryReadStackMetadata(destinationOwner, stack.Id) is { } destMeta
                && sourceMeta.IsStackCompatibleWith(destMeta))
            .Select(stack => stack.Id)
            .FirstOrDefault();
        InventoryStackId target = selection.DestinationStack ?? compatible
            ?? InventoryStackId.Parse($"{stackPrefix}.{sourceStack.Value}");
        _itemInstances.EnsureTransferCompatible(sourceOwner, destinationOwner, sourceStack, target);
        return selection with { DestinationStack = target };
    }

    private DaggerfallItemInstanceMetadata? ResolveMetadata(InventoryContainerSelection selection, DaggerfallItemOwner owner)
    {
        if (selection.UniqueEntityId is ulong entity)
            return TryReadUniqueMetadata(new EntityId(entity));
        return selection.Stack is InventoryStackId stack
            ? TryReadStackMetadata(owner, stack)
            : null;
    }

    private DaggerfallItemInstanceMetadata? TryReadStackMetadata(DaggerfallItemOwner owner, InventoryStackId stack) =>
        _itemInstances.ContainsStack(owner, stack) ? _itemInstances.RequireStack(owner, stack) : null;

    private DaggerfallItemInstanceMetadata? TryReadUniqueMetadata(EntityId runtimeEntity)
    {
        DurableIdentityReference identity = _containers.Entities.IdentityOf(runtimeEntity);
        if (identity.Kind != DurableIdentityKind.Item)
            return null;
        return _itemInstances.ContainsUnique(identity.Value) ? _itemInstances.RequireUnique(identity.Value) : null;
    }

    private string DescribeLabel(InventoryContainerSelection selection, DaggerfallItemOwner owner)
    {
        DaggerfallItemDefinition def = _definitions.RequireItem(new DaggerfallItemId(selection.Item.Value));
        return DaggerfallItemNames.Label(_definitions, def, ResolveMetadata(selection, owner));
    }

    private static string FormatSlotLabel(string slotId)
    {
        string spaced = slotId.Replace('-', ' ');
        return spaced.Length == 0 ? slotId : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
