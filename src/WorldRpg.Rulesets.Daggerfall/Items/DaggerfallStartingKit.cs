using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What a new character starts with: the spellbook, the clothes worn from the start, the career's weapons, the gold and
/// the background's grants. The kit is made in full before the title session is replaced, so a refused item refuses
/// the new game; it is then granted into the new session, its clothes put on and its gold handed to the purse.
/// </summary>
internal sealed class DaggerfallStartingKit
{
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallItemFactory _factory;
    private readonly (DaggerfallCreatedItem Item, string? Slot)[] _grants;

    private DaggerfallStartingKit(DaggerfallDefinitions definitions, DaggerfallItemFactory factory, (DaggerfallCreatedItem, string?)[] grants)
    {
        _definitions = definitions;
        _factory = factory;
        _grants = grants;
    }

    /// <summary>
    /// The kit's items, made now, each with the clothing slot its key names; an armed start wields the career's first
    /// unique weapon in the main hand.
    /// </summary>
    internal static DaggerfallStartingKit Make(DaggerfallDefinitions definitions, IRandomService random,
        DaggerfallCharacterCreationChoices choices, DaggerfallCareerDefinition career)
    {
        DaggerfallItemFactory factory = new(definitions, random);
        DaggerfallItemCreateRequest[] requests = [.. Requests(definitions, choices, career)];
        (DaggerfallCreatedItem Item, string? Slot)[] grants = [.. requests.Select(request => (factory.Create(request), Slot(request.Key)))];
        if (definitions.NewGame.Armed)
        {
            int weapon = Enumerable.Range(0, requests.Length).FirstOrDefault(index =>
                requests[index].Key.StartsWith(WeaponKey, StringComparison.Ordinal) && !grants[index].Item.Stackable, -1);
            if (weapon < 0) throw new ArgumentException($"An armed start wields a weapon, and career '{career.Id}' starts with none that is held alone.");
            grants[weapon] = grants[weapon] with { Slot = MainHand };
        }
        return new(definitions, factory, grants);
    }

    private const string WeaponKey = "new-game.weapon.";
    private const string MainHand = "right-hand";

    /// <summary>The worn slot of a starting item: the shirt goes on the chest and the pants on the legs.</summary>
    internal static string? Slot(string key) => key.EndsWith(".shirt", StringComparison.Ordinal)
        ? "chest-clothes" : key.EndsWith(".pants", StringComparison.Ordinal) ? "legs-clothes" : null;

    /// <summary>The kit's requests in their order: the spellbook, shirt and pants, the career's weapons, the gold, the background's grants.</summary>
    internal static IEnumerable<DaggerfallItemCreateRequest> Requests(DaggerfallDefinitions definitions, DaggerfallCharacterCreationChoices choices,
        DaggerfallCareerDefinition career)
    {
        var config = definitions.NewGame;
        bool female = choices.Gender == DaggerfallCharacterGender.Female;
        string gender = female ? "female" : "male";
        string clothing = DaggerfallItemFactory.ClothingGroup(gender);
        yield return new("MiscItems", "new-game.spellbook", DaggerfallItemOwner.Player, TemplateIndex: config.SpellbookTemplate);
        yield return new(clothing, "new-game.shirt", DaggerfallItemOwner.Player,
            TemplateIndex: female ? config.FemaleShirtTemplate : config.MaleShirtTemplate, Variant: 0, Race: choices.RaceId, Gender: gender, RandomizeClothingDye: true);
        yield return new(clothing, "new-game.pants", DaggerfallItemOwner.Player,
            TemplateIndex: female ? config.FemalePantsTemplate : config.MalePantsTemplate, Race: choices.RaceId, Gender: gender);
        var items = choices.CustomCareer is not null ? config.CustomItems : config.Careers.Single(value => value.Career == career.Id).Items;
        foreach (var (item, index) in items.Select((item, index) => (item, index)))
            yield return new("Weapons", $"{WeaponKey}{index}", DaggerfallItemOwner.Player,
                Quantity: item.Quantity, TemplateIndex: item.Template, Material: item.Material);
        if (config.Gold > 0) yield return new("Currency", "new-game.gold", DaggerfallItemOwner.Player, Quantity: (ulong)config.Gold, TemplateIndex: 276);
        foreach (var (grant, index) in choices.Background!.StartingGrants.Select((grant, index) => (grant, index)))
        {
            var definition = definitions.RequireItem(new DaggerfallItemId(grant.ItemId));
            var template = definition.Template ?? throw new ArgumentException($"Starting grant '{grant.ItemId}' has no normalized template.");
            bool appearance = DaggerfallItemFactory.TakesAppearance(template);
            yield return new(template.Groups[0], $"new-game.background.{index}", DaggerfallItemOwner.Player,
                Quantity: grant.Quantity, TemplateIndex: template.Index, Material: definition.Weapon?.Material ?? definition.Armor?.Material,
                Race: appearance ? choices.RaceId : null, Gender: appearance ? gender : null);
        }
    }

    /// <summary>
    /// Grants the kit in its order: gold to the purse (<paramref name="receiveGold"/>, Economy's), every other item into the
    /// player's pack, a unique one under a newly allocated identity, and the clothes put on and an armed start's weapon wielded.
    /// </summary>
    internal void Grant(MechanicsInventoryCoordinator inventory, DaggerfallItemInstances instances, DaggerfallUniqueItemAllocator uniqueItems,
        EntityDirectory entities, DaggerfallEquipmentMoves equipment, Func<ulong, bool> receiveGold)
    {
        for (int index = 0; index < _grants.Length; index++)
        {
            var grant = _grants[index];
            if (DaggerfallEncumbrancePolicy.IsGold(_definitions.RequireItem(new DaggerfallItemId(grant.Item.Item.Value))))
            {
                if (!receiveGold(grant.Item.Quantity))
                    throw new InvalidOperationException("Starting gold could not be added to the new character's currency.");
                continue;
            }
            DurableIdentityReference? identity = grant.Item.Stackable ? null : uniqueItems.AllocateReference();
            _factory.Materialize(grant.Item, inventory, instances,
                grant.Item.Stackable ? InventoryStackId.Parse($"daggerfall.character.initial.{index}") : null, identity);
            if (grant.Slot is { } slot)
            {
                var equipped = equipment.MoveToSlot(new UniqueInventoryItem(entities.Resolve(identity!.Value).Value, grant.Item.Item), new EquipmentSlotId(slot));
                if (equipped.Outcome != EquipmentMoveOutcome.Applied)
                    throw new ArgumentException($"Starting item '{grant.Item.Item.Value}' could not be put in '{slot}': {equipped.Detail}");
            }
        }
    }

    /// <summary>Empties the player's pack of the loadout the player definition carries, worn items first taken off.</summary>
    internal static void ClearLoadout(MechanicsInventoryCoordinator inventory, MechanicsEquipmentCoordinator equipment,
        DaggerfallItemInstances instances, EntityDirectory entities)
    {
        var read = inventory.Read();
        foreach (var item in read.UniqueItems)
        {
            var unique = new UniqueInventoryItem(item.Entity.Value, new InventoryItemId(item.Definition.Value));
            var identity = inventory.GetDurableItemId(item.Entity);
            if (equipment.Read().Assignments.Any(assignment => assignment.Item.EntityId == unique.EntityId))
                equipment.Unequip(unique);
            inventory.Destroy(unique);
            instances.RemoveUnique(identity.Value);
            entities.Destroy(identity);
        }
        foreach (var item in read.Stacks)
        {
            inventory.Consume(new InventoryConsume(item.Id, item.Quantity));
            instances.RemoveStack(DaggerfallItemOwner.Player, item.Id);
        }
    }
}
