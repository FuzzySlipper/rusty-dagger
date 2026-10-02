using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>EnemyEntity/ItemHelper's class-enemy equipment generation over the canonical item and equipment owners.</summary>
internal static class DaggerfallClassEnemyEquipmentPolicy
{
    private const ulong Seed = 0;
    private const string Scope = "daggerfall.class-enemy-equipment.v1";

    internal static void Equip(
        DaggerfallDefinitions definitions,
        IRandomService random,
        DaggerfallItemInstances instances,
        DaggerfallUniqueItemAllocator identities,
        MechanicsInventoryCoordinator inventory,
        MechanicsEquipmentCoordinator equipment,
        long actorId,
        int mobileId,
        int playerLevel,
        string race,
        string gender,
        int? fixedVariant = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(equipment);
        if (actorId <= 0 || playerLevel < 1) throw new ArgumentOutOfRangeException(actorId <= 0 ? nameof(actorId) : nameof(playerLevel));
        int itemLevel = mobileId == 146 ? 1 : playerLevel;
        DaggerfallItemFactory factory = new(definitions, random);
        if (fixedVariant is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(fixedVariant));
        int variant = fixedVariant ?? Draw(random, actorId, "variant", 0, 1);
        int chance;
        if (variant == 0)
        {
            int primary = Draw(random, actorId, "right-template", 118, 120);
            CreateAndEquip(factory, definitions, instances, identities, inventory, equipment, actorId, "right", primary, itemLevel, race, gender, WeaponSlots(definitions, primary, "right-hand"));
            chance = 50;
            if (Success(random, actorId, "shield", chance))
                CreateAndEquip(factory, definitions, instances, identities, inventory, equipment, actorId, "left-shield", Draw(random, actorId, "shield-template", 109, 110), itemLevel, race, gender, ["left-hand"]);
            else if (Success(random, actorId, "left-weapon", chance))
            {
                int secondary = Draw(random, actorId, "left-weapon-template", 113, 116);
                CreateAndEquip(factory, definitions, instances, identities, inventory, equipment, actorId, "left-weapon-item", secondary, itemLevel, race, gender, WeaponSlots(definitions, secondary, "left-hand"));
            }
        }
        else
        {
            int template = Draw(random, actorId, "right-template", 122, 127);
            CreateAndEquip(factory, definitions, instances, identities, inventory, equipment, actorId, "right", template, itemLevel, race, gender,
                WeaponSlots(definitions, template, "right-hand"));
            chance = variant == 1 ? 75 : 90;
        }

        foreach ((int template, string slot, string key) in new[]
        {
            (107, "head", "helm"), (106, "right-arm", "right-pauldron"), (105, "left-arm", "left-pauldron"),
            (102, "chest-armor", "cuirass"), (104, "legs-armor", "greaves"), (108, "feet", "boots"),
        })
            if (Success(random, actorId, key, chance))
                CreateAndEquip(factory, definitions, instances, identities, inventory, equipment, actorId, key, template, itemLevel, race, gender, [slot]);
    }

    /// <summary>One coating on the actual generated right-hand weapon; no independent charge state.</summary>
    internal static void CoatStartingWeapon(IRandomService random, DaggerfallItemInstances instances,
        MechanicsEquipmentCoordinator equipment, long actorId, int mobileId, int playerLevel)
    {
        if (playerLevel <= 1 || mobileId is not (>= 128 and <= 146 or 7 or 8 or 12)
            || !equipment.Read().TryGet(new KitEquipmentSlotId("right-hand"), out KitUniqueInventoryItem weapon)) return;
        if (!Success(random, actorId, "poison-chance", mobileId == 139 ? 60 : 5)) return;
        ulong id = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        var metadata = instances.RequireUnique(id);
        instances.ReplaceUnique(id, metadata with { PoisonVariant = Draw(random, actorId, "poison-variant", 128, 135) });
    }

    private static string[] WeaponSlots(DaggerfallDefinitions definitions, int template, string preferred)
    {
        DaggerfallItemDefinition item = definitions.TemplateItems.TryGetValue(new DaggerfallItemId($"template-{template}-iron"), out DaggerfallItemDefinition? found)
            ? found : throw new InvalidOperationException($"Class equipment template {template} has no iron materialization.");
        return item.Equipment?.RequiredSlots == 2 ? ["right-hand", "left-hand"] : [preferred];
    }

    private static void CreateAndEquip(DaggerfallItemFactory factory, DaggerfallDefinitions definitions, DaggerfallItemInstances instances,
        DaggerfallUniqueItemAllocator identities, MechanicsInventoryCoordinator inventory, MechanicsEquipmentCoordinator equipment,
        long actorId, string key, int template, int level, string race, string gender, string[] slots)
    {
        string category = template is >= 102 and <= 112 ? "Armor" : "Weapons";
        DaggerfallCreatedItem created = factory.Create(new DaggerfallItemCreateRequest(category, $"actor:{actorId}:{key}", DaggerfallItemOwner.Actor(actorId),
            TemplateIndex: template, Level: level, Race: category == "Armor" ? race : null, Gender: category == "Armor" ? gender : null));
        if (created.Stackable) throw new InvalidOperationException("Class enemy equipment must materialize as unique equipped items.");
        DurableIdentityReference identity = identities.AllocateReference();
        KitUniqueInventoryItem unique = equipment.Materialize(identity, created.Item);
        instances.RegisterUnique(identity.Value, created.Metadata);
        equipment.Equip(unique, slots.Select(value => new KitEquipmentSlotId(value)).ToArray());
    }

    private static bool Success(IRandomService random, long actorId, string key, int chance) => Draw(random, actorId, key, 1, 100) <= chance;
    private static int Draw(IRandomService random, long actorId, string key, int minimum, int maximum) => checked((int)random.DrawKeyed(new KeyedRngRequest(Seed, Scope, $"actor:{actorId}:{key}", minimum, maximum)).Value);
}
