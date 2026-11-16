using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonActionModelContentTests
{
    /// <summary>
    /// Privateer's Hold's moving models are action models with their own model-local collision, never static geometry:
    /// the start stairs (R1X) carry a zero-flag action record and are static, as the donor's HasAction decides.
    /// </summary>
    [Fact]
    public void Shipped_privateers_hold_keeps_action_meshes_model_local_and_out_of_static_geometry()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);

        Assert.Equal(5, inputs.DungeonActionModels.Count);
        Assert.All(inputs.DungeonActionModels, model => Assert.NotEmpty(model.CollisionTriangles));
        HashSet<string> moving = [.. inputs.DungeonActionModels.Select(model => model.Visual.Path)];
        Assert.DoesNotContain(inputs.Geometry.Meshes, mesh => inputs.DungeonActionModels.Any(model =>
            model.Visual.Path == mesh.Path && model.InitialTransform.Translation == mesh.Pose.Translation));
    }
}
