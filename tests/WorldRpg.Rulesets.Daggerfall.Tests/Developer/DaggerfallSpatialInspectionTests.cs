using Rusty.Engine.Debugging;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallSpatialInspection"/> built without the session: what it refuses before it reads collision, over
/// a player with no position. The snapshots over a real scene, and the probe drawn as an overlay, are
/// <c>PlaytestInspectionTests</c>' and <c>DeveloperInspectionTests</c>' end-to-end cases.
/// </summary>
public sealed class DaggerfallSpatialInspectionTests
{
    private readonly DaggerfallSpatialInspection _inspection = new(null!, null!, null!, new PlayerControlState(null, 0f, 0f), null!, null!,
        null!, null!, () => null, () => null, null!);

    [Fact]
    public void Arguments_out_of_range_are_refused_before_collision_is_read()
    {
        AssertInvalid(_inspection.ReadGrid(16, 1, 1), "Grid radii must be 0–15 and cell size positive and finite.");
        AssertInvalid(_inspection.ReadGrid(1, -1, 1), "Grid radii must be 0–15 and cell size positive and finite.");
        AssertInvalid(_inspection.ReadGrid(1, 1, 0), "Grid radii must be 0–15 and cell size positive and finite.");
        AssertInvalid(_inspection.ReadProbe(9), "Probe distance must be finite and within (0, 8].");
        AssertInvalid(_inspection.ReadProbe(double.NaN), "Probe distance must be finite and within (0, 8].");
        AssertInvalid(_inspection.ReadClearance(0, double.PositiveInfinity, 0), "Clearance target must be finite.");
        AssertInvalid(_inspection.ReadJumpPlan(double.NaN, 0, 0), "Jump target must be finite.");
    }

    [Fact]
    public void A_grid_needs_the_player_to_stand_somewhere()
    {
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => _inspection.ReadGrid(1, 1, 1));
        Assert.Equal("Spatial inspection requires a player position.", missing.Message);
    }

    private static void AssertInvalid(DebugCommandResult result, string message)
    {
        Assert.Equal(DebugCommandStatus.InvalidArguments, result.Status);
        Assert.Equal(message, result.Message);
    }
}
