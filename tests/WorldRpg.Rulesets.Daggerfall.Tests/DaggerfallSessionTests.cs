using System.Numerics;
using System.Reflection;
using System.Text;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.PlayerControl;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSessionTests
{
    [Fact]
    public void Daggerfall_rows_drive_the_hud_and_player_melee_uses_exact_revisioned_tracks()
    {
        RecordingEngine engine = new();
        using DaggerfallSession composition = new(engine.Context, SkeletalInputs());

        composition.PublishInitial();
        Assert.Equal(["health", "stamina", "magicka"], ResourceIds(engine.Ui.Projections[^1].Value));
        Assert.Equal((85d, 85d), Resource(engine.Ui.Projections[^1].Value, "health"));
        Assert.Equal((90d, 90d), Resource(engine.Ui.Projections[^1].Value, "stamina"));

        for (int attack = 0; attack < 5; attack++) composition.Update(new ProductUpdateState(1f) { AttackRequested = true });

        Assert.Equal((65d, 90d), Resource(engine.Ui.Projections[^1].Value, "stamina"));
        Assert.Contains(engine.Mechanics.SpendRequests, request => request.Track == "health" && request.RevisionGuard == MechanicsRevisionGuard.Exact);
        Assert.Contains(engine.Mechanics.SpendRequests, request => request.Track == "stamina" && request.RevisionGuard == MechanicsRevisionGuard.Exact);
        Assert.Equal("Defeated skeletal-warrior for 2 damage; gained 450 XP", composition.Presentation.LastOutcome);

        composition.Update(new ProductUpdateState(1f));
        Assert.Equal("Defeated skeletal-warrior for 2 damage; gained 450 XP; looted 10 gold-piece", composition.Presentation.LastOutcome);
    }

    [Fact]
    public void Enemy_melee_spends_the_player_damage_track_until_defeat()
    {
        RecordingEngine engine = new();
        using DaggerfallSession composition = new(engine.Context, SkeletalInputs());

        for (int update = 0; update < 6; update++) composition.Update(new ProductUpdateState(2f));

        Assert.Equal((0d, 85d), Resource(engine.Ui.Projections[^1].Value, "health"));
        Assert.Equal("skeletal-warrior hit for 15 damage", composition.Presentation.LastOutcome);
        Assert.True(engine.Mechanics.SpendRequests.Count(request => request.Track == "health" && request.RevisionGuard == MechanicsRevisionGuard.Exact) >= 6);
    }

    [Fact]
    public void Actor_lifecycle_reads_the_current_engine_damage_track_after_set_and_restore()
    {
        RecordingEngine engine = new();
        using DaggerfallSession composition = new(engine.Context, SkeletalInputs());
        MechanicsEntity player = composition.State.Actors.Player.Mechanics;
        MechanicsTrackReadLeaseReceipt health = engine.Mechanics.ReadTrack(new MechanicsTrackReadRequest(player, "health", "test_read"));

        engine.Mechanics.SetTrack(new MechanicsTrackSetRequest(player, "test_set", "test", "health", health.Minimum, MechanicsTrackSetPolicy.ClampToBounds, MechanicsRevisionGuard.Exact, health.Revision));
        Assert.True(composition.State.Actors.Player.IsDefeated);

        MechanicsTrackReadLeaseReceipt defeated = engine.Mechanics.ReadTrack(new MechanicsTrackReadRequest(player, "health", "test_read"));
        engine.Mechanics.RestoreTrack(new MechanicsTrackMutationRequest(player, "test_restore", "test", "health", 1, MechanicsRevisionGuard.Exact, defeated.Revision));
        Assert.False(composition.State.Actors.Player.IsDefeated);
    }

    [Fact]
    public void Mechanics_construction_releases_catalog_and_all_partially_bound_entities_after_injected_failures()
    {
        RecordingMechanics catalogFailure = RecordingMechanics.Create();
        catalogFailure.FailOnDefineStatCall = 1;
        Assert.Throws<InvalidOperationException>(() => new DaggerfallMechanicsCatalog(catalogFailure.Service));
        Assert.Equal(1, catalogFailure.CatalogDisposals);
        Assert.Equal(0, catalogFailure.EntityDisposals);

        RecordingEngine engine = new();
        engine.Mechanics.FailOnInitialStatCall = 12;
        Assert.Throws<InvalidOperationException>(() => new DaggerfallSession(engine.Context, SkeletalInputs()));
        Assert.Equal(1, engine.Mechanics.CatalogDisposals);
        Assert.Equal(2, engine.Mechanics.EntityDisposals);
    }

    [Fact]
    public void Input_interpretation_uses_typed_edges_controls_and_semantic_intents()
    {
        RecordingEngine engine = new();
        PlayerInputSystem input = new(PlayerControlTuning.Defaults, engine.Look.Service);
        PlayerControlState player = new(new WorldPoint(0, 0, 0));

        ProductUpdateState held = new(1f);
        held.Add(Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW));
        held.Add(Input(InputEventKind.Key, InputEdge.Held, keyboard: KeyboardControl.KeyW));
        held.Add(Input(InputEventKind.PointerDelta, x: .25f, y: -.5f));
        held.Add(Input(InputEventKind.DirectDigital, InputEdge.Held, x: 1f, phase: InputPhase.Held, intent: "attack"));
        held.Add(Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1f, phase: InputPhase.Pressed, intent: "attack"));
        input.Apply(player, held);

        Assert.Equal(new Vector2(0f, 1f), held.PlanarIntent);
        Assert.True(held.AttackRequested);
        Assert.Equal(1, engine.Look.IntegrationCount);

        ProductUpdateState released = new(1f);
        released.Add(Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.KeyW));
        released.Add(Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.KeyW));
        released.Add(Input(InputEventKind.Clear));
        released.Add(Input(InputEventKind.DirectAxis, x: .75f, y: -.25f, phase: InputPhase.DirectUi, intent: "movement"));
        input.Apply(player, released);

        Assert.Equal(new Vector2(.75f, -.25f), released.PlanarIntent);
        Assert.False(released.AttackRequested);
    }

    [Fact]
    public void Realtime_batch_uses_engine_fixed_delta_once_per_admitted_step_and_ignores_invalid_turns()
    {
        RecordingEngine engine = new();
        using WorldRpgProduct game = new(new ProductCreateContext(engine.Context, ProductContentWithPlayer(), EmptyInputConfiguration()));
        game.Start();

        game.Update(new ProductUpdate(
            Facts(ProductTurnKind.Realtime, ProductLifecycleState.Running, admittedSteps: 3, fixedDeltaSeconds: .125),
            new ProductInputEvent[] { Input(InputEventKind.MappedAxis, x: .5f, y: -.25f, phase: InputPhase.Axis, intent: "movement") }));
        Assert.Equal(3, engine.Spatial.StepCalls);
        Assert.All(engine.Spatial.Commands, command => Assert.Equal(.125f, command.StepSeconds));
        Assert.All(engine.Spatial.Commands, command => Assert.Equal(new Vector2(.5f, -.25f), command.PlanarIntent));

        game.Update(new ProductUpdate(Facts(ProductTurnKind.Realtime, ProductLifecycleState.Running, admittedSteps: 0, fixedDeltaSeconds: .125), ReadOnlySpan<ProductInputEvent>.Empty));
        game.Update(new ProductUpdate(Facts(ProductTurnKind.Realtime, ProductLifecycleState.Paused, admittedSteps: 2, fixedDeltaSeconds: .125), ReadOnlySpan<ProductInputEvent>.Empty));
        game.Update(new ProductUpdate(Facts(ProductTurnKind.Demand, ProductLifecycleState.Running, admittedSteps: 2, fixedDeltaSeconds: 0), ReadOnlySpan<ProductInputEvent>.Empty));
        game.Update(new ProductUpdate(Facts(ProductTurnKind.Realtime, ProductLifecycleState.Running, admittedSteps: 2, fixedDeltaSeconds: double.NaN), ReadOnlySpan<ProductInputEvent>.Empty));

        Assert.Equal(3, engine.Spatial.StepCalls);
    }

    [Fact]
    public void Host_gates_updates_and_disposes_the_selected_session_once()
    {
        RecordingEngine engine = new();
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, ProductContentWithPlayer(), EmptyInputConfiguration()));
        ProductUpdate update = new(Facts(ProductTurnKind.Realtime, ProductLifecycleState.Running, admittedSteps: 1, fixedDeltaSeconds: .125), ReadOnlySpan<ProductInputEvent>.Empty);

        product.Update(update);
        Assert.Equal(0, engine.Spatial.StepCalls);

        product.Start();
        product.Pause();
        product.Update(update);
        Assert.Equal(0, engine.Spatial.StepCalls);

        product.Resume();
        product.Update(update);
        Assert.Equal(1, engine.Spatial.StepCalls);

        product.Shutdown();
        product.Update(update);
        Assert.Equal(1, engine.Spatial.StepCalls);
        Assert.Equal(1, engine.Spatial.SessionDisposals);
    }

    [Fact]
    public void Appearance_owners_clear_retained_facts_and_release_partial_construction_once()
    {
        RecordingEngine engine = new();
        AuthoredSprite sprite = new("textures/test.png", default, default, default, Vector2.One, 0);
        PrivateersHoldInputs inputs = new(new ProjectFacts(null, new Dictionary<long, AuthoredActor> { [9] = new(9, "enemy-rat-1", new WorldPoint(0, 0, 0), sprite) }), [], new CollisionMesh([], []), "mesh.json");
        using (PrivateersHoldAppearance appearance = new(engine.Appearance, inputs))
        {
            appearance.Dispose();
            appearance.Dispose();
        }
        Assert.Equal(2, engine.Appearance.AppearanceDisposals);
        Assert.Empty(engine.Appearance.Snapshots[^1]);

        RecordingEngine failing = new();
        failing.Appearance.FailOnCreateCall = 2;
        Assert.Throws<InvalidOperationException>(() => new PrivateersHoldAppearance(failing.Appearance, inputs));
        Assert.Equal(1, failing.Appearance.AppearanceDisposals);
    }

    private static PrivateersHoldInputs SkeletalInputs()
    {
        AuthoredActor skeletal = new(2000, "enemy-skeletalwarrior-1", new WorldPoint(0, 0, 0), null);
        return new PrivateersHoldInputs(new ProjectFacts(new WorldPoint(0, 0, 0), new Dictionary<long, AuthoredActor> { [2000] = skeletal }), [], new CollisionMesh([], []), null);
    }

    private static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl keyboard = KeyboardControl.None, float x = 0f, float y = 0f, InputPhase phase = InputPhase.None, string intent = "") => new(
        kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, keyboard, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None, InputValueKind.None, phase, InputProvenance.None, default, default, default, x, y, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, Encoding.UTF8.GetBytes(intent), ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

    private static ProductUpdateFacts Facts(ProductTurnKind mode, ProductLifecycleState lifecycle, uint admittedSteps, double fixedDeltaSeconds) => new(mode, lifecycle, 1, 1, 1, 1, mode == ProductTurnKind.Realtime ? 60u : 0u, admittedSteps, 0, fixedDeltaSeconds);

    private static ProductContent ProductContentWithPlayer() => new(new ProductContentFile[] {
        new ProductContentFile(Encoding.UTF8.GetBytes("projects/privateers-hold.project.json"), Encoding.UTF8.GetBytes("""{"assets":[],"scenes":[{"entities":[{"name":"player","translation":[0,0,0]}]}]}""")),
        new ProductContentFile(Encoding.UTF8.GetBytes("projects/privateers-hold.navgrid.json"), Encoding.UTF8.GetBytes("""{"cells":[]}""")),
        new ProductContentFile(Encoding.UTF8.GetBytes("imported/privateers-hold.static-mesh.json"), Encoding.UTF8.GetBytes("""{"payload":{"source":{"positions":[],"indices":[]}}}"""))
    });

    private static ProductInputConfiguration EmptyInputConfiguration() => new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);

    private static IReadOnlyList<string> ResourceIds(UiValue value) => Array(value, Field(value, value.Root, "resources")).Select(node => Text(value, value.Nodes.Span[(int)Field(value, node, "id")])).ToArray();
    private static (double Current, double Maximum) Resource(UiValue value, string id)
    {
        uint row = Array(value, Field(value, value.Root, "resources")).Single(node => Text(value, value.Nodes.Span[(int)Field(value, node, "id")]) == id);
        return (value.Nodes.Span[(int)Field(value, row, "current")].NumberValue, value.Nodes.Span[(int)Field(value, row, "maximum")].NumberValue);
    }
    private static uint Field(UiValue value, uint objectNode, string key) => Edges(value, objectNode).Single(edge => Key(value, value.Nodes.Span[(int)edge]) == key);
    private static IReadOnlyList<uint> Array(UiValue value, uint arrayNode) => Edges(value, arrayNode).ToArray();
    private static IEnumerable<uint> Edges(UiValue value, uint node) { StructuredValueNode current = value.Nodes.Span[(int)node]; for (uint index = 0; index < current.ChildCount; index++) yield return value.Edges.Span[checked((int)(current.FirstEdge + index))]; }
    private static string Text(UiValue value, StructuredValueNode node) => Encoding.UTF8.GetString(value.Utf8.Span.Slice(checked((int)node.TextOffset), checked((int)node.TextLen)));
    private static string Key(UiValue value, StructuredValueNode node) => Encoding.UTF8.GetString(value.Utf8.Span.Slice(checked((int)node.KeyOffset), checked((int)node.KeyLen)));

    private sealed class RecordingEngine
    {
        internal RecordingEngine() { Context = DispatchProxy.Create<IEngineContext, EngineContextProxy>(); ((EngineContextProxy)(object)Context).Owner = this; }
        internal IEngineContext Context { get; }
        internal RecordingLook Look { get; } = RecordingLook.Create();
        internal RecordingSpatial Spatial { get; } = RecordingSpatial.Create();
        internal RecordingAppearance Appearance { get; } = new();
        internal RecordingRandom Random { get; } = RecordingRandom.Create();
        internal RecordingMechanics Mechanics { get; } = RecordingMechanics.Create();
        internal RecordingUi Ui { get; } = RecordingUi.Create();
    }

    private class EngineContextProxy : DispatchProxy
    {
        internal RecordingEngine Owner { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch { "get_Look" => Owner.Look.Service, "get_Spatial" => Owner.Spatial.Service, "get_Appearance" => Owner.Appearance.Service, "get_Random" => Owner.Random.Service, "get_Mechanics" => Owner.Mechanics.Service, "get_Ui" => Owner.Ui.Service, _ => throw new NotSupportedException(method?.Name) };
    }

    private class RecordingLook : DispatchProxy
    {
        internal ILookService Service { get; private set; } = null!;
        internal int IntegrationCount { get; private set; }
        internal static RecordingLook Create()
        {
            ILookService service = DispatchProxy.Create<ILookService, RecordingLook>();
            RecordingLook proxy = (RecordingLook)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(ILookService.Integrate))
            {
                LookRequest request = (LookRequest)args![0]!;
                IntegrationCount++;
                LookState after = new(request.State.YawRadians + request.Delta.X, request.State.PitchRadians + request.Delta.Y);
                return new LookReceipt(request.State, after, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
            }
            return method?.ReturnType.IsValueType == true ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
    private class RecordingSpatial : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal int SessionDisposals { get; private set; }
        internal int StepCalls { get; private set; }
        internal List<CharacterControllerCommand> Commands { get; } = [];
        internal static RecordingSpatial Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, RecordingSpatial>();
            RecordingSpatial proxy = (RecordingSpatial)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(ISpatialService.CreateSession)) return new SpatialSession(new SpatialSessionHandle(1), () => SessionDisposals++);
            if (method?.Name == nameof(ISpatialService.ProposeCharacterStep)) { StepCalls++; Commands.Add(((CharacterStepRequest)args![0]!).Command); return default(CharacterStepReceipt); }
            return method?.ReturnType.IsValueType == true ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
    private sealed class RecordingAppearance : IAppearanceService
    {
        internal IAppearanceService Service => this;
        internal int AppearanceDisposals { get; private set; }
        internal List<AppearanceFact[]> Snapshots { get; } = [];
        internal int? FailOnCreateCall { get; set; }
        private int CreateCalls { get; set; }
        public RenderResourceInfo OpenResource(RenderResourceRequest request) => new(new RenderResourceHandle(1), default, 0);
        public Material CreateMaterial(MaterialRequest request) => new(new MaterialHandle(1), () => { });
        public void UpdateMaterial(MaterialUpdateRequest request) { }
        public Material ReplaceMaterial(MaterialUpdateRequest request) => new(new MaterialHandle(1), () => { });
        public Appearance CreatePrimitive(PrimitiveAppearanceRequest request) => CreateAppearance();
        public Appearance ReplacePrimitive(PrimitiveAppearanceReplaceRequest request) => CreateAppearance();
        public Appearance CreateStaticMesh(StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMesh(Appearance appearance, StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMeshFromContent(Appearance appearance, StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public void UpdateStaticMeshMaterials(StaticMeshMaterialUpdateRequest request) { }
        public Appearance CreateSprite(SpriteAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest request) => CreateAppearance();
        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values) => Snapshots.Add(values.ToArray());
        public Light CreateLight(LightRequest request) => new(new LightHandle(1), () => { });
        public void UpdateLight(LightUpdateRequest request) { }
        public Light ReplaceLight(LightUpdateRequest request) => new(new LightHandle(1), () => { });
        public LightReadout ReadLight(Light light) => default;
        public PresentationReadout ReadPresentation() => default;
        private Appearance CreateAppearance()
        {
            if (++CreateCalls == FailOnCreateCall) throw new InvalidOperationException("Injected appearance creation failure.");
            return new(new AppearanceHandle(1), () => AppearanceDisposals++);
        }
    }
    private class RecordingRandom : DispatchProxy
    {
        internal IRandomService Service { get; private set; } = null!;
        internal static RecordingRandom Create()
        {
            IRandomService service = DispatchProxy.Create<IRandomService, RecordingRandom>();
            RecordingRandom proxy = (RecordingRandom)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)args![0]!).Maximum == 100 ? ((KeyedRngRequest)args[0]!).Minimum : ((KeyedRngRequest)args[0]!).Maximum)
            : throw new NotSupportedException(method?.Name);
    }
    private class RecordingUi : DispatchProxy
    {
        internal IUiService Service { get; private set; } = null!;
        internal List<UiProjection> Projections { get; } = [];
        internal static RecordingUi Create()
        {
            IUiService service = DispatchProxy.Create<IUiService, RecordingUi>();
            RecordingUi proxy = (RecordingUi)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IUiService.OpenStream) => new UiStreamHandle(1),
            nameof(IUiService.PublishProjection) => Publish((UiProjection)args![0]!),
            _ => throw new NotSupportedException(method?.Name)
        };
        private object? Publish(UiProjection projection) { Projections.Add(projection); return null; }
    }
}
