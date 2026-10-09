using System.Text;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The per-block world publication, read lazily through the bundle the Host declares for it.</summary>
public sealed class DaggerfallWorldBlocksTests
{
    /// <summary>
    /// The staged content serves the per-block publication as a bundle and never in the eager snapshot. A
    /// reader opens it only when asked for blocks, reads the index and only the documents it was asked for,
    /// and states each block's spatial artifact by the content path the Engine resolves while the bundle is open.
    /// </summary>
    [Fact]
    public void Reads_only_the_requested_blocks_from_the_lazily_opened_bundle()
    {
        ProductContent content = StagedContent(TestData.RepositoryRoot, out BundleContentFake bundles);
        Assert.False(content.TryReadFile($"{DaggerfallWorldBlocks.Root}/{DaggerfallWorldBlocks.IndexPath}", out _));
        DaggerfallWorldBlocks blocks = new(content);
        Assert.False(blocks.IndexRead);
        Assert.Empty(bundles.OpenedBundles);

        DaggerfallWorldBlockKey exterior = new(DaggerfallWorldBlockKind.RmbExterior, "ARMRAL00.RMB");
        DaggerfallWorldBlockKey interior = new(DaggerfallWorldBlockKind.RmbInterior, "MAGEAA14.RMB", 0);
        DaggerfallWorldBlockKey dungeon = new(DaggerfallWorldBlockKind.Rdb, "B0000000.RDB");
        IReadOnlyDictionary<DaggerfallWorldBlockKey, DaggerfallWorldBlockDocument> read = blocks.Read([exterior, interior, dungeon]);

        Assert.True(blocks.IndexRead);
        Assert.Equal([DaggerfallWorldBlocks.BundleId], bundles.OpenedBundles);
        Assert.Equal(
            [DaggerfallWorldBlocks.IndexPath, "rmb/armral00-rmb/exterior.json", "rmb/mageaa14-rmb/interior-0.json", "rdb/b0000000-rdb.json"],
            bundles.ReadFiles.Select(file => file.Path));

        DaggerfallWorldBlockDocument armorer = read[exterior];
        Assert.Equal("rmb/armral00-rmb/exterior", armorer.PublishedKey);
        Assert.Equal($"{DaggerfallWorldBlocks.Root}/rmb/armral00-rmb/exterior.rspatial", armorer.Spatial!.ContentPath);
        Assert.Contains(armorer.Doors, door => door.Id == "door/armral00-rmb-rmb/0" && door.BuildingIndex == 0 && door.CollisionBounds is not null);
        Assert.All(armorer.Models, model => Assert.StartsWith("geometry/mesh-", model.MeshArtifactId, StringComparison.Ordinal));
        Assert.NotNull(armorer.ClearGround);

        DaggerfallWorldBlockDocument mages = read[interior];
        Assert.Equal(0, mages.InteriorBuilding!.Index);
        Assert.NotEmpty(mages.Section("staticNpcs"));

        DaggerfallWorldBlockDocument rdb = read[dungeon];
        DaggerfallWorldBlockModel door = Assert.Single(rdb.Models, model => model.Id == "model/b0000000-rdb/0");
        Assert.Equal("door/b0000000-rdb/0", door.DoorId);
        Assert.NotNull(door.LocalBounds);
        Assert.NotEmpty(door.CollisionTriangles);
        Assert.All(rdb.Models.Where(model => model.Action is null && model.DoorId is null && model.MeshArtifactId is not null),
            model => Assert.InRange(model.SamplePoints.Count, 2, 4));
        Assert.NotEmpty(rdb.Section("actions"));
        Assert.NotEmpty(rdb.RandomEnemies);

        // While the bundle is open, a block's collision artifact resolves by the content path and digest it states.
        using (ProductContentBundle open = blocks.Open())
        using (ContentReference reference = bundles.ResolveReference(new ContentResolveRequest(armorer.Spatial.ContentPath, armorer.Spatial.Sha256)))
            Assert.Equal(armorer.Spatial.Sha256, bundles.ReadReferenceInfo(reference).Span[0].Sha256);
    }

    /// <summary>A block the publication does not carry, or a document that does not read, is named rather than skipped.</summary>
    [Fact]
    public void Names_a_missing_block_and_a_malformed_document()
    {
        BundleContentFake service = new();
        service.Add(DaggerfallWorldBlocks.BundleId, DaggerfallWorldBlocks.IndexPath, Encoding.UTF8.GetBytes("""
            {"blocks":[{"key":"rdb/b0000000-rdb","kind":"rdb","sourceKey":"B0000000.RDB","buildingIndex":null,"document":"rdb/b0000000-rdb.json","spatial":null}]}
            """));
        service.Add(DaggerfallWorldBlocks.BundleId, "rdb/b0000000-rdb.json", Encoding.UTF8.GetBytes("""
            {"key":"rdb/b0000000-rdb","kind":"rdb","sourceKey":"B0000000.RDB","buildingIndex":null,"models":[{"id":"model/b0000000-rdb/0"}],"doors":[],"buildings":[],"randomEnemies":[]}
            """));
        DaggerfallWorldBlocks blocks = new(new ProductContent(Array.Empty<ProductContentFile>(), service));

        DaggerfallContentException missing = Assert.Throws<DaggerfallContentException>(() =>
            blocks.Read([new DaggerfallWorldBlockKey(DaggerfallWorldBlockKind.Rdb, "B0000099.RDB")]));
        Assert.Contains("carries no block for Rdb B0000099.RDB", missing.Message, StringComparison.Ordinal);

        DaggerfallContentException malformed = Assert.Throws<DaggerfallContentException>(() =>
            blocks.Read([new DaggerfallWorldBlockKey(DaggerfallWorldBlockKind.Rdb, "B0000000.RDB")]));
        Assert.Contains("'modelId'", malformed.Message, StringComparison.Ordinal);
    }
}
