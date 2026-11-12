using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class ClassEnemyEquipmentTests
{
    [Fact]
    public void Secondary_staff_replaces_both_hands_and_keeps_the_primary_weapon_for_loot()
    {
        var definitions = TestPayload.Definitions;
        using var rig = new EquipmentRig(definitions);
        rig.Generate(145, 584, StaffDraws.Create());
        var hands = rig.Equipment.Read().Assignments.Where(value => value.Slot.Value.EndsWith("-hand")).ToArray();
        Assert.Equal(2, hands.Length);
        Assert.Single(hands.Select(value => value.Item).Distinct());
        Assert.Contains("template-115-", hands[0].Item.Definition.Value);
        Assert.Contains(rig.Inventory.Read().UniqueItems, item => item.Definition.Value.StartsWith("template-118-"));
    }

    [Fact]
    public void Every_class_produces_legal_equipment_across_keyed_random_seeds()
    {
        var definitions = TestPayload.Definitions;
        var random = SummonRandom.Create();
        for (int mobile = 128; mobile <= 146; mobile++)
        for (int seed = 1; seed <= 64; seed++)
        {
            using var rig = new EquipmentRig(definitions);
            rig.Generate(mobile, mobile * 1000 + seed, random);
            var assignments = rig.Equipment.Read().Assignments;
            Assert.Equal(assignments.Count, assignments.Select(value => value.Slot).Distinct().Count());
            Assert.All(assignments, value => Assert.Contains(rig.Inventory.Read().UniqueItems, item => item.Entity.Value == value.Item.EntityId));
        }
    }

    private sealed class EquipmentRig : IDisposable
    {
        private readonly DaggerfallDefinitions _definitions;
        private readonly EntityDirectory _entities = new();
        internal MechanicsInventoryCoordinator Inventory { get; }
        internal MechanicsEquipmentCoordinator Equipment { get; }
        internal EquipmentRig(DaggerfallDefinitions definitions)
        {
            _definitions = definitions;
            EntityId owner = _entities.Create(new(DurableIdentityKind.Actor, 1), new EntityTypeId("test.enemy"));
            InventoryStore store = new();
            store.RegisterInventory(new InventoryState(owner));
            store.RegisterEquipment(new EquipmentState(owner));
            InventoryComponent inventory = new(store, owner);
            EquipmentComponent equipment = new(store, owner);
            _entities.Store.Add(owner, inventory);
            _entities.Store.Add(owner, equipment);
            var items = definitions.Items.Values.Concat(definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            var slots = definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
            Inventory = new(inventory, _entities, items);
            Equipment = new(inventory, equipment, _entities, items, slots);
        }
        internal void Generate(int mobile, long actor, IRandomService random) =>
            DaggerfallClassEnemyEquipmentPolicy.Equip(_definitions, random, new(), new(1000),
                Inventory, Equipment, actor, mobile, 10, "breton", "male");
        public void Dispose() => _entities.Dispose();
    }

    // The failing draw: variant zero, a primary sword, no shield, then a two-handed staff.
    private class StaffDraws : DispatchProxy
    {
        internal static IRandomService Create() => Create<IRandomService, StaffDraws>();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            var request = (KeyedRngRequest)args![0]!;
            string key = request.Key;
            return new KeyedRngReceipt(key.EndsWith(":shield", StringComparison.Ordinal) ? request.Maximum
                : key.Contains(":left-weapon-template") ? 115 : request.Minimum);
        }
    }
}
