using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallUniqueItemAllocatorTests
{
    [Fact]
    public void Generated_unique_loot_allocator_skips_composed_entity_reservations()
    {
        DaggerfallUniqueItemAllocator allocator = new(1_000, [1_000, 1_001]);

        Assert.Equal(new DurableIdentityReference(DurableIdentityKind.Item, 1_002), allocator.AllocateReference());
        Assert.Equal(new DurableIdentityReference(DurableIdentityKind.Item, 1_003), allocator.AllocateReference());
    }

    [Fact]
    public void Retiring_an_issued_item_tombstones_it_and_an_authored_one_stays_reserved()
    {
        DurableIdentityAllocator ledger = new(DurableIdentityKind.Item, 1_000, [7]);
        DaggerfallUniqueItemAllocator allocator = DaggerfallUniqueItemAllocator.Sharing(ledger, new HashSet<ulong> { 7 });
        DurableIdentityReference issued = allocator.AllocateReference();
        DurableIdentityReference authored = new(DurableIdentityKind.Item, 7);

        allocator.Remove(issued);
        allocator.Remove(authored);

        Assert.Equal(DurableIdentityClassification.Removed, ledger.Classify(issued));
        Assert.Equal(DurableIdentityClassification.Live, ledger.Classify(authored));
        Assert.Equal([7UL], allocator.ReservedEntityIds);
        Assert.Equal([issued.Value], allocator.RemovedEntityIds);
    }

    [Fact]
    public void A_retired_item_ledger_loads_back_as_it_was_saved()
    {
        HashSet<ulong> authored = [7];
        DurableIdentityAllocator ledger = new(DurableIdentityKind.Item, 1_000, [7]);
        DaggerfallUniqueItemAllocator allocator = DaggerfallUniqueItemAllocator.Sharing(ledger, authored);
        DurableIdentityReference issued = allocator.AllocateReference();
        allocator.Remove(issued);
        allocator.Remove(new(DurableIdentityKind.Item, 7));

        DurableIdentityAllocator loaded = DurableIdentityAllocator.Restore(ledger.CaptureState());
        DaggerfallUniqueItemAllocator restored = DaggerfallUniqueItemAllocator.Sharing(loaded, authored);

        Assert.Equal(DurableIdentityClassification.Live, loaded.Classify(new(DurableIdentityKind.Item, 7)));
        Assert.Equal(DurableIdentityClassification.Removed, loaded.Classify(issued));
        Assert.Equal(1_001UL, restored.NextEntityId);
        restored.Remove(new(DurableIdentityKind.Item, 7));
        Assert.DoesNotContain(7UL, restored.RemovedEntityIds);
    }
}
