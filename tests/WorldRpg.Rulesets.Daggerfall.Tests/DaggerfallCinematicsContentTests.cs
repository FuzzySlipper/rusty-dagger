using WorldRpg.Rulesets.Daggerfall.Content;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Cinematic identities through the pack: the opening bound, the Daedric table bound, the rest
/// unresolved, and the DAG2 record distinct.
/// </summary>
public sealed class DaggerfallCinematicsContentTests
{
    [Fact]
    public void Resolves_bindings_and_leaves_the_rest_unresolved()
    {
        DaggerfallDefinitions definitions = Definitions();

        Assert.Equal(33, definitions.Cinematics.Cinematics.Count);
        DaggerfallCinematicDefinition opening = definitions.Cinematics.Resolve("ANIM0000.VID");
        Assert.Equal(DaggerfallCinematicKind.Vid, opening.Kind);
        Assert.Equal(DaggerfallCinematicBinding.Bound, opening.Binding);
        Assert.Equal("new-game opening", opening.Caller);
        Assert.Equal(DaggerfallCinematicBinding.Bound, definitions.Cinematics.Resolve("DAG2.VID").Binding);
        Assert.Equal(DaggerfallCinematicBinding.Unresolved, definitions.Cinematics.Resolve("ANIM0001.VID").Binding);

        DaggerfallCinematicDefinition azura = definitions.Cinematics.Resolve("AZURA.FLC");
        Assert.Equal(DaggerfallCinematicKind.Flc, azura.Kind);
        Assert.Equal(DaggerfallCinematicBinding.Bound, azura.Binding);
        Assert.Equal("Daedric summons", azura.Caller);
        Assert.Equal(16, azura.FactionId);
        Assert.Equal("T0C00Y00", azura.Quest);
        Assert.All(definitions.Cinematics.Cinematics.Values, cinematic => Assert.True(cinematic.ByteLength > 0 && cinematic.Digest.Length == 64));
    }

    [Fact]
    public void Every_published_video_is_indexed_by_its_source_and_matches_its_artifact()
    {
        string content = Path.Combine(RepositoryRoot(), "content");
        DaggerfallCinematicDefinition[] published = Definitions().Cinematics.Cinematics.Values
            .Where(value => value.Artifact is not null).ToArray();
        Assert.Equal(17, published.Count(value => value.Kind == DaggerfallCinematicKind.Vid));
        Assert.Equal(16, published.Count(value => value.Kind == DaggerfallCinematicKind.Flc));
        Assert.All(published.Where(value => value.Kind == DaggerfallCinematicKind.Flc), value =>
        {
            Assert.Equal(14, value.Artifact!.FrameCount);
            Assert.False(value.Artifact.HasAudio);
            Assert.Equal(DaggerfallCinematicBinding.Bound, value.Binding);
        });
        foreach (DaggerfallCinematicDefinition source in published)
        {
            DaggerfallCinematicArtifact artifact = source.Artifact!;
            byte[] bytes = File.ReadAllBytes(Path.Combine(content, artifact.Path));
            Assert.Equal(artifact.ByteLength, bytes.LongLength);
            Assert.Equal(artifact.Sha256, Convert.ToHexString(SHA256.HashData(bytes)), ignoreCase: true);
            Assert.Equal("video/webm", artifact.MimeType);
            Assert.True(artifact.FrameCount > 0 && artifact.DurationSeconds > 0);
        }
        string[] actual = Directory.GetFiles(Path.Combine(content, "worldrpg/media/cinematics"), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(content, path).Replace(Path.DirectorySeparatorChar, '/')).Order().ToArray();
        Assert.Equal(published.Select(value => value.Artifact!.Path).Order(), actual);
    }

    [Fact]
    public void A_published_artifact_cannot_silently_change_source_identity_or_format()
    {
        string path = Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json");
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonObject source = root["cinematics"]!["cinematics"]!.AsArray().First(value => value!["artifact"] is not null)!.AsObject();
        source["artifact"]!["path"] = "worldrpg/media/cinematics/another.webm";
        Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    private static DaggerfallDefinitions Definitions()
    {
        string root = RepositoryRoot();
        return DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
