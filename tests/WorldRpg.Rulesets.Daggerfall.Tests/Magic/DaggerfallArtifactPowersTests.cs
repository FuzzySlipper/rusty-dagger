using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The artifacts' powers built alone over their inputs; summoning on a live site is the session's (SanguineRoseSessionTests).</summary>
public sealed class DaggerfallArtifactPowersTests
{
    [Fact]
    public void The_summoning_artifacts_refuse_without_a_player_or_a_foe_and_spend_nothing()
    {
        using Fixture f = new();
        var rose = f.Item();
        Assert.Equal(new DaggerfallInventoryUseResult(false, "Sanguine Rose cannot summon without a player position."), f.Powers.UseSanguineRose(rose));
        f.Player = new WorldPoint(0f, 0f, 0f);
        Assert.Equal(new DaggerfallInventoryUseResult(false, "No monsters nearby."), f.Powers.UseSanguineRose(rose));
        Assert.Equal(new DaggerfallInventoryUseResult(false, "No monsters nearby."), f.Powers.UseSkullCorruption(rose));
        Assert.Empty(f.Spawned);
    }

    [Fact]
    public void Molag_Bal_and_the_Wabbajack_need_their_source_held_and_whole()
    {
        using Fixture f = new();
        Assert.False(f.Powers.MolagBalEquipped(1, 999));
        Assert.Equal(default, f.Powers.TransferMolagBal(1, 77, 999, 10, 0, 0, 0));
        Assert.Equal(DaggerfallWabbajackOutcome.SourceUnavailable, f.Powers.TransformWithWabbajack(1, 77, 999, 0, 0).Outcome);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GuildMakerFixture _items = new();
        private readonly ActorsState _actors = new();
        internal WorldPoint? Player { get; set; }
        internal List<string> Spawned { get; } = [];
        internal DaggerfallArtifactPowers Powers { get; }

        internal Fixture()
        {
            _actors.CreatePlayer(1, new EntityTypeId("player"), _items.Stats, "health", DaggerActorFactory.PlayerCapabilities);
            DaggerfallEquipmentMoves moves = new(_items.Inventory, _items.Equipment, _items.Definitions, itemInstances: _items.Instances);
            Powers = new(_items.Definitions, _items.Instances, _items.Inventory, new DaggerfallItemConditionService(_items.Definitions, _items.Instances, moves),
                new DaggerfallEffectLifecycle(_actors, DaggerfallEffectCatalog.Empty), _actors, RandomMinimum.Create(), () => DaggerfallCalendar.Start,
                () => Player, _items.Equipment, null!, id => id == 1 ? _actors.Player.Actor : null, _ => null,
                (_, _, _) => [], _ => true, (_, _, _, _, _) => null, (definition, _, _) => { Spawned.Add(definition); return 2; },
                _ => false, (id, _) => new(DaggerfallWabbajackOutcome.InvalidTarget, id));
        }

        internal WorldRpg.Kit.Inventory.UniqueInventoryItem Item() => _items.Weapon().Item;

        public void Dispose() { _actors.Dispose(); _items.Dispose(); }
    }
}
