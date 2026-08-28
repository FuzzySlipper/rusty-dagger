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

        inventory.Grant(new InventoryGrant("reward", "actor:9", new InventoryItemId("gold-piece"), 3));

        Assert.True(mechanics.ReadCalled);
        Assert.Equal(MechanicsRevisionGuard.Exact, mechanics.Grant!.Value.RevisionGuard);
        Assert.Equal("gold-piece", mechanics.Grant.Value.Item);
        Assert.Equal(3UL, mechanics.Grant.Value.Quantity);
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
    }
}
