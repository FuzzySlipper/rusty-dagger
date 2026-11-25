using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// An action's motion, published right-handed: DFU <c>RDBLayout</c> moves a model in its left-handed world and turns it in
/// its own space, so the Z flip negates a Z translation and turns about X and Y.
/// </summary>
public sealed class Arena2ActionMotionTests
{
    [Theory]
    [InlineData(2, -1f, 0f, 0f)]
    [InlineData(1, 1f, 0f, 0f)]
    [InlineData(4, 0f, 1f, 0f)]
    [InlineData(3, 0f, -1f, 0f)]
    [InlineData(6, 0f, 0f, 1f)]
    [InlineData(5, 0f, 0f, -1f)]
    public void A_translation_moves_along_the_donor_axis_flipped_into_the_right_handed_frame(byte axis, float x, float y, float z)
    {
        NormalizedDungeonMotion motion = Assert.IsType<NormalizedDungeonMotion>(Arena2ActionMotion.Read(0x01, axis, 37, 40, null));

        Assert.Equal(NormalizedDungeonMotionKind.Translation, motion.Kind);
        Assert.Equal(new NormalizedVector3(x, y, z), motion.Translation);
        Assert.Equal(37F / 20F, motion.DurationSeconds);
    }

    [Theory]
    [InlineData(2, -1f, 0f, 0f)]
    [InlineData(3, 1f, 0f, 0f)]
    [InlineData(4, 0f, 1f, 0f)]
    [InlineData(5, 0f, -1f, 0f)]
    [InlineData(6, 0f, 0f, 1f)]
    [InlineData(7, 0f, 0f, -1f)]
    public void A_direct_axis_variant_moves_its_raw_axis_times_eight_over_fifty_ticks(byte actionFlag, float x, float y, float z)
    {
        NormalizedDungeonMotion motion = Assert.IsType<NormalizedDungeonMotion>(Arena2ActionMotion.Read(actionFlag, 5, 1, 999, null));

        Assert.Equal(new NormalizedVector3(x, y, z), motion.Translation);
        Assert.Equal(2.5F, motion.DurationSeconds);
    }

    [Theory]
    [InlineData(1, 1f, 0f, 0f, 1f)]   // DFU turns -X by a positive magnitude as x = -m, which the flip makes +m about X.
    [InlineData(2, 1f, 0f, 0f, -1f)]
    [InlineData(3, 0f, 1f, 0f, 1f)]
    [InlineData(4, 0f, 1f, 0f, -1f)]
    [InlineData(5, 0f, 0f, 1f, -1f)]  // Turns about Z keep their sign.
    [InlineData(6, 0f, 0f, 1f, 1f)]
    public void A_rotation_turns_about_a_right_handed_axis_by_a_signed_angle(byte axis, float x, float y, float z, float sign)
    {
        NormalizedDungeonMotion motion = Assert.IsType<NormalizedDungeonMotion>(Arena2ActionMotion.Read(0x08, axis, 30, 32, null));

        Assert.Equal(NormalizedDungeonMotionKind.Rotation, motion.Kind);
        Assert.Equal(new NormalizedVector3(x, y, z), motion.RotationAxis);
        Assert.Equal(sign * 32F / 5.68888888888889F, motion.RotationDegrees, 4);
        Assert.Equal(1.5F, motion.DurationSeconds);
    }

    [Fact]
    public void A_turn_past_half_a_turn_keeps_its_whole_angle_and_the_TRP_fix_applies_only_to_TRP()
    {
        NormalizedDungeonMotion trapdoor = Assert.IsType<NormalizedDungeonMotion>(Arena2ActionMotion.Read(0x08, 1, 30, 1536, null));
        Assert.Equal(270F, trapdoor.RotationDegrees, 3);

        NormalizedDungeonMotion corrected = Assert.IsType<NormalizedDungeonMotion>(Arena2ActionMotion.Read(0x08, 13, 30, 392, "TRP"));
        Assert.Equal(new NormalizedVector3(1F, 0F, 0F), corrected.RotationAxis);
        Assert.Equal(400F / 5.68888888888889F, corrected.RotationDegrees, 4);

        NormalizedDungeonMotion unrecognized = Assert.IsType<NormalizedDungeonMotion>(Arena2ActionMotion.Read(0x08, 13, 30, 392, null));
        Assert.Equal(new NormalizedVector3(0F, 0F, 0F), unrecognized.RotationAxis);
        Assert.Equal(0F, unrecognized.RotationDegrees);
    }

    [Fact]
    public void A_flag_that_moves_nothing_has_no_motion() => Assert.Null(Arena2ActionMotion.Read(0x0B, 1, 0, 0, null));
}
