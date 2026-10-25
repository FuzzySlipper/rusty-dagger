using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DungeonPopulationSessionTests
{
    [Fact]
    public void Classic_start_gives_each_enemy_marker_one_population_actor_beside_the_fixed_authored_actors()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        Assert.NotEmpty(inputs.DungeonPopulation.Enemies);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));

        long[] actors = [.. session.DefinitionsByActor.Keys.Where(id => id != 1)];
        Assert.Equal(inputs.DungeonPopulation.Enemies.Count + inputs.Project.Actors.Count, actors.Length);
        // The authored placements are the source's fixed enemies; a population marker is never also an authored actor.
        Assert.All(inputs.Project.Actors.Values, actor => Assert.DoesNotContain(inputs.DungeonPopulation.Enemies,
            marker => Math.Abs(marker.Position.X - actor.Position.X) < .01 && Math.Abs(marker.Position.Z - actor.Position.Z) < .01));
        var footprints = actors.Select(id => session.State.Actors.Get(id).Position)
            .Select(position => (X: Math.Round(position.X, 2), Z: Math.Round(position.Z, 2))).ToArray();
        Assert.Equal(footprints.Length, footprints.Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Published_and_assembled_dungeons_populate_source_markers_and_keep_taken_loot_and_defeated_enemies(bool assembled)
    {
        var world = new AssembledWorld();
        var published = ReadProfile(TestData.RepositoryRoot, FullContent(TestData.RepositoryRoot), world.Definitions,
            "daggerfall.castle-necromoghan.json");
        var profile = assembled ? world.Profiles.Require(published.ProfileKey) : published;
        Assert.NotEmpty(profile.DungeonPopulation.Enemies);
        Assert.NotEmpty(profile.DungeonPopulation.Treasures);
        Assert.Equal(published.DungeonPopulation.Enemies.OrderBy(value => value.Id), profile.DungeonPopulation.Enemies.OrderBy(value => value.Id));
        var profiles = new DaggerfallSiteProfiles([world.Source, profile]);
        var composition = new DaggerfallSessionComposition(world.Definitions, profile, DaggerfallTuning.Defaults) { Profiles = profiles };
        List<string> releases = [];
        var content = new ContentFake(releases);
        PopulateContent(content, profile); PopulateContent(content, world.Source);
        var spatial = SpatialFake.Create(world.Source.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        var perception = PerceptionFake.Create();
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service, random: DungeonTreasureRandom.Create());
        using var session = DaggerfallSession.StartNew(engine.Context, composition);
        ulong step = 0;
        void Update(params ProductInputEvent[] events) => session.Update(new ProductUpdate(OuterUpdate(++step), events));
        var initial = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Equal(profile.DungeonPopulation.Enemies.Count, initial.DynamicActors.Length);
        Assert.Equal(profile.DungeonPopulation.Treasures.Count, initial.GroundContainers.Count(value => value.TreasurePlacement is not null));
        Assert.Contains(profile.ProfileKey.LogicalId, initial.Encounters.PopulatedDungeons);
        foreach (var marker in profile.DungeonPopulation.Enemies)
        {
            // Grounding may change Y; the source horizontal placement and spawn-range metadata survive.
            var actor = initial.DynamicActors.First(value => Math.Abs(value.X - marker.Position.X) < .01 && Math.Abs(value.Z - marker.Position.Z) < .01);
            Assert.Equal(marker.SpawnDistance, session.State.Actors.Get(actor.EntityId).Actor.Get<DaggerfallEnemyPerceptionMemory>().SpawnDistance);
        }
        long enemy = initial.DynamicActors[0].EntityId;
        session.State.Actors.Get(enemy).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        Update();
        Assert.True(session.State.Actors.Get(enemy).IsDefeated);

        var treasure = initial.GroundContainers.First(value => value.TreasurePlacement is not null
            && (value.Inventory.Stacks.Length > 0 || value.Inventory.UniqueItems.Length > 0));
        var position = new Vector3(treasure.X, treasure.Y, treasure.Z);
        var entity = session.State.Actors.Entities.Resolve(new(DurableIdentityKind.Container, checked((ulong)treasure.Id)));
        perception.Responder = request => request.Observers.ToArray().Any(observer => observer.Entity == 1)
            ? Receipt(new PerceptionPair(1, checked((ulong)treasure.Id), 1, 1, PerceptionPairKind.Visible, 1)) : Receipt();
        // The player stands beside the pile: their centre is above the floor the pile rests on.
        session.State.PlayerControl.MoveTo(position + Vector3.UnitZ + Vector3.UnitY * .9F);
        session.State.PlayerControl.YawRadians = 0;
        spatial.FloorHit = request => request.Direction.Y < -.5 ? default
            : new SpatialHit { Present = true, Kind = SpatialHitKind.Entity, Entity = entity.Value, Point = position, Distance = 1 };
        Update(Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact"));
        Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        int taken = 0;
        while (session.OpenLoot is { Items.Length: > 0 } loot)
        {
            Assert.True(taken++ < 100, $"{loot.Message}; {session.Presentation.LastOutcome}");
            var item = loot.Items[0];
            Update(Ui(JsonSerializer.Serialize(new { action = "loot-take", container = loot.Container, revision = loot.Revision,
                item = item.Key, amount = int.Parse(item.Quantity, System.Globalization.CultureInfo.InvariantCulture) })));
        }
        Assert.True(taken > 0);
        var after = DaggerfallSavePayload.Read(session.CaptureSave());
        var empty = after.GroundContainers.Single(value => value.Id == treasure.Id);
        Assert.Empty(empty.Inventory.Stacks); Assert.Empty(empty.Inventory.UniqueItems);
        // Use the actual profile transition owner and return before saving.
        Update(Ui(JsonSerializer.Serialize(new { action = "loot-close", container = session.OpenLoot!.Container })));
        Assert.True(session.TryTransitionTo(world.Source.ProfileKey));
        Assert.True(session.TryTransitionTo(profile.ProfileKey));
        Assert.True(session.State.Actors.Get(enemy).IsDefeated);
        var saved = session.CaptureSave();
        session.Dispose();
        using var restored = DaggerfallSession.Restore(engine.Context, composition, saved);
        var restoredSave = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Equal(initial.DynamicActors.Select(value => value.EntityId).Order(), restoredSave.DynamicActors.Select(value => value.EntityId).Order());
        Assert.True(restored.State.Actors.Get(enemy).IsDefeated);
        var restoredTreasure = restoredSave.GroundContainers.Single(value => value.Id == treasure.Id);
        Assert.Empty(restoredTreasure.Inventory.Stacks); Assert.Empty(restoredTreasure.Inventory.UniqueItems);
        Assert.Equal(treasure.TreasurePlacement, restoredTreasure.TreasurePlacement);
    }

    [Fact]
    public void Slot_selection_is_shared_and_underwater_uses_the_water_encounter_table()
    {
        var world = new AssembledWorld();
        var site = world.Definitions.Locations.Records.First(value => value.DungeonBlocks.Count > 0);
        var random = KeyedRandomFake.Create(0);
        var runtime = new DaggerfallEncounterRuntime(world.Definitions, random.Service);
        var marker = new DaggerfallDungeonEnemyPlacement("source-a", default, 4, 2, false, false);
        var first = runtime.SelectDungeonEnemy(site, marker, 1);
        Assert.Equal(first, runtime.SelectDungeonEnemy(site, marker with { Id = "source-b" }, 1));
        Assert.Equal(random.Requests[0], random.Requests[2]);
        Assert.Equal(random.Requests[1], random.Requests[3]);
        var water = runtime.SelectDungeonEnemy(site, marker with { Underwater = true, AquaticSpawnAllowed = true }, 1);
        var mobile = world.Definitions.Mobiles.Mobiles[world.Definitions.Encounters.Tables[19][0]];
        Assert.Equal(DaggerfallEncounterActors.ActorFor(mobile), water);
        Assert.Null(runtime.SelectDungeonEnemy(site, marker with { Underwater = true, AquaticSpawnAllowed = false }, 1));
    }
}

// Keep the generated pile small enough to take completely through ordinary carry eligibility.
internal class DungeonTreasureRandom : RandomMinimum
{
    internal new static IRandomService Create() => Create<IRandomService, DungeonTreasureRandom>();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method?.Name == nameof(IRandomService.DrawKeyed) && arguments![0] is KeyedRngRequest request
            && request.Key.Contains("world-treasure", StringComparison.Ordinal))
            return new KeyedRngReceipt(request.Key.EndsWith(".gold", StringComparison.Ordinal) ? Math.Max(1, request.Minimum) : request.Maximum);
        return base.Invoke(method, arguments);
    }
}
