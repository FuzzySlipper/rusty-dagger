using System.Reflection;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>Walkers drawn as a directional parts-rig figure: its views, its layer stack, and the walk or idle each plays.</summary>
public sealed class PartsRigCrowdTests
{
    private const string Stack = """
        {"spec": "parts-rig-layers/1", "layers": [
          {"name": "body", "track": 0, "body": true},
          {"name": "attention", "track": 2, "clip": "attention", "loop": true, "bones": ["head"]},
          {"name": "attention override", "track": 3, "companion": "{clip}.attention"}]}
        """;

    [Fact]
    public void A_view_is_seen_from_its_camera_bearing_with_the_figure_s_left_at_plus_x()
    {
        // The publication's camera angles (0 front, 90 at the figure's left, 270 at its right) are the Engine's headings.
        Assert.Equal(0F, PartsRigFigureView.AtCamera("front", 0, false).Heading);
        Assert.Equal(MathF.PI / 2, PartsRigFigureView.AtCamera("side", 90, true).Heading, 5);
        Assert.Equal(MathF.PI, PartsRigFigureView.AtCamera("back", 180, false).Heading, 5);
        Assert.Equal(-MathF.PI / 2, PartsRigFigureView.AtCamera("side", 270, false).Heading, 5);
        Assert.Equal(-MathF.PI / 4, PartsRigFigureView.AtCamera("front34", 315, false).Heading, 5);
        Assert.True(PartsRigFigureView.AtCamera("side", 90, true).Mirror);
    }

    [Theory]
    [InlineData(0F)]
    [InlineData(1.3F)]
    [InlineData(-2.4F)]
    public void The_figure_faces_where_its_actor_heads(float yaw)
    {
        // Its front is +z; an actor heading yaw faces ActorHeading.Forward(yaw).
        System.Numerics.Vector3 front = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, PartsRigFigure.Facing(yaw));
        System.Numerics.Vector3 forward = Actors.ActorHeading.Forward(yaw);
        Assert.True(System.Numerics.Vector3.Distance(front, forward) < 1e-5F, $"{front} is not {forward}");
    }

    [Theory]
    [InlineData(WanderState.Moving, true)]
    [InlineData(WanderState.Seeking, true)]
    [InlineData(WanderState.Idle, false)]
    [InlineData(WanderState.Blocked, false)]
    [InlineData(WanderState.Unloaded, false)]
    [InlineData(WanderState.Dead, false)]
    public void A_walker_walks_while_it_seeks_or_moves_to_a_waypoint(WanderState state, bool walks) =>
        Assert.Equal(walks, PartsRigCrowd.Walks(state));

    [Theory]
    [InlineData("""{"spec": "parts-rig-layers/2", "layers": [{"name": "body", "track": 0, "body": true}]}""")]
    [InlineData("""{"spec": "parts-rig-layers/1", "layers": [{"name": "a", "track": 2, "clip": "attention"}]}""")]
    [InlineData("""{"spec": "parts-rig-layers/1", "layers": [{"name": "body", "track": 1, "body": true}]}""")]
    [InlineData("""{"spec": "parts-rig-layers/1", "layers": [{"name": "body", "track": 0, "body": true}, {"name": "a", "track": 0, "clip": "x"}]}""")]
    [InlineData("""{"spec": "parts-rig-layers/1", "layers": [{"name": "body", "track": 0, "body": true}, {"name": "a", "track": 2, "companion": "attention"}]}""")]
    public void A_layer_stack_has_one_body_on_track_0_and_each_layer_one_clip_on_its_own_track(string json) =>
        Assert.Throws<InvalidOperationException>(() => PartsRigLayerStack.Read(JsonDocument.Parse(json).RootElement));

    [Fact]
    public void A_shown_walker_walks_while_it_walks_and_idles_when_it_stops_with_its_attention_layer_over_both()
    {
        RigsRecorder rigs = RigsRecorder.Create();
        using PartsRigCrowd crowd = new(() => rigs.Service);
        crowd.Use(Figure(["walk", "idle", "attention", "walk.attention"]));
        Assert.Equal(["load front", "load side", "load back"], rigs.Calls);
        rigs.Calls.Clear();

        Appearance first = crowd.Show(7, 1.7F, walking: true)!;
        Assert.Equal(["appearance 4 views 1.7/850"], rigs.Calls);
        rigs.Calls.Clear();
        crowd.Settle();
        // A new appearance starts from the setup pose: the walk at once, the standing attention layer and the walk's companion.
        Assert.Equal(["play 7 track 0 walk fade 0 speed 1", "play 7 track 2 attention fade 0 speed 1", "play 7 track 3 walk.attention fade 0 speed 1"], rigs.Calls);
        rigs.Calls.Clear();

        // The same clip again plays nothing; stopping crossfades to the idle, and the walk's companion fades out.
        Assert.Same(first, crowd.Show(7, 1.7F, walking: true));
        crowd.Settle();
        Assert.Empty(rigs.Calls);
        crowd.Show(7, 1.7F, walking: false);
        crowd.Settle();
        Assert.Equal(["play 7 track 0 idle fade 0.2 speed 1", "stop 7 track 3 fade 0.2"], rigs.Calls);
        Assert.Equal(1, crowd.Count);
    }

    [Fact]
    public void Off_returns_walkers_to_their_own_art_and_releases_their_appearances_and_then_the_rigs_a_snapshot_later()
    {
        RigsRecorder rigs = RigsRecorder.Create();
        using PartsRigCrowd crowd = new(() => rigs.Service);
        crowd.Use(Figure(["walk", "idle", "attention"]));
        crowd.Show(1, 1.7F, walking: false);
        crowd.Show(2, 1.8F, walking: true);
        crowd.Settle();
        rigs.Calls.Clear();

        crowd.Use(null);
        Assert.Null(crowd.Show(1, 1.7F, walking: false));
        // The snapshot that leaves them out may still be read: they are released at the settle after it.
        crowd.Settle();
        Assert.Empty(rigs.Calls);
        Assert.Equal(0, crowd.Count);
        crowd.Settle();
        Assert.Equal(["release appearance", "release appearance", "release rig", "release rig", "release rig"], rigs.Calls);
    }

    [Fact]
    public void A_new_view_set_gives_each_walker_a_new_appearance_that_starts_its_clips_again()
    {
        RigsRecorder rigs = RigsRecorder.Create();
        using PartsRigCrowd crowd = new(() => rigs.Service);
        crowd.Use(Figure(["walk", "idle", "attention"]));
        Appearance four = crowd.Show(3, 1.7F, walking: true)!;
        crowd.Settle();
        PartsRigLayerStack layers = PartsRigLayerStack.Read(JsonDocument.Parse(Stack).RootElement);
        crowd.Use(new PartsRigFigure("eight", [.. Figure([]).Views, new("front34", MathF.PI / 4, true), new("front34", -MathF.PI / 4, false)],
            850, "walk", "idle", new HashSet<string>(["walk", "idle", "attention"]), layers, .2F, TextureFilter.Linear, default));
        rigs.Calls.Clear();

        Appearance eight = crowd.Show(3, 1.7F, walking: true)!;
        Assert.NotSame(four, eight);
        crowd.Settle();
        // The rigs the first set loaded serve the second; only the new view's rig loaded.
        Assert.Equal(["appearance 6 views 1.7/850", "play 3 track 0 walk fade 0 speed 1", "play 3 track 2 attention fade 0 speed 1"], rigs.Calls);
    }

    private static PartsRigFigure Figure(string[] clips) => new("four",
        [PartsRigFigureView.AtCamera("front", 0, false), PartsRigFigureView.AtCamera("side", 90, true),
         PartsRigFigureView.AtCamera("back", 180, false), PartsRigFigureView.AtCamera("side", 270, false)],
        850, "walk", "idle", new HashSet<string>(clips), PartsRigLayerStack.Read(JsonDocument.Parse(Stack).RootElement), .2F, TextureFilter.Linear, default);

    /// <summary>Records the parts-rig calls a crowd makes, in order.</summary>
    public class RigsRecorder : DispatchProxy
    {
        internal List<string> Calls { get; } = [];
        internal IPartsRigsService Service { get; private set; } = null!;
        private ulong next = 1;

        internal static RigsRecorder Create()
        {
            IPartsRigsService service = DispatchProxy.Create<IPartsRigsService, RigsRecorder>();
            RigsRecorder recorder = (RigsRecorder)(object)service;
            recorder.Service = service;
            return recorder;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (arguments![0])
            {
                case PartsRigLoadRequest load:
                    Calls.Add($"load {load.Path}");
                    return new PartsRigInfo(new PartsRig(new PartsRigHandle(next++), () => Calls.Add("release rig")), 1, 1, 3, 1, 1);
                case PartsRigAppearanceRequest appearance:
                    Calls.Add($"appearance {appearance.Views.Length} views {appearance.Scale * 850:0.##}/850");
                    return new Appearance(new AppearanceHandle(next++), () => Calls.Add("release appearance"));
                case PartsRigPlayRequest play:
                    Calls.Add($"play {play.ObjectId} track {play.Track} {play.Clip} fade {play.CrossfadeSeconds:0.##} speed {play.Speed:0.##}");
                    return null;
                case PartsRigStopRequest stop:
                    Calls.Add($"stop {stop.ObjectId} track {stop.Track} fade {stop.CrossfadeSeconds:0.##}");
                    return null;
                default:
                    throw new NotSupportedException(method?.Name);
            }
        }
    }
}
