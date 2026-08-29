using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Inventory;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class MechanicsInventoryCoordinatorTests
{
    [Fact]
    public void Grant_reads_the_current_inventory_revision_and_uses_an_exact_guard()
    {
        RecordingMechanics mechanics = RecordingMechanics.Create();
        using MechanicsEntity owner = new(new MechanicsEntityHandle(7), static () => { });
        MechanicsInventoryCoordinator inventory = new(mechanics.Service, owner);

        inventory.Grant(new InventoryGrant("reward", "actor:9", new InventoryItemId("test-reward-item"), 3));

        Assert.True(mechanics.ReadCalled);
        Assert.Equal(MechanicsRevisionGuard.Exact, mechanics.Grant!.Value.RevisionGuard);
        Assert.Equal("test-reward-item", mechanics.Grant.Value.Item);
        Assert.Equal(3UL, mechanics.Grant.Value.Quantity);
    }

    [Fact]
    public void Consume_validates_before_reading_and_uses_the_observed_inventory_revision()
    {
        RecordingMechanics mechanics = RecordingMechanics.Create();
        using MechanicsEntity owner = new(new MechanicsEntityHandle(7), static () => { });
        MechanicsInventoryCoordinator inventory = new(mechanics.Service, owner);

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Consume(new InventoryConsume("consume", "test", new InventoryItemId("gold"), 0)));
        Assert.False(mechanics.ReadCalled);

        inventory.Consume(new InventoryConsume("consume", "test", new InventoryItemId("gold"), 2));
        Assert.Equal(MechanicsRevisionGuard.Exact, mechanics.Consume!.Value.RevisionGuard);
        Assert.Equal(2UL, mechanics.Consume.Value.Quantity);
    }

    [Fact]
    public void Equipment_reads_the_engine_assignment_and_guards_equip_with_both_observed_revisions()
    {
        EquipmentRecordingMechanics mechanics = EquipmentRecordingMechanics.Create();
        using MechanicsCatalog catalog = new(new MechanicsCatalogHandle(1), static () => { });
        using MechanicsEntity owner = new(new MechanicsEntityHandle(7), static () => { });
        using MechanicsEntity swordEntity = new(new MechanicsEntityHandle(44), static () => { });
        using MechanicsEntity daggerEntity = new(new MechanicsEntityHandle(45), static () => { });
        UniqueInventoryItem sword = new(44, new InventoryItemId("sword"));
        UniqueInventoryItem dagger = new(45, new InventoryItemId("dagger"));
        using MechanicsEquipmentCoordinator equipment = new(mechanics.Service, catalog, owner, [new EquipmentItemLease(sword, swordEntity), new EquipmentItemLease(dagger, daggerEntity)]);

        EquipmentRead view = equipment.Read();
        Assert.True(view.TryGet(new EquipmentSlotId("hand"), out UniqueInventoryItem equipped));
        Assert.Equal(sword, equipped);

        equipment.Equip(sword, [new EquipmentSlotId("hand")], new EquipmentChange("equip", "test"));
        Assert.Equal(MechanicsRevisionGuard.Exact, mechanics.Equip!.Value.EquipmentRevisionGuard);
        Assert.Equal(12UL, mechanics.Equip.Value.ExpectedEquipmentRevision.Revision);
        Assert.Equal(99UL, mechanics.Equip.Value.ExpectedStateRevision);
        Assert.Equal("hand", mechanics.Equip.Value.Slots.Span[0].Value);

        equipment.Unequip(sword, new EquipmentChange("unequip", "test"));
        equipment.Swap(sword, dagger, [new EquipmentSlotId("hand")], new EquipmentChange("swap", "test"));
        Assert.Equal(MechanicsRevisionGuard.Exact, mechanics.Unequip!.Value.EquipmentRevisionGuard);
        Assert.Equal(MechanicsRevisionGuard.Exact, mechanics.Swap!.Value.EquipmentRevisionGuard);
        Assert.Equal(44UL, mechanics.Swap.Value.OutgoingItem.Handle.Value);
        Assert.Equal(45UL, mechanics.Swap.Value.IncomingItem.Handle.Value);
    }

    [Fact]
    public void Disposing_a_runtime_materialized_item_unequips_then_destroys_it_with_current_engine_revisions()
    {
        EquipmentRecordingMechanics mechanics = EquipmentRecordingMechanics.Create();
        using MechanicsCatalog catalog = new(new MechanicsCatalogHandle(1), static () => { });
        using MechanicsEntity owner = new(new MechanicsEntityHandle(7), static () => { });
        MechanicsEquipmentCoordinator equipment = new(mechanics.Service, catalog, owner);

        equipment.Materialize(new UniqueItemMaterialization("runtime-sword", 88, new InventoryItemId("sword")));
        equipment.Dispose();

        Assert.NotNull(mechanics.Unequip);
        Assert.NotNull(mechanics.Destroy);
        Assert.Equal("kit.dispose.unequip", mechanics.Unequip!.Value.Operation);
        Assert.Equal("kit.dispose.destroy", mechanics.Destroy!.Value.Operation);
        Assert.Equal(99UL, mechanics.Destroy.Value.ExpectedStateRevision);
    }

    private class RecordingMechanics : DispatchProxy
    {
        internal IMechanicsService Service { get; private set; } = null!;
        internal bool ReadCalled { get; private set; }
        internal MechanicsInventoryMutationRequest? Grant { get; private set; }

        internal static RecordingMechanics Create()
        {
            IMechanicsService service = DispatchProxy.Create<IMechanicsService, RecordingMechanics>();
            RecordingMechanics proxy = (RecordingMechanics)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IMechanicsService.ReadInventoryView) => Read(),
            nameof(IMechanicsService.GrantInventory) => Record((MechanicsInventoryMutationRequest)args![0]!),
            nameof(IMechanicsService.ConsumeInventory) => RecordConsume((MechanicsInventoryMutationRequest)args![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private MechanicsInventoryViewLeaseReceipt Read()
        {
            ReadCalled = true;
            return default;
        }

        private MechanicsInventoryMutationLeaseReceipt Record(MechanicsInventoryMutationRequest request)
        {
            Grant = request;
            return default;
        }
        private MechanicsInventoryMutationLeaseReceipt RecordConsume(MechanicsInventoryMutationRequest request)
        {
            Consume = request;
            return default;
        }
        internal MechanicsInventoryMutationRequest? Consume { get; private set; }
    }

    private class EquipmentRecordingMechanics : DispatchProxy
    {
        internal IMechanicsService Service { get; private set; } = null!;
        internal MechanicsEquipmentEquipRequest? Equip { get; private set; }
        internal MechanicsUniqueItemDestroyRequest? Destroy { get; private set; }
        private bool RuntimeItemEquipped { get; set; }
        internal static EquipmentRecordingMechanics Create()
        {
            IMechanicsService service = DispatchProxy.Create<IMechanicsService, EquipmentRecordingMechanics>();
            EquipmentRecordingMechanics proxy = (EquipmentRecordingMechanics)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IMechanicsService.ReadInventoryView) => new MechanicsInventoryViewLeaseReceipt(
                ReadOnlyMemory<MechanicsInventoryViewStackRow>.Empty,
                RuntimeItemEquipped ? new[] { new MechanicsInventoryViewUniqueItemRow(88, "sword") } : new[] { new MechanicsInventoryViewUniqueItemRow(44, "sword"), new MechanicsInventoryViewUniqueItemRow(45, "dagger") },
                ReadOnlyMemory<MechanicsInventoryViewCapacityUsageRow>.Empty,
                0, string.Empty, string.Empty, 7,
                new MechanicsComponentRevision(7, 3, MechanicsRevisionComponent.Inventory, true), 99, default),
            nameof(IMechanicsService.ReadEquipmentAssignmentComponent) => new MechanicsEquipmentAssignmentComponentLeaseReceipt(
                RuntimeItemEquipped ? new[] { new MechanicsEquipmentAssignmentComponentRow("hand", 88) } : new[] { new MechanicsEquipmentAssignmentComponentRow("hand", 44) },
                new MechanicsComponentReadMetadata(7, MechanicsRevisionComponent.Equipment, 12, true, 0, string.Empty, string.Empty)),
            nameof(IMechanicsService.BindEntity) => new MechanicsEntity(new MechanicsEntityHandle(88), static () => { }),
            nameof(IMechanicsService.MaterializeUniqueItem) => Materialize((MechanicsUniqueItemMaterializationRequest)args![0]!),
            nameof(IMechanicsService.EquipEquipment) => RecordEquip((MechanicsEquipmentEquipRequest)args![0]!),
            nameof(IMechanicsService.UnequipEquipment) => RecordUnequip((MechanicsEquipmentUnequipRequest)args![0]!),
            nameof(IMechanicsService.SwapEquipment) => RecordSwap((MechanicsEquipmentSwapRequest)args![0]!),
            nameof(IMechanicsService.DestroyUniqueItem) => RecordDestroy((MechanicsUniqueItemDestroyRequest)args![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };
        private MechanicsEquipmentMutationLeaseReceipt RecordEquip(MechanicsEquipmentEquipRequest request)
        {
            Equip = request;
            return default;
        }
        private MechanicsEquipmentMutationLeaseReceipt RecordUnequip(MechanicsEquipmentUnequipRequest request)
        {
            Unequip = request;
            RuntimeItemEquipped = false;
            return default;
        }
        private MechanicsEquipmentMutationLeaseReceipt RecordSwap(MechanicsEquipmentSwapRequest request)
        {
            Swap = request;
            return default;
        }
        private MechanicsUniqueItemMaterializationLeaseReceipt Materialize(MechanicsUniqueItemMaterializationRequest request)
        {
            RuntimeItemEquipped = true;
            return new(0, string.Empty, string.Empty, 88, "sword", 7, 1, 2, 3, 4, 1, 2, false, 0, true, 7, new MechanicsLifecycleReceipt(88, MechanicsEntityLifecycle.Active, 12));
        }
        private MechanicsUniqueItemDestroyLeaseReceipt RecordDestroy(MechanicsUniqueItemDestroyRequest request)
        {
            Destroy = request;
            return default;
        }
        internal MechanicsEquipmentUnequipRequest? Unequip { get; private set; }
        internal MechanicsEquipmentSwapRequest? Swap { get; private set; }
    }
}
