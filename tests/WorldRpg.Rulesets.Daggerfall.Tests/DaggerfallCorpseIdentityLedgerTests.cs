using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCorpseIdentityLedgerTests
{
    [Fact]
    public void Unload_keeps_the_corpse_container_identity_for_restore()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Container, 2_000_000_000_000UL);
        DaggerfallCorpseIdentityLedger ledger = new(identities);
        using EntityDirectory entities = new();

        DurableIdentityReference corpse = ledger.Allocate(2_000_000_000_001L, entities);
        Assert.Equal(DurableIdentityKind.Container, corpse.Kind);
        Assert.NotEqual(2_000_000_000_001UL, corpse.Value);
        entities.Create(corpse, new EntityTypeId("daggerfall.corpse"));
        Assert.True(ledger.Unload(2_000_000_000_001L, entities));
        Assert.Equal(DurableIdentityClassification.Live, identities.Classify(corpse));

        DaggerfallCorpseIdentityLedger restored = new(identities);
        DurableIdentityReference roundTripped = restored.Restore(
            2_000_000_000_001L,
            new Dictionary<long, DurableIdentityReference> { [2_000_000_000_001L] = corpse },
            entities);

        Assert.Equal(corpse, roundTripped);
        entities.Create(roundTripped, new EntityTypeId("daggerfall.corpse"));
        Assert.True(entities.TryResolve(roundTripped, out _));
    }

    [Fact]
    public void Retirement_tombstones_the_container_and_next_allocation_cannot_reuse_it()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Container, 100);
        DaggerfallCorpseIdentityLedger ledger = new(identities);
        using EntityDirectory entities = new();

        DurableIdentityReference corpse = ledger.Allocate(7001, entities);
        entities.Create(corpse, new EntityTypeId("daggerfall.corpse"));
        Assert.True(ledger.Retire(7001, entities));

        Assert.Equal(DurableIdentityClassification.Removed, identities.Classify(corpse));
        Assert.False(entities.TryResolve(corpse, out _));
        DurableIdentityReference next = identities.Allocate(DurableIdentityKind.Container);
        Assert.NotEqual(corpse, next);
        Assert.False(ledger.Retire(7001, entities));
    }

    [Fact]
    public void Restore_rejects_a_changed_or_non_live_container_identity()
    {
        DurableIdentityAllocator identities = new(DurableIdentityKind.Container, 100);
        DaggerfallCorpseIdentityLedger ledger = new(identities);
        using EntityDirectory entities = new();
        DurableIdentityReference corpse = ledger.Allocate(7001, entities);

        Assert.Throws<ArgumentException>(() => ledger.Restore(
            7001,
            new Dictionary<long, DurableIdentityReference>
            {
                [7001] = new DurableIdentityReference(DurableIdentityKind.Container, corpse.Value + 1),
            },
            entities));
        Assert.Throws<ArgumentException>(() => new DaggerfallCorpseIdentityLedger(identities).Restore(
            7002,
            new Dictionary<long, DurableIdentityReference>
            {
                [7002] = new DurableIdentityReference(DurableIdentityKind.Actor, 8),
            },
            entities));
    }
}
