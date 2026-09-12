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
        // The reservation set is the ledger's record of everything it must never
        // hand out again: the authored identities plus every identity it issued.
        Assert.Equal([500UL, 1_000UL, 1_001UL, 1_002UL, 1_003UL], identities.ReservedIdentities(DurableIdentityKind.Item).Order());
    }

    [Fact]
    public void A_persisted_cursor_may_sit_on_a_reservation_without_letting_the_next_session_reissue_it()
    {
        // A reservation immediately above the last issued identity is the case where
        // the persisted progress marker is not the next identity to issue. It must not
        // be advanced to the next issuable identity, or the identity between them would
        // be reissued after a restore.
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, 10, [11]);
        DurableIdentityReference issued = identities.Allocate(DurableIdentityKind.Item);
        Assert.Equal(10UL, issued.Value);
        Assert.Equal(12UL, identities.NextIdentity(DurableIdentityKind.Item));

        DurableIdentityState captured = identities.CaptureState();
        Assert.Equal(11UL, captured.Kinds.Single().NextIdentity);
        DurableIdentityAllocator restored = DurableIdentityAllocator.Restore(captured);

        // The reservation is still live and the next identity is past it, while the
        // already-issued identity stays issued rather than becoming available again.
        Assert.Equal(DurableIdentityClassification.Live, restored.Classify(new DurableIdentityReference(DurableIdentityKind.Item, 11)));
        DurableIdentityReference next = restored.Allocate(DurableIdentityKind.Item);
        Assert.Equal(12UL, next.Value);
        Assert.NotEqual(issued.Value, next.Value);
    }

    [Fact]
    public void Removal_follows_the_ledger_record_below_the_cursor_and_the_session_guards_authorship()
    {
        // Below the progress marker the ledger cannot tell an authored reservation from
        // an issued identity, so it accepts both and the session owns the distinction;
        // above the marker it still refuses, because nothing there was issued.
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, 10, [100]);
        Assert.Equal(10UL, identities.Allocate(DurableIdentityKind.Item).Value);
        Assert.Equal(11UL, identities.Allocate(DurableIdentityKind.Item).Value);

        // An issued identity below the marker is removable, and its tombstone sticks.
        identities.Remove(new DurableIdentityReference(DurableIdentityKind.Item, 10));
        Assert.Equal(DurableIdentityClassification.Removed, identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, 10)));
        // A reservation is live but was never issued, so the ledger refuses it above the
        // marker and the session owns that distinction below the marker.
        Assert.Equal(DurableIdentityClassification.Live, identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, 100)));
        Assert.Throws<InvalidOperationException>(() =>
            identities.Remove(new DurableIdentityReference(DurableIdentityKind.Item, 100)));
        Assert.Throws<InvalidOperationException>(() =>
            identities.Remove(new DurableIdentityReference(DurableIdentityKind.Item, 5_000)));
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

        // An authored reservation and a value above the cursor were never issued.
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
        // Zero is the exhaustion marker, and a ledger that has spent its space still
        // has to be capturable and restorable.
        DurableIdentityAllocator exhausted = DurableIdentityAllocator.Restore(new DurableIdentityState([
            new KindAllocatorState(DurableIdentityKind.Item, 0, [7], []),
        ]));
        Assert.Throws<InvalidOperationException>(() => exhausted.Allocate(DurableIdentityKind.Item));
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

    [Fact]
    public void Repeated_allocate_remove_capture_and_restore_cycles_never_collide_or_reissue()
    {
        var random = new Random(20260912);
        for (int round = 0; round < 40; round++)
        {
            ulong first = (ulong)random.NextInt64(10, 10_000);
            ulong[] reserved = Enumerable.Range(0, random.Next(0, 40))
                .Select(_ => (ulong)random.NextInt64(1, 20_000))
                .Distinct()
                .ToArray();
            DurableIdentityAllocator identities = new(DurableIdentityKind.Item, first, reserved);
            List<ulong> issued = [];
            for (int step = 0; step < 40; step++)
            {
                DurableIdentityReference reference = identities.Allocate(DurableIdentityKind.Item);
                Assert.DoesNotContain(reference.Value, issued);
                Assert.DoesNotContain(reference.Value, reserved);
                Assert.Equal(DurableIdentityClassification.Live, identities.Classify(reference));
                issued.Add(reference.Value);
            }

            for (int index = 0; index < issued.Count; index += 2)
                identities.Remove(new DurableIdentityReference(DurableIdentityKind.Item, issued[index]));

            DurableIdentityState captured = identities.CaptureState();
            DurableIdentityAllocator restored = DurableIdentityAllocator.Restore(captured);
            for (int index = 0; index < issued.Count; index++)
            {
                DurableIdentityClassification expected = index % 2 == 0
                    ? DurableIdentityClassification.Removed
                    : DurableIdentityClassification.Live;
                Assert.Equal(expected, restored.Classify(new DurableIdentityReference(DurableIdentityKind.Item, issued[index])));
            }

            for (int step = 0; step < 10; step++)
            {
                DurableIdentityReference reference = restored.Allocate(DurableIdentityKind.Item);
                Assert.DoesNotContain(reference.Value, issued);
                issued.Add(reference.Value);
            }

            Assert.Equal(captured.Kinds.Single().Removed.Length, restored.CaptureState().Kinds.Single().Removed.Length);
        }
    }

    [Fact]
    public void The_cursor_never_advertises_an_identity_that_allocation_would_refuse()
    {
        // The last valid identity is issued in full, and the cursor then marks
        // exhaustion rather than wrapping to an invalid identity or a value that
        // allocation would refuse.
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, ulong.MaxValue);

        DurableIdentityReference last = identities.Allocate(DurableIdentityKind.Item);
        Assert.Equal(ulong.MaxValue, last.Value);
        Assert.Equal(DurableIdentityClassification.Live, identities.Classify(last));
        Assert.Throws<InvalidOperationException>(() => identities.Allocate(DurableIdentityKind.Item));
        Assert.Throws<InvalidOperationException>(() => identities.NextIdentity(DurableIdentityKind.Item));

        // The exhausted kind still captures and restores, with zero as the marker.
        DurableIdentityState captured = identities.CaptureState();
        Assert.Equal(0UL, captured.Kinds.Single().NextIdentity);
        DurableIdentityAllocator restored = DurableIdentityAllocator.Restore(captured);
        Assert.Equal(DurableIdentityClassification.Live, restored.Classify(last));
        Assert.Throws<InvalidOperationException>(() => restored.Allocate(DurableIdentityKind.Item));
        Assert.Throws<InvalidOperationException>(() => restored.NextIdentity(DurableIdentityKind.Item));
    }

    [Fact]
    public void Restore_rejects_evidence_that_describes_no_kind_at_all()
    {
        Assert.Throws<ArgumentException>(() => DurableIdentityAllocator.Restore(new DurableIdentityState([])));
        DurableIdentityState empty = new DurableIdentityState([]);
        Assert.Throws<ArgumentException>(() => empty.RequireKinds([DurableIdentityKind.Item]));
    }

    [Fact]
    public void A_value_the_cursor_passed_stays_live_even_when_an_older_save_omits_it_from_reserved()
    {
        // An older save may list only authored reservations; an issued identity below
        // the cursor is still live rather than dangling.
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, 1_000);

        Assert.Equal(DurableIdentityClassification.Live, identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, 999)));
        Assert.Equal(DurableIdentityClassification.NeverIssued, identities.Classify(new DurableIdentityReference(DurableIdentityKind.Item, 1_000)));
    }

    [Fact]
    public void An_exhausted_kind_that_also_holds_tombstones_still_captures_and_restores()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Item, ulong.MaxValue - 1);
        DurableIdentityReference first = identities.Allocate(DurableIdentityKind.Item);
        DurableIdentityReference second = identities.Allocate(DurableIdentityKind.Item);
        identities.Remove(first);

        DurableIdentityState captured = identities.CaptureState();
        Assert.Equal(0UL, captured.Kinds.Single().NextIdentity);
        Assert.Equal([first.Value], captured.Kinds.Single().Removed);

        DurableIdentityAllocator restored = DurableIdentityAllocator.Restore(captured);

        Assert.Equal(DurableIdentityClassification.Removed, restored.Classify(first));
        Assert.Equal(DurableIdentityClassification.Live, restored.Classify(second));
        Assert.Equal([first.Value], restored.RemovedIdentities(DurableIdentityKind.Item));
        Assert.Throws<InvalidOperationException>(() => restored.Allocate(DurableIdentityKind.Item));
    }
}
