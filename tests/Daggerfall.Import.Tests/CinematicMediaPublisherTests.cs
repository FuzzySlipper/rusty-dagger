using System.Security.Cryptography;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class CinematicMediaPublisherTests
{
    [Fact]
    public void Real_vid_publication_preserves_frames_and_regenerates_identically()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        string source = Path.Combine(root?.FullName ?? throw new InvalidOperationException("Repository root missing."), "local/arena2/ANIM0011.VID");
        byte[] bytes = File.ReadAllBytes(source);
        DaggerfallCinematicRecord record = new("ANIM0011.VID", DaggerfallCinematicKind.Vid, bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)), DaggerfallCinematicBinding.Bound, "new-game opening", null, "");
        string directory = Path.Combine(Path.GetTempPath(), "dagger-cinematic-repeat-" + Guid.NewGuid().ToString("N"));
        try
        {
            CinematicMediaArtifact first = CinematicMediaPublisher.Publish(source, record, directory, "worldrpg/media/cinematics");
            CinematicMediaArtifact second = CinematicMediaPublisher.Publish(source, record, directory, "worldrpg/media/cinematics");
            Assert.Equal(first, second);
            Assert.Equal(35, first.FrameCount);
            Assert.True(first.HasAudio);
            Assert.Equal(["anim0011.webm"], Directory.GetFiles(directory).Select(Path.GetFileName));
            Assert.Empty(Directory.GetDirectories(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("ANIM0000.VID", "anim0000.webm")]
    [InlineData("DAG2.VID", "dag2.webm")]
    [InlineData("AZURA.FLC", "azura.webm")]
    public void Artifact_identity_is_source_based_and_logical(string source, string artifact) =>
        Assert.Equal(artifact, CinematicMediaPublisher.ArtifactName(source));

    [Theory]
    [InlineData("../ANIM0000.VID")]
    [InlineData("C:\\source\\DAG2.VID")]
    [InlineData("not-a-video.png")]
    public void Unsupported_source_names_cannot_escape_publication(string source) =>
        Assert.Throws<ArgumentException>(() => CinematicMediaPublisher.ArtifactName(source));

    [Fact]
    public void Bad_container_and_changed_provenance_refuse_before_decoder_or_output_mutation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dagger-cinematic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] bytes = [1, 2, 3];
            string path = Path.Combine(directory, "DAG2.VID");
            File.WriteAllBytes(path, bytes);
            DaggerfallCinematicRecord record = new("DAG2.VID", DaggerfallCinematicKind.Vid, bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)), DaggerfallCinematicBinding.Bound, "new-game", null, "");
            string output = Path.Combine(directory, "out");
            Assert.Contains("supported Vid", Assert.Throws<InvalidDataException>(() => CinematicMediaPublisher.Publish(path, record, output, "media/cinematics", "not-installed")).Message);
            Assert.Contains("source publication", Assert.Throws<InvalidDataException>(() => CinematicMediaPublisher.Publish(path, record with { Digest = "changed" }, output, "media/cinematics", "not-installed")).Message);
            Assert.False(Directory.Exists(output));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
