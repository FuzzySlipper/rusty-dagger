using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallAttributeTransferEffectsTests
{
    private static long _sequence = 100;
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Every_attribute_drains_target_and_heals_only_existing_caster_damage_without_restore_replay(int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        Stat(s, 1, subtype).BaseValue = 50; Stat(s, 2000, subtype).BaseValue = 50;
        Start(s, "caster-drain", 7, subtype, 12, target: 1, caster: 2000);
        Cast(s, subtype, 8); Assert.Equal(42, Stat(s, 2000, subtype).Value); Assert.Equal(46, Stat(s, 1, subtype).Value);
        Cast(s, subtype, 5, DaggerfallCastOutcome.Refreshed); Assert.Equal(37, Stat(s, 2000, subtype).Value); Assert.Equal(50, Stat(s, 1, subtype).Value);
        Assert.Equal(50, Stat(s, 1, subtype).BaseValue); Assert.Empty(Stat(s, 1, subtype).Sources);
        var transferred = Assert.Single(s.State.Effects.Active); Assert.Null(transferred.Context.Caster); Assert.Null(transferred.Lifecycle.RemainingRounds);
        Assert.Equal(1, DaggerfallAttributeDrainEffects.Read(transferred.State, subtype, 11).Cast.Origin!.CasterId);
        // New damage after the accepted heal must remain when loading; Resume cannot heal a second time.
        Start(s, "later-drain", 7, subtype, 6, target: 1, caster: 2000);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(44, Stat(restored, 1, subtype).Value); Assert.Equal(37, Stat(restored, 2000, subtype).Value);
        Assert.Equal(13, Heal(restored, 2000, subtype, 100)); Assert.Equal(50, Stat(restored, 2000, subtype).Value);
        Assert.Equal(44, Stat(restored, 1, subtype).Value);
        var setting = Setting(11, subtype, 2) with { MagnitudeLevelBase = 7, MagnitudeLevelHigh = 7, MagnitudePerLevel = 3 };
        var row = TestPayload.Definitions.Magic.RequireEffectCost(setting);
        Assert.Equal((6, "destruction", 5, 5, 15, 25), (row.SettingsType, row.School, row.Coefficient0, row.Coefficient1, row.Coefficient2, row.Coefficient3));
        var quote = DaggerfallMagicCostPolicy.CalculateEffectCosts(TestPayload.Definitions.Magic, setting, new Dictionary<string,int> { ["destruction"] = 0 });
        Assert.Equal(360, quote.Gold); Assert.Equal(99, quote.SpellPoints);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Missing_or_dead_caster_leaves_accepted_target_damage_without_healing_another_actor(bool dead)
    {
        using Fixture f = new(); var s = f.Session;
        long caster = s.SpawnActor("rat", new(new(10, 0, 10), 0));
        Start(s, "player-damage", 7, 0, 9, target: 1, caster: 2000);
        if (dead) s.State.Actors.Get(caster).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0); else s.RetireActor(caster);
        double before = Stat(s, 2000, 0).Value, playerBefore = Stat(s, 1, 0).Value;
        Start(s, "orphan-transfer", 11, 0, 7, target: 2000, caster: caster);
        Assert.Equal(before - 7, Stat(s, 2000, 0).Value); Assert.Equal(playerBefore, Stat(s, 1, 0).Value);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(before - 7, Stat(restored, 2000, 0).Value);
    }

    [Fact]
    public void Incoming_transfer_merges_into_drain_but_incoming_drain_does_not_merge_into_transfer()
    {
        using Fixture f = new(); var s = f.Session;
        Stat(s, 2000, 0).BaseValue = 50;
        Start(s, "drain-first", 7, 0, 5); Start(s, "transfer-second", 11, 0, 7);
        var first = Assert.Single(s.State.Effects.Active); Assert.Equal("drain-first", first.Context.Instance.Value); Assert.Equal(7, first.Definition.Spell!.Type);
        Assert.Equal(38, Stat(s, 2000, 0).Value); Heal(s, 2000, 0, 100);
        Start(s, "transfer-first", 11, 0, 7); Start(s, "drain-second", 7, 0, 5);
        Assert.Equal(2, s.State.Effects.Active.Count); Assert.Equal(38, Stat(s, 2000, 0).Value);
        Start(s, "transfer-third", 11, 0, 3); Assert.Equal(2, s.State.Effects.Active.Count); Assert.Equal(35, Stat(s, 2000, 0).Value);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(35, Stat(restored, 2000, 0).Value);
    }

    [Fact]
    public void Ordered_partial_healing_follows_saved_cast_sequence_instead_of_lexical_instance_names()
    {
        using Fixture f = new(); var s = f.Session;
        // A Transfer incumbent followed by Drain coexists; lexical order intentionally opposes admitted order.
        Start(s, "cast.2", 11, 0, 7, sequence: 2); Start(s, "cast.10", 7, 0, 5, sequence: 10);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(4, Heal(restored, 2000, 0, 4));
        var transfer = restored.State.Effects.Active.Single(effect => effect.Definition.Spell!.Type == 11);
        var drain = restored.State.Effects.Active.Single(effect => effect.Definition.Spell!.Type == 7);
        Assert.Equal(3, DaggerfallAttributeDrainEffects.Read(transfer.State, 0, 11).Magnitude);
        Assert.Equal(5, DaggerfallAttributeDrainEffects.Read(drain.State, 0).Magnitude);
        Assert.Equal(2, transfer.BundleSequence); Assert.Equal(10, drain.BundleSequence);
    }

    [Fact]
    public void Rolled_healing_is_not_reduced_by_target_floor_and_never_fortifies_an_undamaged_caster()
    {
        using Fixture f = new(); var s = f.Session;
        Stat(s, 2000, 0).BaseValue = 5; Stat(s, 1, 0).BaseValue = 50;
        Start(s, "caster-damage", 7, 0, 10, target: 1, caster: 2000);
        Cast(s, 0, 20); Assert.Equal(1, Stat(s, 2000, 0).Value); Assert.Equal(50, Stat(s, 1, 0).Value);
        Cast(s, 0, 20, DaggerfallCastOutcome.Refreshed); Assert.Equal(50, Stat(s, 1, 0).Value); Assert.Empty(Stat(s, 1, 0).Sources);
    }

    [Fact]
    public void Permanent_result_survives_caster_and_item_retirement_while_target_retirement_removes_it()
    {
        using Fixture f = new(); var s = f.Session;
        long caster = s.SpawnActor("rat", new(new(10, 0, 10), 0));
        ulong item = s.State.Inventory.Read().UniqueItems.Select(value => s.State.Inventory.GetDurableItemId(value.Entity).Value).First();
        Start(s, "persistent", 11, 0, 7, caster: caster, item: item);
        var equipped = s.State.Inventory.Read().UniqueItems.Single(value => s.State.Inventory.GetDurableItemId(value.Entity).Value == item);
        s.State.Equipment.Unequip(new(equipped.Entity.Value, new(equipped.Definition.Value)));
        s.State.ItemInstances.ReplaceUnique(item, s.State.ItemInstances.RequireUnique(item) with { CurrentCondition = 0 }); s.RetireActor(caster);
        var effect = Assert.Single(s.State.Effects.Active); Assert.Null(effect.Context.Caster); Assert.Null(effect.Context.Item);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(caster, DaggerfallAttributeDrainEffects.Read(Assert.Single(restored.State.Effects.Active).State, 0, 11).Cast.Origin!.CasterId);
        restored.State.Effects.CancelActorReferences(2000); Assert.Empty(restored.State.Effects.Active);
    }

    private static DaggerfallSpellEffectDefinition Setting(int type, int subtype, int amount) => new("attribute", type, subtype, 0, 0, 1, 0, 0, 1, amount, amount, 0, 0, 1);
    private static void Start(DaggerfallSession s, string instance, int type, int subtype, int amount, long target = 2000, long caster = 1, long sequence = 0, ulong? item = null) =>
        s.State.Effects.Start(new(instance, type == 7 ? DaggerfallAttributeDrainEffects.Key(subtype) : $"transfer-{DaggerfallMechanicsIds.Attributes[subtype].Value}", "spell.attribute", null, target, "attribute", "Magic", null, 1, null,
            DaggerfallAttributeDrainEffects.Encode(new(new(Setting(type, subtype, amount), 1, amount, 100, new(caster, item, item is null ? DaggerfallCastSource.Spell : DaggerfallCastSource.ItemUse)), amount))) { BundleSequence = sequence, BundleKind = DaggerfallEffectBundleKind.Spell });
    private static int Heal(DaggerfallSession s, long target, int subtype, int amount) => DaggerfallAttributeDrainEffects.Heal(s.State.Effects, target, DaggerfallMechanicsIds.Attributes[subtype], amount, () => s.State.Character.Career);
    private static Stat Stat(DaggerfallSession s, long id, int subtype) => (id == 1 ? s.State.Actors.Player.Stats : s.State.Actors.Get(id).Stats).GetStat(StatId.Parse(DaggerfallMechanicsIds.Attributes[subtype].Value));
    private static void Cast(DaggerfallSession s, int subtype, int amount, DaggerfallCastOutcome expected = DaggerfallCastOutcome.Applied)
    {
        var spell = TestPayload.Definitions.Magic.Spells["spell.009"] with { Key = "transfer.test", Effects = [Setting(11, subtype, amount)] };
        var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(10000);
        var casting = new DaggerfallCasting(TestPayload.Definitions.Magic with { Spells = new Dictionary<string,DaggerfallSpellDefinition> { [spell.Key] = spell } }, s.State.Effects,
            id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id, out var actor) ? actor.Actor : null, s.MagicProfile, _ => true, _ => { }, _ => { },
            RandomMaximum.Create(), 1, nextSequence: Interlocked.Increment(ref _sequence), playerKnowsSpell: _ => true, casterLevel: _ => 1);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome); var bundle = casting.Release(1, true).Bundle!; casting.Deliver(bundle, [2000]);
        Assert.Equal(expected, Assert.Single(bundle.Results).Outcome); Assert.True(Assert.Single(s.State.Effects.Active.Where(effect => effect.Context.Target.Value == 2000)).BundleSequence > 0);
    }
    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSession Session { get; }
        private readonly DaggerfallSessionComposition _composition;
        internal Fixture() { _composition = new(TestPayload.Definitions, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults); Session = DaggerfallSession.StartNew(Engine().Context, _composition); }
        private EngineContextFake Engine() { List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, _composition.StartSite); return EngineContextFake.Create(content, SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases), random: RandomMaximum.Create()); }
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine().Context, _composition, payload);
        public void Dispose() => Session.Dispose();
    }
}
