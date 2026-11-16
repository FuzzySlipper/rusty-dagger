using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DungeonPoisonActionTests
{
    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "PAL.PAL")]
    public void Actual_treasure_action_is_published_with_its_approved_unresolved_source_disposition()
    {
        BsaArchive blocks = BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus("BLOCKS.BSA")), "arena2/BLOCKS.BSA");
        List<(string Record, RdbFlatSource Flat)> poisonFlats = [];
        foreach (BsaRecord record in blocks.Records.Where(record => record.Name!.EndsWith(".RDB", StringComparison.OrdinalIgnoreCase)))
        {
            RdbBlockSource decoded = RdbDecoder.Decode(blocks.GetPayload(record).Span, record.Name!);
            Assert.DoesNotContain(decoded.Models, model => model.Action?.Flags == 0x1A);
            poisonFlats.AddRange(decoded.Flats.Where(flat => flat.Action == 0x1A).Select(flat => (record.Name!, flat)));
        }
        var actual = Assert.Single(poisonFlats);
        Assert.Equal("N0000007.RDB", actual.Record);
        Assert.Equal((20287, (byte)2, (byte)0, (byte)7, -2, (ushort)199, (ushort)19),
            (actual.Flat.ObjectOffset, actual.Flat.Flags, actual.Flat.Magnitude, actual.Flat.SoundIndex,
                actual.Flat.NextObjectOffset, actual.Flat.TextureArchive, actual.Flat.TextureRecord));

        DaggerfallWorldBlock block = new WorldBlockNormalizer(WorldBlockTests.Sources()).Rdb(actual.Record).Block;
        NormalizedDungeonAction action = Assert.Single(block.Actions, action => action.ActionFlag == 0x1A);
        Assert.Equal(actual.Flat.ObjectOffset, action.SourceOffset);
        Assert.Equal(actual.Flat.SoundIndex, action.RawIndex);
        Assert.Equal(actual.Flat.SoundIndex, action.SoundIndex);
        Assert.Equal(actual.Flat.NextObjectOffset, action.NextObjectOffset);
        Assert.Equal(new NormalizedDungeonPoisonAction(actual.Record, "source-unresolved"), action.Poison);
    }
}
