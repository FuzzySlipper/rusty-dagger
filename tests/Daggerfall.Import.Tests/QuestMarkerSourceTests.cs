using System.Text.Json;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class QuestMarkerSourceTests
{
    [CorpusFact]
    public void Compact_block_catalog_retains_every_source_quest_marker_and_no_other_editor_flat()
    {
        DaggerfallBlocks blocks = DaggerfallBlocksBuilder.Build(File.ReadAllBytes(TestData.Corpus("BLOCKS.BSA")), "arena2/BLOCKS.BSA", SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "data/content-source-manifest.csv"))));
        DaggerfallBlockBuildingSet compact = DaggerfallBlockBuildingSet.From(blocks);
        int expected = blocks.Records.Sum(record =>
            (record.Objects?.FlatPlacements.Count(flat => flat.TextureArchive == 199 && flat.TextureRecord is 11 or 18) ?? 0)
            + (record.Rmb?.Buildings.Sum(building => building.InteriorPlacements.Flats.Count(flat => flat.TextureArchive == 199 && flat.TextureRecord is 11 or 18)) ?? 0));
        Assert.True(expected > 100);
        Assert.Equal(expected, compact.QuestMarkers.Sum(set => set.Markers.Count));
        foreach (DaggerfallBlockQuestMarkers set in compact.QuestMarkers)
        {
            Assert.Equal(set.Markers.OrderBy(marker => marker.SourceOrdinal), set.Markers);
            foreach (NormalizedQuestMarker marker in set.Markers) marker.Validate();
            Assert.Equal(set.Markers.Count, set.Markers.Select(marker => marker.Id).Distinct().Count());
        }
        DaggerfallBlockQuestMarkers selected = compact.QuestMarkers.First(set => set.BuildingIndex is not null && set.Markers.Count > 0);
        var raw = blocks.Records.Single(record => record.SourceKey == selected.SourceKey).Rmb!.Buildings[selected.BuildingIndex!.Value].InteriorPlacements.Flats[selected.Markers[0].SourceOrdinal];
        Assert.Equal(new NormalizedVector3(raw.X * .025f, -raw.Y * .025f, -raw.Z * .025f), selected.Markers[0].Position);
        Assert.Equal(raw.TextureRecord == 11 ? NormalizedQuestMarkerKind.Spawn : NormalizedQuestMarkerKind.Item, selected.Markers[0].Kind);
        Assert.Contains("\"questMarkers\"", JsonSerializer.Serialize(compact, PublishedJson.SectionCompact));
    }
}
