using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class SpriteAtlasAdapterTests
{
    [Fact]
    public void MapsPixelFramesAndPlaybackTimingExactlyOnce()
    {
        SpriteAtlasFrame[] atlas = SpriteAtlasAdapter.ToAtlasFrames(32, 16,
            [new(4, 8, 4, 8, 8, new Vector2(2F, 3F))]);
        SpritePlaybackFrame[] playback = SpriteAtlasAdapter.ToPlaybackFrames([4, 4], 8D);

        Assert.Equal(4U, Assert.Single(atlas).FrameId);
        Assert.Equal(new Vector2(.25F, .25F), atlas[0].UvMin);
        Assert.Equal(new Vector2(.5F, .75F), atlas[0].UvMax);
        Assert.True(atlas[0].HasSize);
        Assert.Equal(.125D, playback[0].DurationSeconds);
        Assert.Equal(2, playback.Length);
    }

    [Fact]
    public void RejectsOutOfBoundsFramesAndInvalidTiming()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteAtlasAdapter.ToAtlasFrames(8, 8, [new(1, 4, 0, 8, 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteAtlasAdapter.ToPlaybackFrames([1], 0D));
    }
}
