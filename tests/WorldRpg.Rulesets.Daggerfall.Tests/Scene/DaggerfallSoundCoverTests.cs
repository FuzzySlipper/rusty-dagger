using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSoundCoverTests
{
    [Fact]
    public void Static_geometry_between_source_and_listener_covers_the_sound_from_eye_height_clear_of_the_source()
    {
        List<string> releases = [];
        SpatialFake fake = SpatialFake.Create(new ContentSha256(1, 2, 3, 4), releases);
        using SpatialMovementSystem spatial = new(fake.Service, new ContentFake(releases), null, DaggerfallTuning.Defaults.Spatial);
        DaggerfallSoundCover cover = new(spatial, 1.5f);
        SpatialHitKind kind = SpatialHitKind.StaticMesh;
        fake.FloorHit = _ => new SpatialHit { Present = true, Kind = kind, Distance = 1 };

        Assert.True(cover.Blocks(new WorldPoint(0f, 0f, 0f), new WorldPoint(10f, 0f, 0f)));
        SpatialRaycastRequest ray = Assert.Single(fake.FloorProbes);
        Assert.Equal(new Vector3(DaggerfallSoundCover.CastStartMeters, 1.5f, 0f), ray.Origin);
        Assert.Equal(Vector3.UnitX, ray.Direction);
        Assert.Equal(10f - DaggerfallSoundCover.CastStartMeters, ray.MaxDistance, 4);

        kind = SpatialHitKind.Entity;
        Assert.False(cover.Blocks(new WorldPoint(0f, 0f, 0f), new WorldPoint(10f, 0f, 0f)));
        // A listener within the clear start is never covered, and asks nothing.
        Assert.False(cover.Blocks(new WorldPoint(0f, 0f, 0f), new WorldPoint(.2f, 0f, 0f)));
        Assert.Equal(2, fake.FloorProbes.Count);
    }
}
