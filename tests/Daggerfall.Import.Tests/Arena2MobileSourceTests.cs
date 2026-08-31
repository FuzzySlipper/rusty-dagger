using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2MobileSourceTests
{
    [Fact]
    public void KnownMobilePreservesClassicArchiveCorpseAndAttackSourceRecords()
    {
        Assert.True(MobileSourceMetadata.TryGet(new(0), out Arena2MobileSource? rat));

        Assert.Equal("Rat", rat.SourceName);
        Assert.Equal((ushort)255, rat.TextureArchive.Value);
        Assert.Equal(new Arena2MobileCorpseSource(new(401), 1), rat.Corpse);
        Assert.Equal(new sbyte[] { 0, 1, 2, -1, 3, 4, 5 }, rat.AttackSequence.PrimaryFrames);
        Assert.Empty(rat.AttackSequence.Alternates);

        Assert.True(MobileSourceMetadata.TryGet(new(138), out Arena2MobileSource? thief));
        Assert.Equal((ushort)484, thief.TextureArchive.Value);
        Assert.Equal(new byte[] { 33, 33 }, thief.AttackSequence.Alternates.Select(alternate => alternate.Chance));
        Assert.Contains((sbyte)-1, thief.AttackSequence.Alternates[1].Frames);
    }

    [Fact]
    public void MobileAnimationMetadataPreservesPreferredRestAndFlyingCadence()
    {
        Assert.True(MobileSourceMetadata.TryGet(new(0), out Arena2MobileSource? rat));
        Assert.Equal(Arena2MobileFrameGroup.RatIdle, rat.Animation.PreferredRestGroup);
        Assert.Null(rat.Animation.EffectiveFramesPerSecond(Arena2MobileFrameGroup.Move));
        Assert.Null(rat.Animation.EffectiveFramesPerSecond(Arena2MobileFrameGroup.Idle));

        Assert.True(MobileSourceMetadata.TryGet(new(1), out Arena2MobileSource? imp));
        Assert.True(MobileSourceMetadata.TryGet(new(3), out Arena2MobileSource? giantBat));
        foreach (Arena2MobileSource flying in new[] { imp, giantBat })
        {
            Assert.Equal(Arena2MobileFrameGroup.Move, flying.Animation.PreferredRestGroup);
            Assert.Equal(10F, flying.Animation.EffectiveFramesPerSecond(Arena2MobileFrameGroup.Move));
            Assert.Null(flying.Animation.EffectiveFramesPerSecond(Arena2MobileFrameGroup.Idle));
        }

        Assert.True(MobileSourceMetadata.TryGet(new(7), out Arena2MobileSource? orc));
        Assert.Equal(Arena2MobileFrameGroup.Idle, orc.Animation.PreferredRestGroup);
        Assert.Null(orc.Animation.EffectiveFramesPerSecond(Arena2MobileFrameGroup.Move));
        Assert.Null(orc.Animation.EffectiveFramesPerSecond(Arena2MobileFrameGroup.Idle));
    }

    [Fact]
    public void UnknownMobileIdRemainsAbsentFromTheSourceTable()
    {
        Assert.False(MobileSourceMetadata.TryGet(new(42), out Arena2MobileSource? source));
        Assert.Null(source);
    }

    [Fact]
    public void SourceFrameGroupsPreserveEightSectorRecordAndMirrorLayout()
    {
        Assert.Equal(
            [
                new Arena2MobileFrameRecord(0, false),
                new Arena2MobileFrameRecord(1, false),
                new Arena2MobileFrameRecord(2, false),
                new Arena2MobileFrameRecord(3, false),
                new Arena2MobileFrameRecord(4, false),
                new Arena2MobileFrameRecord(3, true),
                new Arena2MobileFrameRecord(2, true),
                new Arena2MobileFrameRecord(1, true),
            ],
            MobileSourceMetadata.GetFrameRecords(Arena2MobileFrameGroup.Move).ToArray());

        Assert.Equal(new Arena2MobileFrameRecord(16, true), MobileSourceMetadata.GetFrameRecords(Arena2MobileFrameGroup.RatIdle)[1]);
        Assert.Equal(new Arena2MobileFrameRecord(17, false), MobileSourceMetadata.GetFrameRecords(Arena2MobileFrameGroup.RatIdle)[6]);
        Assert.Throws<ArgumentOutOfRangeException>(() => MobileSourceMetadata.GetFrameRecords((Arena2MobileFrameGroup)99));
    }

    [Fact]
    public void SourceRecordWorldSizeUsesTheDfuScaleFormulaWithoutPresentationWork()
    {
        Assert.Equal(new Arena2RecordWorldSize(1.6F, 1.6F), MobileSourceMetadata.GetRecordWorldSize(64, 64, 0, 0));
        Assert.Equal(new Arena2RecordWorldSize(3.2F, 1.6F), MobileSourceMetadata.GetRecordWorldSize(64, 64, 256, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MobileSourceMetadata.GetRecordWorldSize(-1, 64, 0, 0));
    }
}
