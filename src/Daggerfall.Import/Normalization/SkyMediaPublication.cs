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

/// <summary>The exact NIGHTSKY.COL palette paired with every vanilla NITE##I0.IMG source.</summary>
public sealed class NightSkyPaletteSource
{
    public const string FileName = "NIGHTSKY.COL";
    private readonly byte[] bytes;

    public NightSkyPaletteSource(string sourcePath, ReadOnlySpan<byte> bytes)
    {
        ValidateSourcePath(sourcePath);
        if (bytes.Length != Arena2FormatConstants.PaletteHeaderedBytes)
        {
            throw new ArgumentException($"{FileName} must be exactly {Arena2FormatConstants.PaletteHeaderedBytes} bytes.", nameof(bytes));
        }

        SourcePath = sourcePath;
        this.bytes = bytes.ToArray();
        Palette = PaletteDecoder.Decode(this.bytes, sourcePath);
    }

    public string SourcePath { get; }

    public ReadOnlyMemory<byte> Bytes => bytes;

    public Arena2Palette Palette { get; }

    public ContentDigest SourceHash => ContentDigest.Compute(bytes);

    public long ByteLength => bytes.Length;

    internal static void ValidateSourcePath(string sourcePath)
    {
        SkyFileDecoder.ValidateSource(sourcePath, nameof(sourcePath));
        int separator = sourcePath.LastIndexOf('/');
        string leaf = sourcePath[(separator + 1)..];
        if (!StringComparer.OrdinalIgnoreCase.Equals(leaf, FileName))
        {
            throw new ArgumentException($"Night sky media requires the donor {FileName} palette, not '{leaf}'.", nameof(sourcePath));
        }
    }
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

/// <summary>The donor's deterministic night-star mutation applied before palette conversion.</summary>
public sealed record SkyNightStarPolicy(
    int RandomSeed,
    float Chance,
    int EligibleIndexMinimumExclusive,
    int EligibleIndexMaximumExclusive,
    IReadOnlyList<byte> PaletteIndices)
{
    public static SkyNightStarPolicy Donor { get; } = new(0, 0.004F, 16, 32, [16, 32, 74, 105, 112, 120]);

    public void Validate()
    {
        if (RandomSeed != 0 || Chance != 0.004F || EligibleIndexMinimumExclusive != 16
            || EligibleIndexMaximumExclusive != 32 || !PaletteIndices.SequenceEqual(Donor.PaletteIndices))
        {
            throw new InvalidOperationException("Night-star policy must retain the donor seed, chance, eligible index range, and palette indices.");
        }
    }
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
    ContentDigest PaletteSourceHash,
    long PaletteSourceByteLength,
    SkyNightStarPolicy StarPolicy,
    bool SeamFixed,
    IReadOnlyList<float> ClearColor)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalPath(RelativePath, nameof(RelativePath));
        ContentHash.Validate();
        SourceHash.Validate();
        PaletteSourceHash.Validate();
        NightSkyPaletteSource.ValidateSourcePath(PaletteSourcePath);
        StarPolicy.Validate();
        if (ClearColor.Count != 3 || ClearColor.Any(channel => !float.IsFinite(channel) || channel is < 0F or > 1F))
            throw new InvalidOperationException($"Night sky resource '{Id}' must carry a normalized RGB clear color.");
        SkyFileDecoder.ValidateSource(SourcePath, nameof(SourcePath));
        SkyFileDecoder.ValidateSource(PaletteSourcePath, nameof(PaletteSourcePath));
        if (ByteLength <= 0 || Width != SkyMediaPublication.PanoramaWidth || Height != SkyMediaPublication.PanoramaHeight
            || SourceWidth != SkyMediaPublication.NightWidth || SourceHeight != SkyMediaPublication.NightHeight
            || NightIndex is < 0 or >= 4
            || SourceByteLength != SkyMediaPublication.NightSourceBytes
            || PaletteSourceByteLength != Arena2FormatConstants.PaletteHeaderedBytes || !SeamFixed)
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

/// <summary>
/// One panorama with its painted horizon cleared (<see cref="SkyHorizonClearing"/>), drawn over a real horizon.
/// </summary>
/// <param name="Id">This resource's identity.</param>
/// <param name="Of">The day or night panorama it clears.</param>
public sealed record SkyClearedResource(string Id, string Of, string RelativePath, ContentDigest ContentHash, long ByteLength, int Width, int Height,
    IReadOnlyList<float> ClearColor)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalId(Of, nameof(Of));
        NormalizedImportDocument.RequireLogicalPath(RelativePath, nameof(RelativePath));
        ContentHash.Validate();
        if (ByteLength <= 0 || Width != SkyMediaPublication.PanoramaWidth || Height != SkyMediaPublication.PanoramaHeight || ClearColor.Count != 3)
            throw new InvalidOperationException($"Cleared sky panorama '{Id}' carries invalid generated facts.");
    }
}

/// <summary>One moon's source: its CFA animation of the 32 days of the lunar cycle.</summary>
public sealed record SkyMoonSource(string Moon, string SourcePath, ReadOnlyMemory<byte> Bytes);

/// <summary>One moon's phases as an atlas, frame 0 at the top left, running along each row.</summary>
public sealed record SkyMoonResource(string Id, string Moon, string RelativePath, ContentDigest ContentHash, long ByteLength, int Width, int Height,
    int FrameWidth, int FrameHeight, int Columns, int FrameCount, string SourcePath, ContentDigest SourceHash)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        NormalizedImportDocument.RequireLogicalPath(RelativePath, nameof(RelativePath));
        ContentHash.Validate();
        SourceHash.Validate();
        if (ByteLength <= 0 || FrameCount != SkyMediaPublication.MoonFrames || Columns != SkyMediaPublication.MoonColumns
            || Width != FrameWidth * Columns || Height != FrameHeight * (FrameCount / Columns))
            throw new InvalidOperationException($"Moon '{Id}' carries invalid generated facts.");
    }
}

/// <summary>Complete product-wide day-sky metadata and generated resource closure.</summary>
public sealed record SkyMediaManifest(
    IReadOnlyList<PublishedSource> Sources,
    IReadOnlyList<SkyPanoramaResource> Resources,
    IReadOnlyList<SkyPanoramaSelection> Selections,
    IReadOnlyList<PublishedSource> NightSources,
    IReadOnlyList<SkyNightResource> NightResources,
    IReadOnlyList<SkyCurveKnot> DaylightFrameCurve,
    IReadOnlyList<SkyClearedResource> ClearedResources,
    IReadOnlyList<SkyMoonResource> Moons)
{
    public SkyMediaManifest Canonicalize() => this with
    {
        Moons = Moons.OrderBy(moon => moon.Id, StringComparer.Ordinal).ToArray(),
        Sources = Sources.OrderBy(source => source.Path, StringComparer.Ordinal).ToArray(),
        Resources = Resources.OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray(),
        Selections = Selections.OrderBy(selection => selection.SkyIndex).ThenBy(selection => selection.TimeFrame).ToArray(),
        NightSources = NightSources.OrderBy(source => source.Path, StringComparer.Ordinal).ToArray(),
        NightResources = NightResources.OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray(),
        DaylightFrameCurve = DaylightFrameCurve.OrderBy(knot => knot.Time).ToArray(),
        ClearedResources = ClearedResources.OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray(),
    };

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(Resources);
        ArgumentNullException.ThrowIfNull(Selections);
        ArgumentNullException.ThrowIfNull(NightSources);
        ArgumentNullException.ThrowIfNull(NightResources);
        ArgumentNullException.ThrowIfNull(DaylightFrameCurve);
        if (Sources.Count != SkyFileDecoder.FileCount || Resources.Count != SkyFileDecoder.FileCount * SkyFileDecoder.FramesPerRecord * 2
            || Selections.Count != SkyFileDecoder.FileCount * 64 || (NightSources.Count != 0 && NightSources.Count != 4)
            || NightResources.Count != NightSources.Count || DaylightFrameCurve.Count != 7)
        {
            throw new InvalidOperationException("The sky publication must close over all day sources, panoramas, time frames, optional four night sources and the frame curve.");
        }

        NormalizedImportDocument.ValidateUnique(Sources, source => source.Path, "sky source");
        NormalizedImportDocument.ValidateUnique(NightSources, source => source.Path, "night sky source");
        NormalizedImportDocument.ValidateUnique(Resources, resource => resource.Id, "sky panorama");
        NormalizedImportDocument.ValidateUnique(NightResources, resource => resource.Id, "night sky resource");
        foreach (PublishedSource source in Sources) source.Validate();
        foreach (SkyPanoramaResource resource in Resources) resource.Validate();
        foreach (SkyPanoramaSelection selection in Selections) selection.Validate();
        foreach (PublishedSource source in NightSources) source.Validate();
        foreach (SkyNightResource resource in NightResources) resource.Validate();
        foreach (SkyCurveKnot knot in DaylightFrameCurve) knot.Validate();
        foreach (SkyClearedResource resource in ClearedResources) resource.Validate();
        NormalizedImportDocument.ValidateUnique(ClearedResources, resource => resource.Id, "cleared sky panorama");
        if (ClearedResources.Count != Resources.Count + NightResources.Count)
            throw new InvalidOperationException("Every day and night panorama needs its cleared horizon.");
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

        foreach (SkyMoonResource moon in Moons) moon.Validate();
        if (Moons.Count is not (0 or 2)) throw new InvalidOperationException("A sky publishes both moons or neither.");

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
    /// <summary>A moon's frames, one for each day of the lunar cycle.</summary>
    public const int MoonFrames = 32;
    public const int MoonColumns = 8;

    /// <summary>Source values from Daggerfall Unity's SkyRig.prefab SkyCurve.</summary>
    public static IReadOnlyList<SkyCurveKnot> DaylightFrameCurve { get; } =
    [
        new(0F, 0F, 1.807229F), new(0.083F, 0.15F, 1.9096386F), new(0.166F, 0.317F, 1.2799762F),
        new(0.5F, 0.5F, 0F), new(0.834F, 0.683F, 1.2799761F), new(0.917F, 0.85F, 1.9096383F),
        new(1F, 1F, 1.8072286F),
    ];

    public static SkyMediaPublication Create(IEnumerable<SkyMediaSource> inputs)
        => Create(inputs, [], nightPalette: null);

    public static SkyMediaPublication Create(
        IEnumerable<SkyMediaSource> inputs,
        IEnumerable<NightSkyMediaSource> nightInputs,
        NightSkyPaletteSource? nightPalette,
        IEnumerable<SkyMoonSource>? moons = null,
        Arena2Palette? moonPalette = null)
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
        List<SkyClearedResource> cleared = [];
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
                    byte[] panorama = SkyPanoramaEncoder.Pixels(document, frame, eastWestSwapped);
                    byte[] png = SkyPanoramaEncoder.Png(panorama);
                    artifacts.Add(new(path, png, mediaId: id));
                    cleared.Add(Cleared(artifacts, id, $"media/sky/resources/sky-{source.SkyIndex:00}-frame-{frame:00}{suffix}-clear.png",
                        panorama));
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
        if (nightSources.Length != 0 && nightPalette is null)
            throw new ArgumentNullException(nameof(nightPalette), $"Night sky publication requires {NightSkyPaletteSource.FileName}.");
        if (nightSources.Length != 0 && (nightSources.Length != 4 || nightSources.Select(source => source.NightIndex).Distinct().Count() != 4))
            throw new InvalidOperationException("Night sky publication requires NITE00I0.IMG through NITE03I0.IMG exactly once.");

        List<SkyNightResource> nightResources = [];
        List<PublishedSource> nightProvenance = [];
        foreach (NightSkyMediaSource source in nightSources)
        {
            IndexedImg image = ImgDecoder.DecodeHeaderless(source.Bytes.Span, source.SourcePath);
            if (image.Width != NightWidth || image.Height != NightHeight)
                throw new InvalidOperationException($"Night sky source '{source.SourcePath}' is {image.Width}x{image.Height}, not {NightWidth}x{NightHeight}.");
            byte[] indexed = ApplyDonorNightStars(image.Pixels.Span, SkyNightStarPolicy.Donor);
            byte[] rgba = nightPalette!.Palette.ToRgbaBytes(indexed, PaletteAlphaMode.Opaque);
            for (int row = 0; row < NightHeight; row++)
            {
                int seam = ((row * NightWidth) + NightWidth - 2) * 4;
                rgba.AsSpan(seam, 4).CopyTo(rgba.AsSpan(seam + 4, 4));
            }

            string id = $"sky/night/{source.NightIndex:00}";
            string path = $"media/sky/resources/night-{source.NightIndex:00}.png";
            byte[] panorama = SkyNightPanoramaEncoder.Pixels(rgba);
            byte[] png = SkyPanoramaEncoder.Png(panorama);
            artifacts.Add(new(path, png, mediaId: id));
            cleared.Add(Cleared(artifacts, id, $"media/sky/resources/night-{source.NightIndex:00}-clear.png", panorama));
            nightResources.Add(new(id, path, ContentDigest.Compute(png), png.LongLength, PanoramaWidth, PanoramaHeight,
                NightWidth, NightHeight, source.NightIndex, source.SourcePath, ContentDigest.Compute(source.Bytes.Span), source.Bytes.Length,
                nightPalette.SourcePath, nightPalette.SourceHash, nightPalette.ByteLength, SkyNightStarPolicy.Donor,
                SeamFixed: true, ClearColor: SkyNightPanoramaEncoder.ClearColor(rgba)));
            nightProvenance.Add(PublishedSource.Of(source.SourcePath, source.Bytes.Span));
        }

        SkyMoonResource[] moonResources = [.. (moons ?? []).Select(moon => PublishMoon(artifacts, moon,
            moonPalette ?? throw new ArgumentNullException(nameof(moonPalette), "Moons are published through the art palette.")))];

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
            DaylightFrameCurve, cleared, moonResources).Canonicalize();
        manifest.Validate();
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PublishedJson.Section);
        artifacts.Add(new(ManifestRelativePath, [.. manifestBytes, (byte)'\n']));
        return new(artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray(), manifest, provenance);
    }

    /// <summary>Lays a moon's frames into one atlas. Palette index 0 is the sky around the disc, and clear.</summary>
    private static SkyMoonResource PublishMoon(List<ImportPublicationArtifact> artifacts, SkyMoonSource source, Arena2Palette palette)
    {
        CfaFrames frames = CfaDecoder.Decode(source.Bytes.Span, source.SourcePath);
        if (frames.FrameCount != MoonFrames)
            throw new InvalidOperationException($"Moon source '{source.SourcePath}' has {frames.FrameCount} frames for a {MoonFrames}-day cycle.");
        int width = frames.Width * MoonColumns, height = frames.Height * (MoonFrames / MoonColumns);
        byte[] atlas = new byte[checked(width * height * 4)];
        for (int frame = 0; frame < MoonFrames; frame++)
        {
            byte[] rgba = palette.ToRgbaBytes(frames.Frame(frame), PaletteAlphaMode.IndexZeroTransparent);
            int left = frame % MoonColumns * frames.Width, top = frame / MoonColumns * frames.Height;
            for (int row = 0; row < frames.Height; row++)
                rgba.AsSpan(row * frames.Width * 4, frames.Width * 4).CopyTo(atlas.AsSpan((((top + row) * width) + left) * 4));
        }

        string id = $"sky/moon/{source.Moon}", path = $"media/sky/resources/moon-{source.Moon}.png";
        byte[] png = DeterministicPngEncoder.EncodeRgba8(width, height, atlas);
        artifacts.Add(new(path, png, mediaId: id));
        return new(id, source.Moon, path, ContentDigest.Compute(png), png.LongLength, width, height, frames.Width, frames.Height,
            MoonColumns, MoonFrames, source.SourcePath, ContentDigest.Compute(source.Bytes.Span));
    }

    private static SkyClearedResource Cleared(List<ImportPublicationArtifact> artifacts, string of, string path, byte[] panorama)
    {
        string id = $"{of}/clear";
        byte[] png = SkyPanoramaEncoder.Png(SkyHorizonClearing.Clear(panorama));
        artifacts.Add(new(path, png, mediaId: id));
        return new(id, of, path, ContentDigest.Compute(png), png.LongLength, PanoramaWidth, PanoramaHeight, SkyHorizonClearing.HorizonColor(panorama));
    }

    private static byte[] ApplyDonorNightStars(ReadOnlySpan<byte> indexedPixels, SkyNightStarPolicy policy)
    {
        // DaggerfallSky owns one System.Random(0). Offline publication uses the same seed for
        // each independent NITE resource so the generated closure stays deterministic while
        // retaining the donor's strict clear-sky index gate and replacement palette choices.
        byte[] pixels = indexedPixels.ToArray();
        Random random = new(policy.RandomSeed);
        for (int index = 0; index < pixels.Length; index++)
        {
            int sourceIndex = pixels[index];
            if (sourceIndex > policy.EligibleIndexMinimumExclusive
                && sourceIndex < policy.EligibleIndexMaximumExclusive
                && random.NextDouble() < policy.Chance)
            {
                pixels[index] = policy.PaletteIndices[random.Next(policy.PaletteIndices.Count)];
            }
        }

        return pixels;
    }
}

/// <summary>
/// Converts the two native 512x220 sky hemispheres into one deterministic 2:1 panorama.
/// The donor's fake-sky quads put the source image's bottom row at the horizon. The Engine
/// samples an equirectangular sky with the horizon at v=.5, so the source occupies the upper
/// half and its horizon row is extended below it. The quarter turn aligns donor yaw zero
/// (forward -Z, Engine u=.25) with the west source's first column.
/// </summary>
internal static class SkyPanoramaEncoder
{
    private const int UpperHemisphereHeight = SkyMediaPublication.PanoramaHeight / 2;
    private const int QuarterPanoramaWidth = SkyMediaPublication.PanoramaWidth / 4;

    public static IReadOnlyList<float> ClearColor(SkyFileDocument document, int frame, bool eastWestSwapped)
    {
        int sourceRecord = eastWestSwapped ? 0 : 1;
        Rgba32 pixel = document.Palettes[frame].ToRgba(document.Frame(sourceRecord, frame).IndexedPixels.Span[..1], PaletteAlphaMode.Opaque)[0];
        return [pixel.Red / 255F, pixel.Green / 255F, pixel.Blue / 255F];
    }

    public static byte[] Encode(SkyFileDocument document, int frame, bool eastWestSwapped) => Png(Pixels(document, frame, eastWestSwapped));

    public static byte[] Png(byte[] panorama) =>
        DeterministicPngEncoder.EncodeRgba8(SkyMediaPublication.PanoramaWidth, SkyMediaPublication.PanoramaHeight, panorama);

    public static byte[] Pixels(SkyFileDocument document, int frame, bool eastWestSwapped)
    {
        Arena2Palette palette = document.Palettes[frame];
        byte[] east = palette.ToRgbaBytes(document.Frame(0, frame).IndexedPixels.Span, PaletteAlphaMode.Opaque);
        byte[] west = palette.ToRgbaBytes(document.Frame(1, frame).IndexedPixels.Span, PaletteAlphaMode.Opaque);
        byte[] panorama = new byte[checked(SkyMediaPublication.PanoramaWidth * SkyMediaPublication.PanoramaHeight * 4)];
        for (int y = 0; y < SkyMediaPublication.PanoramaHeight; y++)
        {
            int sourceY = y < UpperHemisphereHeight
                ? Math.Min(SkyFileDecoder.FrameHeight - 1,
                    (int)((long)y * SkyFileDecoder.FrameHeight / UpperHemisphereHeight))
                : SkyFileDecoder.FrameHeight - 1;
            for (int x = 0; x < SkyMediaPublication.PanoramaWidth; x++)
            {
                // The unrotated source layout is [east, west] (or [west, east] for the
                // realized afternoon swap). Shift it left by one quarter panorama so west
                // column zero lands at Engine u=.25 for the normal selection.
                int unrotatedX = (x + QuarterPanoramaWidth) % SkyMediaPublication.PanoramaWidth;
                int sourceX = unrotatedX % SkyFileDecoder.FrameWidth;
                bool eastHalf = unrotatedX < SkyFileDecoder.FrameWidth;
                byte[] source = eastHalf == eastWestSwapped ? west : east;
                int sourceOffset = ((sourceY * SkyFileDecoder.FrameWidth) + sourceX) * 4;
                int targetOffset = ((y * SkyMediaPublication.PanoramaWidth) + x) * 4;
                source.AsSpan(sourceOffset, 4).CopyTo(panorama.AsSpan(targetOffset, 4));
            }
        }

        return panorama;
    }
}

/// <summary>
/// Clears a panorama's painted horizon for a sky drawn over real distant ground. The classic paints its mountains,
/// forests and low cloud into the sky's lower third, and they would float above the real skyline: everything from
/// <see cref="FromRow"/> to the horizon becomes a fade from the sky above it down to the horizon's colour, stepped and
/// dithered like the palette art around it.
/// </summary>
public static class SkyHorizonClearing
{
    /// <summary>The panorama row above every painted horizon, about 34 degrees up.</summary>
    public const int FromRow = 160;
    private const int HorizonRow = SkyMediaPublication.PanoramaHeight / 2;
    /// <summary>The rows and columns either side averaged into the colour the fade starts from.</summary>
    private const int SourceRows = 6, SourceColumns = 24;
    /// <summary>Colour channels step by this, as the palette's ramps do.</summary>
    private const int ColourStep = 6;
    private static readonly int[] Dither = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    /// <summary>The panorama's mean colour at the horizon: what haze must fade distant ground into to meet the sky.</summary>
    public static IReadOnlyList<float> HorizonColor(byte[] panorama)
    {
        const int width = SkyMediaPublication.PanoramaWidth;
        float[] sum = new float[3];
        for (int x = 0; x < width; x++)
        for (int channel = 0; channel < 3; channel++)
            sum[channel] += panorama[(((HorizonRow * width) + x) * 4) + channel];
        return [.. sum.Select(channel => channel / (width * 255F))];
    }

    public static byte[] Clear(byte[] panorama)
    {
        const int width = SkyMediaPublication.PanoramaWidth;
        byte[] cleared = (byte[])panorama.Clone();
        float[] top = new float[3];
        for (int x = 0; x < width; x++)
        {
            Array.Clear(top);
            for (int row = FromRow - SourceRows; row < FromRow; row++)
            for (int column = x - SourceColumns; column <= x + SourceColumns; column++)
            for (int channel = 0; channel < 3; channel++)
                top[channel] += panorama[(((row * width) + ((column + width) % width)) * 4) + channel];
            for (int channel = 0; channel < 3; channel++) top[channel] /= SourceRows * ((SourceColumns * 2) + 1);

            for (int y = FromRow; y < HorizonRow; y++)
            {
                float share = (y - FromRow) / (float)(HorizonRow - FromRow);
                share = share * share * (3 - (2 * share));
                float threshold = (Dither[((y & 3) * 4) + (x & 3)] / 16F) - .5F;
                int at = ((y * width) + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    float value = top[channel] + ((panorama[(((HorizonRow * width) + x) * 4) + channel] - top[channel]) * share);
                    cleared[at + channel] = (byte)Math.Clamp(MathF.Round((value / ColourStep) + threshold) * ColourStep, 0, 255);
                }
            }
        }

        return cleared;
    }
}

/// <summary>Converts the donor's single night image, used for both hemispheres, into a 2:1 panorama.</summary>
internal static class SkyNightPanoramaEncoder
{
    private const int UpperHemisphereHeight = SkyMediaPublication.PanoramaHeight / 2;
    private const int QuarterPanoramaWidth = SkyMediaPublication.PanoramaWidth / 4;

    public static IReadOnlyList<float> ClearColor(ReadOnlySpan<byte> sourceRgba) =>
        [sourceRgba[0] / 255F, sourceRgba[1] / 255F, sourceRgba[2] / 255F];

    public static byte[] Encode(ReadOnlySpan<byte> sourceRgba) => SkyPanoramaEncoder.Png(Pixels(sourceRgba));

    public static byte[] Pixels(ReadOnlySpan<byte> sourceRgba)
    {
        int width = SkyMediaPublication.PanoramaWidth;
        int height = SkyMediaPublication.PanoramaHeight;
        byte[] panorama = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int sourceY = y < UpperHemisphereHeight
                ? Math.Min(SkyMediaPublication.NightHeight - 1,
                    (int)((long)y * SkyMediaPublication.NightHeight / UpperHemisphereHeight))
                : SkyMediaPublication.NightHeight - 1;
            for (int x = 0; x < width; x++)
            {
                int sourceX = ((x + QuarterPanoramaWidth) % width) % SkyMediaPublication.NightWidth;
                int sourceOffset = ((sourceY * SkyMediaPublication.NightWidth) + sourceX) * 4;
                int targetOffset = ((y * width) + x) * 4;
                sourceRgba.Slice(sourceOffset, 4).CopyTo(panorama.AsSpan(targetOffset, 4));
            }
        }

        return panorama;
    }
}
