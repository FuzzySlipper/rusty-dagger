using System.Text;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class NormalizedContractTests
{
    [Fact]
    public void CanonicalSerializationRoundTripsWithAStableDigestAndFinalNewline()
    {
        NormalizedImportDocument document = CreateDocument();

        byte[] first = NormalizedImportSerializer.Serialize(document);
        NormalizedImportDocument parsed = NormalizedImportSerializer.Deserialize(first);
        byte[] second = NormalizedImportSerializer.Serialize(parsed);

        Assert.Equal(first, second);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.Equal(NormalizedImportSerializer.ComputeDigest(document), ContentDigest.Compute(first));
        Assert.Matches("^[0-9a-f]{64}$", NormalizedImportSerializer.ComputeDigest(document).Value);
    }

    [Fact]
    public void CompactSerializationRoundTripsAndRetainsTheFinalNewline()
    {
        NormalizedImportDocument document = CreateDocument();

        byte[] indented = NormalizedImportSerializer.Serialize(document);
        byte[] compact = NormalizedImportSerializer.SerializeCompact(document);
        NormalizedImportDocument parsed = NormalizedImportSerializer.Deserialize(compact);

        Assert.True(compact.Length < indented.Length);
        Assert.Equal((byte)'\n', compact[^1]);
        Assert.Equal(indented, NormalizedImportSerializer.Serialize(parsed));
    }

    [Fact]
    public void CanonicalSerializationSortsClosureAndPlacementsWithoutChangingGeometryOrder()
    {
        NormalizedImportDocument document = CreateDocument() with
        {
            Artifacts = CreateDocument().Artifacts.Reverse().ToArray(),
            Resources = CreateDocument().Resources.Reverse().ToArray(),
            World = CreateDocument().World with { Actors = CreateDocument().World.Actors.Reverse().ToArray() },
        };

        string json = Encoding.UTF8.GetString(NormalizedImportSerializer.Serialize(document));

        Assert.True(json.IndexOf("geometry.json", StringComparison.Ordinal) < json.IndexOf("materials.json", StringComparison.Ordinal));
        Assert.True(json.IndexOf("actor/a", StringComparison.Ordinal) < json.IndexOf("actor/b", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"firstVertex\": 0", StringComparison.Ordinal) < json.IndexOf("\"firstVertex\": 2", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationRejectsMalformedBoundsDuplicatesAndUnknownReferences()
    {
        NormalizedImportDocument invalidBounds = CreateDocument() with
        {
            Bounds = new(new(2F, 0F, 0F), new(1F, 0F, 0F)),
        };
        NormalizedImportDocument duplicateResource = CreateDocument() with
        {
            Resources = [.. CreateDocument().Resources, CreateDocument().Resources[0]],
        };
        NormalizedImportDocument unknownMaterial = CreateDocument() with
        {
            Meshes = [CreateDocument().Meshes[0] with
            {
                MaterialGroups = [new("material/missing", 0, 2, true)],
            }],
        };

        Assert.Throws<ArgumentException>(invalidBounds.Validate);
        Assert.Throws<InvalidOperationException>(duplicateResource.Validate);
        Assert.Throws<InvalidOperationException>(unknownMaterial.Validate);
    }

    [Fact]
    public void ValidationRejectsMutualArtifactDependencyCycles()
    {
        NormalizedImportDocument cyclic = CreateDocument() with
        {
            Artifacts =
            [
                new("artifact/geometry", "geometry.json", new ContentDigest("1111111111111111111111111111111111111111111111111111111111111111"), 12, ["artifact/materials"]),
                new("artifact/materials", "materials.json", new ContentDigest("2222222222222222222222222222222222222222222222222222222222222222"), 8, ["artifact/geometry"]),
            ],
        };

        Assert.Throws<InvalidOperationException>(cyclic.Validate);
    }

    [Fact]
    public void NormalizedContractsHaveNoEncounterOrRuntimeEntitySurface()
    {
        string json = Encoding.UTF8.GetString(NormalizedImportSerializer.Serialize(CreateDocument()));
        Type[] types = typeof(NormalizedImportDocument).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(NormalizedImportDocument).Namespace).ToArray();

        Assert.DoesNotContain("encounter", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(types, type => type.Name.Contains("Encounter", StringComparison.OrdinalIgnoreCase)
            || type.GetProperties().Any(property => property.Name.Contains("EntityId", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void LogicalSourceRequiresPositiveCallerByteLengthAndSerializesSourcePath()
    {
        ContentDigest digest = new("1111111111111111111111111111111111111111111111111111111111111111");
        LogicalSourceRecord invalid = new("arena2/MAPS.BSA", digest, 0);

        Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);
        string json = Encoding.UTF8.GetString(NormalizedImportSerializer.Serialize(CreateDocument()));
        Assert.Contains("\"sourcePath\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sourceUri\"", json, StringComparison.Ordinal);
    }

    private static NormalizedImportDocument CreateDocument()
    {
        ContentDigest geometryDigest = new("1111111111111111111111111111111111111111111111111111111111111111");
        ContentDigest materialDigest = new("2222222222222222222222222222222222222222222222222222222222222222");
        return new(
            new(
                "importer/example",
                "test-revision",
                [new("content/example.mesh.json", geometryDigest, 12)]),
            [
                new("artifact/geometry", "geometry.json", geometryDigest, 12, ["artifact/materials"]),
                new("artifact/materials", "materials.json", materialDigest, 8, []),
            ],
            new(NormalizedHandedness.Right, NormalizedVerticalAxis.PositiveY, 1F),
            new(new(0F, 0F, 0F), new(2F, 1F, 1F)),
            [new(
                "mesh/example",
                "artifact/geometry",
                [new(0F, 0F, 0F), new(1F, 0F, 0F), new(1F, 1F, 0F), new(0F, 1F, 0F)],
                [new(0F, 0F, 1F), new(0F, 0F, 1F), new(0F, 0F, 1F), new(0F, 0F, 1F)],
                [new(0F, 0F), new(1F, 0F), new(1F, 1F), new(0F, 1F)],
                [new(0, 1, 2), new(2, 3, 0)],
                [new("material/stone", 0, 2, true)])],
            new(
                "mesh/example",
                ["mesh/example"],
                "navigation/example",
                new("start", new(0F, 0F, 0F)),
                new("enter", new(1F, 0F, 0F)),
                [new("light/main", new(1F, 1F, 0F), 5F, 2F, new(1F, 1F, 1F))],
                [new("billboard/sign", "sprite/sign", new(1F, 0F, 0F), new(1F, 1F))],
                [new("actor/b", "actor/example", new(1F, 0F, 0F)), new("actor/a", "actor/example", new(0F, 0F, 0F))],
                [new("treasure/chest", "treasure/example", new(1F, 0F, 0F))],
                [new("door/main", "door/example", ["mesh/example"], new(2F, 0F, 0F), new(0F, 90F, 0F))]),
            [
                new("material/stone", NormalizedResourceKind.Material, "artifact/materials", [], []),
                new("sprite/sign", NormalizedResourceKind.Sprite, "artifact/materials", [], [new("frame/idle", 0, 0, 0, 16, 16, new(0.5F, 0F))]),
                new("actor/example", NormalizedResourceKind.ActorDefinition, "artifact/materials", [], []),
                new("treasure/example", NormalizedResourceKind.TreasureDefinition, "artifact/materials", [], []),
                new("door/example", NormalizedResourceKind.DoorDefinition, "artifact/materials", [], []),
            ]);
    }
}
