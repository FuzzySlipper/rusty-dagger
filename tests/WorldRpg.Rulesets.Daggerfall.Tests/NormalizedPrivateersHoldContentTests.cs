using System.Text;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NormalizedPrivateersHoldContentTests
{
    [Fact]
    public void ReadsTheGeneratedClosureWithoutSourceShapedSpatialOrSpriteFields()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root), payload, definitions);

        Assert.Equal("worldrpg/imports/privateers-hold/spatial/privateer-s-hold/collision-navigation.json", inputs.SpatialArtifact.Path);
        Assert.Equal("worldrpg/imports/privateers-hold/spatial/privateer-s-hold/static-mesh.json", inputs.StaticMesh.Path);
        Assert.NotEmpty(inputs.Materials);
        Assert.Equal(inputs.Materials.Count, inputs.Materials.Select(material => material.Slot).Distinct().Count());
        Assert.NotEmpty(inputs.ActorSprites);
        Assert.All(inputs.ActorSprites.Values, sprite => Assert.InRange(sprite.Frames.Count, 1, 4096));
    }

    [Fact]
    public void RejectsAClosureWhoseSpatialArtifactDoesNotMatchTheImportDigest()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));
        ProductContent content = GeneratedContent(root);
        ProductContentFile[] changed = content.Files.ToArray();
        int index = Array.FindIndex(changed, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("collision-navigation.json", StringComparison.Ordinal));
        Assert.True(index >= 0);
        byte[] bytes = changed[index].Bytes.ToArray();
        bytes[0] ^= 1;
        changed[index] = new ProductContentFile(changed[index].Path, bytes);

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(new ProductContent(changed), payload, definitions));
    }

    [Fact]
    public void RejectsAnUnknownPlacementActorAsContentDiagnostics()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        string payload = File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        DaggerfallContentException exception = Assert.Throws<DaggerfallContentException>(() =>
            PrivateersHoldContent.Read(GeneratedContent(root), Encoding.UTF8.GetBytes(payload.Replace("\"actor\": \"skeletal-warrior\"", "\"actor\": \"missing-actor\"", StringComparison.Ordinal)), definitions));

        Assert.Contains("missing actor 'missing-actor'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateScenarioProperties()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        string payload = File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        DaggerfallContentException exception = Assert.Throws<DaggerfallContentException>(() =>
            PrivateersHoldContent.Read(GeneratedContent(root), Encoding.UTF8.GetBytes(payload.Replace("\"ruleset\": \"daggerfall\"", "\"ruleset\": \"daggerfall\", \"ruleset\": \"daggerfall\"", StringComparison.Ordinal)), definitions));

        Assert.Contains("repeats property 'ruleset'", exception.Message, StringComparison.Ordinal);
    }

    private static ProductContent GeneratedContent(string root)
    {
        string contentRoot = Path.Combine(root, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports/privateers-hold"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray();
        return new ProductContent(files);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
