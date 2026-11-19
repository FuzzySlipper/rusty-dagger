using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>Walkers drawn as a gsplat character: an instance each of its one appearance, playing its walk or its idle.</summary>
public sealed class SplatCrowdTests
{
    [Fact]
    public void A_shown_walker_is_an_instance_of_the_figure_that_walks_at_its_rate_and_crossfades_to_its_idle_when_it_stops()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service);
        Assert.Null(crowd.Show(7, 1.7F, walking: true));
        SplatFigure figure = Figure("385", 1);
        crowd.Use(figure);

        // Every walker draws the figure's one appearance, at its own size, through an instance on its own object.
        Assert.Same(figure.Appearance, crowd.Show(7, 1.7F, walking: true));
        Assert.Same(figure.Appearance, crowd.Show(8, 1.9F, walking: false));
        Assert.Equal(["instance 1 of 1 on 7", "instance 2 of 1 on 8"], engine.Calls);
        engine.Calls.Clear();
        crowd.Settle();
        // A new instance starts its clip at once; the walk at the rate that keeps its stride at the walker's pace.
        Assert.Equal(["play 1 walk speed 0.9 restart fade 0", "play 2 idle speed 1 restart fade 0"], engine.Calls);
        engine.Calls.Clear();

        // The same clip again plays nothing; stopping crossfades to the idle, and setting off again to the walk.
        crowd.Show(7, 1.7F, walking: true);
        crowd.Show(8, 1.9F, walking: false);
        crowd.Settle();
        Assert.Empty(engine.Calls);
        crowd.Show(7, 1.7F, walking: false);
        crowd.Show(8, 1.9F, walking: true);
        crowd.Settle();
        Assert.Equal(["play 1 idle speed 1 fade 0.25", "play 2 walk speed 0.9 fade 0.25"], engine.Calls);
        Assert.Equal(2, crowd.Count);
    }

    [Fact]
    public void Another_character_replaces_each_walker_s_instance_and_starts_its_clip_again()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service);
        crowd.Use(Figure("385", 1));
        crowd.Show(3, 1.7F, walking: true);
        crowd.Settle();
        engine.Calls.Clear();

        SplatFigure other = Figure("454", 2);
        crowd.Use(other);
        Assert.Same(other.Appearance, crowd.Show(3, 1.7F, walking: true));
        crowd.Settle();
        // An object holds one instance: the old one is given up for the new.
        Assert.Equal(["replace 1 with instance 2 of 2 on 3", "play 2 walk speed 0.9 restart fade 0"], engine.Calls);
    }

    [Fact]
    public void A_walker_a_snapshot_leaves_out_is_released_at_its_settle_and_off_releases_them_all()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service);
        crowd.Use(Figure("385", 1));
        crowd.Show(1, 1.7F, walking: false);
        crowd.Show(2, 1.8F, walking: true);
        crowd.Settle();
        engine.Calls.Clear();

        // Walker 2 walked out of the snapshot: its object is gone, so its instance goes with the settle.
        crowd.Show(1, 1.7F, walking: false);
        crowd.Settle();
        Assert.Equal(["release 2"], engine.Calls);
        // Back again, it is a new instance on its object.
        crowd.Show(1, 1.7F, walking: false);
        crowd.Show(2, 1.8F, walking: true);
        crowd.Settle();
        Assert.Equal(["release 2", "instance 3 of 1 on 2", "play 3 walk speed 0.9 restart fade 0"], engine.Calls);
        engine.Calls.Clear();

        crowd.Use(null);
        Assert.Null(crowd.Show(1, 1.7F, walking: false));
        crowd.Settle();
        Assert.Equal(["release 1", "release 3"], engine.Calls);
        Assert.Equal(0, crowd.Count);
    }

    private static SplatFigure Figure(string name, ulong appearance) =>
        new(name, new Appearance(new AppearanceHandle(appearance), static () => { }), "walk", "idle", .9F, .25F);

    /// <summary>Records the animation calls a crowd makes, in order.</summary>
    public class AnimationRecorder : DispatchProxy
    {
        internal List<string> Calls { get; } = [];
        internal IAnimationService Service { get; private set; } = null!;
        private ulong instances;

        internal static AnimationRecorder Create()
        {
            IAnimationService service = Create<IAnimationService, AnimationRecorder>();
            AnimationRecorder recorder = (AnimationRecorder)(object)service;
            recorder.Service = service;
            return recorder;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case nameof(IAnimationService.CreateInstance):
                    return Instance((AnimationInstanceRequest)args![0]!, "");
                case nameof(IAnimationService.ReplaceInstance):
                    AnimationInstance prior = (AnimationInstance)args![0]!;
                    return Instance((AnimationInstanceRequest)args[1]!, $"replace {prior.Handle.Value} with ");
                case nameof(IAnimationService.SetPlayback):
                    AnimationPlaybackRequest play = (AnimationPlaybackRequest)args![0]!;
                    Assert.Equal((AnimationPlaybackKind.Play, AnimationLoopMode.Repeat, 1F), (play.Kind, play.LoopMode, play.Weight));
                    Calls.Add($"play {play.Instance.Handle.Value} {play.Clip} speed {play.Speed:0.##}{(play.Restart ? " restart" : "")} fade {(play.HasFade ? play.FadeSeconds : 0):0.##}");
                    return null;
                default:
                    throw new NotSupportedException(method?.Name);
            }
        }

        private AnimationInstance Instance(AnimationInstanceRequest request, string prefix)
        {
            ulong handle = ++instances;
            Calls.Add($"{prefix}instance {handle} of {request.Appearance.Handle.Value} on {request.ObjectId}");
            return new(new AnimationInstanceHandle(handle), () => Calls.Add($"release {handle}"));
        }
    }
}
