using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalization;

/// <summary>One published character canvas: its media identity and the PNG written for it.</summary>
public sealed record CharacterMediaArtifact(string MediaId, string RelativePath, byte[] Bytes, int Width, int Height);

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
    /// <param name="palette">The shared palette every canvas without its own is painted in.</param>
    public static CharacterMediaPublicationResult Publish(
        string family,
        IEnumerable<CharacterCanvasReference> references,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> sources,
        Arena2Palette palette)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(palette);

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

            // A classic animation carries its own palette; every other family is painted in the shared
            // one, which is what the publisher resolves its canvases through.
            byte[] png = Encode(canvas!.Width, canvas.Height, canvas.Pixels, canvas.OwnPalette ?? palette);
            artifacts.Add(new CharacterMediaArtifact(reference.MediaId, $"media/character/{Slug(reference.MediaId)}.png", png, canvas.Width, canvas.Height));
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

            IReadOnlyList<IndexedImg> records = file.EndsWith(".CIF", StringComparison.OrdinalIgnoreCase) && !file.Contains("WEAPO", StringComparison.OrdinalIgnoreCase)
                ? ImgDecoder.DecodeRecordSequence(bytes, file)
                : [ImgDecoder.Decode(bytes, file)];
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
