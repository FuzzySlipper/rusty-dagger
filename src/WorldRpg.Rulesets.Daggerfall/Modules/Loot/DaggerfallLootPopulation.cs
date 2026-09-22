using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Loot;

/// <summary>
/// Ruleset-owned conversion from a selected Daggerfall loot table to Engine
/// inventory seeds. Container components and Engine inventory contents remain
/// the one authority for whether a source was already populated or emptied;
/// this class only shares Daggerfall selection and item-instance construction.
/// </summary>
internal sealed class DaggerfallLootPopulation
{
    private readonly DaggerfallDefinitions _catalog;
    private readonly IRandomService _random;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly DaggerfallItemFactory _items;

    internal DaggerfallLootPopulation(DaggerfallDefinitions catalog, IRandomService random, DaggerfallUniqueItemAllocator uniqueItems)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _uniqueItems = uniqueItems ?? throw new ArgumentNullException(nameof(uniqueItems));
        _items = new DaggerfallItemFactory(_catalog, _random);
    }

    /// <summary>
    /// Produces seeds for a source the caller has established as new. The caller
    /// must use its existing durable container/component lifecycle to make this
    /// result live exactly once; reopening and restore read that Engine owner
    /// instead of calling this method again.
    /// </summary>
    internal DaggerfallLootPopulationResult Generate(DaggerfallLootPopulationRequest request)
    {
        request.Validate();
        Func<string, int, int, int> draw = (id, minimum, maximum) => checked((int)_random.DrawKeyed(new KeyedRngRequest(
            LootRandomKey.Seed,
            LootRandomKey.Scope,
            LootRandomKey.For(request.Generation, request.Sequence, checked((long)request.Id.Value), $"{request.Id.Scope}.{id}"),
            minimum,
            maximum)).Value);
        DaggerfallDungeonLootResult? dungeon = request.DungeonType is int dungeonType
            ? DaggerfallLootPolicy.GenerateDungeon(_catalog, dungeonType, request.PlayerLevel, draw, request.ClothingGroup)
            : null;
        DaggerfallLootResult loot = dungeon?.Loot ?? DaggerfallLootPolicy.Generate(
            _catalog, request.TableKey, request.PlayerLevel, draw, request.ClothingGroup);
        List<GeneratedLootSeed> seeds = [];
        foreach ((DaggerfallLootDrop drop, int ordinal) in loot.Drops.Select((drop, ordinal) => (drop, ordinal)))
            AddSeed(request, drop, ordinal, seeds);

        return new(request.Id, request.Owner, dungeon?.TableKey ?? request.TableKey, loot, seeds);
    }

    /// <summary>Registers the product meaning of seeds after their source created the Engine container.</summary>
    internal void RegisterGeneratedMetadata(DaggerfallLootPopulationResult result, DaggerfallItemInstances instances)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(instances);
        foreach (GeneratedLootSeed generated in result.Generated)
        {
            InventoryContainerSeed seed = generated.Seed;
            DaggerfallItemDefinition definition = _catalog.RequireItem(new DaggerfallItemId(seed.Item.Value));
            if (seed.Stack is InventoryStackId stack)
            {
                if (generated.Metadata is { } metadata) instances.RegisterStack(result.Owner, stack, metadata);
                else instances.RegisterDefaultStack(result.Owner, new InventoryStack(stack, ItemDefinitionId.Parse(seed.Item.Value), seed.Quantity), definition);
            }
            else if (seed.UniqueItem is DurableIdentityReference unique)
            {
                if (generated.Metadata is { } metadata) instances.RegisterUnique(unique.Value, metadata);
                else instances.RegisterDefaultUnique(unique.Value, definition, result.Owner);
            }
        }
    }

    private void AddSeed(DaggerfallLootPopulationRequest request, DaggerfallLootDrop drop, int ordinal, List<GeneratedLootSeed> seeds)
    {
        if (_catalog.Magic.MagicItems.ContainsKey(drop.ItemId))
        {
            DaggerfallCreatedItem createdMagic = _items.Create(new DaggerfallItemCreateRequest(
                "Magic", $"loot.{request.Id.Scope}.{request.Id.Value}.{request.Sequence}.{ordinal}.magic", request.Owner,
                Level: request.PlayerLevel, Race: request.Race, Gender: request.Gender, MagicItemKey: drop.ItemId));
            seeds.Add(new GeneratedLootSeed(new InventoryContainerSeed(createdMagic.Item, UniqueItem: _uniqueItems.AllocateReference()), createdMagic.Metadata));
            return;
        }

        DaggerfallItemDefinition item = _catalog.RequireItem(new DaggerfallItemId(drop.ItemId));
        if (item.Template is DaggerfallItemTemplateDefinition template)
        {
            bool appearance = template.Groups.Contains("Armor", StringComparer.Ordinal)
                || template.Groups.Contains("MensClothing", StringComparer.Ordinal)
                || template.Groups.Contains("WomensClothing", StringComparer.Ordinal);
            DaggerfallCreatedItem created = _items.Create(new DaggerfallItemCreateRequest(
                template.Groups[0], $"loot.{request.Id.Scope}.{request.Id.Value}.{request.Sequence}.{ordinal}.{template.Index}", request.Owner,
                TemplateIndex: template.Index, Level: request.PlayerLevel,
                Race: appearance ? request.Race : null, Gender: appearance ? request.Gender : null,
                PotionRecipeKey: drop.PotionRecipeKey));
            if (created.Stackable)
            {
                InventoryStackId stack = DaggerfallInventoryStackIds.ForLoot((long)request.Id.Value, request.Sequence, ordinal);
                seeds.Add(new GeneratedLootSeed(new InventoryContainerSeed(created.Item, created.Quantity, Stack: stack), created.Metadata));
            }
            else
                seeds.Add(new GeneratedLootSeed(new InventoryContainerSeed(created.Item, UniqueItem: _uniqueItems.AllocateReference()), created.Metadata));
            return;
        }

        if (item.IsFungible)
        {
            seeds.Add(new GeneratedLootSeed(new InventoryContainerSeed(new InventoryItemId(drop.ItemId), checked((ulong)drop.Quantity),
                Stack: DaggerfallInventoryStackIds.ForLoot((long)request.Id.Value, request.Sequence, ordinal)), null));
            return;
        }
        seeds.Add(new GeneratedLootSeed(new InventoryContainerSeed(new InventoryItemId(drop.ItemId), UniqueItem: _uniqueItems.AllocateReference()), null));
    }
}

/// <summary>Stable source identity for a single container population decision.</summary>
internal sealed record DaggerfallLootPopulationId(string Scope, ulong Value)
{
    internal DaggerfallLootPopulationId Validate()
    {
        if (Scope is not ("corpse" or "world-treasure" or "encounter") || Value == 0)
            throw new ArgumentException("Loot population must name one positive corpse, world-treasure, or encounter identity.");
        return this;
    }
}

/// <summary>Inputs shared by corpse, world-treasure, and encounter population callers.</summary>
internal sealed record DaggerfallLootPopulationRequest(
    DaggerfallLootPopulationId Id,
    DaggerfallItemOwner Owner,
    string TableKey,
    int PlayerLevel,
    ulong Generation,
    ulong Sequence,
    string Race,
    string Gender,
    string ClothingGroup,
    int? DungeonType = null)
{
    internal DaggerfallLootPopulationRequest Validate()
    {
        (Id ?? throw new ArgumentNullException(nameof(Id))).Validate();
        (Owner ?? throw new ArgumentNullException(nameof(Owner))).Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(TableKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Race);
        if (Owner.Scope != Id.Scope || Owner.Id != checked((long)Id.Value)
            || PlayerLevel < 1 || Generation == 0 || Sequence == 0 || DungeonType is < 0 or > 18
            || Gender is not ("male" or "female") || ClothingGroup is not ("MensClothing" or "WomensClothing"))
            throw new ArgumentException("Loot population request carries invalid source, level, or player appearance data.");
        if (DungeonType is int dungeonType && !string.Equals(TableKey, DaggerfallLootPolicy.DungeonTableKey(dungeonType), StringComparison.Ordinal))
            throw new ArgumentException("Dungeon loot population must use the table selected by its dungeon type.", nameof(TableKey));
        return this;
    }
}

/// <summary>One source's retained inventory seeds and the table receipt that selected them.</summary>
internal sealed record DaggerfallLootPopulationResult(
    DaggerfallLootPopulationId Id,
    DaggerfallItemOwner Owner,
    string TableKey,
    DaggerfallLootResult Loot,
    IReadOnlyList<GeneratedLootSeed> Generated)
{
    internal IReadOnlyList<InventoryContainerSeed> Seeds => Generated.Select(value => value.Seed).ToArray();
}
