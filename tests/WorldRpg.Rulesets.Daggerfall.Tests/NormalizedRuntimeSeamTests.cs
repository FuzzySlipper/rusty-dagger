using System.Numerics;
using System.Reflection;
using System.Text;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NormalizedRuntimeSeamTests
{
    private static readonly ContentSha256 Hash = new(1, 2, 3, 4);

    [Fact]
    public void Spatial_system_admits_one_content_artifact_reads_it_back_and_releases_session_before_reference()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning))
        {
            Assert.Equal(1, spatial.ReplaceCalls);
            Assert.Equal(1, spatial.ReadCalls);
            Assert.Equal((ulong)7, spatial.LastRequest!.Value.NavigationGridId);
            Assert.Equal((ulong)1, spatial.LastRequest.Value.Content.Handle.Value);
        }

        Assert.Equal(["session", "content"], releases);
    }

    [Fact]
    public void Appearance_uses_normalized_material_slots_and_atlases_then_releases_dependents_in_order()
    {
        List<string> releases = [];
        ContentFake content = new("mesh/hold.json", Hash, releases);
        content.Add("texture/wall.png", Hash);
        content.Add("sprite/rat.png", Hash);
        AppearanceFake appearance = new(releases);
        PrivateersHoldInputs inputs = new(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            new SpatialContentArtifact("spatial/hold.json", Hash, 1),
            new ContentArtifact("mesh/hold.json", Hash),
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0),
            [new NormalizedMaterial(3, "texture/wall.png", Hash)],
            new Dictionary<long, NormalizedActorSprite>
            {
                [11] = new("sprite/rat.png", Hash, 32, 32, [new NormalizedAtlasFrame(0, 0, 0, 16, 16)], 0, new Vector2(.5F, 0F), Vector2.One),
            });

        using (PrivateersHoldAppearance presentation = new(content, appearance, inputs))
        {
            Assert.Equal([3u], appearance.StaticMeshBindings.Select(binding => binding.MaterialSlot));
            Assert.Single(appearance.AtlasRequests);
            Assert.Single(appearance.SpriteRequests);
            presentation.Dispose();
        }

        Assert.True(releases.IndexOf("appearance") < releases.IndexOf("atlas"));
        Assert.True(releases.IndexOf("atlas") < releases.IndexOf("material"));
    }

    [Fact]
    public void Host_admits_one_realtime_update_and_releases_the_normalized_session_owners()
    {
        string root = RepositoryRoot();
        List<string> releases = [];
        ContentFake content = new(releases);
        PrivateersHoldInputs inputs = ReadInputs(root);
        content.Add(inputs.SpatialArtifact.Path, inputs.SpatialArtifact.Sha256);
        content.Add(inputs.StaticMesh.Path, inputs.StaticMesh.Sha256);
        foreach (NormalizedMaterial material in inputs.Materials) content.Add(material.TexturePath, material.TextureSha256);
        foreach (NormalizedActorSprite sprite in inputs.ActorSprites.Values) content.Add(sprite.TexturePath, sprite.TextureSha256);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);

        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input)))
        {
            product.Start();
            ProductUpdateFacts facts = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, .125);
            Assert.Equal(ProductUpdateResult.None, product.Update(new ProductUpdate(facts, ReadOnlySpan<ProductInputEvent>.Empty)));
            Assert.Equal(1, spatial.StepCalls);
            product.Shutdown();
        }

        Assert.Equal(1, engine.UiOpenCalls);
        Assert.True(releases.IndexOf("session") < releases.LastIndexOf("content"));
    }

    [Fact]
    public void Typed_input_and_explicit_combat_remain_product_semantic_above_the_normalized_engine_seams()
    {
        LookRecorder look = LookRecorder.Create();
        InputActionId attack = new("test.attack");
        PlayerControlBindings controls = new(["move"u8.ToArray()], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD, new DirectionalMovementBindings("forward"u8.ToArray(), "backward"u8.ToArray(), "left"u8.ToArray(), "right"u8.ToArray()));
        PlayerInputSystem input = new(DaggerfallTuning.Defaults.PlayerControl, look.Service, controls, [new InputActionBinding(attack, "attack"u8.ToArray())]);
        PlayerControlState player = new(new WorldPoint(0, 0, 0), 0, 0);
        ProductUpdateState update = new(.125F);
        update.Add(Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW));
        update.Add(Input(InputEventKind.PointerDelta, x: .25F, y: -.5F));
        update.Add(Input(InputEventKind.DirectDigital, x: 1F, phase: InputPhase.DirectUi, intent: "attack"));
        input.Apply(player, update);
        Assert.Equal(new Vector2(0, 1), update.PlanarIntent);
        Assert.True(update.IsRequested(attack));
        Assert.Equal(new Vector2(.25F, -.5F), look.LastDelta);

        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        long healthBefore = session.State.Actors.All[2000].Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw;
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        Assert.True(session.State.Actors.All[2000].Mechanics.ReadTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current.Raw < healthBefore);
    }

    private static PrivateersHoldInputs ReadInputs(string root)
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        return PrivateersHoldContent.Read(ImportContent(root), File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
    }

    private static ProductContent ImportContent(string root) => ContentAt(root, "worldrpg/imports/privateers-hold");

    private static ProductContent FullContent(string root) => ContentAt(root, "worldrpg");

    private static ProductContent ContentAt(string root, string relativeDirectory)
    {
        string contentRoot = Path.Combine(root, "content");
        string selected = Path.Combine(contentRoot, relativeDirectory);
        return new ProductContent(Directory.GetFiles(selected, "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray());
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }

    private static void PopulateContent(ContentFake content, PrivateersHoldInputs inputs)
    {
        content.Add(inputs.SpatialArtifact.Path, inputs.SpatialArtifact.Sha256);
        content.Add(inputs.StaticMesh.Path, inputs.StaticMesh.Sha256);
        foreach (NormalizedMaterial material in inputs.Materials) content.Add(material.TexturePath, material.TextureSha256);
        foreach (NormalizedActorSprite sprite in inputs.ActorSprites.Values) content.Add(sprite.TexturePath, sprite.TextureSha256);
    }

    private static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl keyboard = KeyboardControl.None, float x = 0F, float y = 0F, InputPhase phase = InputPhase.None, string intent = "") => new(kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, keyboard, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None, InputValueKind.None, phase, InputProvenance.None, default, default, default, x, y, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, Encoding.UTF8.GetBytes(intent), ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    private sealed class ContentFake : IContentService
    {
        private readonly List<string> releases;
        private readonly Dictionary<string, ContentSha256> values = new(StringComparer.Ordinal);
        private readonly Dictionary<ulong, KeyValuePair<string, ContentSha256>> references = [];
        private ulong nextHandle = 1;

        internal ContentFake(string path, ContentSha256 hash, List<string> releaseLog) : this(releaseLog) => Add(path, hash);
        internal ContentFake(List<string> releaseLog) => releases = releaseLog;

        internal void Add(string contentPath, ContentSha256 contentHash) => values[contentPath] = contentHash;

        public ContentReference OpenReference(ContentOpenRequest request) => ResolveReference(new ContentResolveRequest(request.Path, values[request.Path]));

        public ContentReference ResolveReference(ContentResolveRequest request)
        {
            if (!values.TryGetValue(request.Path, out ContentSha256 known) || known != request.Sha256) throw new InvalidOperationException("Unexpected content reference.");
            ulong value = nextHandle++;
            references.Add(value, new KeyValuePair<string, ContentSha256>(request.Path, known));
            return new ContentReference(new ContentReferenceHandle(value), () => releases.Add("content"));
        }

        public ReadOnlyMemory<ContentReferenceInfo> ReadReferenceInfo(ContentReference reference)
        {
            KeyValuePair<string, ContentSha256> item = references[reference.Handle.Value];
            return new[] { new ContentReferenceInfo(item.Key, item.Value, 1) };
        }

        public ReadOnlyMemory<byte> ReadBytes(ContentReadBytesRequest request) => ReadOnlyMemory<byte>.Empty;
    }

    private class SpatialFake : DispatchProxy
    {
        private ContentSha256 hash = Hash;
        private List<string> releases = null!;
        internal ISpatialService Service { get; private set; } = null!;
        internal int ReplaceCalls { get; private set; }
        internal int ReadCalls { get; private set; }
        internal int StepCalls { get; private set; }
        internal SpatialContentArtifactReplaceRequest? LastRequest { get; private set; }

        internal static SpatialFake Create(ContentSha256 contentHash, List<string> releaseLog)
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialFake>();
            SpatialFake fake = (SpatialFake)(object)service;
            fake.Service = service;
            fake.hash = contentHash;
            fake.releases = releaseLog;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.CreateSession) => new SpatialSession(new SpatialSessionHandle(1), () => releases.Add("session")),
            nameof(ISpatialService.DefaultCharacterControllerConfig) => default(CharacterControllerConfig),
            nameof(ISpatialService.ReplaceContentArtifact) => Replace((SpatialContentArtifactReplaceRequest)arguments![0]!),
            nameof(ISpatialService.ReadContentArtifact) => Read(),
            nameof(ISpatialService.ProposeCharacterStep) => Step((CharacterStepRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private SpatialContentArtifactReplaceReceipt Replace(SpatialContentArtifactReplaceRequest request)
        {
            ReplaceCalls++;
            LastRequest = request;
            return new(request.Content.Handle.Value, hash, 1, 2, 3, 4, 5, 6, 7, 8);
        }

        private SpatialContentArtifactReadout Read()
        {
            ReadCalls++;
            SpatialContentArtifactReplaceRequest request = LastRequest ?? throw new InvalidOperationException("Read before replace.");
            return new(true, request.Content.Handle.Value, hash, 2, 3, 4, 5, 6, 7, 8);
        }

        private CharacterStepReceipt Step(CharacterStepRequest request)
        {
            StepCalls++;
            return default(CharacterStepReceipt) with { Transform = new Transform(request.Position, Quaternion.Identity, Vector3.One), Motion = request.Motion };
        }
    }

    private class LookRecorder : DispatchProxy
    {
        internal ILookService Service { get; private set; } = null!;
        internal Vector2 LastDelta { get; private set; }
        internal static LookRecorder Create()
        {
            ILookService service = DispatchProxy.Create<ILookService, LookRecorder>();
            LookRecorder proxy = (LookRecorder)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == nameof(ILookService.Integrate))
            {
                LookRequest request = (LookRequest)arguments![0]!;
                LastDelta = request.Delta;
                LookState after = new(request.State.YawRadians + request.Delta.X, request.State.PitchRadians + request.Delta.Y);
                return new LookReceipt(request.State, after, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
            }
            return default(LookReceipt);
        }
    }

    private class EngineContextFake : DispatchProxy
    {
        internal IEngineContext Context { get; private set; } = null!;
        internal int UiOpenCalls { get; private set; }
        private IContentService content = null!;
        private ISpatialService spatial = null!;
        private IAppearanceService appearance = null!;
        private ILookService look = null!;
        private ICameraViewService camera = null!;
        private IRandomService random = null!;
        private IUiService ui = null!;

        internal static EngineContextFake Create(IContentService content, ISpatialService spatial, IAppearanceService appearance)
        {
            IEngineContext context = DispatchProxy.Create<IEngineContext, EngineContextFake>();
            EngineContextFake fake = (EngineContextFake)(object)context;
            fake.Context = context;
            fake.content = content;
            fake.spatial = spatial;
            fake.appearance = appearance;
            fake.look = ServiceProxy<ILookService, LookServiceFake>.Create();
            fake.camera = ServiceProxy<ICameraViewService, CameraServiceFake>.Create();
            fake.random = ServiceProxy<IRandomService, RandomServiceFake>.Create();
            fake.ui = UiServiceFake.Create(fake);
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            "get_Content" => content,
            "get_Spatial" => spatial,
            "get_Appearance" => appearance,
            "get_Look" => look,
            "get_CameraView" => camera,
            "get_Random" => random,
            "get_Ui" => ui,
            _ => throw new NotSupportedException(method?.Name),
        };

        private class LookServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(ILookService.Integrate) => Integrate((LookRequest)arguments![0]!),
                nameof(ILookService.Reset) => default(LookReceipt),
                nameof(ILookService.Rebase) => default(LookReceipt),
                nameof(ILookService.Diagnose) => default(LookDiagnostic),
                _ => throw new NotSupportedException(method?.Name),
            };
            private static LookReceipt Integrate(LookRequest request) => new(request.State, new LookState(request.State.YawRadians + request.Delta.X, request.State.PitchRadians + request.Delta.Y), Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
        }

        private class CameraServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(ICameraViewService.CreateCamera) => new Camera(new CameraHandle(1), () => { }),
                nameof(ICameraViewService.UpdateCamera) or nameof(ICameraViewService.SetActiveCamera) or nameof(ICameraViewService.ClearActiveCamera) or nameof(ICameraViewService.SetSkyBackground) or nameof(ICameraViewService.ClearSkyBackground) => null,
                nameof(ICameraViewService.ReplaceCamera) => new Camera(new CameraHandle(1), () => { }),
                _ => throw new NotSupportedException(method?.Name),
            };
        }

        private class RandomServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
                ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
                : throw new NotSupportedException(method?.Name);
        }

        private class UiServiceFake : DispatchProxy
        {
            private EngineContextFake owner = null!;
            internal static IUiService Create(EngineContextFake parent)
            {
                IUiService service = DispatchProxy.Create<IUiService, UiServiceFake>();
                ((UiServiceFake)(object)service).owner = parent;
                return service;
            }
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(IUiService.OpenStream) => Open(),
                nameof(IUiService.PublishProjection) => null,
                _ => throw new NotSupportedException(method?.Name),
            };
            private UiStream Open() { owner.UiOpenCalls++; return new UiStream(new UiStreamHandle(1), () => { }); }
        }
    }

    private class ServiceProxy<TService, TProxy> : DispatchProxy where TService : class where TProxy : DispatchProxy
    {
        internal static TService Create()
        {
            return DispatchProxy.Create<TService, TProxy>();
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => throw new NotSupportedException(method?.Name);
    }

    private sealed class AppearanceFake(List<string> releases) : IAppearanceService
    {
        internal List<MeshMaterialBinding> StaticMeshBindings { get; } = [];
        internal List<SpriteAtlasCreateRequest> AtlasRequests { get; } = [];
        internal List<SpriteFromAtlasRequest> SpriteRequests { get; } = [];

        public RenderResourceInfo OpenResource(RenderResourceRequest request) => new(new RenderResourceHandle(1), default, 0);
        public Material CreateMaterial(MaterialRequest request) => new(new MaterialHandle(1), () => releases.Add("material"));
        public void UpdateMaterial(MaterialUpdateRequest request) { }
        public Material ReplaceMaterial(MaterialUpdateRequest request) => CreateMaterial(request.Replacement);
        public Appearance CreatePrimitive(PrimitiveAppearanceRequest request) => CreateAppearance();
        public Appearance ReplacePrimitive(PrimitiveAppearanceReplaceRequest request) => CreateAppearance();
        public Appearance CreateStaticMesh(StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMesh(Appearance appearance, StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMeshFromContent(Appearance appearance, StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public void UpdateStaticMeshMaterials(StaticMeshMaterialUpdateRequest request) => StaticMeshBindings.AddRange(request.Bindings.ToArray());
        public Appearance CreateSprite(SpriteAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest request) => CreateAppearance();
        public SpriteAtlas CreateSpriteAtlas(SpriteAtlasCreateRequest request) { AtlasRequests.Add(request); return new(new SpriteAtlasHandle(1), () => releases.Add("atlas")); }
        public Appearance CreateSpriteFromAtlas(SpriteFromAtlasRequest request) { SpriteRequests.Add(request); return CreateAppearance(); }
        public Appearance ReplaceSpriteFromAtlas(SpriteFromAtlasReplaceRequest request) => CreateAppearance();
        public void SetSpriteFrame(SpriteFrameUpdateRequest request) { }
        public SpriteReadout ReadSprite(Appearance appearance) => default;
        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values) { }
        public Light CreateLight(LightRequest request) => new(new LightHandle(1), () => { });
        public void UpdateLight(LightUpdateRequest request) { }
        public Light ReplaceLight(LightUpdateRequest request) => NewLight(request.Replacement);
        public LightReadout ReadLight(Light light) => default;
        public PresentationReadout ReadPresentation() => default;

        private Appearance CreateAppearance() => new(new AppearanceHandle(1), () => releases.Add("appearance"));
        private static Light NewLight(LightRequest request) => new(new LightHandle(1), () => { });
    }
}
