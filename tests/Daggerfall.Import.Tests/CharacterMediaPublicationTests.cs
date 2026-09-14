using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The character/face canvases a presentation reference names have to become admitted bytes, not stay
/// pending forever. These checks publish real corpus canvases and pin the two refusals that keep a missing
/// canvas visible.
/// </summary>
public sealed class CharacterMediaPublicationTests
{
    [Fact]
    public void PublishesEachCanvasOfAFamilyInItsOwnPalette()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Arena2Palette palette) = Corpus();
        CharacterCanvasReference[] references =
        [
            new("character.head.breton.male", "FACE00I0.CIF", "FACE", "head", "an unstated consumer", MediaBinding.RequiredPending, 3, "ART_PAL.COL", [], "the canvas the reference names"),
            new("character.head.breton.female", "FACE00I0.CIF", "FACE", "head", "an unstated consumer", MediaBinding.RequiredPending, 0, "ART_PAL.COL", [], "the canvas the reference names"),
            // The face grid's cells are enumerated but their pixels are not decodable here, so it is
            // refused with that reason rather than published as a shape with no image.
            new("character.faction-face.00", "FACES.CIF", "FACE", "faction-face", "an unstated consumer", MediaBinding.RequiredPending, 0, "ART_PAL.COL", [], "the canvas the reference names"),
        ];

        CharacterMediaPublicationResult published = CharacterMediaPublication.Publish("FACE", references, sources, palette);

        Assert.Equal(2, published.Artifacts.Count);
        // Artifacts come out in canvas order, which is the order the reader enumerates records in.
        Assert.Equal(["character.head.breton.female", "character.head.breton.male"], published.Artifacts.Select(artifact => artifact.MediaId));
        Assert.True(published.Refusals.Count == 1, $"refused: {string.Join(" | ", published.Refusals)}");
        Assert.Contains("character.faction-face.00", published.Refusals[0], StringComparison.Ordinal);
        Assert.Contains("FACES.CIF", published.Refusals[0], StringComparison.Ordinal);
        Assert.Equal(published.PublishedMediaIds.Count, published.PublishedMediaIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(published.Artifacts, artifact =>
        {
            // A PNG signature, and the published path is derived from the media identity rather than
            // from the source file, so two files carrying one identity cannot collide.
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], artifact.Bytes.Take(4));
            Assert.StartsWith("media/character/", artifact.RelativePath, StringComparison.Ordinal);
            Assert.DoesNotContain(".CIF", artifact.RelativePath, StringComparison.Ordinal);
            Assert.True(artifact.Width > 0 && artifact.Height > 0);
        });
        // A family the references do not carry publishes nothing rather than guessing at a file list.
        Assert.Empty(CharacterMediaPublication.Publish("BODY", references, sources, palette).Artifacts);
    }

    [Fact]
    public void RefusesAMissingFileAndACanvasThatDoesNotExist()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Arena2Palette palette) = Corpus();
        CharacterCanvasReference[] references =
        [
            new("character.faction-face.00", "NOTHERE.CIF", "FACE", "faction-face", "an unstated consumer", MediaBinding.RequiredPending, 0, "ART_PAL.COL", [], "the canvas the reference names"),
            new("character.faction-face.01", "FACES.CIF", "FACE", "faction-face", "an unstated consumer", MediaBinding.RequiredPending, 999, "ART_PAL.COL", [], "the canvas the reference names"),
        ];

        CharacterMediaPublicationResult published = CharacterMediaPublication.Publish("FACE", references, sources, palette);

        Assert.Empty(published.Artifacts);
        Assert.Equal(2, published.Refusals.Count);
        Assert.Contains(published.Refusals, refusal => refusal.Contains("character.faction-face.00", StringComparison.Ordinal) && refusal.Contains("NOTHERE.CIF", StringComparison.Ordinal));
        Assert.Contains(published.Refusals, refusal => refusal.Contains("character.faction-face.01", StringComparison.Ordinal) && refusal.Contains("999", StringComparison.Ordinal));
    }

    private static (Dictionary<string, ReadOnlyMemory<byte>> Sources, Arena2Palette Palette) Corpus()
    {
        string arena2 = Path.Combine(RepositoryRoot(), "local", "arena2");
        Dictionary<string, ReadOnlyMemory<byte>> sources = new(StringComparer.Ordinal)
        {
            ["FACES.CIF"] = File.ReadAllBytes(Path.Combine(arena2, "FACES.CIF")),
            ["FACE00I0.CIF"] = File.ReadAllBytes(Path.Combine(arena2, "FACE00I0.CIF")),
        };
        return (sources, PaletteDecoder.Decode(File.ReadAllBytes(Path.Combine(arena2, "ART_PAL.COL")), "arena2/ART_PAL.COL"));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
