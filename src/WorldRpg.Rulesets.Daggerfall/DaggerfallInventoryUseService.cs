using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Named deferred use owners; the inventory does not pretend their effects succeeded.</summary>
internal enum DaggerfallInventoryUseReceiver { BookReading, PotionConsumption, UsedEnchantment }
internal sealed record DaggerfallInventoryUseResult(bool Applied, string Message, DaggerfallInventoryUseReceiver? DeferredTo = null,
    DaggerfallReadableBook? OpenedBook = null);

/// <summary>
/// Daggerfall item-use policy over the existing Engine inventory and site owners. Map use is complete
/// here; book, potion, and enchantment payloads retain their named receiving owners until they exist.
/// </summary>
internal sealed class DaggerfallInventoryUseService(
    MechanicsInventoryCoordinator inventory,
    DaggerfallDefinitions definitions,
    DaggerfallItemInstances instances,
    DaggerfallUniqueItemAllocator uniqueItems,
    DaggerfallSiteContext sites,
    IRandomService random,
    DaggerfallItemConditionService? condition = null,
    DaggerfallBookNotebook? notebook = null,
    Func<int, bool>? useDrug = null,
    Func<bool>? useOghma = null,
    Func<KitUniqueInventoryItem, DaggerfallInventoryUseResult>? useSanguineRose = null,
    Func<KitUniqueInventoryItem, DaggerfallInventoryUseResult>? useSkullCorruption = null,
    Func<KitUniqueInventoryItem, DaggerfallInventoryUseResult>? useItemSpell = null)
{
    private const int FirstDrugTemplate = 78;
    private const int LastDrugTemplate = 81;
    private const int MapTemplate = 287;
    private const int OilTemplate = 252;
    private const int LanternTemplate = 248;
    private const ulong RandomSeed = 0;
    private const string RandomScope = "daggerfall.inventory.map-use.v1";

    internal DaggerfallInventoryUseResult Use(string key, ulong expectedWorldRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        InventoryView current = inventory.Read();
        if (current.StoreRevision != expectedWorldRevision)
            return new(false, "Inventory changed. Choose the item again.");

        if (key.StartsWith("unique:", StringComparison.Ordinal)
            && ulong.TryParse(key.AsSpan("unique:".Length), out ulong entity))
            return UseUnique(current, entity);
        if (key.StartsWith("stack:", StringComparison.Ordinal))
            return UseStack(current, InventoryStackId.Parse(key["stack:".Length..]));
        return new(false, "That item is no longer in your inventory.");
    }

    private DaggerfallInventoryUseResult UseStack(InventoryView current, InventoryStackId stack)
    {
        InventoryStack? entry = current.Stacks.Where(candidate => candidate.Id == stack)
            .Select(candidate => (InventoryStack?)candidate).SingleOrDefault();
        if (entry is not InventoryStack found) return new(false, "That item is no longer in your inventory.");
        DaggerfallItemInstanceMetadata metadata = instances.RequireStack(DaggerfallItemOwner.Player, stack);
        if (Template(found.Definition.Value) == OilTemplate)
            return RefuelLantern(current, stack, metadata);
        return Route(found.Definition.Value, metadata, () =>
        {
            InventoryMutationReceipt receipt = inventory.Consume(new InventoryConsume(stack, 1));
            if (receipt.AfterQuantity == 0) instances.RemoveStack(DaggerfallItemOwner.Player, stack);
        }, $"stack:{stack.Value}");
    }

    private DaggerfallInventoryUseResult UseUnique(InventoryView current, ulong entity)
    {
        Rusty.Engine.Mechanics.UniqueInventoryItem? entry = current.UniqueItems.Where(candidate => candidate.Entity.Value == entity)
            .Select(candidate => (Rusty.Engine.Mechanics.UniqueInventoryItem?)candidate).SingleOrDefault();
        if (entry is not { } found) return new(false, "That item is no longer in your inventory.");
        DurableIdentityReference identity = inventory.GetDurableItemId(found.Entity);
        DaggerfallItemInstanceMetadata metadata = instances.RequireUnique(identity.Value);
        return Route(found.Definition.Value, metadata, () =>
        {
            _ = inventory.Destroy(new KitUniqueInventoryItem(found.Entity.Value, new InventoryItemId(found.Definition.Value)));
            inventory.Entities.Destroy(identity);
            instances.RemoveUnique(identity.Value);
            uniqueItems.Remove(identity);
        }, $"unique:{identity.Value}", new KitUniqueInventoryItem(found.Entity.Value, new InventoryItemId(found.Definition.Value)));
    }

    private DaggerfallInventoryUseResult Route(string itemId, DaggerfallItemInstanceMetadata metadata, Action consume, string useKey, KitUniqueInventoryItem? unique = null)
    {
        if (metadata.Enchantment is { } enchantment)
        {
            if (definitions.Magic.TryEnchantments(enchantment, out var payloads)
                && payloads.Any(effect => effect.Type == 26 && effect.Param == 4))
            {
                if (metadata.CurrentCondition <= 0) return new(false, "The Sanguine Rose is broken.");
                if (unique is not { } source) return new(false, "Sanguine Rose requires a unique item source.");
                return useSanguineRose?.Invoke(source) ?? new(false, "Sanguine Rose summoning is unavailable.");
            }
            if (definitions.Magic.TryEnchantments(enchantment, out var skullPayloads)
                && skullPayloads.Any(effect => effect.Type == 26 && effect.Param == 8))
            {
                if (metadata.CurrentCondition <= 0) return new(false, "The Skull of Corruption is broken.");
                if (unique is not { } source) return new(false, "Skull of Corruption requires a unique item source.");
                return useSkullCorruption?.Invoke(source) ?? new(false, "Skull of Corruption copying is unavailable.");
            }
            if (definitions.Magic.MagicItems.TryGetValue(enchantment, out DaggerfallMagicItemDefinition? magic)
                && magic.Enchantments.Any(effect => effect.ParamMeaning == "artifact-effect" && effect.Param == 5))
            {
                if (useOghma is null) return new(false, "Oghma Infinium allocation is unavailable.", DaggerfallInventoryUseReceiver.UsedEnchantment);
                if (!useOghma()) return new(false, "Finish your current attribute allocation before using Oghma Infinium.");
                consume();
                return new(true, "Oghma Infinium grants 30 attribute points. Allocate them on your character sheet.");
            }
            if (definitions.Magic.TryEnchantments(enchantment, out var spellPayloads) && spellPayloads.Any(effect => effect.Type == 0))
                return unique is { } spellSource && useItemSpell is not null ? useItemSpell(spellSource)
                    : new(false, "Item casting requires an available unique source.");
            return new(false, "This enchantment has no use effect.");
        }
        if (metadata.BookId is int bookId)
        {
            if (notebook is null) return new(false, "Reading this book is not available yet.", DaggerfallInventoryUseReceiver.BookReading);
            DaggerfallReadableBook book = notebook.Open(bookId);
            return new(true, $"Reading {book.Title}.", OpenedBook: book);
        }
        if (metadata.PotionRecipeKey is not null)
            return new(false, "Potion effects are not available yet.", DaggerfallInventoryUseReceiver.PotionConsumption);
        if (Template(itemId) is int template and (>= FirstDrugTemplate and <= LastDrugTemplate))
        {
            // A drug is taken, not applied: the dose is consumed whatever it does to the taker, and what it
            // does is the poison owner's decision, not this rule's.
            if (useDrug is null) return new(false, "Drug effects are not available yet.");
            if (DaggerfallPoisonPolicy.VariantForDrugTemplate(template) is not int variant)
                return new(false, "This item cannot be used.");
            bool took = useDrug(variant);
            consume();
            return new(true, took ? "The drug takes hold." : "The drug has no effect on you.");
        }

        if (Template(itemId) != MapTemplate)
            return new(false, "This item cannot be used.");
        if (sites.Region is not int region)
            return new(false, "A map can only reveal a location while you are in a region.");
        DaggerfallSiteRecord[] eligible = sites.Records.Where(site => site.Id.Region == region && !sites.IsDiscovered(site.Id)).ToArray();
        if (eligible.Length == 0)
            return new(false, "You have already discovered every location in this region.");
        int selected = checked((int)random.DrawKeyed(new KeyedRngRequest(RandomSeed, RandomScope,
            $"region:{region}:{useKey}", 0, eligible.Length - 1)).Value);
        DaggerfallSiteRecord revealed = eligible[selected];
        sites.Discover(revealed.Id);
        consume();
        return new(true, $"Map reveals {revealed.Name}.");
    }

    private DaggerfallInventoryUseResult RefuelLantern(InventoryView current, InventoryStackId oil, DaggerfallItemInstanceMetadata oilMetadata)
    {
        if (condition is null) return new(false, "Lantern refuelling is not available yet.");
        Rusty.Engine.Mechanics.UniqueInventoryItem? found = current.UniqueItems
            .Where(item => Template(item.Definition.Value) == LanternTemplate)
            .OrderBy(item => item.Entity.Value)
            .Select(item => (Rusty.Engine.Mechanics.UniqueInventoryItem?)item)
            .FirstOrDefault();
        if (found is not { } lantern) return new(false, "You need a lantern to use this oil.");
        DaggerfallItemConditionResult result = condition.Refuel(
            new KitUniqueInventoryItem(lantern.Entity.Value, new InventoryItemId(lantern.Definition.Value)), oilMetadata.CurrentCondition);
        if (result.Outcome == DaggerfallItemConditionOutcome.AlreadyRepaired)
            return new(false, "Your lantern is already full.");
        InventoryMutationReceipt receipt = inventory.Consume(new InventoryConsume(oil, 1));
        if (receipt.AfterQuantity == 0) instances.RemoveStack(DaggerfallItemOwner.Player, oil);
        return new(true, "Lantern refuelled.");
    }

    private int? Template(string itemId) => definitions.RequireItem(new DaggerfallItemId(itemId)).Template?.Index;
}
