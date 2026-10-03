using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Controls;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NativePursuitNavigationTests
{
    [Theory]
    [InlineData(2008)]
    [InlineData(2010)]
    public void Nearby_rat_has_an_admitted_route_to_the_starting_player(long actorId)
    {
        string root = TestData.RepositoryRoot;
        var inputs = ReadInputs(root);
        var tuning = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root,
            "content/worldrpg/tuning-payloads/daggerfall.defaults.json")));
        Dictionary<string, ReadOnlyMemory<byte>> files = new()
        {
            [inputs.SpatialArtifact.Path] = File.ReadAllBytes(Path.Combine(root, "content", inputs.SpatialArtifact.Path)),
        };
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        host.Call(engine =>
        {
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, inputs.SpatialArtifact, tuning.Spatial);
            var grounding = new DaggerfallActorGrounding(engine.Spatial, spatial,
                tuning.EnemyBehavior.SpawnGroundProbeLift, tuning.EnemyBehavior.SpawnGroundProbeDistance);
            var from = grounding.GroundPosition(inputs.Project.Actors[actorId].Position).ToVector();
            var player = inputs.Project.PlayerPosition!.Value;
            Vector3 target = new(player.X, from.Y, player.Z);
            var result = engine.Spatial.EvaluateNavigationStep(new(spatial.Session, from, target,
                tuning.EnemyBehavior.ChaseSpeedUnitsPerSecond / 60, tuning.EnemyBehavior.NavigationMaximumVisited));
            Assert.Equal(NavigationPathOutcome.Reached, result.Outcome);
            Assert.NotEqual(from, result.NextWaypoint);
        });
    }
}
