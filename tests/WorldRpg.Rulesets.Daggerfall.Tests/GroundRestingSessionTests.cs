using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Things that come to rest in the world rest on its floor: dropped piles, treasure piles and corpses.</summary>
public sealed class GroundRestingSessionTests
{
    [Fact]
    public void A_dropped_pile_rests_on_the_floor_under_the_player_not_at_the_body_centre_and_stays_there_after_restore()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        s.State.PlayerControl.MoveTo(new Vector3(10F, 1.2F, 10F));
        f.FloorY = .25F;
        var stack = s.State.Inventory.Read().Stacks.First();
        s.PublishInitial();

        f.Submit(new { action = "inventory-drop", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"stack:{stack.Id.Value}", amount = 1 });

        DaggerfallGroundContainerSave dropped = Assert.Single(DaggerfallSavePayload.Read(s.CaptureSave()).GroundContainers,
            pile => pile.TreasurePlacement is null && pile.PropertyPlacement is null);
        Assert.Equal((10F, .25F, 10F), (dropped.X, dropped.Y, dropped.Z));
        using DaggerfallSession restored = f.Restore(s.CaptureSave());
        Assert.Equal(.25F, Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).GroundContainers,
            pile => pile.TreasurePlacement is null && pile.PropertyPlacement is null).Y);
    }

    [Fact]
    public void A_corpse_falls_to_the_floor_beneath_where_its_actor_died()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        WorldPoint player = s.State.PlayerControl.Position ?? throw new InvalidOperationException("The fixture player has no position.");
        long rat = s.SpawnActor("rat", new ActorPose(player with { Z = player.Z - 1F }, 0F));
        // Killed at the player's height, three metres above the floor beneath it, as a flyer dies in the air.
        s.State.Actors.Get(rat).ApplyPose(new ActorPose(player with { Z = player.Z - 1F }, 0F));
        f.FloorY = player.Y - 3F;
        s.State.Actors.Get(rat).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60;

        s.ResolveExplicitMelee(new ExplicitMeleeRequest(1, rat, 1, 1, .125));

        Assert.True(s.Corpses.ContainsKey(rat));
        WorldPoint corpse = s.State.Actors.Get(rat).Position;
        Assert.Equal((player.X, player.Y - 3F, player.Z - 1F), (corpse.X, corpse.Y, corpse.Z));
    }

    [Fact]
    public void Dungeon_treasure_rests_on_the_floor_under_its_marker_with_no_fixed_offset()
    {
        using Fixture f = new(floorBelowProbe: 1F);
        DaggerfallSiteProfile inputs = f.Inputs;
        Assert.NotEmpty(inputs.DungeonPopulation.Treasures);
        DaggerfallGroundContainerSave[] piles = [.. DaggerfallSavePayload.Read(f.Session.CaptureSave()).GroundContainers.Where(pile => pile.TreasurePlacement is not null)];
        Assert.Equal(inputs.DungeonPopulation.Treasures.Count, piles.Length);
        foreach (var marker in inputs.DungeonPopulation.Treasures)
        {
            DaggerfallGroundContainerSave pile = Assert.Single(piles, value => value.TreasurePlacement == marker.Id);
            // The fake floor lies one metre below each probe's origin, which starts the probe lift above the marker.
            Assert.Equal(marker.Position.Y + DaggerfallTuning.Defaults.EnemyBehavior.SpawnGroundProbeLift - 1F, pile.Y, precision: 4);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<string> releases = [];
        private readonly SpatialFake spatial;
        private ulong step;
        internal DaggerfallSiteProfile Inputs { get; } = ReadInputs(TestData.RepositoryRoot);
        internal EngineContextFake Engine { get; }
        internal DaggerfallSession Session { get; }

        /// <summary>The floor height every downward probe meets once set; none before.</summary>
        internal float? FloorY { get; set; }

        internal Fixture(float? floorBelowProbe = null)
        {
            ContentFake content = new(releases);
            PopulateContent(content, Inputs);
            spatial = SpatialFake.Create(Inputs.SpatialArtifact.Sha256, releases);
            spatial.KeepPosition = true;
            spatial.FloorHit = request => request.Direction.Y >= -.5F ? default
                : floorBelowProbe is float below ? new SpatialHit { Present = true, Point = request.Origin - Vector3.UnitY * below, Normal = Vector3.UnitY }
                : FloorY is float floor ? new SpatialHit { Present = true, Point = request.Origin with { Y = floor }, Normal = Vector3.UnitY }
                : default;
            Engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
            Session = DaggerfallSession.StartNew(Engine.Context, new(TestPayload.Definitions, Inputs, DaggerfallTuning.Defaults));
        }

        internal void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(++step), [Ui(JsonSerializer.Serialize(action))]));

        internal DaggerfallSession Restore(RulesetSavePayload saved)
        {
            ContentFake content = new(releases);
            PopulateContent(content, Inputs);
            EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(Inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
            return DaggerfallSession.Restore(engine.Context, new(TestPayload.Definitions, Inputs, DaggerfallTuning.Defaults), saved);
        }

        public void Dispose() => Session.Dispose();
    }
}
