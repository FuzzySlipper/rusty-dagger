using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSessionExteriorTests
{
    [Fact]
    public void WorldOrigin_commit_moves_product_local_positions_by_previous_minus_target_cell()
    {
        WorldOriginCommitReceipt receipt = new(
            7,
            8,
            100,
            -2,
            4,
            112,
            3,
            -7,
            9,
            10,
            2,
            8192F);

        Assert.Equal(
            new Vector3(-12F, -5F, 11F),
            DaggerfallExteriorSessionOrigin.LocalDelta(receipt));
    }

    [Fact]
    public void WorldOrigin_commit_shifts_player_and_actor_position_in_the_same_local_frame()
    {
        WorldOriginCommitReceipt receipt = new(
            1,
            2,
            10,
            0,
            -3,
            12,
            4,
            2,
            0,
            0,
            1,
            2048F);
        Vector3 delta = DaggerfallExteriorSessionOrigin.LocalDelta(receipt);

        Assert.Equal(
            new WorldPoint(6F, 1F, -10F),
            DaggerfallExteriorSessionOrigin.Shift(new WorldPoint(8F, 5F, -5F), delta));
    }

    [Fact]
    public void WorldOrigin_commit_rejects_a_position_that_overflows_product_coordinates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DaggerfallExteriorSessionOrigin.Shift(
                new WorldPoint(float.MaxValue, 0F, 0F),
                new Vector3(float.MaxValue, 0F, 0F)));
    }

    [Fact]
    public void Current_cell_uses_local_x_and_reversed_z_at_tile_boundaries()
    {
        DaggerfallExteriorWorldBounds bounds = new(100, 80);
        DaggerfallExteriorWorldOrigin origin = new(40, 30, new Vector3(2F, 7F, -3F));

        Assert.Equal(
            new DaggerfallExteriorCellId(40, 30),
            DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                new WorldPoint(2F, 7F, -3F), origin, bounds));
        Assert.Equal(
            new DaggerfallExteriorCellId(41, 29),
            DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                new WorldPoint(2F + DaggerfallExteriorCellResidency.CellSize, 7F, -3F + DaggerfallExteriorCellResidency.CellSize),
                origin, bounds));
        Assert.Equal(
            new DaggerfallExteriorCellId(39, 31),
            DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                new WorldPoint(2F - .01F, 7F, -3F - .01F), origin, bounds));
    }
}
