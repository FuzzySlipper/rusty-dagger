using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalization;

/// <summary>One published character canvas: its media identity and the PNG written for it.</summary>
/// <param name="Reference">The canvas reference this artifact is the emission of.</param>
/// <param name="RelativePath">The content-group-relative path the artifact is written to, without the group.</param>
/// <param name="Bytes">The encoded PNG.</param>
/// <param name="Width">The canvas width in pixels.</param>
/// <param name="Height">The canvas height in pixels.</param>
/// <param name="Palette">The palette the pixels were actually painted in, which is not always the one the reference names.</param>
/// <param name="OwnPalette">Whether that palette came from the source container rather than from a supplied file.</param>
public sealed record CharacterMediaArtifact(
    CharacterCanvasReference Reference,
    string RelativePath,
    byte[] Bytes,
    int Width,
    int Height,
    Arena2Palette Palette,
    bool OwnPalette)
{
    /// <summary>The stable identity of this canvas as a published artifact.</summary>
    public string MediaId => Reference.MediaId;

    /// <summary>
    /// Where the painting palette came from, which is a fact about the colours a consumer inherits: a
    /// container's own palette is inside the source bytes and a supplied palette is a file beside it.
    /// </summary>
    public string PaletteSource => OwnPalette
        ? CharacterMediaPublisher.EmbeddedPaletteSource
        : CharacterMediaPublisher.SuppliedPaletteSource;

    /// <summary>
    /// The published identity of the palette these pixels were painted in: the supplied file's name, or a
    /// digest of a container palette's own colours, which has no file name to state.
    /// </summary>
    /// <remarks>
    /// A reference cannot state this for a container palette - it only knows the fallback name the classic
    /// reader would use for the family - and naming that fallback describes colours these bytes do not have,
    /// so every consumer of the reference states this instead.
    /// </remarks>
    public string PaletteIdentity => OwnPalette ? CharacterMediaReferences.EmbeddedPaletteIdentity(Palette) : Reference.Palette;
}

/// <summary>
/// One published character canvas as the generated index states it, so a consumer resolves a media
/// identity to bytes and can tell what the artifact was decoded from.
/// </summary>
/// <remarks>
/// The entry carries the same <c>path</c>, <c>byteLength</c> and <c>sha256</c> keys the classic media index
/// uses, so one reader resolves an artifact of either group rather than special-casing the key.
/// </remarks>
/// <param name="MediaId">The stable identity of the canvas.</param>
/// <param name="Path">The content-group-relative path the artifact is written to, without the group.</param>
/// <param name="ByteLength">The artifact's length in bytes.</param>
/// <param name="Sha256">The artifact's digest, so a consumer can refuse bytes that are not these.</param>
/// <param name="Width">The canvas width in pixels.</param>
/// <param name="Height">The canvas height in pixels.</param>
/// <param name="Family">The documented family the canvas came from.</param>
/// <param name="SourceFile">The supplied source file the canvas came from.</param>
/// <param name="CanvasIndex">The canvas's index within its file, which is its record, frame or cell.</param>
/// <param name="Palette">The palette the canvas is painted in, whether supplied or carried by the file.</param>
/// <param name="PaletteSource">Where that palette comes from, which is a fact about the colours.</param>
/// <param name="Binding">Whether a published consumer binds the canvas, or it is still required-pending.</param>
/// <param name="Consumer">The consumer that binds it, or the inventory's label for one that does not.</param>
public sealed record CharacterMediaIndexEntry(
    string MediaId,
    string Path,
    long ByteLength,
    string Sha256,
    int Width,
    int Height,
    string Family,
    string SourceFile,
    int CanvasIndex,
    string Palette,
    string PaletteSource,
    MediaBinding Binding,
    string Consumer);

/// <summary>One family or file whose canvases this repository cannot publish, stated with the reason.</summary>
/// <param name="Family">The documented family, or the shape when only part of a family is unreadable.</param>
/// <param name="Kind">What the supplied files carry.</param>
/// <param name="Files">The supplied files this entry accounts for.</param>
/// <param name="Reason">Why their canvases are unavailable here, naming what would have to exist.</param>
/// <param name="DonorAnchor">The donor class that reads the format, or empty when no reader exists anywhere.</param>
public sealed record CharacterMediaUnreadableFamily(
    string Family,
    string Kind,
    IReadOnlyList<string> Files,
    string Reason,
    string DonorAnchor);

/// <summary>What one publication pass produced and what it could not.</summary>
public sealed record CharacterMediaPublicationResult(
    IReadOnlyList<CharacterMediaArtifact> Artifacts,
    IReadOnlyList<string> Refusals)
{
    /// <summary>The media identities this pass actually published, which is what makes a binding resolved.</summary>
    public IReadOnlySet<string> PublishedMediaIds { get; } =
        Artifacts.Select(artifact => artifact.MediaId).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Publishes the character and face canvases a presentation reference names, so the references resolve to
/// admitted bytes instead of staying pending forever.
/// </summary>
/// <remarks>
/// This is the emission half of the character presentation: it takes the canvas references the inventory
/// enumerated and the source bytes they came from, decodes each canvas through the reader that owns its
/// format, and writes it as a PNG in that canvas's palette. It deliberately returns the identities it
/// published rather than assuming them, because the reference's binding has to be derived from what
/// exists.
/// A canvas whose format has no decoder here, or whose file the corpus does not carry, is refused with
/// the identity and the reason rather than skipped: a missing canvas must be visible.
/// </remarks>
public static class CharacterMediaPublication
{
    /// <summary>
    /// Publishes every canvas of one family.
    /// </summary>
    /// <param name="family">The family whose references are published, matched case-insensitively.</param>
    /// <param name="references">The enumerated canvas references, in the order the inventory produced them.</param>
    /// <param name="sources">The supplied corpus, by file name.</param>
    /// <param name="palettes">The supplied palettes by file name, which is how a reference names its own.</param>
    public static CharacterMediaPublicationResult Publish(
        string family,
        IEnumerable<CharacterCanvasReference> references,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> sources,
        IReadOnlyDictionary<string, Arena2Palette> palettes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(palettes);

        List<CharacterMediaArtifact> artifacts = [];
        List<string> refusals = [];
        foreach (CharacterCanvasReference reference in references
            .Where(canvas => string.Equals(canvas.Family, family, StringComparison.OrdinalIgnoreCase))
            .OrderBy(canvas => canvas.CanvasIndex))
        {
            string file = System.IO.Path.GetFileName(reference.Path);
            if (!sources.TryGetValue(file, out ReadOnlyMemory<byte> bytes))
            {
                refusals.Add($"'{reference.MediaId}' needs '{file}', which the supplied corpus does not carry.");
                continue;
            }

            if (!TryDecodeCanvas(file, bytes.Span, reference.CanvasIndex, out CharacterCanvas? canvas, out string reason))
            {
                refusals.Add($"'{reference.MediaId}' cannot be published: {reason}");
                continue;
            }

            // The palette the reference names, not a default: a family paired with the wrong palette
            // publishes every colour wrong while looking successful, and the NITE files are paired with
            // their own.
            Arena2Palette? named = canvas!.OwnPalette;
            if (named is null)
            {
                string expected = CharacterMediaReferences.PaletteFor(file);
                string named_ = System.IO.Path.GetFileName(reference.Palette);
                if (!string.Equals(named_, expected, StringComparison.OrdinalIgnoreCase))
                {
                    // A palette that exists but belongs to another family is as wrong as a missing one:
                    // the classic reader pairs this file with 'expected', and trusting the reference
                    // would publish every colour wrong while looking successful.
                    refusals.Add($"'{reference.MediaId}' names palette '{reference.Palette}', but '{file}' is read with '{expected}'.");
                    continue;
                }

                if (!palettes.TryGetValue(named_, out named))
                {
                    refusals.Add($"'{reference.MediaId}' names palette '{reference.Palette}', which the supplied corpus does not carry.");
                    continue;
                }
            }

            byte[] png = Encode(canvas.Width, canvas.Height, canvas.Pixels, named);
            artifacts.Add(new CharacterMediaArtifact(
                reference,
                $"media/character/{Slug(reference.MediaId)}.png",
                png,
                canvas.Width,
                canvas.Height,
                named,
                canvas.OwnPalette is not null));
        }

        return new CharacterMediaPublicationResult(artifacts, refusals);
    }

    /// <summary>One decoded canvas: its shape, its indexed pixels, and the palette its own file carries.</summary>
    private sealed record CharacterCanvas(int Width, int Height, byte[] Pixels, Arena2Palette? OwnPalette);
    /// <summary>
    /// Decodes one canvas of a supplied file. The reader that owns the format decides the shape, so a
    /// canvas index past the end is a refusal rather than a wrapped read.
    /// </summary>
    private static bool TryDecodeCanvas(string file, ReadOnlySpan<byte> bytes, int canvasIndex, out CharacterCanvas? canvas, out string reason)
    {
        canvas = null;
        try
        {
            Arena2CanvasSet set = Arena2CanvasReader.Read(bytes, file);
            if (canvasIndex < 0 || canvasIndex >= set.Canvases.Count)
            {
                reason = $"'{file}' carries {set.Canvases.Count} canvas(es), so canvas {canvasIndex} does not exist";
                return false;
            }

            Arena2Canvas enumerated = set.Canvases[canvasIndex];
            if (file.EndsWith(".CEL", StringComparison.OrdinalIgnoreCase))
            {
                // A classic animation's frames are the canvases, and the container carries the palette
                // the frames are painted in.
                IReadOnlyList<FlcDecoder.FlcFrameImage> frames = FlcDecoder.DecodeFrames(bytes, file, out Arena2Palette? own);
                if (enumerated.Record < 0 || enumerated.Record >= frames.Count)
                {
                    reason = $"'{file}' decodes {frames.Count} frame(s), so frame {enumerated.Record} does not exist";
                    return false;
                }

                FlcDecoder.FlcFrameImage frame = frames[enumerated.Record];
                canvas = new CharacterCanvas(frame.Width, frame.Height, frame.Pixels, own);
                reason = string.Empty;
                return true;
            }

            IReadOnlyList<IndexedImg> records = set.Kind switch
            {
                // The reader established which shape the file has; asking the other reader for it is how a
                // headerless canvas ends up refused as a malformed headered one.
                Arena2CanvasKind.HeaderlessCanvas => [ImgDecoder.DecodeHeaderless(bytes, file)],
                Arena2CanvasKind.ImgRecordSequence when !file.Contains("WEAPO", StringComparison.OrdinalIgnoreCase) => ImgDecoder.DecodeRecordSequence(bytes, file),
                Arena2CanvasKind.ImgRecord => [ImgDecoder.Decode(bytes, file)],
                Arena2CanvasKind.RciGrid => throw new Arena2FormatException(file, 0,
                    $"an RCI grid's {set.Canvases.Count} cells are enumerated by shape; this repository has no decoder that slices their pixels"),
                _ => throw new Arena2FormatException(file, 0,
                    $"the {set.Kind} shape of '{file}' has no pixel decoder here"),
            };
            if (enumerated.Record < 0 || enumerated.Record >= records.Count)
            {
                reason = $"'{file}' decodes {records.Count} record(s), so record {enumerated.Record} does not exist";
                return false;
            }

            IndexedImg image = records[enumerated.Record];
            canvas = new CharacterCanvas(image.Width, image.Height, image.Pixels.ToArray(), null);
            reason = string.Empty;
            return true;
        }
        catch (Arena2FormatException failure)
        {
            reason = failure.Message;
            return false;
        }
    }

    private static byte[] Encode(int width, int height, ReadOnlySpan<byte> indexed, Arena2Palette palette)
    {
        Rgba32[] colors = palette.ToRgba(indexed, PaletteAlphaMode.IndexZeroTransparent);
        byte[] rgba = new byte[checked(colors.Length * 4)];
        for (int index = 0; index < colors.Length; index++)
        {
            int target = index * 4;
            rgba[target] = colors[index].Red;
            rgba[target + 1] = colors[index].Green;
            rgba[target + 2] = colors[index].Blue;
            rgba[target + 3] = colors[index].Alpha;
        }

        return DeterministicPngEncoder.EncodeRgba8(width, height, rgba);
    }

    private static string Slug(string value) => value.Replace('.', '-');
}
