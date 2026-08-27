using System.Numerics;
using System.Reflection;
using System.Text;
using Rusty.Engine;
using RustyDagger.Game.Content;
using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Modules.PlayerControl;
using Xunit;

namespace Dagger.Game.Tests;

public sealed class DaggerfallCompositionTests
{
    [Fact]
    public void Daggerfall_rows_drive_the_hud_and_player_melee_uses_exact_revisioned_tracks()
    {
        RecordingEngine engine = new();
        using DaggerfallComposition composition = new(engine.Context, SkeletalInputs());

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
        using DaggerfallComposition composition = new(engine.Context, SkeletalInputs());

        for (int update = 0; update < 6; update++) composition.Update(new ProductUpdateState(2f));

        Assert.Equal((0d, 85d), Resource(engine.Ui.Projections[^1].Value, "health"));
        Assert.Equal("skeletal-warrior hit for 15 damage", composition.Presentation.LastOutcome);
        Assert.True(engine.Mechanics.SpendRequests.Count(request => request.Track == "health" && request.RevisionGuard == MechanicsRevisionGuard.Exact) >= 6);
    }

    private static PrivateersHoldInputs SkeletalInputs()
    {
        AuthoredActor skeletal = new(2000, "enemy-skeletalwarrior-1", new WorldPoint(0, 0, 0), null);
        return new PrivateersHoldInputs(new ProjectFacts(new WorldPoint(0, 0, 0), new Dictionary<long, AuthoredActor> { [2000] = skeletal }), [], new CollisionMesh([], []), null);
    }

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
        internal RecordingLook Look { get; } = new();
        internal RecordingSpatial Spatial { get; } = new();
        internal RecordingAppearance Appearance { get; } = new();
        internal RecordingRandom Random { get; } = new();
        internal RecordingMechanics Mechanics { get; } = new();
        internal RecordingUi Ui { get; } = new();
    }

    private class EngineContextProxy : DispatchProxy
    {
        internal RecordingEngine Owner { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch { "get_Look" => Owner.Look, "get_Spatial" => Owner.Spatial, "get_Appearance" => Owner.Appearance, "get_Random" => Owner.Random, "get_Mechanics" => Owner.Mechanics, "get_Ui" => Owner.Ui, _ => throw new NotSupportedException(method?.Name) };
    }

    private sealed class RecordingLook : ILookService { public LookReceipt Integrate(LookRequest request) => new(request.State, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY); }
    private sealed class RecordingSpatial : ISpatialService
    {
        public SpatialSession CreateSession(SpatialSessionConfig request) => new(new SpatialSessionHandle(1), () => { });
        public CollisionReplaceReceipt ReplaceCollision(CollisionReplaceRequest request) => default;
        public NavigationReplaceReceipt ReplaceNavigation(NavigationReplaceRequest request) => default;
        public CharacterStepReceipt ProposeCharacterStep(CharacterStepRequest request) => default;
        public NavigationStepReceipt ProposeNavigationStep(NavigationStepRequest request) => default;
    }
    private sealed class RecordingAppearance : IAppearanceService
    {
        public RenderResourceInfo OpenResource(RenderResourceRequest request) => new(new RenderResourceHandle(1), 0, 0);
        public AppearanceHandle CreatePrimitive(PrimitiveAppearanceRequest request) => new(1);
        public AppearanceHandle CreateStaticMesh(StaticMeshAppearanceRequest request) => new(1);
        public AppearanceHandle CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest request) => new(1);
        public AppearanceHandle CreateSprite(SpriteAppearanceRequest request) => new(1);
        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values) { }
    }
    private sealed class RecordingRandom : IRandomService
    {
        public KeyedRngReceipt DrawKeyed(KeyedRngRequest request) => new(request.Maximum == 100 ? request.Minimum : request.Maximum);
        public Rng CreateScoped(ScopedRngCreateRequest request) => throw new NotSupportedException(); public Rng ForkScoped(ScopedRngForkRequest request) => throw new NotSupportedException(); public RngValue NextU64(Rng rng) => throw new NotSupportedException(); public RngValue NextBoundedU32(ScopedRngBoundedRequest request) => throw new NotSupportedException(); public RngValue NextBool(Rng rng) => throw new NotSupportedException();
    }
    private sealed class RecordingUi : IUiService { internal List<UiProjection> Projections { get; } = []; public UiStreamHandle OpenStream(UiStreamRequest request) => new(1); public void PublishProjection(UiProjection projection) => Projections.Add(projection); }
}
