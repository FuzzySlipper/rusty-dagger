using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The door spells built alone over the effect lifecycle and a door runtime; the activation path is the session's (DaggerfallDoorMagicSessionTests).</summary>
public sealed class DaggerfallDoorSpellsTests
{
    [Fact]
    public void A_ready_Open_spell_opens_the_door_it_is_used_on_and_is_spent_and_without_one_the_hand_opens_it()
    {
        using Fixture f = new();
        Assert.Null(f.Spells.Activate(f.Doors, f.Door));
        f.Ready("open", 17);
        Assert.Equal(new DaggerfallActivationOutcome(true, "The spell opens the door."), f.Spells.Activate(f.Doors, f.Door));
        Assert.Empty(f.Effects.Active);
        Assert.Null(f.Spells.Activate(f.Doors, f.Door));
    }

    [Fact]
    public void A_ready_Lock_spell_goes_before_an_Open_spell_and_locks_the_door()
    {
        using Fixture f = new();
        f.Ready("open", 17);
        f.Ready("lock", 16);
        Assert.Equal(new DaggerfallActivationOutcome(true, "The door is locked by your spell."), f.Spells.Activate(f.Doors, f.Door));
        Assert.True(f.Doors.Read(f.Door).LockValue > 0);
        Assert.Equal("open", Assert.Single(f.Effects.Active).Definition.Key);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ActorsState _actors = new();
        private readonly EntityDirectory _store = new();
        internal DaggerfallEffectLifecycle Effects { get; }
        internal DaggerfallDoorRuntime Doors { get; }
        internal DaggerfallDoorSpells Spells { get; }
        internal DaggerfallRdbDoorId Door { get; } = new("S0000007.RDB", 1, 1, 3);

        internal Fixture()
        {
            DaggerfallActorDefinition player = TestPayload.Definitions.RequireActor(new("player"));
            _actors.CreatePlayer(1, new EntityTypeId("player"), new DaggerfallMechanicsState().CreateStats(player,
                DaggerfallPlayerVitals.Initial(player.Stats, TestPayload.Definitions.Catalogs.RequireCareer("class00"))), "health", DaggerActorFactory.PlayerCapabilities);
            Effects = new(_actors, new DaggerfallEffectCatalog(DaggerfallDoorMagicEffects.Definitions()));
            Doors = new(_store, RandomMinimum.Create(), [new(Door, new Vector3(2, 0, -4), new Vector3(0, 90, 0),
                new Vector3(-.5F, 0, -.1F), new Vector3(.5F, 2, .1F), DaggerfallDoorKind.Normal, 0)], "test");
            Spells = new(Effects, new DaggerfallItemInstances(), () => 30);
        }

        internal void Ready(string key, int type) => Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, Effects.Start(new($"{key}.1", key, $"test.{key}", 1, 1, key, "Magic", null, 1, null,
            JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(new(key, type, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1), 1, 0, 100), DaggerfallMagicJson.Default.DaggerfallCastEffectState))));

        public void Dispose() { Doors.Dispose(); _store.Dispose(); _actors.Dispose(); }
    }
}
