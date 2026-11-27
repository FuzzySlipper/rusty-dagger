using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Travel;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests.Travel;

public sealed class DaggerfallOverlandMarkersTests
{
    [Fact]
    public void Compass_heading_maps_clockwise_from_north_into_world_xz_basis()
    {
        Vector3 north = DaggerfallOverlandModels.HeadingForward(0);
        Vector3 east = DaggerfallOverlandModels.HeadingForward(90);
        Vector3 south = DaggerfallOverlandModels.HeadingForward(180);
        Vector3 west = DaggerfallOverlandModels.HeadingForward(270);

        Assert.Equal(0f, north.X, 4);
        Assert.Equal(-1f, north.Z, 4);
        Assert.Equal(1f, east.X, 4);
        Assert.Equal(0f, east.Z, 4);
        Assert.Equal(0f, south.X, 4);
        Assert.Equal(1f, south.Z, 4);
        Assert.Equal(-1f, west.X, 4);
        Assert.Equal(0f, west.Z, 4);
    }

    [Fact]
    public void Align_to_surface_orients_up_along_terrain_normal_and_forward_along_heading()
    {
        Vector3 slopedNormal = Vector3.Normalize(new Vector3(-.35f, .92f, .18f));
        Vector3 east = DaggerfallOverlandModels.HeadingForward(90);
        Quaternion q = DaggerfallOverlandModels.AlignToSurface(slopedNormal, east);

        Vector3 localUp = Vector3.Transform(Vector3.UnitY, q);
        Vector3 localFwd = Vector3.Transform(Vector3.UnitZ, q);
        Vector3 localRight = Vector3.Transform(Vector3.UnitX, q);

        Assert.Equal(0f, Vector3.Distance(slopedNormal, localUp), 4);
        Assert.Equal(0f, Vector3.Dot(localUp, localFwd), 4);
        Assert.True(Vector3.Dot(localFwd, east) > .85f, "Projected forward should preserve compass direction.");
        Assert.Equal(0f, Vector3.Distance(Vector3.Cross(localUp, localFwd), localRight), 4);
    }

    [Fact]
    public void Align_chord_to_surface_pitches_sight_and_route_dashes_along_hill_slope()
    {
        Vector3 start = new(0f, 100f, 0f);
        Vector3 end = new(100f, 140f, 0f);
        Vector3 expectedFwd = Vector3.Normalize(end - start);
        Quaternion q = DaggerfallOverlandModels.AlignChordToSurface(Vector3.UnitY, start, end);

        Vector3 localFwd = Vector3.Transform(Vector3.UnitZ, q);
        Vector3 localUp = Vector3.Transform(Vector3.UnitY, q);

        Assert.Equal(0f, Vector3.Distance(expectedFwd, localFwd), 4);
        Assert.True(localUp.Y > .85f, "Dash top should remain facing upward along the climb.");
        Assert.Equal(0f, Vector3.Dot(localFwd, localUp), 4);
    }

    [Fact]
    public void March_bob_is_zero_when_idle_and_bounded_positive_when_marching()
    {
        Assert.Equal(0f, DaggerfallOverlandModels.MarchBobOffset(2.5, 0, 420f, moving: false));

        float peak = 0f;
        for (int i = 0; i < 60; i++)
        {
            float bob = DaggerfallOverlandModels.MarchBobOffset(i * .04, seed: 3, tokenSize: 420f, moving: true);
            Assert.InRange(bob, 0f, 420f * .115f);
            if (bob > peak) peak = bob;
        }
        Assert.True(peak > 420f * .08f, "Marching bob should visibly lift the figure during a stride.");
    }

    [Fact]
    public void Token_shadow_material_uses_blend_and_softens_on_winter_snow_and_at_night()
    {
        MaterialRequest summer = DaggerfallOverlandModels.ShadowMaterialRequest(winter: false, night: 0f, default);
        MaterialRequest winter = DaggerfallOverlandModels.ShadowMaterialRequest(winter: true, night: 0f, default);
        MaterialRequest night = DaggerfallOverlandModels.ShadowMaterialRequest(winter: false, night: 1f, default);

        Assert.Equal(MaterialAlphaMode.Blend, summer.AlphaMode);
        Assert.True(summer.Unlit);
        Assert.True(winter.Color.A < summer.Color.A, "Snow contact shadows should soften against bright terrain.");
        Assert.True(night.Color.A < summer.Color.A, "Night contact shadows should soften in moonlight.");
        Assert.True(winter.Color.B > winter.Color.R, "Winter contact shadows should shift cool slate-blue.");
    }
}
