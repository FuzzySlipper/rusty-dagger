using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalization;

/// <summary>One operator-supplied SKY##.DAT source admitted to the product-wide publication.</summary>
public sealed class SkyMediaSource
{
    private readonly byte[] bytes;

    public SkyMediaSource(int skyIndex, string sourcePath, ReadOnlySpan<byte> bytes)
    {
        if (skyIndex is < 0 or >= SkyFileDecoder.FileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(skyIndex));
        }

        SkyFileDecoder.ValidateSource(sourcePath, nameof(sourcePath));
        if (bytes.Length != SkyFileDecoder.FileBytes)
        {
            throw new ArgumentException($"SKY source must be exactly {SkyFileDecoder.FileBytes} bytes.", nameof(bytes));
        }

        SkyIndex = skyIndex;
        SourcePath = sourcePath;
        this.bytes = bytes.ToArray();
    }

    public int SkyIndex { get; }

    public string SourcePath { get; }

    public ReadOnlyMemory<byte> Bytes => bytes;
}

/// <summary>One NITE##I0.IMG source used by the donor's vanilla night-sky selector.</summary>
public sealed class NightSkyMediaSource
{
    private readonly byte[] bytes;

    public NightSkyMediaSource(int nightIndex, string sourcePath, ReadOnlySpan<byte> bytes)
    {
        if (nightIndex is < 0 or >= 4)
        {
            throw new ArgumentOutOfRangeException(nameof(nightIndex));
        }

        SkyFileDecoder.ValidateSource(sourcePath, nameof(sourcePath));
        if (bytes.Length != SkyMediaPublication.NightSourceBytes)
        {
            throw new ArgumentException($"A NITE source must be exactly {SkyMediaPublication.NightSourceBytes} bytes.", nameof(bytes));
        }

        NightIndex = nightIndex;
        SourcePath = sourcePath;
        this.bytes = bytes.ToArray();
    }

    public int NightIndex { get; }

    public string SourcePath { get; }

    public ReadOnlyMemory<byte> Bytes => bytes;
}

/// <summary>The weather family encoded by the donor's sky index offset within an eight-file climate set.</summary>
public enum SkyWeatherVariant
{
    Normal,
    Rain1,
    Rain2,
    Snow1,
    Snow2,
}

/// <summary>One generated 2:1 panorama and its exact source-frame provenance.</summary>
public sealed record SkyPanoramaResource(
    string Id,
    string RelativePath,
    ContentDigest ContentHash,
    long ByteLength,
    int Width,
    int Height,
    int SkyIndex,
    int SourceFrame,
    int EastSourceRecord,
    int WestSourceRecord,
    int PaletteIndex,
    bool EastWestSwapped,
    IReadOnlyList<float> ClearColor,
    string SourcePath)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalPath(RelativePath, nameof(RelativePath));
        ContentHash.Validate();
        ValidateClearColor(ClearColor, Id);
        if (ByteLength <= 0 || Width != SkyMediaPublication.PanoramaWidth || Height != SkyMediaPublication.PanoramaHeight
            || SkyIndex is < 0 or >= SkyFileDecoder.FileCount || SourceFrame is < 0 or >= SkyFileDecoder.FramesPerRecord
            || PaletteIndex != SourceFrame
            || (EastWestSwapped && (EastSourceRecord != 1 || WestSourceRecord != 0))
            || (!EastWestSwapped && (EastSourceRecord != 0 || WestSourceRecord != 1)))
        {
            throw new InvalidOperationException($"Sky panorama '{Id}' carries invalid generated or source-frame facts.");
        }

        SkyFileDecoder.ValidateSource(SourcePath, nameof(SourcePath));
    }

    private static void ValidateClearColor(IReadOnlyList<float> color, string id)
    {
        if (color.Count != 3 || color.Any(channel => !float.IsFinite(channel) || channel is < 0F or > 1F))
            throw new InvalidOperationException($"Sky resource '{id}' must carry a normalized RGB clear color.");
    }
}

/// <summary>One time-of-day lookup entry, including the donor's mirrored afternoon frame rule.</summary>
public sealed record SkyPanoramaSelection(
    int SkyIndex,
    int SkyBase,
    int? Season,
    SkyWeatherVariant Weather,
    int TimeFrame,
    string ResourceId,
    bool SwapEastWest)
{
    public void Validate()
    {
        if (SkyIndex is < 0 or >= SkyFileDecoder.FileCount || SkyBase is not (0 or 8 or 16 or 24)
            || TimeFrame is < 0 or >= 64 || !Enum.IsDefined(Weather))
        {
            throw new InvalidOperationException("A sky panorama selection has invalid climate, weather, or time-frame facts.");
        }

        if (SkyIndex < SkyBase || SkyIndex >= SkyBase + 8)
        {
            throw new InvalidOperationException("A sky panorama selection's source index is outside its climate set.");
        }

        int offset = SkyIndex - SkyBase;
        if (Weather == SkyWeatherVariant.Normal)
        {
            if (offset > 3 || Season != offset)
                throw new InvalidOperationException("A normal sky selection must retain its source season offset.");
        }
        else if (Season is not null)
        {
            throw new InvalidOperationException("A weather sky selection cannot carry a normal season offset.");
        }

        NormalizedImportDocument.RequireLogicalId(ResourceId, nameof(ResourceId));
    }
}

/// <summary>One generated vanilla night background and the source facts behind it.</summary>
public sealed record SkyNightResource(
    string Id,
    string RelativePath,
    ContentDigest ContentHash,
    long ByteLength,
    int Width,
    int Height,
    int SourceWidth,
    int SourceHeight,
    int NightIndex,
    string SourcePath,
    ContentDigest SourceHash,
    long SourceByteLength,
    string PaletteSourcePath,
    bool SeamFixed,
    IReadOnlyList<float> ClearColor)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalPath(RelativePath, nameof(RelativePath));
        ContentHash.Validate();
        SourceHash.Validate();
        if (ClearColor.Count != 3 || ClearColor.Any(channel => !float.IsFinite(channel) || channel is < 0F or > 1F))
            throw new InvalidOperationException($"Night sky resource '{Id}' must carry a normalized RGB clear color.");
        SkyFileDecoder.ValidateSource(SourcePath, nameof(SourcePath));
        SkyFileDecoder.ValidateSource(PaletteSourcePath, nameof(PaletteSourcePath));
        if (ByteLength <= 0 || Width != SkyMediaPublication.PanoramaWidth || Height != SkyMediaPublication.PanoramaHeight
            || SourceWidth != SkyMediaPublication.NightWidth || SourceHeight != SkyMediaPublication.NightHeight
            || NightIndex is < 0 or >= 4
            || SourceByteLength != SkyMediaPublication.NightSourceBytes || !SeamFixed)
        {
            throw new InvalidOperationException($"Night sky resource '{Id}' carries invalid source or generated image facts.");
        }
    }
}

/// <summary>One knot copied from the donor SkyRig's day-frame animation curve.</summary>
public sealed record SkyCurveKnot(float Time, float Value, float Tangent)
{
    public void Validate()
    {
        if (!float.IsFinite(Time) || !float.IsFinite(Value) || !float.IsFinite(Tangent)
            || Time is < 0F or > 1F || Value is < 0F or > 1F)
        {
            throw new InvalidOperationException("Sky frame-curve knots must be finite normalized values.");
        }
    }
}

/// <summary>Product-authored neutral precipitation sprite metadata; it is separate from Bethesda source facts.</summary>
public enum SkyWeatherParticleKind
{
    Rain,
    Snow,
}

/// <summary>One deterministic, product-authored precipitation billboard resource.</summary>
public sealed record SkyWeatherParticleResource(
    string Id,
    string RelativePath,
    ContentDigest ContentHash,
    long ByteLength,
    int Width,
    int Height,
    SkyWeatherParticleKind Kind,
    string Provenance)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalPath(RelativePath, nameof(RelativePath));
        ContentHash.Validate();
        if (ByteLength <= 0 || Width <= 0 || Height <= 0 || !Enum.IsDefined(Kind)
            || !StringComparer.Ordinal.Equals(Provenance, "product-authored-neutral"))
        {
            throw new InvalidOperationException($"Weather particle resource '{Id}' carries invalid authored-resource facts.");
        }
    }
}

/// <summary>Complete product-wide day-sky metadata and generated resource closure.</summary>
public sealed record SkyMediaManifest(
    IReadOnlyList<PublishedSource> Sources,
    IReadOnlyList<SkyPanoramaResource> Resources,
    IReadOnlyList<SkyPanoramaSelection> Selections,
    IReadOnlyList<PublishedSource> NightSources,
    IReadOnlyList<SkyNightResource> NightResources,
    IReadOnlyList<SkyWeatherParticleResource> WeatherParticles,
    IReadOnlyList<SkyCurveKnot> DaylightFrameCurve)
{
    public SkyMediaManifest Canonicalize() => this with
    {
        Sources = Sources.OrderBy(source => source.Path, StringComparer.Ordinal).ToArray(),
        Resources = Resources.OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray(),
        Selections = Selections.OrderBy(selection => selection.SkyIndex).ThenBy(selection => selection.TimeFrame).ToArray(),
        NightSources = NightSources.OrderBy(source => source.Path, StringComparer.Ordinal).ToArray(),
        NightResources = NightResources.OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray(),
        WeatherParticles = WeatherParticles.OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray(),
        DaylightFrameCurve = DaylightFrameCurve.OrderBy(knot => knot.Time).ToArray(),
    };

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(Resources);
        ArgumentNullException.ThrowIfNull(Selections);
        ArgumentNullException.ThrowIfNull(NightSources);
        ArgumentNullException.ThrowIfNull(NightResources);
        ArgumentNullException.ThrowIfNull(WeatherParticles);
        ArgumentNullException.ThrowIfNull(DaylightFrameCurve);
        if (Sources.Count != SkyFileDecoder.FileCount || Resources.Count != SkyFileDecoder.FileCount * SkyFileDecoder.FramesPerRecord * 2
            || Selections.Count != SkyFileDecoder.FileCount * 64 || (NightSources.Count != 0 && NightSources.Count != 4)
            || NightResources.Count != NightSources.Count || WeatherParticles.Count != 2 || DaylightFrameCurve.Count != 7)
        {
            throw new InvalidOperationException("The sky publication must close over all day sources, panoramas, time frames, optional four night sources, two precipitation sprites, and the seven day-frame curve knots.");
        }

        NormalizedImportDocument.ValidateUnique(Sources, source => source.Path, "sky source");
        NormalizedImportDocument.ValidateUnique(NightSources, source => source.Path, "night sky source");
        NormalizedImportDocument.ValidateUnique(Resources, resource => resource.Id, "sky panorama");
        NormalizedImportDocument.ValidateUnique(NightResources, resource => resource.Id, "night sky resource");
        NormalizedImportDocument.ValidateUnique(WeatherParticles, resource => resource.Id, "weather particle resource");
        foreach (PublishedSource source in Sources) source.Validate();
        foreach (SkyPanoramaResource resource in Resources) resource.Validate();
        foreach (SkyPanoramaSelection selection in Selections) selection.Validate();
        foreach (PublishedSource source in NightSources) source.Validate();
        foreach (SkyNightResource resource in NightResources) resource.Validate();
        foreach (SkyWeatherParticleResource resource in WeatherParticles) resource.Validate();
        foreach (SkyCurveKnot knot in DaylightFrameCurve) knot.Validate();
        bool valuesDecrease = false;
        for (int index = 1; index < DaylightFrameCurve.Count; index++)
        {
            if (DaylightFrameCurve[index].Value < DaylightFrameCurve[index - 1].Value)
            {
                valuesDecrease = true;
                break;
            }
        }

        if (!DaylightFrameCurve.Select(knot => knot.Time).SequenceEqual(DaylightFrameCurve.Select(knot => knot.Time).Order())
            || DaylightFrameCurve[0].Time != 0F || DaylightFrameCurve[^1].Time != 1F
            || valuesDecrease)
        {
            throw new InvalidOperationException("The sky frame curve must be ordered from dawn to dusk and nondecreasing.");
        }

        HashSet<int> nightIndexes = NightResources.Select(resource => resource.NightIndex).ToHashSet();
        if (NightSources.Count != 0 && (nightIndexes.Count != 4 || nightIndexes.Any(index => index is < 0 or >= 4)))
            throw new InvalidOperationException("Night sky resources must cover NITE00 through NITE03 exactly.");

        HashSet<int> sourceIndexes = Resources.Select(resource => resource.SkyIndex).ToHashSet();
        if (sourceIndexes.Count != SkyFileDecoder.FileCount || sourceIndexes.Any(index => index is < 0 or >= SkyFileDecoder.FileCount))
            throw new InvalidOperationException("The sky panorama resources must cover every source sky index exactly.");
        foreach (SkyPanoramaSelection selection in Selections)
        {
            if (!Resources.Any(resource => StringComparer.Ordinal.Equals(resource.Id, selection.ResourceId)
                && resource.SkyIndex == selection.SkyIndex
                    && resource.SourceFrame == (selection.TimeFrame < 32 ? selection.TimeFrame : 63 - selection.TimeFrame)
                    && resource.EastWestSwapped == selection.SwapEastWest))
            {
                throw new InvalidOperationException($"Sky selection {selection.SkyIndex}/{selection.TimeFrame} names no matching source panorama.");
            }
        }
    }
}

/// <summary>Offline publication of all source day skies as ordinary 2:1 RGBA panorama resources.</summary>
public sealed record SkyMediaPublication(
    IReadOnlyList<ImportPublicationArtifact> Artifacts,
    SkyMediaManifest Manifest,
    IReadOnlyList<PublishedSource> Sources)
{
    public const string ManifestRelativePath = "media/sky/manifest.json";
    public const int PanoramaWidth = 1024;
    public const int PanoramaHeight = 512;
    public const int NightSourceBytes = 112_128;
    public const int NightWidth = 512;
    public const int NightHeight = 219;

    /// <summary>Source values from Daggerfall Unity's SkyRig.prefab SkyCurve.</summary>
    public static IReadOnlyList<SkyCurveKnot> DaylightFrameCurve { get; } =
    [
        new(0F, 0F, 1.807229F), new(0.083F, 0.15F, 1.9096386F), new(0.166F, 0.317F, 1.2799762F),
        new(0.5F, 0.5F, 0F), new(0.834F, 0.683F, 1.2799761F), new(0.917F, 0.85F, 1.9096383F),
        new(1F, 1F, 1.8072286F),
    ];

    public static SkyMediaPublication Create(IEnumerable<SkyMediaSource> inputs)
        => Create(inputs, [], palette: null);

    public static SkyMediaPublication Create(
        IEnumerable<SkyMediaSource> inputs,
        IEnumerable<NightSkyMediaSource> nightInputs,
        Arena2Palette? palette)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(nightInputs);
        SkyMediaSource[] sources = inputs.OrderBy(source => source.SkyIndex).ToArray();
        if (sources.Length != SkyFileDecoder.FileCount || sources.Select(source => source.SkyIndex).Distinct().Count() != SkyFileDecoder.FileCount
            || sources.Select(source => source.SourcePath).Distinct(StringComparer.Ordinal).Count() != SkyFileDecoder.FileCount)
        {
            throw new InvalidOperationException("Sky publication requires one distinct source for every SKY00.DAT through SKY31.DAT.");
        }

        List<ImportPublicationArtifact> artifacts = [];
        List<SkyPanoramaResource> resources = [];
        List<PublishedSource> provenance = [];
        foreach (SkyMediaSource source in sources)
        {
            SkyFileDocument document = SkyFileDecoder.Decode(source.SkyIndex, source.Bytes.Span, source.SourcePath);
            provenance.Add(PublishedSource.Of(source.SourcePath, source.Bytes.Span));
            for (int frame = 0; frame < SkyFileDecoder.FramesPerRecord; frame++)
            {
                for (int swap = 0; swap < 2; swap++)
                {
                    bool eastWestSwapped = swap == 1;
                    string suffix = eastWestSwapped ? "-swapped" : string.Empty;
                    string id = $"sky/{source.SkyIndex:00}/frame-{frame:00}{suffix}";
                    string path = $"media/sky/resources/sky-{source.SkyIndex:00}-frame-{frame:00}{suffix}.png";
                    byte[] png = SkyPanoramaEncoder.Encode(document, frame, eastWestSwapped);
                    artifacts.Add(new(path, png, mediaId: id));
                    resources.Add(new(
                        id,
                        path,
                        ContentDigest.Compute(png),
                        png.LongLength,
                        PanoramaWidth,
                        PanoramaHeight,
                        source.SkyIndex,
                        frame,
                        eastWestSwapped ? 1 : 0,
                        eastWestSwapped ? 0 : 1,
                        frame,
                        eastWestSwapped,
                        SkyPanoramaEncoder.ClearColor(document, frame, eastWestSwapped),
                        source.SourcePath));
                }
            }
        }

        NightSkyMediaSource[] nightSources = nightInputs.OrderBy(source => source.NightIndex).ToArray();
        if (nightSources.Length != 0 && palette is null)
            throw new ArgumentNullException(nameof(palette), "Night sky publication requires the shared PAL.PAL palette.");
        if (nightSources.Length != 0 && (nightSources.Length != 4 || nightSources.Select(source => source.NightIndex).Distinct().Count() != 4))
            throw new InvalidOperationException("Night sky publication requires NITE00I0.IMG through NITE03I0.IMG exactly once.");

        List<SkyNightResource> nightResources = [];
        List<PublishedSource> nightProvenance = [];
        foreach (NightSkyMediaSource source in nightSources)
        {
            IndexedImg image = ImgDecoder.DecodeHeaderless(source.Bytes.Span, source.SourcePath);
            if (image.Width != NightWidth || image.Height != NightHeight)
                throw new InvalidOperationException($"Night sky source '{source.SourcePath}' is {image.Width}x{image.Height}, not {NightWidth}x{NightHeight}.");
            byte[] rgba = palette!.ToRgbaBytes(image.Pixels.Span, PaletteAlphaMode.Opaque);
            for (int row = 0; row < NightHeight; row++)
            {
                int seam = ((row * NightWidth) + NightWidth - 2) * 4;
                rgba.AsSpan(seam, 4).CopyTo(rgba.AsSpan(seam + 4, 4));
            }

            string id = $"sky/night/{source.NightIndex:00}";
            string path = $"media/sky/resources/night-{source.NightIndex:00}.png";
            byte[] png = SkyNightPanoramaEncoder.Encode(rgba);
            artifacts.Add(new(path, png, mediaId: id));
            nightResources.Add(new(id, path, ContentDigest.Compute(png), png.LongLength, PanoramaWidth, PanoramaHeight,
                NightWidth, NightHeight, source.NightIndex, source.SourcePath, ContentDigest.Compute(source.Bytes.Span), source.Bytes.Length,
                "arena2/PAL.PAL", SeamFixed: true, ClearColor: SkyNightPanoramaEncoder.ClearColor(rgba)));
            nightProvenance.Add(PublishedSource.Of(source.SourcePath, source.Bytes.Span));
        }

        SkyWeatherParticleResource[] particleResources = CreateWeatherParticles(artifacts);

        List<SkyPanoramaSelection> selections = [];
        foreach (SkyMediaSource source in sources)
        {
            int skyBase = (source.SkyIndex / 8) * 8;
            int offset = source.SkyIndex - skyBase;
            (SkyWeatherVariant weather, int? season) = offset switch
            {
                <= 3 => (SkyWeatherVariant.Normal, (int?)offset),
                4 => (SkyWeatherVariant.Rain1, null),
                5 => (SkyWeatherVariant.Rain2, null),
                6 => (SkyWeatherVariant.Snow1, null),
                7 => (SkyWeatherVariant.Snow2, null),
                _ => throw new InvalidOperationException("Sky index offset is outside the donor's eight-file weather set."),
            };
            for (int timeFrame = 0; timeFrame < 64; timeFrame++)
            {
                int sourceFrame = timeFrame < 32 ? timeFrame : 63 - timeFrame;
                string suffix = timeFrame >= 32 ? "-swapped" : string.Empty;
                selections.Add(new(source.SkyIndex, skyBase, season, weather, timeFrame,
                    $"sky/{source.SkyIndex:00}/frame-{sourceFrame:00}{suffix}", timeFrame >= 32));
            }
        }

        SkyMediaManifest manifest = new SkyMediaManifest(provenance, resources, selections, nightProvenance, nightResources,
            particleResources, DaylightFrameCurve).Canonicalize();
        manifest.Validate();
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PublishedJson.Section);
        artifacts.Add(new(ManifestRelativePath, [.. manifestBytes, (byte)'\n']));
        return new(artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray(), manifest, provenance);
    }

    private static SkyWeatherParticleResource[] CreateWeatherParticles(List<ImportPublicationArtifact> artifacts)
    {
        (string Id, string Path, SkyWeatherParticleKind Kind, int Width, int Height, byte[] Bytes)[] definitions =
        [
            ("weather.particle.rain", "media/sky/resources/particles/rain.png", SkyWeatherParticleKind.Rain, 16, 64, WeatherParticleEncoder.Rain()),
            ("weather.particle.snow", "media/sky/resources/particles/snow.png", SkyWeatherParticleKind.Snow, 32, 32, WeatherParticleEncoder.Snow()),
        ];
        List<SkyWeatherParticleResource> resources = [];
        foreach (var definition in definitions)
        {
            artifacts.Add(new(definition.Path, definition.Bytes, mediaId: definition.Id));
            resources.Add(new(definition.Id, definition.Path, ContentDigest.Compute(definition.Bytes), definition.Bytes.LongLength,
                definition.Width, definition.Height, definition.Kind, "product-authored-neutral"));
        }

        return [.. resources];
    }
}

/// <summary>Converts the two native 512x220 sky hemispheres into one deterministic 2:1 panorama.</summary>
internal static class SkyPanoramaEncoder
{
    public static IReadOnlyList<float> ClearColor(SkyFileDocument document, int frame, bool eastWestSwapped)
    {
        int sourceRecord = eastWestSwapped ? 0 : 1;
        Rgba32 pixel = document.Palettes[frame].ToRgba(document.Frame(sourceRecord, frame).IndexedPixels.Span[..1], PaletteAlphaMode.Opaque)[0];
        return [pixel.Red / 255F, pixel.Green / 255F, pixel.Blue / 255F];
    }

    public static byte[] Encode(SkyFileDocument document, int frame, bool eastWestSwapped)
    {
        Arena2Palette palette = document.Palettes[frame];
        byte[] east = palette.ToRgbaBytes(document.Frame(0, frame).IndexedPixels.Span, PaletteAlphaMode.Opaque);
        byte[] west = palette.ToRgbaBytes(document.Frame(1, frame).IndexedPixels.Span, PaletteAlphaMode.Opaque);
        byte[] panorama = new byte[checked(SkyMediaPublication.PanoramaWidth * SkyMediaPublication.PanoramaHeight * 4)];
        for (int y = 0; y < SkyMediaPublication.PanoramaHeight; y++)
        {
            int sourceY = Math.Min(SkyFileDecoder.FrameHeight - 1,
                (int)((long)y * SkyFileDecoder.FrameHeight / SkyMediaPublication.PanoramaHeight));
            for (int x = 0; x < SkyMediaPublication.PanoramaWidth; x++)
            {
                int sourceX = x % SkyFileDecoder.FrameWidth;
                bool eastHalf = x < SkyFileDecoder.FrameWidth;
                byte[] source = eastHalf == eastWestSwapped ? west : east;
                int sourceOffset = ((sourceY * SkyFileDecoder.FrameWidth) + sourceX) * 4;
                int targetOffset = ((y * SkyMediaPublication.PanoramaWidth) + x) * 4;
                source.AsSpan(sourceOffset, 4).CopyTo(panorama.AsSpan(targetOffset, 4));
            }
        }

        return DeterministicPngEncoder.EncodeRgba8(SkyMediaPublication.PanoramaWidth, SkyMediaPublication.PanoramaHeight, panorama);
    }
}

/// <summary>Converts the donor's single night image, used for both hemispheres, into a 2:1 panorama.</summary>
internal static class SkyNightPanoramaEncoder
{
    public static IReadOnlyList<float> ClearColor(ReadOnlySpan<byte> sourceRgba) =>
        [sourceRgba[0] / 255F, sourceRgba[1] / 255F, sourceRgba[2] / 255F];

    public static byte[] Encode(ReadOnlySpan<byte> sourceRgba)
    {
        int width = SkyMediaPublication.PanoramaWidth;
        int height = SkyMediaPublication.PanoramaHeight;
        byte[] panorama = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(SkyMediaPublication.NightHeight - 1,
                (int)((long)y * SkyMediaPublication.NightHeight / height));
            for (int x = 0; x < width; x++)
            {
                int sourceX = x % SkyMediaPublication.NightWidth;
                int sourceOffset = ((sourceY * SkyMediaPublication.NightWidth) + sourceX) * 4;
                int targetOffset = ((y * width) + x) * 4;
                sourceRgba.Slice(sourceOffset, 4).CopyTo(panorama.AsSpan(targetOffset, 4));
            }
        }

        return DeterministicPngEncoder.EncodeRgba8(width, height, panorama);
    }
}

/// <summary>Small deterministic authored sprites for Engine precipitation billboards.</summary>
internal static class WeatherParticleEncoder
{
    public static byte[] Rain()
    {
        const int width = 16, height = 64;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            float along = MathF.Sin((y + 1F) / (height + 1F) * MathF.PI);
            for (int x = 0; x < width; x++)
            {
                float distance = MathF.Abs(x - (width - 1) * 0.5F);
                float alpha = MathF.Max(0F, 1F - distance / 3.5F) * along;
                int offset = ((y * width) + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = byte.MaxValue;
                pixels[offset + 3] = (byte)Math.Clamp(MathF.Round(alpha * 210F), 0F, 255F);
            }
        }

        return DeterministicPngEncoder.EncodeRgba8(width, height, pixels);
    }

    public static byte[] Snow()
    {
        const int width = 32, height = 32;
        byte[] pixels = new byte[width * height * 4];
        float center = (width - 1) * 0.5F;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float distance = MathF.Sqrt(MathF.Pow(x - center, 2F) + MathF.Pow(y - center, 2F));
                float alpha = MathF.Max(0F, 1F - distance / (width * 0.5F));
                int offset = ((y * width) + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = byte.MaxValue;
                pixels[offset + 3] = (byte)Math.Clamp(MathF.Round(alpha * 190F), 0F, 255F);
            }
        }

        return DeterministicPngEncoder.EncodeRgba8(width, height, pixels);
    }
}
