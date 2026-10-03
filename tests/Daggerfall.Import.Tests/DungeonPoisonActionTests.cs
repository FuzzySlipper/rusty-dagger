using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DungeonPoisonActionTests
{
    [CorpusFact("MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "PAL.PAL", "CLIMATE.PAK")]
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

        BsaArchive maps = BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus("MAPS.BSA")), "arena2/MAPS.BSA");
        MapsDungeonLocation location = MapsDecoder.DecodeRegionGroups(maps)
            .Where(region => region.Tables.All(table => table.Length > 0))
            .SelectMany(region => MapsDecoder.DecodeRegionDungeons(maps, region.Region))
            .Where(location => location.Blocks.Any(block => block.SourceName == actual.Record))
            .OrderBy(location => location.Blocks.Count).ThenBy(location => location.Region).ThenBy(location => location.Index).First();
        Arena2SiteSources sources = Arena2SiteSources.ForSite(TestData.CorpusRoot);
        DungeonNormalizationResult publication = sources.LoadingOnDemand(() => DungeonNormalizer.Normalize(
            DungeonNormalizationRequest.Create(new DungeonLogicalSourceSet(sources.DungeonSources), location.Region, location.Name)));
        NormalizedDungeonAction action = Assert.Single(publication.Document.World.Actions, action => action.ActionFlag == 0x1A);
        Assert.Equal(actual.Flat.ObjectOffset, action.SourceOffset);
        Assert.Equal(actual.Flat.SoundIndex, action.RawIndex);
        Assert.Equal(actual.Flat.SoundIndex, action.SoundIndex);
        Assert.Equal(actual.Flat.NextObjectOffset, action.NextObjectOffset);
        Assert.Equal(new NormalizedDungeonPoisonAction(actual.Record, "source-unresolved"), action.Poison);
        Assert.Contains(publication.RecordProvenance, record => record.Id == action.Id && record.SourceRecordOrdinal == actual.Flat.ObjectOffset);
        NormalizedImportDocument decodedPublication = NormalizedImportSerializer.Deserialize(NormalizedImportSerializer.Serialize(publication.Document));
        Assert.Equal(action, Assert.Single(decodedPublication.World.Actions, value => value.Id == action.Id));
    }

    [Fact]
    public void Poison_requires_an_explicit_disposition_and_rejects_invented_variants_or_other_source_records()
    {
        NormalizedDungeonAction action = new("action/n0000007-rdb/0/0/flat-34", 20287, 2, 0x1A, 0, 0, 0, -2, null,
            IsFlat: true, SoundIndex: 7, RawIndex: 7, Poison: new("N0000007.RDB", "source-unresolved"));
        HashSet<string> ids = [action.Id];
        HashSet<string> doors = [];
        action.Validate(ids, doors);
        Assert.Throws<InvalidOperationException>(() => (action with { Poison = null }).Validate(ids, doors));
        foreach (int variant in new[] { 7 }.Concat(Enumerable.Range(128, 12)).Append(140))
            Assert.Throws<InvalidOperationException>(() => (action with { Poison = action.Poison! with { PoisonId = variant } }).Validate(ids, doors));
        Assert.Throws<InvalidOperationException>(() => (action with { Poison = action.Poison! with { SourceRecord = "S0000007.RDB" } }).Validate(ids, doors));
        Assert.Throws<InvalidOperationException>(() => (action with { Poison = action.Poison! with { Disposition = "supported" } }).Validate(ids, doors));
        Assert.Throws<InvalidOperationException>(() => (action with { RawIndex = 128 }).Validate(ids, doors));
        Assert.Throws<InvalidOperationException>(() => (action with { ActionFlag = 0x09 }).Validate(ids, doors));
    }
}
