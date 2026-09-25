using System.Numerics;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonActionModelContentTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Normalized_action_model_reader_preserves_model_local_triangles_only_for_admitted_groups(
        bool participatesInCollision,
        int expectedTriangleCount)
    {
        const string publicationRoot = "worldrpg/imports/fixture-dungeon";
        const string visualPath = publicationRoot + "/spatial/actions/platform.json";
        ContentSha256 hash = new(1, 2, 3, 4);
        byte[] visual = """
            {"materialSlots":[{"slot":0,"material":"material/stone"}]}
            """u8.ToArray();
        string normalizedJson = """
            {
              "world": {
                "actionModels": [{
                  "actionId": "action/block-1/model-0",
                  "description": "PLT",
                  "modelIndex": 0,
                  "rawIndex": 188,
                  "doorId": null,
                  "position": {"x":3,"y":4,"z":5},
                  "rotationDegrees": {"x":0,"y":90,"z":0},
                  "localBounds": {"minimum":{"x":0,"y":0,"z":0},"maximum":{"x":1,"y":1,"z":1}},
                  "visualArtifactId": "artifact/action-platform",
                  "meshIds": ["mesh/action/platform"]
                }]
              },
              "artifacts": [{"id":"artifact/action-platform","relativePath":"spatial/actions/platform.json"}],
              "meshes": [{
                "id":"mesh/action/platform",
                "artifactId":"artifact/action-platform",
                "vertices":[{"x":0,"y":0,"z":0},{"x":1,"y":0,"z":0},{"x":0,"y":1,"z":0}],
                "triangles":[{"firstVertex":0,"secondVertex":1,"thirdVertex":2}],
                "materialGroups":[{"materialResourceId":"material/stone","startTriangle":0,"triangleCount":1,"participatesInCollision":PARTICIPATES}]
              }]
            }
            """;
        byte[] normalized = Encoding.UTF8.GetBytes(normalizedJson.Replace(
            "PARTICIPATES",
            participatesInCollision ? "true" : "false",
            StringComparison.Ordinal));
        ProductContentFile[] contentFiles =
        [
            new ProductContentFile(Encoding.UTF8.GetBytes(visualPath), visual),
        ];
        AdmittedFiles files = AdmittedFiles.From(new ProductContent(contentFiles));
        Dictionary<string, ContentSha256> artifacts = new(StringComparer.Ordinal) { [visualPath] = hash };
        NormalizedMaterial[] materials = [new(7, "worldrpg/materials/stone.png", hash, "material/stone")];
        DaggerfallContentDiagnostics diagnostics = new();

        DaggerfallDungeonActionModelDefinition model = Assert.Single(PrivateersHoldContent.ReadNormalizedActionModels(
            normalized,
            publicationRoot,
            artifacts,
            files,
            materials,
            diagnostics));

        diagnostics.ThrowIfAny();
        Assert.Equal((byte)188, model.RawIndex);
        Assert.Equal(new Vector3(3, 4, 5), model.InitialTransform.Translation);
        Assert.Equal(Vector3.One, model.InitialTransform.Scale);
        Assert.Equal(expectedTriangleCount, model.CollisionTriangles.Length);
        Assert.Equal(expectedTriangleCount == 0 ? 0 : 3, model.CollisionVertices.Length);
        if (expectedTriangleCount != 0)
        {
            Assert.Equal(new Triangle(0, 1, 2), Assert.Single(model.CollisionTriangles));
            Assert.Equal(Vector3.UnitX, model.CollisionVertices[1]);
            Assert.Equal(Vector3.UnitY, model.CollisionVertices[2]);
        }
    }

    [Fact]
    public void Shipped_privateers_hold_keeps_action_meshes_model_local_and_out_of_static_geometry()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = GeneratedContent(root);
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(content,
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
        using JsonDocument normalized = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root, "content/worldrpg/imports/privateers-hold/normalized.json")));
        JsonElement world = normalized.RootElement.GetProperty("world");
        HashSet<string> dynamicMeshIds = world.GetProperty("actionModels").EnumerateArray()
            .SelectMany(model => model.GetProperty("meshIds").EnumerateArray())
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> staticMeshIds = world.GetProperty("staticMeshIds").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(6, inputs.DungeonActionModels.Count);
        Assert.All(inputs.DungeonActionModels, model => Assert.NotEmpty(model.CollisionTriangles));
        Assert.Empty(dynamicMeshIds.Intersect(staticMeshIds, StringComparer.Ordinal));
        Assert.All(world.GetProperty("geometryPlacements").EnumerateArray(), placement =>
            Assert.Empty(placement.GetProperty("meshIds").EnumerateArray()
                .Select(value => value.GetString()!)
                .Intersect(dynamicMeshIds, StringComparer.Ordinal)));
    }

    private static ProductContent GeneratedContent(string root)
    {
        string contentRoot = Path.Combine(root, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(
                Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')),
                File.ReadAllBytes(path)))
            .ToArray();
        return new ProductContent(files);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
