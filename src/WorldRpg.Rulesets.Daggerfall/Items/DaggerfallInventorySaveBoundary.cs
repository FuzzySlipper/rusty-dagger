using Rusty.Engine.Mechanics;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Current inventory values shared by save capture and resource unloading.</summary>
internal static class DaggerfallInventorySaveBoundary
{
    internal static (DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems) CaptureContents(
        InventoryView contents, DaggerfallItemOwner owner, DaggerfallItemInstances instances, EntityDirectory entities) => (
        contents.Stacks.OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Select(stack => new DaggerfallStackSave(stack.Id.Value, stack.Definition.Value, stack.Quantity,
                instances.RequireStack(owner, stack.Id).Capture())).ToArray(),
        contents.UniqueItems.OrderBy(item => item.Entity.Value)
            .Select(item =>
            {
                ulong identity = entities.IdentityOf(item.Entity).Value;
                return new DaggerfallUniqueSave(item.Definition.Value, identity, instances.RequireUnique(identity).Capture());
            }).ToArray());
}

internal sealed record DaggerfallInventorySave(DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems, DaggerfallEquipmentSave[] Equipment)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Stacks);
        ArgumentNullException.ThrowIfNull(UniqueItems);
        ArgumentNullException.ThrowIfNull(Equipment);
        HashSet<string> stackIds = [];
        foreach (DaggerfallStackSave stack in Stacks)
            if (stack is null || string.IsNullOrWhiteSpace(stack.StackId) || string.IsNullOrWhiteSpace(stack.ItemId) || stack.Quantity == 0 || !stackIds.Add(stack.StackId))
                throw new ArgumentException("Inventory stacks must have distinct explicit identities, definitions, and positive quantities.");
            else _ = DaggerfallItemInstanceMetadata.Restore(stack.ItemId, stack.Metadata);
        HashSet<ulong> items = [];
        foreach (DaggerfallUniqueSave unique in UniqueItems)
            if (unique is null || string.IsNullOrWhiteSpace(unique.ItemId) || unique.EntityId == 0 || !items.Add(unique.EntityId))
                throw new ArgumentException("Unique inventory entries must be valid and distinct.");
            else _ = DaggerfallItemInstanceMetadata.Restore(unique.ItemId, unique.Metadata);
        HashSet<string> slots = [];
        foreach (DaggerfallEquipmentSave equipment in Equipment)
            if (equipment is null || string.IsNullOrWhiteSpace(equipment.SlotId) || !slots.Add(equipment.SlotId) || !items.Contains(equipment.ItemEntityId))
                throw new ArgumentException("Equipment must refer to a saved unique item once per slot.");
    }
}

/// <summary>One Engine-backed stack preserved by its product-selected owner-scoped identity.</summary>
internal sealed record DaggerfallStackSave(string StackId, string ItemId, ulong Quantity, DaggerfallItemMetadataSave Metadata);

internal sealed record DaggerfallUniqueSave(string ItemId, ulong EntityId, DaggerfallItemMetadataSave Metadata);

internal sealed record DaggerfallItemOwnerSave(string Scope, long Id);

internal sealed record DaggerfallItemMetadataSave(
    string Material,
    int Variant,
    int CurrentCondition,
    int MaximumCondition,
    bool Identified,
    bool Stolen,
    string? QuestId,
    string? QuestItemSymbol,
    string? Enchantment,
    DaggerfallItemOwnerSave Owner,
    string? Race = null,
    string? Gender = null,
    string? Dye = null,
    int? BookId = null,
    int? PotionRecipeKey = null,
    ulong? CreditValue = null,
    int? PoisonVariant = null, DaggerfallHeldCastState? HeldCast = null, long HealthLeechLastUsedMinute = 0, int? CapturedSoulMobileId = null, DaggerfallConjuredItem? Conjuration = null,
    DaggerfallMadeEnchantment? MadeEnchantment = null, ulong? WeightClassicUnits = null, bool BoundSoulReleased = false, bool BoundSoulReleasePending = false);

internal sealed record DaggerfallEquipmentSave(string SlotId, ulong ItemEntityId);

internal sealed record DaggerfallCorpseSave(long ActorId, ulong ContainerId, ulong OriginatingSequence, bool IsRegistered, bool IsInteractable, DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Stacks);
        ArgumentNullException.ThrowIfNull(UniqueItems);
        if (ActorId <= 0 || ContainerId == 0)
            throw new ArgumentOutOfRangeException(nameof(ContainerId), "A saved corpse must name positive actor and container identities.");
        if (!IsRegistered && (Stacks.Length != 0 || UniqueItems.Length != 0))
            throw new ArgumentException("An unregistered corpse cannot contain inventory.");
        new DaggerfallInventorySave(Stacks, UniqueItems, []).Validate();
    }
}

/// <summary>One persistent dropped-item container at a world position.</summary>
internal sealed record DaggerfallGroundContainerSave(DaggerfallWorldProfileKeySave Profile, long Id, float X, float Y, float Z, DaggerfallInventorySave Inventory, string? PropertyPlacement = null, long StockedDay = 0, string? TreasurePlacement = null)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Profile);
        Profile.Validate();
        if (Id <= 0 || !float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z))
            throw new ArgumentException("Ground containers require a positive identity and finite position.");
        if (TreasurePlacement is not null && (string.IsNullOrWhiteSpace(TreasurePlacement) || PropertyPlacement is not null))
            throw new ArgumentException("Dungeon treasure must name one source marker, separate from property furniture.");
        if (StockedDay < -1 || PropertyPlacement is not null && string.IsNullOrWhiteSpace(PropertyPlacement))
            throw new ArgumentException("Property containers require a valid placement and stock date.");
        ArgumentNullException.ThrowIfNull(Inventory);
        Inventory.Validate();
    }
}
