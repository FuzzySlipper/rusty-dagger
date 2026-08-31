using System.Collections.ObjectModel;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// One explicit caller-supplied media source. Its bytes are copied while the
/// source set is built; this importer never opens an Arena2 directory.
/// </summary>
public sealed class Arena2DungeonMediaSource
{
    private readonly byte[] bytes;

    public Arena2DungeonMediaSource(string label, ReadOnlySpan<byte> bytes)
    {
        NormalizedImportDocument.RequireLogicalPath(label, nameof(label));
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("An Arena2 media source cannot be empty.", nameof(bytes));
        }

        Label = label;
        this.bytes = bytes.ToArray();
    }

    public string Label { get; }

    internal ReadOnlyMemory<byte> Bytes => bytes;
}

/// <summary>
/// Narrow source closure for one dungeon media publication. Only PAL.PAL and
/// TEXTURE.nnn leaves are admitted; the normalizer later proves that every
/// supplied texture archive is actually required by the normalized document.
/// </summary>
public sealed class Arena2DungeonMediaSourceSet
{
    private readonly Arena2DungeonMediaSource palette;
    private readonly IReadOnlyDictionary<ushort, Arena2DungeonMediaSource> textures;

    public Arena2DungeonMediaSourceSet(IEnumerable<Arena2DungeonMediaSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Dictionary<ushort, Arena2DungeonMediaSource> textureSources = [];
        Arena2DungeonMediaSource? paletteSource = null;
        foreach (Arena2DungeonMediaSource source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            string leaf = LeafName(source.Label);
            if (StringComparer.OrdinalIgnoreCase.Equals(leaf, "PAL.PAL"))
            {
                if (paletteSource is not null)
                {
                    throw new ArgumentException("The Arena2 media source set contains PAL.PAL more than once.", nameof(sources));
                }

                paletteSource = source;
                continue;
            }

            if (!TryParseTextureLeaf(leaf, out ushort archive))
            {
                throw new ArgumentException($"Arena2 dungeon media only accepts PAL.PAL or TEXTURE.nnn sources, not '{leaf}'.", nameof(sources));
            }

            if (!textureSources.TryAdd(archive, source))
            {
                throw new ArgumentException($"The Arena2 media source set contains TEXTURE.{archive:000} more than once.", nameof(sources));
            }
        }

        palette = paletteSource ?? throw new ArgumentException("Arena2 dungeon media requires PAL.PAL.", nameof(sources));
        textures = new ReadOnlyDictionary<ushort, Arena2DungeonMediaSource>(textureSources);
    }

    internal Arena2Palette DecodePalette() => PaletteDecoder.Decode(palette.Bytes.Span, palette.Label);

    internal TextureArchive RequireTexture(ushort archive)
    {
        Arena2DungeonMediaSource source = textures.TryGetValue(archive, out Arena2DungeonMediaSource? value)
            ? value
            : throw new InvalidOperationException($"Arena2 dungeon media requires TEXTURE.{archive:000}.");
        return TextureArchive.Parse(source.Bytes.Span, source.Label);
    }

    internal IReadOnlySet<ushort> TextureArchives => textures.Keys.ToHashSet();

    internal int SourceCount => textures.Count + 1;

    internal long SourceByteLength => checked(palette.Bytes.Length + textures.Values.Sum(source => (long)source.Bytes.Length));

    private static string LeafName(string label) => label[(label.LastIndexOf('/') + 1)..];

    private static bool TryParseTextureLeaf(string leaf, out ushort archive)
    {
        archive = 0;
        const string prefix = "TEXTURE.";
        if (!leaf.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || leaf.Length != prefix.Length + 3
            || !int.TryParse(leaf.AsSpan(prefix.Length), out int parsed)
            || parsed is < 0 or > 999)
        {
            return false;
        }

        archive = checked((ushort)parsed);
        return true;
    }
}

/// <summary>Explicit resource and output limits for one offline media pass.</summary>
public sealed record Arena2DungeonMediaQuotas(
    int MaximumSources,
    long MaximumSourceBytes,
    int MaximumArtifacts,
    int MaximumFramesPerAtlas,
    int MaximumAtlasDimension,
    long MaximumArtifactBytes)
{
    public static Arena2DungeonMediaQuotas Default { get; } = new(
        MaximumSources: 256,
        MaximumSourceBytes: 256L * 1024L * 1024L,
        MaximumArtifacts: 100_000,
        MaximumFramesPerAtlas: 16_384,
        MaximumAtlasDimension: 4096,
        MaximumArtifactBytes: 16L * 1024L * 1024L);

    public void Validate()
    {
        if (MaximumSources <= 0 || MaximumSourceBytes <= 0 || MaximumArtifacts <= 0
            || MaximumFramesPerAtlas <= 0 || MaximumAtlasDimension <= 0 || MaximumArtifactBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSources), "Arena2 dungeon media quotas must all be positive.");
        }
    }
}

/// <summary>Pure request for normalized dungeon media generation.</summary>
public sealed record Arena2DungeonMediaRequest(
    NormalizedImportDocument Dungeon,
    Arena2DungeonMediaSourceSet Sources,
    Arena2DungeonMediaQuotas Quotas)
{
    /// <summary>
    /// Narrow authored tuning may name generated sprite artifact IDs only.
    /// Layout, bytes, dimensions, and digests are always regenerated.
    /// </summary>
    public IReadOnlyList<AuthoredMediaOverlay> AuthoredOverlays { get; init; } = [];

    /// <summary>
    /// Typed, discoverable display defaults. Rulesets or authored overlays may
    /// replace these values without changing Arena2 layout interpretation.
    /// </summary>
    public DungeonMediaDisplayProfile DisplayProfile { get; init; } = DungeonMediaDisplayProfile.DaggerfallDefault;

    public static Arena2DungeonMediaRequest Create(NormalizedImportDocument dungeon, Arena2DungeonMediaSourceSet sources) =>
        new(dungeon, sources, Arena2DungeonMediaQuotas.Default);

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Dungeon);
        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(Quotas);
        Dungeon.Validate();
        Quotas.Validate();
        ArgumentNullException.ThrowIfNull(AuthoredOverlays);
        ArgumentNullException.ThrowIfNull(DisplayProfile);
        DisplayProfile.Validate();
    }
}

/// <summary>
/// Adjustments to generated visual presentation, separated from Arena2
/// archive layout and source-coordinate conversion. An explicit media overlay
/// wins over these defaults for its one generated descriptor.
/// </summary>
public sealed record DungeonMediaDisplayProfile(
    NormalizedVector2 BillboardPivot,
    NormalizedVector2 ActorPivot,
    NormalizedVector2 CorpsePivot,
    float BillboardFramesPerSecond,
    float MoveFramesPerSecond,
    float IdleFramesPerSecond,
    float PrimaryAttackFramesPerSecond,
    float HurtFramesPerSecond,
    float BillboardWorldScale,
    float ActorWorldScale,
    float CorpseWorldScale)
{
    public static DungeonMediaDisplayProfile DaggerfallDefault { get; } = new(
        BillboardPivot: new(0.5F, 0.5F),
        ActorPivot: new(0.5F, 0F),
        CorpsePivot: new(0.5F, 0F),
        BillboardFramesPerSecond: 5F,
        MoveFramesPerSecond: 6F,
        IdleFramesPerSecond: 4F,
        PrimaryAttackFramesPerSecond: 10F,
        HurtFramesPerSecond: 4F,
        BillboardWorldScale: 1F,
        ActorWorldScale: 1F,
        CorpseWorldScale: 1F);

    public void Validate()
    {
        BillboardPivot.Validate(nameof(BillboardPivot));
        ActorPivot.Validate(nameof(ActorPivot));
        CorpsePivot.Validate(nameof(CorpsePivot));
        ValidatePositive(BillboardFramesPerSecond, nameof(BillboardFramesPerSecond));
        ValidatePositive(MoveFramesPerSecond, nameof(MoveFramesPerSecond));
        ValidatePositive(IdleFramesPerSecond, nameof(IdleFramesPerSecond));
        ValidatePositive(PrimaryAttackFramesPerSecond, nameof(PrimaryAttackFramesPerSecond));
        ValidatePositive(HurtFramesPerSecond, nameof(HurtFramesPerSecond));
        ValidatePositive(BillboardWorldScale, nameof(BillboardWorldScale));
        ValidatePositive(ActorWorldScale, nameof(ActorWorldScale));
        ValidatePositive(CorpseWorldScale, nameof(CorpseWorldScale));
    }

    public float FramesPerSecondFor(DungeonActorSpriteState state) => state switch
    {
        DungeonActorSpriteState.Move => MoveFramesPerSecond,
        DungeonActorSpriteState.Idle or DungeonActorSpriteState.RatIdle => IdleFramesPerSecond,
        DungeonActorSpriteState.PrimaryAttack => PrimaryAttackFramesPerSecond,
        DungeonActorSpriteState.Hurt => HurtFramesPerSecond,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static void ValidatePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0F)
        {
            throw new ArgumentOutOfRangeException(name, "Dungeon media display values must be finite and positive.");
        }
    }
}

/// <summary>Source-backed, non-playing frame rate and looping fact.</summary>
public sealed record DungeonSpritePlaybackSource(float FramesPerSecond, bool Loops)
{
    public void Validate()
    {
        if (!float.IsFinite(FramesPerSecond) || FramesPerSecond <= 0F)
        {
            throw new ArgumentOutOfRangeException(nameof(FramesPerSecond), "A source frame rate must be finite and positive.");
        }
    }
}

/// <summary>One regenerated layout entry, retaining just enough source provenance to map semantic states.</summary>
public sealed record DungeonMediaFrameLayout(
    int AtlasFrameIndex,
    int SourceRecord,
    int SourceFrame,
    int Orientation,
    bool Mirrored,
    NormalizedAtlasFrame AtlasFrame,
    NormalizedVector2 SourceWorldSize)
{
    public void Validate()
    {
        if (AtlasFrameIndex < 0 || SourceRecord < 0 || SourceFrame < 0 || Orientation is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(AtlasFrameIndex), "Frame layout indexes must be non-negative and orientations must be eight-sector values.");
        }

        if (AtlasFrame.FrameIndex != AtlasFrameIndex)
        {
            throw new InvalidOperationException("A media frame layout must identify its regenerated atlas frame.");
        }

        SourceWorldSize.Validate(nameof(SourceWorldSize));
        if (SourceWorldSize.X <= 0F || SourceWorldSize.Y <= 0F)
        {
            throw new ArgumentOutOfRangeException(nameof(SourceWorldSize), "A media frame world size must be positive.");
        }
    }
}

/// <summary>Named source groups for a directional mobile atlas. This is metadata, not playback policy.</summary>
public enum DungeonActorSpriteState
{
    Move,
    Idle,
    RatIdle,
    PrimaryAttack,
    Hurt,
}

/// <summary>One state-major range within a selected actor atlas.</summary>
public sealed record DungeonActorSpriteStateLayout(
    DungeonActorSpriteState State,
    DungeonSpritePlaybackSource SourcePlayback,
    DungeonSpritePlaybackSource Playback,
    int FrameStart,
    int FramesPerOrientation,
    IReadOnlyList<DungeonMediaFrameLayout> Frames)
{
    public void Validate()
    {
        if (!Enum.IsDefined(State))
        {
            throw new ArgumentOutOfRangeException(nameof(State));
        }

        ArgumentNullException.ThrowIfNull(SourcePlayback);
        SourcePlayback.Validate();
        ArgumentNullException.ThrowIfNull(Playback);
        Playback.Validate();
        if (FrameStart < 0 || FramesPerOrientation <= 0 || Frames is null || Frames.Count != checked(8 * FramesPerOrientation))
        {
            throw new ArgumentException("A source state must contain one equal frame range for each of eight orientations.", nameof(Frames));
        }

        foreach (DungeonMediaFrameLayout frame in Frames)
        {
            frame.Validate();
        }
    }
}

/// <summary>One source attack alternative. The sequence retains -1 damage-beat markers without evaluating them.</summary>
public sealed record DungeonActorAttackAlternateSource(byte Chance, IReadOnlyList<sbyte> Frames)
{
    public void Validate()
    {
        if (Chance == 0 || Frames is null || Frames.Count == 0 || Frames.Any(frame => frame < -1))
        {
            throw new ArgumentOutOfRangeException(nameof(Frames), "Source attack alternatives require a positive chance and source frame or damage-beat values.");
        }
    }
}

/// <summary>Offline source sequence facts only; consumers decide any attack semantics later.</summary>
public sealed record DungeonActorAttackSequenceSource(IReadOnlyList<sbyte> PrimaryFrames, IReadOnlyList<DungeonActorAttackAlternateSource> Alternates)
{
    public void Validate()
    {
        if (PrimaryFrames is null || PrimaryFrames.Count == 0 || PrimaryFrames.Any(frame => frame < -1) || Alternates is null)
        {
            throw new ArgumentOutOfRangeException(nameof(PrimaryFrames), "Source attack sequences require frame or damage-beat values.");
        }

        foreach (DungeonActorAttackAlternateSource alternate in Alternates)
        {
            alternate.Validate();
        }
    }
}

/// <summary>One content-addressed dungeon material texture.</summary>
public sealed record DungeonMaterialTextureMedia(
    string TextureResourceId,
    string MaterialResourceId,
    ushort TextureArchive,
    ushort TextureRecord,
    int Width,
    int Height,
    ImportPublicationArtifact Artifact,
    NormalizedMediaDescriptor Descriptor)
{
    /// <summary>
    /// Compatibility construction still derives a canonical descriptor from
    /// the exact generated artifact; callers cannot supply stale hash or
    /// layout facts.
    /// </summary>
    public DungeonMaterialTextureMedia(
        string textureResourceId,
        string materialResourceId,
        ushort textureArchive,
        ushort textureRecord,
        int width,
        int height,
        ImportPublicationArtifact artifact)
        : this(
            textureResourceId,
            materialResourceId,
            textureArchive,
            textureRecord,
            width,
            height,
            artifact,
            new NormalizedMediaDescriptor(
                $"material/texture-{textureArchive}-{textureRecord}",
                NormalizedMediaKind.Texture,
                artifact.RelativePath,
                artifact.ContentHash,
                artifact.Bytes.Length,
                "image/png",
                width,
                height,
                0,
                0,
                [],
                null,
                null,
                null,
                null,
                null,
                null))
    {
        ArgumentNullException.ThrowIfNull(artifact);
    }
}

/// <summary>One visible world billboard and its regenerated sprite atlas.</summary>
public sealed record DungeonBillboardSpriteMedia(
    string SpriteResourceId,
    ushort TextureArchive,
    ushort TextureRecord,
    NormalizedVector2 Pivot,
    NormalizedVector2 WorldSize,
    DungeonSpritePlaybackSource? SourcePlayback,
    DungeonSpritePlaybackSource? Playback,
    IReadOnlyList<DungeonMediaFrameLayout> Frames,
    ImportPublicationArtifact Artifact,
    NormalizedMediaDescriptor Descriptor);

/// <summary>One optional source-backed corpse sprite for a selected mobile.</summary>
public sealed record DungeonActorCorpseSpriteMedia(
    ushort TextureArchive,
    ushort TextureRecord,
    NormalizedVector2 Pivot,
    NormalizedVector2 WorldSize,
    NormalizedVector2 SourceWorldSize,
    DungeonMediaFrameLayout Frame,
    ImportPublicationArtifact Artifact,
    NormalizedMediaDescriptor Descriptor);

/// <summary>One selected normalized actor/mobile atlas, without runtime entity or animation ownership.</summary>
public sealed record DungeonActorSpriteMedia(
    string ActorResourceId,
    Arena2MobileId MobileId,
    string SourceName,
    DungeonActorSpriteState PreferredRestState,
    DungeonActorAttackSequenceSource SourceAttackSequence,
    string SpriteResourceId,
    NormalizedVector2 Pivot,
    NormalizedVector2 WorldSize,
    NormalizedVector2 SourceWorldSize,
    IReadOnlyList<DungeonActorSpriteStateLayout> States,
    DungeonActorCorpseSpriteMedia? Corpse,
    ImportPublicationArtifact Artifact,
    NormalizedMediaDescriptor Descriptor);

/// <summary>
/// Pure media closure for a normalized dungeon. Artifacts are ready for an
/// <see cref="ImportPublicationPlan"/> but this component owns no filesystem
/// publication and never emits raw Arena2 bytes or source paths.
/// </summary>
public sealed record Arena2DungeonMediaPublication(
    IReadOnlyList<ImportPublicationArtifact> Artifacts,
    IReadOnlyList<DungeonMaterialTextureMedia> MaterialTextures,
    NormalizedMediaManifest MediaManifest,
    IReadOnlyList<DungeonBillboardSpriteMedia> Billboards,
    IReadOnlyList<DungeonActorSpriteMedia> Actors)
{
    // Daggerfall Unity EnemyBasics and DaggerfallBillboard source facts. The
    // request profile exposes the display defaults; these facts stay local so
    // profile tuning cannot rewrite archive interpretation.
    private static readonly DungeonSpritePlaybackSource SourceBillboardPlayback = new(5F, true);
    private static readonly DungeonSpritePlaybackSource SourceMovePlayback = new(6F, true);
    private static readonly DungeonSpritePlaybackSource SourceIdlePlayback = new(4F, true);
    private static readonly DungeonSpritePlaybackSource SourcePrimaryAttackPlayback = new(10F, false);
    private static readonly DungeonSpritePlaybackSource SourceHurtPlayback = new(4F, false);

    /// <summary>
    /// Converts the source-selected visual facts of a normalized dungeon to
    /// deterministic PNG artifacts and typed media metadata. It does not
    /// create Engine resources, choose actor behavior, schedule playback, or
    /// inspect a filesystem.
    /// </summary>
    public static Arena2DungeonMediaPublication Create(Arena2DungeonMediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        EnforceSourceQuotas(request.Sources, request.Quotas);

        Arena2Palette palette = request.Sources.DecodePalette();
        Selection selection = Select(request.Dungeon);
        EnforceExactTextureClosure(request.Sources, selection.RequiredArchives);
        EnforceSpriteOnlyOverlays(request.AuthoredOverlays, selection.Materials);

        Dictionary<ushort, TextureArchive> archives = selection.RequiredArchives
            .OrderBy(value => value)
            .ToDictionary(archive => archive, request.Sources.RequireTexture);
        List<GeneratedMediaArtifact> generated = [];
        List<MaterialDraft> materialDrafts = BuildMaterials(selection.Materials, archives, palette, request.Quotas, generated);
        List<BillboardDraft> billboardDrafts = BuildBillboards(selection.Billboards, archives, palette, request.Quotas, generated);
        List<ActorDraft> actorDrafts = BuildActors(selection.Actors, archives, palette, request.Quotas, generated);

        NormalizedMediaManifest mediaManifest = MediaManifestNormalizer.Normalize(
            generated,
            request.AuthoredOverlays,
            request.Quotas.MaximumArtifactBytes);
        Dictionary<string, NormalizedMediaDescriptor> descriptors = mediaManifest.Resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
        List<DungeonMaterialTextureMedia> materials = materialDrafts
            .Select(draft => draft.Finish(descriptors[draft.Id]))
            .OrderBy(media => media.TextureResourceId, StringComparer.Ordinal)
            .ToList();
        List<DungeonBillboardSpriteMedia> billboards = billboardDrafts
            .Select(draft => draft.Finish(descriptors[draft.Id], request.DisplayProfile))
            .OrderBy(media => media.SpriteResourceId, StringComparer.Ordinal)
            .ToList();
        List<DungeonActorSpriteMedia> actors = actorDrafts
            .Select(draft => draft.Finish(descriptors, request.DisplayProfile))
            .OrderBy(media => media.ActorResourceId, StringComparer.Ordinal)
            .ToList();

        List<ImportPublicationArtifact> artifacts = [.. generated.Select(ToPublicationArtifact)];
        if (artifacts.Count > request.Quotas.MaximumArtifacts)
        {
            throw new InvalidOperationException($"Dungeon media produced {artifacts.Count} artifacts, above the configured quota {request.Quotas.MaximumArtifacts}.");
        }

        if (artifacts.Select(artifact => artifact.RelativePath).Distinct(StringComparer.Ordinal).Count() != artifacts.Count)
        {
            throw new InvalidOperationException("Dungeon media produced duplicate artifact paths.");
        }

        return new(
            artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray(),
            materials.OrderBy(media => media.TextureResourceId, StringComparer.Ordinal).ToArray(),
            mediaManifest,
            billboards,
            actors);
    }

    private static List<MaterialDraft> BuildMaterials(
        IReadOnlyList<MaterialSelection> selections,
        IReadOnlyDictionary<ushort, TextureArchive> archives,
        Arena2Palette palette,
        Arena2DungeonMediaQuotas quotas,
        List<GeneratedMediaArtifact> generated)
    {
        List<MaterialDraft> materials = new(selections.Count);
        foreach (MaterialSelection selection in selections)
        {
            TextureArchive archive = archives[selection.Archive];
            IndexedTextureFrame frame = archive.DecodeFrame(selection.Record, 0);
            byte[] rgba = ToRgba(frame, palette, PaletteAlphaMode.Opaque);
            byte[] png = DeterministicPngEncoder.EncodeRgba8(frame.Width, frame.Height, rgba);
            RequireArtifactSize(png, quotas, $"material '{selection.TextureResourceId}'");
            string id = $"material/texture-{selection.Archive}-{selection.Record}";
            GeneratedMediaArtifact generatedArtifact = new(
                id,
                NormalizedMediaKind.Texture,
                $"media/dungeon/materials/texture-{selection.Archive}-{selection.Record}.png",
                png,
                frame.Width,
                frame.Height,
                null,
                "image/png");
            generated.Add(generatedArtifact);
            ImportPublicationArtifact artifact = ToPublicationArtifact(generatedArtifact);
            materials.Add(new(
                id,
                selection.TextureResourceId,
                selection.MaterialResourceId,
                selection.Archive,
                selection.Record,
                frame.Width,
                frame.Height,
                artifact));
        }

        return materials;
    }

    private static List<BillboardDraft> BuildBillboards(
        IReadOnlyList<BillboardSelection> selections,
        IReadOnlyDictionary<ushort, TextureArchive> archives,
        Arena2Palette palette,
        Arena2DungeonMediaQuotas quotas,
        List<GeneratedMediaArtifact> generated)
    {
        List<BillboardDraft> drafts = new(selections.Count);
        foreach (BillboardSelection selection in selections)
        {
            TextureArchive archive = archives[selection.Archive];
            TextureRecordInfo info = archive.GetRecordInfo(selection.Record);
            int frameCount = info.FrameCount;
            EnforceFrameQuota(frameCount, quotas, selection.SpriteResourceId);
            List<DecodedSpriteFrame> frames = new(frameCount);
            for (int sourceFrame = 0; sourceFrame < frameCount; sourceFrame++)
            {
                IndexedTextureFrame decoded = archive.DecodeFrame(selection.Record, sourceFrame);
                frames.Add(DecodedSpriteFrame.FromPalette(
                    $"frame/billboard-{selection.Archive}-{selection.Record}/{sourceFrame}",
                    decoded.Width,
                    decoded.Height,
                    decoded.Pixels.Span,
                    palette,
                    PaletteAlphaMode.IndexZeroTransparent));
            }

            NormalizedSpriteAtlas atlas = SpriteAtlasNormalizer.Normalize(frames, SpriteAtlasOptions.Strip(quotas.MaximumAtlasDimension));
            RequireArtifactSize(atlas.PngBytes, quotas, $"billboard '{selection.SpriteResourceId}'");
            GeneratedMediaArtifact artifact = GeneratedMediaArtifact.FromAtlas(
                selection.SpriteResourceId,
                NormalizedMediaKind.Billboard,
                $"media/dungeon/billboards/texture-{selection.Archive}-{selection.Record}.png",
                atlas);
            generated.Add(artifact);
            Arena2RecordWorldSize sourceWorldSize = MobileSourceMetadata.GetRecordWorldSize(info.Width, info.Height, info.ScaleX, info.ScaleY);
            DungeonMediaFrameLayout[] layouts = atlas.Frames.Select((frame, index) => new DungeonMediaFrameLayout(
                index,
                selection.Record,
                index,
                0,
                false,
                frame,
                ScaleWorldSize(sourceWorldSize, frame))).ToArray();
            drafts.Add(new(
                selection.SpriteResourceId,
                selection.Archive,
                selection.Record,
                new(sourceWorldSize.WidthMeters, sourceWorldSize.HeightMeters),
                frameCount > 1 ? SourceBillboardPlayback : null,
                layouts,
                ToPublicationArtifact(artifact)));
        }

        return drafts;
    }

    private static List<ActorDraft> BuildActors(
        IReadOnlyList<ActorSelection> selections,
        IReadOnlyDictionary<ushort, TextureArchive> archives,
        Arena2Palette palette,
        Arena2DungeonMediaQuotas quotas,
        List<GeneratedMediaArtifact> generated)
    {
        List<ActorDraft> drafts = new(selections.Count);
        foreach (ActorSelection selection in selections)
        {
            TextureArchive archive = archives[selection.Source.TextureArchive.Value];
            List<ActorFrameDraft> sourceFrames = [];
            List<StateDraft> states = [];
            foreach ((DungeonActorSpriteState state, Arena2MobileFrameGroup group, DungeonSpritePlaybackSource sourcePlayback) in ActorStates(selection.Source))
            {
                IReadOnlyList<Arena2MobileFrameRecord> orientations = MobileSourceMetadata.GetFrameRecords(group);
                if (!HasAllSourceRecords(archive, orientations))
                {
                    if (state is DungeonActorSpriteState.Idle or DungeonActorSpriteState.RatIdle)
                    {
                        continue;
                    }

                    throw new InvalidOperationException($"Actor '{selection.ActorResourceId}' is missing required {state} source records.");
                }

                int frameStart = sourceFrames.Count;
                int? framesPerOrientation = null;
                for (int orientation = 0; orientation < orientations.Count; orientation++)
                {
                    Arena2MobileFrameRecord record = orientations[orientation];
                    TextureRecordInfo info = archive.GetRecordInfo(record.Record);
                    int frameCount = info.FrameCount;
                    EnforceFrameQuota(frameCount, quotas, selection.ActorResourceId);
                    if (framesPerOrientation is null)
                    {
                        framesPerOrientation = frameCount;
                    }
                    else if (framesPerOrientation != frameCount)
                    {
                        throw new InvalidOperationException($"Actor '{selection.ActorResourceId}' has non-uniform source frame counts for {state}.");
                    }

                    Arena2RecordWorldSize size = MobileSourceMetadata.GetRecordWorldSize(info.Width, info.Height, info.ScaleX, info.ScaleY);
                    for (int sourceFrame = 0; sourceFrame < frameCount; sourceFrame++)
                    {
                        IndexedTextureFrame decoded = archive.DecodeFrame(record.Record, sourceFrame);
                        sourceFrames.Add(new(
                            state,
                            orientation,
                            record.Record,
                            sourceFrame,
                            size,
                            new DecodedSpriteFrame(
                                $"frame/mobile-{selection.Source.Id.Value}/{state}/{orientation}/{sourceFrame}",
                                decoded.Width,
                                decoded.Height,
                                ToRgba(decoded, palette, PaletteAlphaMode.IndexZeroTransparent),
                                record.IsHorizontallyMirrored)));
                    }
                }

                states.Add(new(
                    state,
                    sourcePlayback,
                    selection.Source.Animation.EffectiveFramesPerSecond(group),
                    frameStart,
                    framesPerOrientation ?? throw new InvalidOperationException("An actor state requires eight source orientations.")));
            }

            EnforceFrameQuota(sourceFrames.Count, quotas, selection.ActorResourceId);
            NormalizedSpriteAtlas atlas = SpriteAtlasNormalizer.Normalize(
                sourceFrames.Select(frame => frame.Decoded).ToArray(),
                SpriteAtlasOptions.Grid(quotas.MaximumAtlasDimension, cropTransparentPixels: true, bottomAlign: true));
            RequireArtifactSize(atlas.PngBytes, quotas, $"actor '{selection.ActorResourceId}'");
            string spriteResourceId = $"sprite/mobile-{selection.Source.Id.Value}";
            GeneratedMediaArtifact artifact = GeneratedMediaArtifact.FromAtlas(
                spriteResourceId,
                NormalizedMediaKind.EnemySprite,
                $"media/dungeon/actors/mobile-{selection.Source.Id.Value}.png",
                atlas);
            generated.Add(artifact);
            DungeonMediaFrameLayout[] layouts = sourceFrames.Select((frame, index) => new DungeonMediaFrameLayout(
                index,
                frame.SourceRecord,
                frame.SourceFrame,
                frame.Orientation,
                frame.Decoded.MirrorHorizontally,
                atlas.Frames[index],
                ScaleWorldSize(frame.WorldSize, atlas.Frames[index]))).ToArray();
            ActorStateDraft[] stateLayouts = states.Select(state => new ActorStateDraft(
                new DungeonActorSpriteStateLayout(
                    state.State,
                    state.SourcePlayback,
                    state.SourcePlayback,
                    state.FrameStart,
                    state.FramesPerOrientation,
                    layouts.Skip(state.FrameStart).Take(checked(8 * state.FramesPerOrientation)).ToArray()),
                state.SourceEffectiveFramesPerSecond)).ToArray();
            CorpseDraft? corpse = BuildCorpse(selection.Source, archives, palette, quotas, generated);
            drafts.Add(new(
                selection.ActorResourceId,
                selection.Source,
                ToSpriteState(selection.Source.Animation.PreferredRestGroup),
                ToAttackSequenceSource(selection.Source.AttackSequence),
                spriteResourceId,
                MedianSourceWorldSize(layouts),
                stateLayouts,
                corpse,
                ToPublicationArtifact(artifact)));
        }

        return drafts;
    }

    private static CorpseDraft? BuildCorpse(
        Arena2MobileSource source,
        IReadOnlyDictionary<ushort, TextureArchive> archives,
        Arena2Palette palette,
        Arena2DungeonMediaQuotas quotas,
        List<GeneratedMediaArtifact> generated)
    {
        if (source.Corpse is not Arena2MobileCorpseSource corpseSource)
        {
            return null;
        }

        TextureArchive archive = archives[corpseSource.TextureArchive.Value];
        TextureRecordInfo info = archive.GetRecordInfo(corpseSource.Record);
        IndexedTextureFrame decoded = archive.DecodeFrame(corpseSource.Record, 0);
        DecodedSpriteFrame frame = new(
            $"frame/mobile-{source.Id.Value}/corpse/0/0",
            decoded.Width,
            decoded.Height,
            ToRgba(decoded, palette, PaletteAlphaMode.IndexZeroTransparent));
        NormalizedSpriteAtlas atlas = SpriteAtlasNormalizer.Normalize(
            [frame],
            SpriteAtlasOptions.Grid(quotas.MaximumAtlasDimension, cropTransparentPixels: true, bottomAlign: true));
        RequireArtifactSize(atlas.PngBytes, quotas, $"corpse '{source.Id.Value}'");
        string resourceId = $"sprite/mobile-{source.Id.Value}/corpse";
        GeneratedMediaArtifact artifact = GeneratedMediaArtifact.FromAtlas(
            resourceId,
            NormalizedMediaKind.EnemySprite,
            $"media/dungeon/actors/mobile-{source.Id.Value}-corpse.png",
            atlas);
        generated.Add(artifact);
        Arena2RecordWorldSize size = MobileSourceMetadata.GetRecordWorldSize(info.Width, info.Height, info.ScaleX, info.ScaleY);
        DungeonMediaFrameLayout layout = new(
            0,
            corpseSource.Record,
            0,
            0,
            false,
            atlas.Frames[0],
            ScaleWorldSize(size, atlas.Frames[0]));
        return new(
            resourceId,
            corpseSource.TextureArchive.Value,
            corpseSource.Record,
            new(size.WidthMeters, size.HeightMeters),
            layout,
            ToPublicationArtifact(artifact));
    }

    private static IEnumerable<(DungeonActorSpriteState State, Arena2MobileFrameGroup Group, DungeonSpritePlaybackSource Playback)> ActorStates(Arena2MobileSource source)
    {
        yield return (DungeonActorSpriteState.Move, Arena2MobileFrameGroup.Move, SourceMovePlayback);
        if (source.Animation.PreferredRestGroup is Arena2MobileFrameGroup.Idle or Arena2MobileFrameGroup.RatIdle)
        {
            yield return (ToSpriteState(source.Animation.PreferredRestGroup), source.Animation.PreferredRestGroup, SourceIdlePlayback);
        }

        yield return (DungeonActorSpriteState.PrimaryAttack, Arena2MobileFrameGroup.PrimaryAttack, SourcePrimaryAttackPlayback);
        yield return (DungeonActorSpriteState.Hurt, Arena2MobileFrameGroup.Hurt, SourceHurtPlayback);
    }

    private static bool HasAllSourceRecords(TextureArchive archive, IReadOnlyList<Arena2MobileFrameRecord> records) =>
        records.All(record => record.Record < archive.RecordCount);

    private static byte[] ToRgba(IndexedTextureFrame frame, Arena2Palette palette, PaletteAlphaMode alphaMode)
    {
        Rgba32[] colors = palette.ToRgba(frame.Pixels.Span, alphaMode);
        byte[] rgba = new byte[checked(colors.Length * 4)];
        for (int index = 0; index < colors.Length; index++)
        {
            int offset = index * 4;
            rgba[offset] = colors[index].Red;
            rgba[offset + 1] = colors[index].Green;
            rgba[offset + 2] = colors[index].Blue;
            rgba[offset + 3] = colors[index].Alpha;
        }

        return rgba;
    }

    private static NormalizedVector2 ScaleWorldSize(Arena2RecordWorldSize source, NormalizedAtlasFrame frame) => new(
        source.WidthMeters * frame.Width / frame.SourceWidth,
        source.HeightMeters * frame.Height / frame.SourceHeight);

    private static NormalizedVector2 MedianSourceWorldSize(IReadOnlyList<DungeonMediaFrameLayout> frames)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("An actor atlas requires source frames.", nameof(frames));
        }

        float[] widths = frames.Select(frame => frame.SourceWorldSize.X).OrderBy(value => value).ToArray();
        float[] heights = frames.Select(frame => frame.SourceWorldSize.Y).OrderBy(value => value).ToArray();
        int middle = widths.Length / 2;
        return new(widths[middle], heights[middle]);
    }

    private static DungeonActorAttackSequenceSource ToAttackSequenceSource(Arena2MobileAttackSequence source)
    {
        ArgumentNullException.ThrowIfNull(source);
        DungeonActorAttackAlternateSource[] alternates = source.Alternates
            .Select(alternate => new DungeonActorAttackAlternateSource(alternate.Chance, alternate.Frames.ToArray()))
            .ToArray();
        DungeonActorAttackSequenceSource result = new(source.PrimaryFrames.ToArray(), alternates);
        result.Validate();
        return result;
    }

    private static DungeonActorSpriteState ToSpriteState(Arena2MobileFrameGroup group) => group switch
    {
        Arena2MobileFrameGroup.Move => DungeonActorSpriteState.Move,
        Arena2MobileFrameGroup.Idle => DungeonActorSpriteState.Idle,
        Arena2MobileFrameGroup.RatIdle => DungeonActorSpriteState.RatIdle,
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "A preferred mobile rest group must map to a playable sprite state."),
    };

    private static DungeonSpritePlaybackSource ResolvePlayback(
        DungeonSpritePlaybackSource source,
        float? sourceEffectiveFramesPerSecond,
        NormalizedMediaDescriptor descriptor,
        float profileFramesPerSecond) => new(
            descriptor.FramesPerSecond ?? sourceEffectiveFramesPerSecond ?? profileFramesPerSecond,
            descriptor.Loop ?? source.Loops);

    private static NormalizedVector2 ResolveWorldSize(
        NormalizedVector2 source,
        NormalizedMediaDescriptor descriptor,
        float profileScale) => descriptor.DisplaySize ?? new(source.X * profileScale, source.Y * profileScale);

    private static ImportPublicationArtifact ToPublicationArtifact(GeneratedMediaArtifact artifact) =>
        new(artifact.RelativePath, artifact.Bytes);

    private static void RequireArtifactSize(byte[] bytes, Arena2DungeonMediaQuotas quotas, string subject)
    {
        if (bytes.LongLength > quotas.MaximumArtifactBytes)
        {
            throw new InvalidOperationException($"Dungeon media {subject} exceeds the encoded artifact byte quota.");
        }
    }

    private static void EnforceFrameQuota(int frameCount, Arena2DungeonMediaQuotas quotas, string subject)
    {
        if (frameCount <= 0 || frameCount > quotas.MaximumFramesPerAtlas)
        {
            throw new InvalidOperationException($"Dungeon media '{subject}' has {frameCount} frames outside the configured atlas quota.");
        }
    }

    private static void EnforceSourceQuotas(Arena2DungeonMediaSourceSet sources, Arena2DungeonMediaQuotas quotas)
    {
        if (sources.SourceCount > quotas.MaximumSources || sources.SourceByteLength > quotas.MaximumSourceBytes)
        {
            throw new InvalidOperationException("Arena2 dungeon media source closure exceeds the configured quota.");
        }
    }

    private static void EnforceExactTextureClosure(Arena2DungeonMediaSourceSet sources, IReadOnlySet<ushort> requiredArchives)
    {
        IReadOnlySet<ushort> supplied = sources.TextureArchives;
        if (!supplied.SetEquals(requiredArchives))
        {
            string missing = string.Join(", ", requiredArchives.Except(supplied).OrderBy(value => value).Select(value => $"TEXTURE.{value:000}"));
            string unneeded = string.Join(", ", supplied.Except(requiredArchives).OrderBy(value => value).Select(value => $"TEXTURE.{value:000}"));
            throw new InvalidOperationException($"Arena2 dungeon media texture closure does not match normalized references. Missing: [{missing}]. Unneeded: [{unneeded}].");
        }
    }

    private static void EnforceSpriteOnlyOverlays(
        IEnumerable<AuthoredMediaOverlay> overlays,
        IReadOnlyList<MaterialSelection> materials)
    {
        HashSet<string> materialIds = materials
            .Select(material => $"material/texture-{material.Archive}-{material.Record}")
            .ToHashSet(StringComparer.Ordinal);
        foreach (AuthoredMediaOverlay overlay in overlays)
        {
            if (materialIds.Contains(overlay.Id))
            {
                throw new InvalidOperationException("Dungeon material texture metadata is regenerated and cannot accept a sprite overlay.");
            }
        }
    }

    private static Selection Select(NormalizedImportDocument document)
    {
        Dictionary<string, NormalizedResourceCatalogEntry> resources = document.Resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
        List<MaterialSelection> materials = [];
        foreach (string materialId in document.Meshes.SelectMany(mesh => mesh.MaterialGroups).Select(group => group.MaterialResourceId).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            NormalizedResourceCatalogEntry material = resources[materialId];
            string textureId = material.Dependencies.SingleOrDefault(dependency => resources[dependency].Kind == NormalizedResourceKind.Texture)
                ?? throw new InvalidOperationException($"Dungeon material '{materialId}' must depend on exactly one normalized texture resource.");
            if (material.Dependencies.Count(dependency => resources[dependency].Kind == NormalizedResourceKind.Texture) != 1)
            {
                throw new InvalidOperationException($"Dungeon material '{materialId}' must depend on exactly one normalized texture resource.");
            }

            (ushort archive, ushort record) = ParseTextureResourceId(textureId, "texture/");
            materials.Add(new(materialId, textureId, archive, record));
        }

        List<BillboardSelection> billboards = document.World.Billboards
            .Select(billboard => billboard.SpriteResourceId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(id =>
            {
                (ushort archive, ushort record) = ParseTextureResourceId(id, "sprite/texture-");
                return new BillboardSelection(id, archive, record);
            })
            .ToList();
        List<ActorSelection> actors = document.World.Actors
            .Select(actor => actor.ActorResourceId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(actorId =>
            {
                Arena2MobileId id = ParseMobileResourceId(actorId);
                if (!MobileSourceMetadata.TryGet(id, out Arena2MobileSource? source))
                {
                    throw new InvalidOperationException($"Normalized actor '{actorId}' has no supported Arena2 mobile source metadata.");
                }

                return new ActorSelection(actorId, source);
            })
            .ToList();
        HashSet<ushort> requiredArchives = materials.Select(material => material.Archive)
            .Concat(billboards.Select(billboard => billboard.Archive))
            .Concat(actors.Select(actor => actor.Source.TextureArchive.Value))
            .Concat(actors.Where(actor => actor.Source.Corpse is not null).Select(actor => actor.Source.Corpse!.Value.TextureArchive.Value))
            .ToHashSet();
        if (requiredArchives.Count == 0)
        {
            throw new InvalidOperationException("Normalized dungeon media has no material, visible billboard, or selected actor source references.");
        }

        return new(materials, billboards, actors, requiredArchives);
    }

    private static (ushort Archive, ushort Record) ParseTextureResourceId(string id, string prefix)
    {
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Normalized media resource '{id}' does not use the expected '{prefix}<archive>-<record>' source handle.");
        }

        string[] parts = id[prefix.Length..].Split('-');
        if (parts is not [string archiveText, string recordText]
            || !ushort.TryParse(archiveText, out ushort archive)
            || !ushort.TryParse(recordText, out ushort record))
        {
            throw new InvalidOperationException($"Normalized media resource '{id}' does not use the expected '{prefix}<archive>-<record>' source handle.");
        }

        return (archive, record);
    }

    private static Arena2MobileId ParseMobileResourceId(string actorResourceId)
    {
        const string prefix = "actor/mobile-";
        if (!actorResourceId.StartsWith(prefix, StringComparison.Ordinal)
            || !byte.TryParse(actorResourceId.AsSpan(prefix.Length), out byte id))
        {
            throw new InvalidOperationException($"Normalized actor resource '{actorResourceId}' does not use the expected '{prefix}<id>' source handle.");
        }

        return new(id);
    }

    private sealed record MaterialSelection(string MaterialResourceId, string TextureResourceId, ushort Archive, ushort Record);

    private sealed record BillboardSelection(string SpriteResourceId, ushort Archive, ushort Record);

    private sealed record ActorSelection(string ActorResourceId, Arena2MobileSource Source);

    private sealed record Selection(
        IReadOnlyList<MaterialSelection> Materials,
        IReadOnlyList<BillboardSelection> Billboards,
        IReadOnlyList<ActorSelection> Actors,
        IReadOnlySet<ushort> RequiredArchives);

    private sealed record ActorFrameDraft(
        DungeonActorSpriteState State,
        int Orientation,
        ushort SourceRecord,
        int SourceFrame,
        Arena2RecordWorldSize WorldSize,
        DecodedSpriteFrame Decoded);

    private sealed record StateDraft(
        DungeonActorSpriteState State,
        DungeonSpritePlaybackSource SourcePlayback,
        float? SourceEffectiveFramesPerSecond,
        int FrameStart,
        int FramesPerOrientation);

    private sealed record ActorStateDraft(DungeonActorSpriteStateLayout Layout, float? SourceEffectiveFramesPerSecond);

    private sealed record MaterialDraft(
        string Id,
        string TextureResourceId,
        string MaterialResourceId,
        ushort TextureArchive,
        ushort TextureRecord,
        int Width,
        int Height,
        ImportPublicationArtifact Artifact)
    {
        public DungeonMaterialTextureMedia Finish(NormalizedMediaDescriptor descriptor) => new(
            TextureResourceId,
            MaterialResourceId,
            TextureArchive,
            TextureRecord,
            Width,
            Height,
            Artifact,
            descriptor);
    }

    private sealed record BillboardDraft(
        string Id,
        ushort Archive,
        ushort Record,
        NormalizedVector2 SourceWorldSize,
        DungeonSpritePlaybackSource? SourcePlayback,
        IReadOnlyList<DungeonMediaFrameLayout> Frames,
        ImportPublicationArtifact Artifact)
    {
        public DungeonBillboardSpriteMedia Finish(NormalizedMediaDescriptor descriptor, DungeonMediaDisplayProfile profile) => new(
            Id,
            Archive,
            Record,
            descriptor.Pivot ?? profile.BillboardPivot,
            ResolveWorldSize(SourceWorldSize, descriptor, profile.BillboardWorldScale),
            SourcePlayback,
            SourcePlayback is null ? null : ResolvePlayback(SourcePlayback, null, descriptor, profile.BillboardFramesPerSecond),
            Frames,
            Artifact,
            descriptor);
    }

    private sealed record ActorDraft(
        string ActorResourceId,
        Arena2MobileSource Source,
        DungeonActorSpriteState PreferredRestState,
        DungeonActorAttackSequenceSource SourceAttackSequence,
        string SpriteResourceId,
        NormalizedVector2 SourceWorldSize,
        IReadOnlyList<ActorStateDraft> States,
        CorpseDraft? Corpse,
        ImportPublicationArtifact Artifact)
    {
        public DungeonActorSpriteMedia Finish(IReadOnlyDictionary<string, NormalizedMediaDescriptor> descriptors, DungeonMediaDisplayProfile profile)
        {
            NormalizedMediaDescriptor descriptor = descriptors[SpriteResourceId];
            DungeonActorCorpseSpriteMedia? corpse = Corpse is null
                ? null
                : new DungeonActorCorpseSpriteMedia(
                    Corpse.TextureArchive,
                    Corpse.TextureRecord,
                    descriptors[Corpse.SpriteResourceId].Pivot ?? profile.CorpsePivot,
                    ResolveWorldSize(Corpse.SourceWorldSize, descriptors[Corpse.SpriteResourceId], profile.CorpseWorldScale),
                    Corpse.SourceWorldSize,
                    Corpse.Frame,
                    Corpse.Artifact,
                    descriptors[Corpse.SpriteResourceId]);
            DungeonActorSpriteStateLayout[] states = States.Select(state => state.Layout with
            {
                Playback = ResolvePlayback(
                    state.Layout.SourcePlayback,
                    state.SourceEffectiveFramesPerSecond,
                    descriptor,
                    profile.FramesPerSecondFor(state.Layout.State)),
            }).ToArray();
            if (!states.Any(state => state.State == PreferredRestState))
            {
                throw new InvalidOperationException($"Actor '{ActorResourceId}' does not publish its preferred rest state '{PreferredRestState}'.");
            }

            return new(
                ActorResourceId,
                Source.Id,
                Source.SourceName,
                PreferredRestState,
                SourceAttackSequence,
                SpriteResourceId,
                descriptor.Pivot ?? profile.ActorPivot,
                ResolveWorldSize(SourceWorldSize, descriptor, profile.ActorWorldScale),
                SourceWorldSize,
                states,
                corpse,
                Artifact,
                descriptor);
        }
    }

    private sealed record CorpseDraft(
        string SpriteResourceId,
        ushort TextureArchive,
        ushort TextureRecord,
        NormalizedVector2 SourceWorldSize,
        DungeonMediaFrameLayout Frame,
        ImportPublicationArtifact Artifact);
}
