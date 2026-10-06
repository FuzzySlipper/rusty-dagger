using System.Numerics;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDoorMagicSessionTests
{
    [Fact]
    public void Ready_operation_survives_rounds_recast_and_restore_without_a_second_chance_roll()
    {
        using Fixture f = new(); var s = f.Session;
        Assert.Equal(DaggerfallCastOutcome.Applied, f.Cast(s, lockDoor: true));
        s.Update(new ProductUpdate(OuterUpdate(++f.Step), []));
        Assert.Contains(s.Slots.Read(), slot => slot.Label == "Ready to lock");
        var effect = Assert.Single(s.State.Effects.Active); Assert.Null(effect.Lifecycle.RemainingRounds);
        s.State.Effects.AdvanceElapsedRounds(200);
        Assert.Equal(DaggerfallCastOutcome.IncumbentRejected, f.Cast(s, lockDoor: true));
        Assert.Same(effect, Assert.Single(s.State.Effects.Active));
        using var restored = f.Restore(s.CaptureSave(), maximumRandom: true);
        Assert.Equal(effect.Context.Instance, Assert.Single(restored.State.Effects.Active).Context.Instance);
        f.Interact(restored, f.Door.Id);
        Assert.Equal(restored.State.Progression.Level, restored.Doors.Read(f.Door.Id).LockValue);
        Assert.Empty(restored.State.Effects.Active);
        Assert.DoesNotContain(restored.Slots.Read(), slot => slot.Owner == "magic.door-ready");
    }

    [Fact]
    public void Lock_uses_live_level_closes_open_door_and_precedes_ordinary_lockpick()
    {
        using Fixture f = new(); var s = f.Session;
        s.Doors.Open(f.Door.Id, DaggerfallDoorOperationSource.Player); s.Doors.Advance(2);
        f.Cast(s, lockDoor: true); s.State.Progression.AdvanceTo(0, 7);
        f.Mode(s, "lockpick"); f.Interact(s, f.Door.Id);
        var door = s.Doors.Read(f.Door.Id); Assert.Equal(7, door.LockValue); Assert.Equal(DaggerfallDoorMotion.Closing, door.Motion);
        Assert.Null(s.Doors.FailedLockpickingSkill(f.Door.Id)); Assert.Contains("locked by", s.ActivationView.Message);
        s.Doors.Advance(2); Assert.True(s.Doors.Read(f.Door.Id).CollisionEnabled);
        Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Lock_does_not_reverse_a_door_that_is_still_opening()
    {
        using Fixture f = new(); var s = f.Session;
        s.Doors.Open(f.Door.Id, DaggerfallDoorOperationSource.Player);
        f.Cast(s, lockDoor: true); f.Interact(s, f.Door.Id);
        Assert.Equal(1, s.Doors.Read(f.Door.Id).LockValue); Assert.Equal(DaggerfallDoorMotion.Opening, s.Doors.Read(f.Door.Id).Motion);
        Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Lock_precedes_open_regardless_of_cast_order_and_each_operation_is_consumed_once()
    {
        using Fixture f = new(); var s = f.Session;
        f.Cast(s, lockDoor: false); f.Cast(s, lockDoor: true);
        f.Interact(s, f.Door.Id); Assert.Equal(1, s.Doors.Read(f.Door.Id).LockValue);
        Assert.Equal(DaggerfallDoorMagic.Open, Assert.Single(s.State.Effects.Active).Definition.DoorMagic);
        f.Interact(s, f.Door.Id); Assert.Empty(s.State.Effects.Active);
        Assert.Equal(0, s.Doors.Read(f.Door.Id).LockValue); Assert.Equal(DaggerfallDoorMotion.Opening, s.Doors.Read(f.Door.Id).Motion);
        Assert.True(s.ActivationView.Applied); Assert.Contains("spell opens", s.ActivationView.Message);
    }

    [Fact]
    public void Already_locked_open_door_keeps_strength_but_closes_and_consumes_lock()
    {
        using Fixture f = new(); var s = f.Session;
        s.Doors.Open(f.Door.Id, DaggerfallDoorOperationSource.Player); s.Doors.Advance(2);
        s.Doors.Lock(f.Door.Id, DaggerfallDoorOperationSource.Spell, 15);
        f.Cast(s, lockDoor: true); f.Interact(s, f.Door.Id);
        Assert.Equal(15, s.Doors.Read(f.Door.Id).LockValue); Assert.Equal(DaggerfallDoorMotion.Closing, s.Doors.Read(f.Door.Id).Motion);
        Assert.True(s.ActivationView.Applied); Assert.Contains("already locked", s.ActivationView.Message); Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Open_checks_live_level_after_cast_and_opens_at_equal_magical_lock_strength()
    {
        using Fixture f = new(); var s = f.Session;
        s.Doors.Lock(f.Door.Id, DaggerfallDoorOperationSource.Spell, 20);
        f.Cast(s, lockDoor: false); s.State.Progression.AdvanceTo(0, 20); f.Interact(s, f.Door.Id);
        Assert.True(s.ActivationView.Applied); Assert.Equal(0, s.Doors.Read(f.Door.Id).LockValue);
        Assert.Equal(DaggerfallDoorMotion.Opening, s.Doors.Read(f.Door.Id).Motion); Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Strong_lock_rejects_open_consumes_effect_and_preserves_ordinary_activation_failure()
    {
        using Fixture f = new(); var s = f.Session;
        s.Doors.Lock(f.Door.Id, DaggerfallDoorOperationSource.Spell, 30);
        f.Cast(s, lockDoor: false); f.Interact(s, f.Door.Id);
        Assert.False(s.ActivationView.Applied); Assert.Contains("too strong", s.ActivationView.Message);
        Assert.Empty(s.State.Effects.Active); Assert.Equal(30, s.Doors.Read(f.Door.Id).LockValue);
        f.Interact(s, f.Door.Id); Assert.False(s.ActivationView.Applied); Assert.Equal(30, s.Doors.Read(f.Door.Id).LockValue);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void All_item_open_sources_bypass_chance_but_do_not_bypass_lock_level(int source)
    {
        using Fixture f = new(maximumRandom: true); var s = f.Session;
        Assert.Equal(DaggerfallCastOutcome.ChanceFailed, f.Cast(s, lockDoor: false)); Assert.Empty(s.State.Effects.Active);
        ulong item = s.State.Inventory.Read().UniqueItems.Select(value => s.State.Inventory.GetDurableItemId(value.Entity).Value).First();
        Assert.Equal(DaggerfallCastOutcome.Applied, f.Cast(s, lockDoor: false, item, (DaggerfallCastSource)source));
        s.Doors.Lock(f.Door.Id, DaggerfallDoorOperationSource.Spell, 30); f.Interact(s, f.Door.Id);
        Assert.Equal(30, s.Doors.Read(f.Door.Id).LockValue); Assert.False(s.ActivationView.Applied); Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Real_skeleton_key_opens_magically_held_interior_lock_after_save_restore()
    {
        using Fixture f = new(maximumRandom: true); var s = f.Session;
        var created = new DaggerfallItemFactory(TestPayload.Definitions, RandomMinimum.Create())
            .Create(new("Magic", "skeleton-key", DaggerfallItemOwner.Player, MagicItemKey: DaggerfallMagicItemIds.SkeletonKey));
        Assert.Equal(2, TestPayload.Definitions.Magic.MagicItems[DaggerfallMagicItemIds.SkeletonKey].Type);
        var identity = s.UniqueItemAllocator.AllocateReference(); s.State.Equipment.Materialize(identity, created.Item);
        s.State.ItemInstances.RegisterUnique(identity.Value, created.Metadata);
        s.Doors.Lock(f.Door.Id, DaggerfallDoorOperationSource.Spell, 30);
        f.Cast(s, lockDoor: false, identity.Value, DaggerfallCastSource.ItemUse);
        using var restored = f.Restore(s.CaptureSave(), maximumRandom: true);
        f.Interact(restored, f.Door.Id); Assert.True(restored.ActivationView.Applied);
        Assert.Equal(0, restored.Doors.Read(f.Door.Id).LockValue); Assert.Equal(DaggerfallDoorMotion.Opening, restored.Doors.Read(f.Door.Id).Motion);
        Assert.Empty(restored.State.Effects.Active);
    }

    [Fact]
    public void Destroyed_item_source_cancels_ready_operation_before_door_mutation()
    {
        using Fixture f = new(); var s = f.Session;
        ulong item = s.State.Inventory.Read().UniqueItems.Select(value => s.State.Inventory.GetDurableItemId(value.Entity).Value).First();
        f.Cast(s, lockDoor: true, item, DaggerfallCastSource.ItemUse);
        // A used item's ready effect outlives the item breaking, as in the donor; destroying it ends it.
        s.State.ItemInstances.ReplaceUnique(item, s.State.ItemInstances.RequireUnique(item) with { CurrentCondition = 0 });
        Assert.Single(s.State.Effects.Active);
        s.State.ItemInstances.RemoveUnique(item);
        Assert.Empty(s.State.Effects.Active);
        f.Interact(s, f.Door.Id); Assert.Equal(0, s.Doors.Read(f.Door.Id).LockValue); Assert.Equal(DaggerfallDoorMotion.Opening, s.Doors.Read(f.Door.Id).Motion);
    }

    [Fact]
    public void No_target_and_information_leave_ready_effect_while_real_door_state_survives_site_return()
    {
        using Fixture f = new(); var s = f.Session;
        f.Cast(s, lockDoor: true);
        f.Spatial.FloorHit = _ => default;
        s.Update(new ProductUpdate(OuterUpdate(++f.Step), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.Single(s.State.Effects.Active); f.Mode(s, "info"); f.Interact(s, f.Door.Id); Assert.Single(s.State.Effects.Active);
        f.Mode(s, "grab"); f.Interact(s, f.Door.Id); Assert.Empty(s.State.Effects.Active);
        Assert.Equal(1, s.Doors.Read(f.Door.Id).LockValue);
        Assert.True(s.TryTransitionTo(f.Destination.ProfileKey));
        using var restored = f.Restore(s.CaptureSave()); Assert.True(restored.TryTransitionTo(f.Composition.StartSite.ProfileKey));
        Assert.Equal(1, restored.Doors.Read(f.Door.Id).LockValue); Assert.True(restored.Doors.Read(f.Door.Id).CollisionEnabled);
    }

    [Fact]
    public void Malformed_finite_ready_effect_is_rejected_in_current_save()
    {
        using Fixture f = new(); f.Cast(f.Session, lockDoor: true);
        var saved = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        var invalid = saved with { ActiveEffects = [saved.ActiveEffects.Single() with { RemainingRounds = 1 }] };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(invalid)));
    }

    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSession Session { get; }
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSiteProfile Destination { get; }
        internal DaggerfallDoorView Door { get; }
        internal SpatialFake Spatial { get; private set; } = null!;
        internal ulong Step;
        private readonly bool _maximumRandom;
        internal Fixture(bool maximumRandom = false)
        {
            _maximumRandom = maximumRandom;
            var inputs = ReadInputs(TestData.RepositoryRoot);
            Destination = DaggerfallSiteContent.Read(FullContent(TestData.RepositoryRoot), File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot,
                "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), TestPayload.Definitions);
            Composition = new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults) { Profiles = new DaggerfallSiteProfiles([inputs, Destination]) };
            Session = DaggerfallSession.StartNew(Engine(maximumRandom).Context, Composition);
            Door = Session.Doors.All.First(value => !value.IsLocked && value.Kind == DaggerfallDoorKind.Normal);
        }
        private EngineContextFake Engine(bool maximumRandom)
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, Composition.StartSite); PopulateContent(content, Destination);
            Spatial = SpatialFake.Create(Composition.StartSite.SpatialArtifact.Sha256, releases); Spatial.KeepPosition = true;
            var perception = PerceptionFake.Create(); perception.Responder = request => Receipt(request.Targets.Span.ToArray()
                .Select(target => new PerceptionPair(1, target.Entity, 1, 1, PerceptionPairKind.Visible, 1)).ToArray());
            return EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(releases), perception.Service,
                random: maximumRandom ? RandomMaximum.Create() : RandomMinimum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload, bool maximumRandom = false) => DaggerfallSession.Restore(Engine(maximumRandom).Context, Composition, payload);
        internal DaggerfallCastOutcome Cast(DaggerfallSession s, bool lockDoor, ulong? item = null, DaggerfallCastSource source = DaggerfallCastSource.Spell)
        {
            string key = lockDoor ? "spell.019" : "spell.018"; s.State.Character.LearnSpell(key);
            var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(10000);
            Assert.Equal(DaggerfallCastOutcome.Ready, s.Casting.Ready(1, key, item, source).Outcome);
            return Assert.Single(s.ReleaseReadySpell(1, Vector3.UnitZ).Bundle!.Results).Outcome;
        }
        internal void Mode(DaggerfallSession s, string mode) => s.Update(new ProductUpdate(OuterUpdate(++Step), [Input(InputEventKind.DirectDigital) with
            { ValueKind = InputValueKind.ProductPayload, PayloadContract = "dagger.ui.action.v1"u8.ToArray(), PayloadData = Encoding.UTF8.GetBytes("{\"action\":\"activation-mode\",\"mode\":\"" + mode + "\"}") }]));
        internal void Interact(DaggerfallSession s, DaggerfallRdbDoorId id)
        {
            var door = s.Doors.Read(id); s.State.PlayerControl.MoveTo(door.Pose.Translation + Vector3.UnitZ); s.State.PlayerControl.YawRadians = 0; s.State.PlayerControl.PitchRadians = 0;
            Spatial.FloorHit = request => request.Direction.Y < -.5 ? default : new SpatialHit { Present = true, Kind = SpatialHitKind.Entity,
                Entity = door.Entity.Value, Point = door.Pose.Translation, Distance = 1 };
            s.Update(new ProductUpdate(OuterUpdate(++Step), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        }
        public void Dispose() => Session.Dispose();
    }
}
