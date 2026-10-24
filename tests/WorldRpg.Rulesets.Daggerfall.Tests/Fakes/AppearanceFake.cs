using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal sealed class AppearanceFake(List<string> releases) : IGraphicsService
{

    public TextureResourceInfo ReadTextureInfo(RenderResource resource) => throw new NotSupportedException();
    public void PublishChanges(AppearanceChangesRequest request) => throw new NotSupportedException();
    public void PublishOverlaySet(OverlaySetRequest request) => OverlaySets.Add(request);
    /// <summary>Every overlay set published, in order; an empty one removes its set.</summary>
    internal List<OverlaySetRequest> OverlaySets { get; } = [];
    internal List<TerrainLayerMaterialRequest> TerrainLayerMaterialRequests { get; } = [];
    /// <summary>The content path each created static mesh's reference names, in order; null for a reference no content fake issued.</summary>
    internal List<string?> StaticMeshPaths { get; } = [];
    internal Dictionary<string, Appearance> StaticMeshByPath { get; } = new(StringComparer.Ordinal);
    internal List<MeshMaterialBinding> StaticMeshBindings { get; } = [];
    internal List<StaticMeshMaterialFactorsRequest> StaticMeshFactorUpdates { get; } = [];
    internal List<SpriteAtlasCreateRequest> AtlasRequests { get; } = [];
    internal List<SpriteFromAtlasRequest> SpriteRequests { get; } = [];
    internal List<SpritePlaybackCreateRequest> PlaybackRequests { get; } = [];
    internal List<SpritePlaybackControlRequest> ControlRequests { get; } = [];
    internal List<SpritePlaybackAdvanceRequest> AdvanceRequests { get; } = [];
    internal List<LightRequest> LightRequests { get; } = [];
    internal List<LightUpdateRequest> LightUpdates { get; } = [];
    internal List<AppearanceFact[]> Snapshots { get; } = [];
    internal List<SpriteFrameUpdateRequest> SetFrameRequests { get; } = [];
    internal List<SpritePlayback> CreatedPlaybacks { get; } = [];
    internal Queue<SpritePlaybackAdvanceResult> AdvanceReceipts { get; } = [];
    internal int CreatedAtlases { get; private set; }
    internal int DisposedAtlases { get; private set; }
    internal int CreatedAppearances { get; private set; }
    internal int DisposedAppearances { get; private set; }
    internal int DisposedPlaybacks { get; private set; }
    internal int DisposedLights { get; private set; }
    internal List<SpritePlaybackHandle> DisposedPlaybackHandles { get; } = [];
    internal int FailSpritePlaybackCreateAt { get; set; }
    internal int FailSpritePlaybackControlAt { get; set; }
    internal int FailSpriteAtlasCreateAt { get; set; }
    internal int FailPublishAt { get; set; }
    internal bool RejectLateResourceOpen { get; set; }
    internal bool RejectDisposeOfRetainedAppearance { get; set; }
    internal bool FailNextProjectionDispose { get; set; }
    private bool failDuringProjectionDispose;
    internal int PublishCalls { get; private set; }
    internal ulong LastCrossingSequence { get; private set; }
    internal IReadOnlyCollection<Appearance> RetainedAppearances => retainedAppearances;
    private readonly HashSet<Appearance> retainedAppearances = new(ReferenceEqualityComparer.Instance);
    private ulong nextHandle = 1;
    private readonly Dictionary<ulong, ulong> liveLightIds = [];

    // The ruleset opens published textures and meshes by content reference, wherever the Engine stages them.
    public RenderResourceInfo OpenResource(RenderResourceRequest request) =>
        throw new NotSupportedException($"The ruleset opens published textures by content reference, not by the path '{request.Path}'.");
    public RenderResourceInfo OpenResourceFromContent(RenderResourceContentRequest request)
    {
        if (RejectLateResourceOpen) throw new InvalidOperationException("Render resource selection is sealed after product creation.");
        OpenResourceContentRequests.Add(request);
        OpenedResourcePaths.Add(TestContentReferences.PathOf(request.Content));
        return new(OwnResource(checked((ulong)OpenResourceContentRequests.Count)), default, 0);
    }
    internal List<RenderResourceContentRequest> OpenResourceContentRequests { get; } = [];
    /// <summary>The content path each opened resource's reference names, in order; null for a reference no content fake issued.</summary>
    internal List<string?> OpenedResourcePaths { get; } = [];

    /// <summary>Counts how many opened render resources the caller released.</summary>
    internal int ReleasedResources { get; private set; }

    private RenderResource OwnResource(ulong handle) => new(new RenderResourceHandle(handle), () =>
    {
        ReleasedResources++;
        releases.Add("resource");
    });
    public Material CreateMaterial(MaterialRequest request) => new(new MaterialHandle(1), () => releases.Add("material"));
    public Material CreateTerrainLayerMaterial(TerrainLayerMaterialRequest request)
    {
        TerrainLayerMaterialRequests.Add(request);
        return new(new MaterialHandle(nextHandle++), () => releases.Add("material"));
    }
    public Material CreateAuthoredMaterial(AuthoredMaterialAppearanceRequest request) => CreateMaterial(default);
    internal List<MaterialUpdateRequest> MaterialUpdates { get; } = [];
    public void UpdateMaterial(MaterialUpdateRequest request) { MaterialUpdates.Add(request); }
    public Material ReplaceMaterial(MaterialUpdateRequest request) => CreateMaterial(request.Replacement);
    public Appearance CreatePrimitive(PrimitiveAppearanceRequest request) => CreateAppearance();
    public Appearance ReplacePrimitive(PrimitiveAppearanceReplaceRequest request) => CreateAppearance();
    public Appearance CreateStaticMesh(StaticMeshAppearanceRequest request) => CreateAppearance();
    public MeshResource CreateMeshResource(MeshResourceCreateRequest request) => new(
        new MeshResourceHandle(nextHandle++),
        () => releases.Add("mesh"));
    public Appearance CreateMeshAppearance(MeshResource resource) => CreateAppearance();
    public MeshPartition PartitionMesh(MeshPartitionRequest request) => throw new NotSupportedException();
    public MeshPartitionReadout ReadMeshPartition(MeshPartition partition) => throw new NotSupportedException();
    public MeshResource TakeMeshPartitionPart(MeshPartitionPartRequest request) => throw new NotSupportedException();
    public Appearance CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest request) =>
        throw new NotSupportedException($"The ruleset creates published meshes by content reference, not by the path '{request.Path}'.");
    public Appearance CreateStaticMeshFromContentReference(StaticMeshContentReferenceRequest request)
    {
        string? path = TestContentReferences.PathOf(request.Content);
        StaticMeshPaths.Add(path);
        Appearance value = CreateAppearance();
        if (path is not null) StaticMeshByPath[path] = value;
        return value;
    }
    public Appearance ReplaceStaticMesh(Appearance appearance, StaticMeshAppearanceRequest request) => CreateAppearance();
    public Appearance ReplaceStaticMeshFromContent(Appearance appearance, StaticMeshContentAppearanceRequest request) => CreateAppearance();
    public void UpdateStaticMeshMaterials(StaticMeshMaterialUpdateRequest request) => StaticMeshBindings.AddRange(request.Bindings.ToArray());
    public void UpdateStaticMeshMaterialFactors(StaticMeshMaterialFactorsRequest request) => StaticMeshFactorUpdates.Add(request);
    public Appearance CreateSprite(SpriteAppearanceRequest request) => CreateAppearance();
    public Appearance CreateSpriteBatch(SpriteBatchRequest request) { BatchRequests.Add(request); return CreateAppearance(); }
    internal List<SpriteBatchRequest> BatchRequests { get; } = [];
    public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest request) => CreateAppearance();
    public SpriteAtlas CreateSpriteAtlas(SpriteAtlasCreateRequest request)
    {
        AtlasRequests.Add(request);
        if (FailSpriteAtlasCreateAt == AtlasRequests.Count) throw new InvalidOperationException("Injected sprite atlas create failure.");
        CreatedAtlases++;
        return new(new SpriteAtlasHandle(nextHandle++), () => { DisposedAtlases++; releases.Add("atlas"); });
    }
    public Appearance CreateSpriteFromAtlas(SpriteFromAtlasRequest request) { SpriteRequests.Add(request); return CreateAppearance(); }
    public Appearance ReplaceSpriteFromAtlas(SpriteFromAtlasReplaceRequest request) => CreateAppearance();
    public void SetSpriteViewport(SpriteViewportUpdateRequest request) => ViewportRequests.Add(request);
    internal List<SpriteViewportUpdateRequest> ViewportRequests { get; } = [];
    public void SetSpriteFrame(SpriteFrameUpdateRequest request) => SetFrameRequests.Add(request);
    public SpriteReadout ReadSprite(Appearance appearance) => default;
    public SpritePlayback CreateSpritePlayback(SpritePlaybackCreateRequest request)
    {
        PlaybackRequests.Add(request);
        if (FailSpritePlaybackCreateAt == PlaybackRequests.Count) throw new InvalidOperationException("Injected sprite playback create failure.");
        SpritePlaybackHandle handle = new(nextHandle++);
        SpritePlayback playback = new(handle, () =>
        {
            DisposedPlaybacks++;
            DisposedPlaybackHandles.Add(handle);
            releases.Add("playback");
        });
        CreatedPlaybacks.Add(playback);
        return playback;
    }
    public SpritePlaybackReadout ControlSpritePlayback(SpritePlaybackControlRequest request)
    {
        ControlRequests.Add(request);
        if (FailSpritePlaybackControlAt == ControlRequests.Count) throw new InvalidOperationException("Injected sprite playback control failure.");
        return default;
    }
    public SpritePlaybackReadout SelectSpritePlaybackFrame(SpritePlaybackFrameSelectionRequest request) => default;
    /// <summary>When set, every advanced playback reports this receipt, so a crossing reaches whichever actor is attacking.</summary>
    internal SpritePlaybackAdvanceResult? AdvanceReceiptForAll { get; set; }
    public SpritePlaybackAdvanceResult AdvanceSpritePlayback(SpritePlaybackAdvanceRequest request)
    {
        AdvanceRequests.Add(request);
        SpritePlaybackAdvanceResult receipt = AdvanceReceiptForAll ?? (AdvanceReceipts.Count == 0 ? default : AdvanceReceipts.Dequeue());
        foreach (SpritePlaybackMarkerCrossing crossing in receipt.Crossings.Span) LastCrossingSequence = Math.Max(LastCrossingSequence, crossing.CrossingSequence);
        return receipt;
    }
    public SpritePlaybackSample SampleSpritePlayback(SpritePlaybackSampleRequest request) => default;
    public SpritePlaybackReadout ReadSpritePlayback(SpritePlayback playback) => default;
    public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values)
    {
        PublishCalls++;
        if (values.IsEmpty && FailNextProjectionDispose)
        {
            FailNextProjectionDispose = false;
            failDuringProjectionDispose = true;
        }
        Snapshots.Add(values.ToArray());
        retainedAppearances.Clear();
        foreach (AppearanceFact value in values) retainedAppearances.Add(value.Appearance);
        if (FailPublishAt == PublishCalls) throw new InvalidOperationException("Injected presentation publish failure.");
    }
    public Light CreateLight(LightRequest request) => NewLight(request);
    public void UpdateLight(LightUpdateRequest request) => LightUpdates.Add(request);
    public Light ReplaceLight(LightUpdateRequest request)
    {
        liveLightIds.Remove(request.Light.Handle.Value);
        return NewLight(request.Replacement);
    }
    public LightReadout ReadLight(Light light) => default;
    public PresentationReadout ReadPresentation() => default;
    public LandmarkReadResult ReadLandmarks(LandmarkReadRequest request) => throw new NotSupportedException();

    private Appearance CreateAppearance()
    {
        CreatedAppearances++;
        Appearance value = null!;
        value = new(new AppearanceHandle(nextHandle++), () =>
        {
            if (failDuringProjectionDispose)
            {
                failDuringProjectionDispose = false;
                throw new InvalidOperationException("Injected appearance dispose failure.");
            }
            if (RejectDisposeOfRetainedAppearance && retainedAppearances.Contains(value)) throw new InvalidOperationException("CSHARP_APPEARANCE_IN_USE");
            DisposedAppearances++;
            releases.Add("appearance");
        });
        return value;
    }
    // Engine refuses a logical light ID that another live light owns, as CSHARP_LIGHT_LOGICAL_ID.
    private Light NewLight(LightRequest request)
    {
        if (liveLightIds.ContainsValue(request.LogicalId))
            throw new InvalidOperationException($"CSHARP_LIGHT_LOGICAL_ID: logical light id {request.LogicalId} is already owned by a live light");
        LightRequests.Add(request);
        ulong handle = nextHandle++;
        liveLightIds.Add(handle, request.LogicalId);
        return new(new LightHandle(handle), () => { liveLightIds.Remove(handle); DisposedLights++; releases.Add("light"); });
    }
}
