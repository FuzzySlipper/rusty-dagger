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
    [InlineData(40)]
    [InlineData(42)]
    public void Nearby_rat_has_an_admitted_route_to_the_starting_player(int source)
    {
        long actorId = HoldActor(source);
        string root = TestData.RepositoryRoot;
        var inputs = ReadInputs(root);
        var tuning = DaggerfallTuning.Read(TestContentFiles.ReadCopy(Path.Combine(root,
            "content/worldrpg/tuning/daggerfall.defaults/payload.json")));
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = SpatialFiles(inputs) });
        host.Call(engine =>
        {
            using SpatialMovementSystem spatial = SpatialOf(engine, inputs, tuning.Spatial);
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
