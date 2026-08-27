using System.Numerics;
using Rusty.Engine;
using RustyDagger.Game.Content;
using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Modules.PlayerControl;
using Xunit;

namespace Dagger.Game.Tests;

public sealed class DaggerfallCompositionTests
{
    [Fact]
    public void Two_update_fact_boundary_preserves_origin_key_and_presents_after_reactions()
    {
        RecordingEngine engine = new();
        using DaggerfallComposition composition = new(engine, InputsWithSkeletalWarrior());

        for (int attack = 0; attack < 5; attack++)
        {
            ProductUpdateState update = new(1f) { AttackRequested = true };
            composition.Update(update);
        }

        Assert.Equal(5, engine.Ui.Projections.Count);
        Assert.Equal("Defeated skeletal-warrior for 2 damage; gained 450 XP", composition.Presentation.LastOutcome);
        Assert.Contains("step:4:loot:2000:H", engine.Random.Keys);

        composition.Update(new ProductUpdateState(1f));

        Assert.Equal(6, engine.Ui.Projections.Count);
        Assert.Equal("Defeated skeletal-warrior for 2 damage; gained 450 XP; looted 10 gold-piece", composition.Presentation.LastOutcome);
    }

    private static PrivateersHoldInputs InputsWithSkeletalWarrior()
    {
        AuthoredActor skeletal = new(2000, "enemy-skeletalwarrior-1", new WorldPoint(0, 0, 0), null);
        return new PrivateersHoldInputs(new ProjectFacts(new WorldPoint(0, 0, 0), new Dictionary<long, AuthoredActor> { [2000] = skeletal }), [], new CollisionMesh([], []), null);
    }

    private sealed class RecordingEngine : IEngineContext
    {
        public RecordingLook Look { get; } = new();
        public RecordingSpatial Spatial { get; } = new();
        public RecordingAppearance Appearance { get; } = new();
        public RecordingRandom Random { get; } = new();
        public RecordingUi Ui { get; } = new();
        ILookService IEngineContext.Look => Look;
        IDynamicsService IEngineContext.Dynamics => throw new NotSupportedException();
        ISpatialService IEngineContext.Spatial => Spatial;
        IAppearanceService IEngineContext.Appearance => Appearance;
        IRandomService IEngineContext.Random => Random;
        IUiService IEngineContext.Ui => Ui;
    }

    private sealed class RecordingLook : ILookService
    {
        public LookReceipt Integrate(LookRequest request) => new(request.State, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
    }

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

    private sealed class RecordingUi : IUiService
    {
        public List<UiProjection> Projections { get; } = [];
        public UiStreamHandle OpenStream(UiStreamRequest request) => new(1);
        public void PublishProjection(UiProjection projection) => Projections.Add(projection);
    }

    private sealed class RecordingRandom : IRandomService
    {
        public List<string> Keys { get; } = [];
        public KeyedRngReceipt DrawKeyed(KeyedRngRequest request)
        {
            Keys.Add(request.Key);
            return new(request.Maximum == 100 ? request.Minimum : request.Maximum);
        }
        public Rng CreateScoped(ScopedRngCreateRequest request) => throw new NotSupportedException();
        public Rng ForkScoped(ScopedRngForkRequest request) => throw new NotSupportedException();
        public RngValue NextU64(Rng rng) => throw new NotSupportedException();
        public RngValue NextBoundedU32(ScopedRngBoundedRequest request) => throw new NotSupportedException();
        public RngValue NextBool(Rng rng) => throw new NotSupportedException();
    }
}
