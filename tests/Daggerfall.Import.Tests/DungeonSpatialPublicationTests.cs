using System.Text;
using System.Text.Json;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DungeonSpatialPublicationTests
{
    [Fact]
    public void StaticMeshOutputUsesExactInlineEngineContentShapeAndPreservesAllVisualGroups()
    {
        NormalizedMesh collision = Floor("mesh/example/floor", "artifact/generated/static", "material/stone", 0F, true);
        NormalizedMesh doorVisual = Floor("mesh/example/door", "artifact/generated/static", "material/door", 2F, false);
        NavigationDerivationConfig config = NavigationDerivationConfig.ClassicDefault with { CellSize = 1F, LevelQuantum = 0.5F };
        NormalizedNavigationSurface navigation = OfflineNavigationDeriver.Derive(
            "navigation/example", "artifact/generated/spatial", [collision, doorVisual], config);
        NormalizedWorld world = new(
            NormalizedWorld.CurrentSchemaVersion,
            "mesh/example",
            [collision.Id, doorVisual.Id],
            navigation.Id,
            null,
            null,
            [], [], [], [], []);
        NormalizedResourceCatalogEntry[] resources =
        [
            new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, "material/stone", NormalizedResourceKind.Material, "artifact/generated/resources", [], []),
            new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, "material/door", NormalizedResourceKind.Material, "artifact/generated/resources", [], []),
        ];
        DungeonSpatialPublication publication = DungeonSpatialPublication.Create(
            "artifact/generated/static",
            "spatial/example/static-mesh.json",
            "artifact/generated/spatial",
            "spatial/example/collision-navigation.json",
            "artifact/generated/resources",
            "resources/example/catalog.json",
            world.VisualMeshAssetId,
            new(NormalizedBounds.CurrentSchemaVersion, new(0F, 0F, 0F), new(2F, 2F, 2F)),
            [collision, doorVisual],
            world,
            navigation,
            resources);

        using JsonDocument staticMesh = JsonDocument.Parse(publication.StaticMesh.Bytes);
        JsonElement root = staticMesh.RootElement;
        Assert.Equal("mesh/example", root.GetProperty("asset").GetString());
        JsonElement payload = root.GetProperty("payload");
        Assert.Equal("u32", payload.GetProperty("layout").GetProperty("indexWidth").GetString());
        Assert.Equal("inline", payload.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal("staticAsset", payload.GetProperty("provenance").GetString());
        Assert.Equal("visualOnly", root.GetProperty("collision").GetProperty("kind").GetString());
        Assert.Equal(2, payload.GetProperty("groups").GetArrayLength());
        Assert.Equal(2, root.GetProperty("materialSlots").GetArrayLength());

        using JsonDocument spatial = JsonDocument.Parse(publication.CollisionNavigation.Bytes);
        Assert.Equal("artifact/generated/static", spatial.RootElement.GetProperty("staticMeshArtifactId").GetString());
        Assert.Equal(2, spatial.RootElement.GetProperty("collision").GetProperty("triangles").GetArrayLength());
        Assert.Equal("navigation/example", spatial.RootElement.GetProperty("navigation").GetProperty("id").GetString());
        Assert.Equal(["artifact/generated/static"], publication.CollisionNavigation.DependsOnArtifactIds);
        using JsonDocument resourceCatalog = JsonDocument.Parse(publication.ResourceCatalog.Bytes);
        Assert.Equal("artifact/generated/resources", resourceCatalog.RootElement.GetProperty("resources")[0].GetProperty("artifactId").GetString());
        Assert.All(publication.ArtifactDescriptors, descriptor => Assert.Equal(descriptor.ContentDigest, ContentDigest.Compute(publication.Artifacts.Single(artifact => artifact.Id == descriptor.Id).Bytes.Span)));
    }

    [Fact]
    public void DerivesSignedMultiLevelWalkableSupportsFromCollisionGeometry()
    {
        NavigationDerivationConfig config = NavigationDerivationConfig.ClassicDefault with
        {
            CellSize = 1F,
            LevelQuantum = 0.5F,
            RequiredHeadroom = 1.5F,
        };
        NormalizedNavigationSurface navigation = OfflineNavigationDeriver.Derive(
            "navigation/negative",
            "artifact/spatial/negative",
            [Floor("mesh/lower", "artifact/static/negative", "material/lower", 0F, true, -2F), Floor("mesh/upper", "artifact/static/negative", "material/upper", 3F, true, -2F)],
            config);

        navigation.Validate(new HashSet<string>(["artifact/spatial/negative"], StringComparer.Ordinal));
        Assert.Contains(navigation.Cells, cell => cell.Column < 0 && cell.Row < 0);
        Assert.Contains(navigation.Cells, cell => cell.Level == 0 && cell.Walkable);
        Assert.Contains(navigation.Cells, cell => cell.Level == 6 && cell.Walkable);
    }

    [Fact]
    public void DoesNotTreatDownwardOrientedCeilingTrianglesAsWalkableSupports()
    {
        NavigationDerivationConfig config = NavigationDerivationConfig.ClassicDefault with { CellSize = 1F, LevelQuantum = 0.5F };
        NormalizedNavigationSurface navigation = OfflineNavigationDeriver.Derive(
            "navigation/ceiling",
            "artifact/spatial/ceiling",
            [Floor("mesh/ceiling", "artifact/static/ceiling", "material/ceiling", 2F, true, 0F, upward: false)],
            config);

        Assert.Empty(navigation.Cells);
    }

    [Fact]
    public void RejectsMalformedSpatialBoundsAndNavigationLevelQuantization()
    {
        NormalizedBounds invalidBounds = new(NormalizedBounds.CurrentSchemaVersion, new(2F, 0F, 0F), new(1F, 0F, 0F));
        Assert.Throws<ArgumentException>(invalidBounds.Validate);

        NavigationDerivationConfig config = NavigationDerivationConfig.ClassicDefault with { LevelQuantum = 1F };
        NormalizedNavigationSurface invalidLevel = new(
            NormalizedNavigationSurface.CurrentSchemaVersion,
            "navigation/invalid",
            "artifact/spatial/invalid",
            config,
            [new(-1, -1, 2, 0F, true)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => invalidLevel.Validate(new HashSet<string>(["artifact/spatial/invalid"], StringComparer.Ordinal)));
    }

    [Fact]
    public void RejectsSpatialPublicationMeshInputThatDoesNotExactlyMatchWorld()
    {
        NormalizedMesh included = Floor("mesh/included", "artifact/static", "material/included", 0F, true);
        NormalizedMesh extra = Floor("mesh/extra", "artifact/static", "material/extra", 0F, true);
        NormalizedNavigationSurface navigation = OfflineNavigationDeriver.Derive(
            "navigation/example", "artifact/spatial", [included], NavigationDerivationConfig.ClassicDefault with { CellSize = 1F });
        NormalizedWorld world = new(NormalizedWorld.CurrentSchemaVersion, "mesh/example", [included.Id], navigation.Id, null, null, [], [], [], [], []);
        NormalizedResourceCatalogEntry[] resources = [new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, "material/included", NormalizedResourceKind.Material, "artifact/resources", [], [])];

        Assert.Throws<InvalidOperationException>(() => DungeonSpatialPublication.Create(
            "artifact/static",
            "spatial/example/static.json",
            "artifact/spatial",
            "spatial/example/spatial.json",
            "artifact/resources",
            "resources/example/catalog.json",
            world.VisualMeshAssetId,
            new(NormalizedBounds.CurrentSchemaVersion, new(0F, 0F, 0F), new(2F, 0F, 2F)),
            [included, extra],
            world,
            navigation,
            resources));
    }

    private static NormalizedMesh Floor(string id, string artifactId, string material, float height, bool collision, float minimum = 0F, bool upward = true) => new(
        NormalizedMesh.CurrentSchemaVersion,
        id,
        artifactId,
        [new(minimum, height, minimum), new(minimum + 2F, height, minimum), new(minimum + 2F, height, minimum + 2F), new(minimum, height, minimum + 2F)],
        [new(0F, 1F, 0F), new(0F, 1F, 0F), new(0F, 1F, 0F), new(0F, 1F, 0F)],
        [new(0F, 0F), new(1F, 0F), new(1F, 1F), new(0F, 1F)],
        upward ? [new(0, 2, 1), new(2, 0, 3)] : [new(0, 1, 2), new(2, 3, 0)],
        [new(material, 0, 2, collision)]);
}
