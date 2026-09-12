using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class DurableIdentityTests
{
    [Fact]
    public void Allocation_skips_reserved_identities_and_orders_the_cursor()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, 1_000, [1_000, 1_001, 500]);

        Assert.Equal(1_002UL, identities.NextIdentity(DurableIdentityKind.Item));
        DurableIdentityReference first = identities.Allocate(DurableIdentityKind.Item);
        DurableIdentityReference second = identities.Allocate(DurableIdentityKind.Item);
        Assert.Equal(new DurableIdentityReference(DurableIdentityKind.Item, 1_002), first);
        Assert.Equal(1_003UL, second.Value);
        Assert.Equal(1_004UL, identities.NextIdentity(DurableIdentityKind.Item));
        // Issued identities live below the cursor; the reservation set holds only
        // the authored identities the allocator must never hand out.
        Assert.Equal([500UL, 1_000UL, 1_001UL], identities.ReservedIdentities(DurableIdentityKind.Item).Order());
    }

    [Fact]
    public void Removal_tombstones_an_issued_identity_and_classification_separates_removed_from_never_issued()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, 1_000);
        DurableIdentityReference live = identities.Allocate(DurableIdentityKind.Item);
        DurableIdentityReference retired = identities.Allocate(DurableIdentityKind.Item);

        identities.Remove(retired);

        Assert.Equal(DurableIdentityClassification.Live, identities.Classify(live));
        Assert.Equal(DurableIdentityClassification.Removed, identities.Classify(retired));
        Assert.DoesNotContain(retired.Value, identities.ReservedIdentities(DurableIdentityKind.Item));
        Assert.Contains(retired.Value, identities.RemovedIdentities(DurableIdentityKind.Item));
        Assert.Equal(DurableIdentityClassification.NeverIssued,
            identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, identities.NextIdentity(DurableIdentityKind.Item))));
        Assert.Equal(DurableIdentityClassification.WrongKind,
            identities.Classify(new DurableIdentityReference(DurableIdentityKind.Container, live.Value)));
        Assert.Equal(DurableIdentityClassification.WrongKind,
            identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, 0)));
    }

    [Fact]
    public void Removed_identities_are_never_reissued_and_removing_twice_is_a_no_op()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, 1_000);
        DurableIdentityReference first = identities.Allocate(DurableIdentityKind.Item);
        identities.Remove(first);
        identities.Remove(first);

        for (int index = 0; index < 8; index++) identities.Allocate(DurableIdentityKind.Item);

        Assert.Equal(DurableIdentityClassification.Removed, identities.Classify(first));
        Assert.Single(identities.RemovedIdentities(DurableIdentityKind.Item));
    }

    [Fact]
    public void Removing_an_unissued_identity_is_rejected()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, 1_000);

        // Neither an authored reservation nor a value above the cursor was issued.
        Assert.Throws<InvalidOperationException>(() =>
            identities.Remove(new DurableIdentityReference(DurableIdentityKind.Item, 1_000)));
        Assert.Throws<InvalidOperationException>(() =>
            identities.Remove(new DurableIdentityReference(DurableIdentityKind.Item, 1_001)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            identities.Remove(new DurableIdentityReference(DurableIdentityKind.Item, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            identities.Remove(new DurableIdentityReference((DurableIdentityKind)9, 1)));
    }

    [Fact]
    public void Captured_state_restores_a_ledger_that_continues_without_collision()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, 1_000, [1_000]);
        DurableIdentityReference live = identities.Allocate(DurableIdentityKind.Item);
        DurableIdentityReference retired = identities.Allocate(DurableIdentityKind.Item);
        identities.Remove(retired);
        DurableIdentityState captured = identities.CaptureState();

        DurableIdentityAllocator restored = DurableIdentityAllocator.Restore(captured);

        Assert.Equal(identities.NextIdentity(DurableIdentityKind.Item), restored.NextIdentity(DurableIdentityKind.Item));
        Assert.Equal(DurableIdentityClassification.Live, restored.Classify(live));
        Assert.Equal(DurableIdentityClassification.Removed, restored.Classify(retired));
        DurableIdentityReference next = restored.Allocate(DurableIdentityKind.Item);
        Assert.True(next.Value > retired.Value);
        Assert.NotEqual(live.Value, next.Value);
    }

    [Fact]
    public void Malformed_persisted_state_is_rejected_before_any_identity_is_issued()
    {
        Assert.Throws<ArgumentException>(() => DurableIdentityAllocator.Restore(new DurableIdentityState([
            new KindAllocatorState(DurableIdentityKind.Item, 1_000, [1_000, 1_000], []),
        ])));
        Assert.Throws<ArgumentException>(() => DurableIdentityAllocator.Restore(new DurableIdentityState([
            new KindAllocatorState(DurableIdentityKind.Item, 1_000, [0], []),
        ])));
        Assert.Throws<ArgumentException>(() => DurableIdentityAllocator.Restore(new DurableIdentityState([
            new KindAllocatorState(DurableIdentityKind.Item, 1_000, [], [0]),
        ])));
        Assert.Throws<ArgumentException>(() => DurableIdentityAllocator.Restore(new DurableIdentityState([
            new KindAllocatorState(DurableIdentityKind.Item, 1_000, [], [1_000]),
        ])));
        Assert.Throws<ArgumentException>(() => DurableIdentityAllocator.Restore(new DurableIdentityState([
            new KindAllocatorState(DurableIdentityKind.Item, 1_000, [7], [7]),
        ])));
        Assert.Throws<ArgumentOutOfRangeException>(() => DurableIdentityAllocator.Restore(new DurableIdentityState([
            new KindAllocatorState(DurableIdentityKind.Item, 0, [], []),
        ])));
        Assert.Throws<ArgumentException>(() => DurableIdentityAllocator.Restore(new DurableIdentityState([
            new KindAllocatorState(DurableIdentityKind.Item, 1_000, [], []),
            new KindAllocatorState(DurableIdentityKind.Item, 2_000, [], []),
        ])));
        Assert.Throws<ArgumentException>(() => new DurableIdentityState([
            new KindAllocatorState(DurableIdentityKind.Item, 1_000, [], []),
        ]).RequireKinds([DurableIdentityKind.Item, DurableIdentityKind.Actor]));
    }

    [Fact]
    public void Authored_reservations_above_the_cursor_stay_live_and_are_skipped()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, 1_000, [1_002]);

        Assert.Equal(DurableIdentityClassification.Live, identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, 1_002)));
        Assert.Equal(1_000UL, identities.Allocate(DurableIdentityKind.Item).Value);
        Assert.Equal(1_001UL, identities.Allocate(DurableIdentityKind.Item).Value);
        Assert.Equal(1_003UL, identities.Allocate(DurableIdentityKind.Item).Value);
    }

    [Fact]
    public void Kinds_allocate_and_tombstone_independently()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Actor, 10);
        DurableIdentityState actorOnly = identities.CaptureState();

        Assert.Throws<InvalidOperationException>(() => identities.Allocate(DurableIdentityKind.Item));
        Assert.Equal([DurableIdentityKind.Actor], actorOnly.Kinds.Select(state => state.Kind));
        Assert.Throws<ArgumentException>(() => DurableIdentityAllocator.Restore(actorOnly).CaptureState().RequireKinds([DurableIdentityKind.Item]));
    }

    [Fact]
    public void A_reference_that_resolves_to_nothing_is_dangling_while_a_live_unloaded_one_is_not()
    {
        // The player and one authored placement are reserved before gameplay, and no
        // dynamic identity has been issued yet, so the whole reserved range is content
        // that a later site load can legitimately materialize.
        DurableIdentityAllocator identities = new(DurableIdentityKind.Actor, 1_000, [1, 1001]);
        DurableIdentityReference player = new(DurableIdentityKind.Actor, 1);
        DurableIdentityReference placement = new(DurableIdentityKind.Actor, 1001);
        DurableIdentityReference dangling = new(DurableIdentityKind.Actor, 1002);

        Assert.True(identities.Classify(player) is DurableIdentityClassification.Live);
        Assert.True(identities.Classify(placement) is DurableIdentityClassification.Live);
        Assert.Equal(DurableIdentityClassification.NeverIssued, identities.Classify(dangling));
        Assert.Throws<InvalidOperationException>(() => identities.Remove(dangling));
        Assert.Equal(DurableIdentityClassification.NeverIssued, identities.Classify(dangling));

        // Issuing and removing a dynamic identity is a removal, not a dangling value.
        DurableIdentityReference spawned = identities.Allocate(DurableIdentityKind.Actor);
        identities.Remove(spawned);
        Assert.Equal(DurableIdentityClassification.Removed, identities.Classify(spawned));
        Assert.Equal(DurableIdentityClassification.Live, identities.Classify(placement));
    }

    [Fact]
    public void References_are_rejected_for_the_wrong_kind_even_when_the_value_is_live_elsewhere()
    {
        DurableIdentityAllocator actors = new(DurableIdentityKind.Actor, 10);
        DurableIdentityReference actor = actors.Allocate(DurableIdentityKind.Actor);
        DurableIdentityAllocator items = new(DurableIdentityKind.Item, 10);

        Assert.Equal(DurableIdentityClassification.WrongKind,
            items.Classify(new DurableIdentityReference(DurableIdentityKind.Container, actor.Value)));
        Assert.Equal(DurableIdentityClassification.WrongKind,
            items.Classify(new DurableIdentityReference(DurableIdentityKind.Resource, actor.Value)));
        Assert.Throws<InvalidOperationException>(() => items.Remove(new DurableIdentityReference(DurableIdentityKind.Container, actor.Value)));
        Assert.Throws<InvalidOperationException>(() => items.Allocate(DurableIdentityKind.Container));
        // The same numeric value is live for its own kind and meaningless for another,
        // which is what keeps a corpse id from being read as an item id.
        Assert.Equal(DurableIdentityClassification.Live, actors.Classify(actor));
    }
}
