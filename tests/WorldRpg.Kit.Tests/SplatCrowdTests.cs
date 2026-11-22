using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>Walkers drawn as a gsplat character: an instance each of its one appearance, playing the walk or the idle its seed picks.</summary>
public sealed class SplatCrowdTests
{
    [Fact]
    public void A_shown_walker_is_an_instance_of_the_figure_that_walks_at_its_rate_and_crossfades_to_its_idle_when_it_stops()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service, _ => 0, 0F);
        Assert.Null(crowd.Show(7, 7, 1.7F, walking: true));
        SplatFigure figure = Figure("385", 1);
        crowd.Use(figure);

        // Every walker draws the figure's one appearance, at its own size, through an instance on its own object.
        Assert.Same(figure.Appearance, crowd.Show(7, 7, 1.7F, walking: true));
        Assert.Same(figure.Appearance, crowd.Show(8, 8, 1.9F, walking: false));
        Assert.Equal(["instance 1 of 1 on 7", "instance 2 of 1 on 8"], engine.Calls);
        engine.Calls.Clear();
        Publish(crowd);
        // A new instance starts its clip at once, at its seed's phase; the walk at the rate that keeps its stride at the walker's pace.
        Assert.Equal([$"play 1 walk speed 0.9 restart fade 0{At0}", $"play 2 idle speed 1 restart fade 0{At0}"], engine.Calls);
        engine.Calls.Clear();

        // The same clip again plays nothing; stopping crossfades to the idle, and setting off again to the walk.
        crowd.Show(7, 7, 1.7F, walking: true);
        crowd.Show(8, 8, 1.9F, walking: false);
        Publish(crowd);
        Assert.Empty(engine.Calls);
        crowd.Show(7, 7, 1.7F, walking: false);
        crowd.Show(8, 8, 1.9F, walking: true);
        Publish(crowd);
        Assert.Equal([$"play 1 idle speed 1 fade 0.25{At0}", $"play 2 walk speed 0.9 fade 0.25{At0}"], engine.Calls);
        Assert.Equal(2, crowd.Count);
    }

    [Fact]
    public void Each_walker_walks_and_stands_as_its_seed_picks_from_the_figure_s_walks_and_idles_at_a_rate_near_1_its_pace_shares()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        Dictionary<long, int> seeds = new() { [1] = 3, [2] = 4, [3] = 5, [4] = 6, [5] = 7, [6] = 8, [7] = 9, [8] = 3 };
        using SplatCrowd crowd = new(() => engine.Service, actor => seeds[actor], .06F);
        SplatFigure figure = Figure("385", 1) with
        {
            Walks = [new("walk", 1F, .9F), new("stroll", 1F, 1F), new("gentle", .85F, 1F)],
            Idles = ["idle", "idle-relaxed", "idle-crossed"],
        };
        crowd.Use(figure);
        foreach (long actor in seeds.Keys) crowd.Show((ulong)actor, actor, 1.7F, walking: actor % 2 == 0);
        Publish(crowd);

        // Each plays the walk or idle its seed draws from the figure's, at the rate its seed draws within the jitter.
        SplatGait[] gaits = [.. seeds.Keys.Select(actor => crowd.Gait((ulong)actor)!.Value)];
        Assert.Equal(seeds.Values.Select(seed => figure.Gait(seed, .06F)), gaits);
        Assert.Equal(seeds.Keys.Select((actor, at) => actor % 2 == 0
                ? $"play {at + 1} {gaits[at].Walk.Clip} speed {gaits[at].WalkRate:0.##} restart fade 0 at {gaits[at].Phase:0.##}"
                : $"play {at + 1} {gaits[at].Idle} speed {gaits[at].Rate:0.##} restart fade 0 at {gaits[at].Phase:0.##}"),
            engine.Calls.Where(call => call.StartsWith("play", StringComparison.Ordinal)));
        Assert.All(gaits, gait => Assert.InRange(gait.Rate, .94F, 1.06F));
        // The rate both plays the walk at and walks the walker at, so its stride keeps its pace.
        Assert.All(gaits, gait => Assert.Equal((gait.Walk.Pace * gait.Rate, gait.Walk.Rate * gait.Rate), (gait.Pace, gait.WalkRate)));
        // Seven seeds take every walk and idle between them and not one rate: no two walkers move as one.
        Assert.Equal(3, gaits.Select(gait => gait.Walk).Distinct().Count());
        Assert.Equal(3, gaits.Select(gait => gait.Idle).Distinct().Count());
        Assert.Equal(7, gaits.Select(gait => gait.Rate).Distinct().Count());
        // Nor do they start in step: each starts its clips its own share of the way through.
        Assert.Equal(7, gaits.Select(gait => gait.Phase).Distinct().Count());
        Assert.All(gaits, gait => Assert.InRange(gait.Phase, 0F, .9999F));
        // A seed draws one gait, in any crowd and run: walker 8 shares walker 1's, as a walker restored from a save keeps its own.
        Assert.Equal(gaits[0], gaits[7]);
        Assert.Null(crowd.Gait(99));
        // Walkers who share a seed's last bits, as walkers of one townsperson's art do, still walk and stand apart.
        SplatGait[] alike = [.. Enumerable.Range(0, 16).Select(at => figure.Gait(at * 4 + 1, .06F))];
        Assert.Equal(3, alike.Select(gait => gait.Walk).Distinct().Count());
        Assert.Equal(3, alike.Select(gait => gait.Idle).Distinct().Count());
        Assert.Equal(16, alike.Select(gait => gait.Phase).Distinct().Count());

        // A figure with nothing to walk or stand in, or a rate that could stop a walker, is refused.
        Assert.Throws<InvalidOperationException>(() => (figure with { Idles = [] }).Gait(3, 0F));
        Assert.Throws<ArgumentOutOfRangeException>(() => figure.Gait(3, 1F));
    }

    [Fact]
    public void Another_character_replaces_each_walker_s_instance_and_starts_its_clip_again()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service, _ => 0, 0F);
        crowd.Use(Figure("385", 1));
        crowd.Show(3, 3, 1.7F, walking: true);
        Publish(crowd);
        engine.Calls.Clear();

        SplatFigure other = Figure("454", 2);
        crowd.Use(other);
        Assert.Same(other.Appearance, crowd.Show(3, 3, 1.7F, walking: true));
        Publish(crowd);
        // An object holds one instance: the old one is given up for the new.
        Assert.Equal(["replace 1 with instance 2 of 2 on 3", $"play 2 walk speed 0.9 restart fade 0{At0}"], engine.Calls);
    }

    [Fact]
    public void A_walker_a_snapshot_leaves_out_is_released_before_it_is_published_and_off_releases_them_all()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service, _ => 0, 0F);
        crowd.Use(Figure("385", 1));
        crowd.Show(1, 1, 1.7F, walking: false);
        crowd.Show(2, 2, 1.8F, walking: true);
        Publish(crowd);
        engine.Calls.Clear();

        // Walker 2 walked out of the snapshot (or died, and shows its corpse): its instance goes before that snapshot is
        // published, since the Engine refuses a pose for an object that shows a sprite.
        crowd.Show(1, 1, 1.7F, walking: false);
        crowd.Publishing();
        Assert.Equal(["release 2"], engine.Calls);
        crowd.Settle();
        // Back again, it is a new instance on its object.
        crowd.Show(1, 1, 1.7F, walking: false);
        crowd.Show(2, 2, 1.8F, walking: true);
        Publish(crowd);
        Assert.Equal(["release 2", "instance 3 of 1 on 2", $"play 3 walk speed 0.9 restart fade 0{At0}"], engine.Calls);
        engine.Calls.Clear();

        crowd.Use(null);
        Assert.Null(crowd.Show(1, 1, 1.7F, walking: false));
        crowd.Publishing();
        Assert.Equal(["release 1", "release 3"], engine.Calls);
        crowd.Settle();
        Assert.Equal(0, crowd.Count);
    }

    [Fact]
    public void A_walker_assigned_a_figure_draws_as_it_whether_or_not_the_crowd_uses_one()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service, _ => 0, 0F);
        SplatFigure own = Figure("454", 2);
        crowd.Assign(5, own);

        // Walker 5 is its own figure; the rest show their own art while the crowd uses none.
        Assert.Same(own.Appearance, crowd.Show(9, 5, 1.7F, walking: false));
        Assert.Null(crowd.Show(10, 6, 1.7F, walking: false));
        // Once the crowd uses a figure the rest draw as it, and walker 5 still as its own.
        SplatFigure everyone = Figure("385", 1);
        crowd.Use(everyone);
        Assert.Same(own.Appearance, crowd.Show(9, 5, 1.7F, walking: false));
        Assert.Same(everyone.Appearance, crowd.Show(10, 6, 1.7F, walking: false));
        Assert.Equal(new[] { 5L }, crowd.Assigned.Keys);

        crowd.Assign(5, null);
        Assert.Same(everyone.Appearance, crowd.Show(9, 5, 1.7F, walking: false));
        Assert.Empty(crowd.Assigned);
    }

    [Fact]
    public void Each_walker_draws_as_its_own_figure_where_the_crowd_uses_none()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service, _ => 0, 0F);
        SplatFigure townsman = Figure("385", 1), townswoman = Figure("453", 2), spawned = Figure("454", 3);
        crowd.UseOwn(actor => actor switch { 5 => townsman, 6 => townswoman, _ => null });
        crowd.Assign(7, spawned);

        // Each walker is its own figure, one with none shows its own art, and an assigned one stays assigned.
        Assert.Same(townsman.Appearance, crowd.Show(9, 5, 1.7F, walking: true));
        Assert.Same(townswoman.Appearance, crowd.Show(10, 6, 1.7F, walking: true));
        Assert.Null(crowd.Show(11, 8, 1.7F, walking: true));
        Assert.Same(spawned.Appearance, crowd.Show(12, 7, 1.7F, walking: true));
        // The crowd's figure takes over from their own while it is used.
        SplatFigure everyone = Figure("390-B", 4);
        crowd.Use(everyone);
        Assert.Same(everyone.Appearance, crowd.Show(9, 5, 1.7F, walking: true));
        crowd.Use(null);
        Assert.Same(townsman.Appearance, crowd.Show(9, 5, 1.7F, walking: true));
        crowd.UseOwn(null);
        Assert.Null(crowd.Show(9, 5, 1.7F, walking: true));
    }

    [Fact]
    public void A_skirted_walker_places_its_skirt_from_the_legs_it_is_drawn_with_each_settle_that_time_moved()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service, _ => 0, 0F);
        SplatFigure skirted = Figure("456", 1) with { Skirt = Skirt(out JointPose[] rest) };
        engine.Pose = rest;
        crowd.Use(skirted);
        crowd.Show(4, 4, 1.7F, walking: true);
        crowd.Show(5, 5, 1.7F, walking: false);
        Publish(crowd);
        // Its first settle hangs each skirt at rest from the clip as it is drawn now, its own controls left out: every
        // walker's legs are read, then their sheets step together, then each is placed.
        Assert.Equal(["instance 1 of 1 on 4", "instance 2 of 1 on 5", $"play 1 walk speed 0.9 restart fade 0{At0}", $"play 2 idle speed 1 restart fade 0{At0}",
            "sample 1 now without controls", "sample 2 now without controls", "pose 1 places 3 in Model", "pose 2 places 3 in Model"], engine.Calls);
        crowd.Show(4, 4, 1.7F, walking: true);
        Publish(crowd);
        Assert.Equal("release 2", engine.Calls[^1]);
        engine.Calls.Clear();

        // A settle that drew no new time leaves the skirt as it was; one that did moves it on.
        crowd.Show(4, 4, 1.7F, walking: true);
        Publish(crowd);
        Assert.Empty(engine.Calls);
        crowd.Advance(1F / 60);
        crowd.Show(4, 4, 1.7F, walking: true);
        Publish(crowd);
        Assert.Equal(["sample 1 now without controls", "pose 1 places 3 in Model"], engine.Calls);
        Assert.Throws<ArgumentOutOfRangeException>(() => crowd.Advance(-1));

        // A figure without a skirt draws its walkers from their clips alone.
        engine.Calls.Clear();
        crowd.Use(Figure("385", 2));
        crowd.Advance(1F / 60);
        crowd.Show(4, 4, 1.7F, walking: true);
        Publish(crowd);
        Assert.Equal(["replace 1 with instance 3 of 2 on 4", $"play 3 walk speed 0.9 restart fade 0{At0}"], engine.Calls);
    }

    /// <summary>A three-column ring skirt under the hips, on a rig of its legs and the skirt.</summary>
    private static SplatSkirtRig Skirt(out JointPose[] rest)
    {
        SplatSkirtLegs legs = new("LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot");
        (string Name, Vector3 At)[] named = [("Hips", new(0, 1, 0)), ("LeftUpLeg", new(.09F, .95F, 0)), ("LeftLeg", new(.09F, .5F, 0)), ("LeftFoot", new(.09F, .08F, 0)),
            ("RightUpLeg", new(-.09F, .95F, 0)), ("RightLeg", new(-.09F, .5F, 0)), ("RightFoot", new(-.09F, .08F, 0)),
            ("Skirt0", new(0, .95F, .25F)), ("Skirt1", new(.22F, .95F, -.12F)), ("Skirt2", new(-.22F, .95F, -.12F))];
        rest = [.. named.Select(joint => new JointPose(new(joint.At, Quaternion.Identity, Vector3.One), new(joint.At, Quaternion.Identity, Vector3.One)))];
        float[] theta = [0, MathF.Tau / 3, 2 * MathF.Tau / 3];
        SplatRingSkirt ring = new("Hips", legs, Vector2.Zero, .95F, .2F, ["Skirt0", "Skirt1", "Skirt2"], theta, [.. named.Skip(7).Select(joint => joint.At)],
            [.95F, .2F], [[.25F, .3F], [.25F, .3F], [.25F, .3F]]);
        return SplatSkirtRig.Resolve(ring, [.. named.Select(joint => new AnimationJointInfo(joint.Name, 0))], rest);
    }

    [Fact]
    public void Walkers_with_eyes_blink_each_on_its_own_seeded_clock_and_the_engine_hears_only_changes()
    {
        AnimationRecorder engine = AnimationRecorder.Create();
        using SplatCrowd crowd = new(() => engine.Service, actor => (int)actor * 7, 0F);
        SplatEyes eyes = new(1, new Color(.8F, .6F, .5F, 1F));
        SplatFigure seeing = Figure("385", 1) with { Eyes = eyes }, blind = Figure("386", 2);
        SplatBlinks blinks = new(2F, 6F, .06F, .15F, .1F);
        crowd.Blink(blinks);
        crowd.Assign(3, blind);
        crowd.Use(seeing);
        // Each walker's clock starts at the settle that first draws it, so the reference clocks start with no time.
        SplatBlinkClock[] clocks = [blinks.Clock(7), blinks.Clock(14)];
        foreach (SplatBlinkClock clock in clocks) clock.Step(0);
        float[] drawn = [0, 0];
        List<string> expected = [];
        Show();
        engine.Calls.Clear();

        // 30 seconds of 20 ms frames: each tint is a change in how shut that walker's lids are, toward its lid colour.
        for (int frame = 0; frame < 1500; frame++)
        {
            crowd.Advance(.02F);
            Show();
            for (int walker = 0; walker < 2; walker++)
            {
                float closure = clocks[walker].Step(.02F);
                if (closure != drawn[walker]) expected.Add($"tint {walker + 1} group 1 to 0.8,0.6,0.5 by {closure:0.###}");
                drawn[walker] = closure;
            }
        }
        Assert.Equal(expected, engine.Calls);
        // Each blinked every 2 to 6 seconds (one change to shut a blink), shutting fully, and the blind walker (instance 3) never tinted.
        foreach (string walker in new[] { "1", "2" })
            Assert.InRange(engine.Calls.Count(call => call == $"tint {walker} group 1 to 0.8,0.6,0.5 by 1"), 4, 14);
        Assert.DoesNotContain(engine.Calls, call => call.StartsWith("tint 3", StringComparison.Ordinal));
        Assert.NotEqual(engine.Calls.Where(call => call.StartsWith("tint 1", StringComparison.Ordinal)).Select(call => call[6..]),
            engine.Calls.Where(call => call.StartsWith("tint 2", StringComparison.Ordinal)).Select(call => call[6..]));

        // Blinks off opens any lid shut now at the next settle, and tells the Engine nothing more.
        while (!engine.Calls[^1].EndsWith("by 1", StringComparison.Ordinal))
        {
            crowd.Advance(.02F);
            Show();
        }
        string shutWalker = engine.Calls[^1].Split(' ')[1];
        engine.Calls.Clear();
        crowd.Blink(null);
        crowd.Advance(.02F);
        Show();
        Assert.Equal([$"tint {shutWalker} group 1 to 0.8,0.6,0.5 by 0"], engine.Calls);
        engine.Calls.Clear();
        for (int frame = 0; frame < 500; frame++)
        {
            crowd.Advance(.02F);
            Show();
        }
        Assert.Empty(engine.Calls);

        void Show()
        {
            crowd.Show(1, 1, 1.7F, walking: true);
            crowd.Show(2, 2, 1.7F, walking: false);
            crowd.Show(3, 3, 1.7F, walking: true);
            Publish(crowd);
        }
    }

    [Fact]
    public void A_blink_closes_stays_shut_and_opens_and_a_seed_blinks_alike_in_every_run_within_its_interval()
    {
        SplatBlinks blinks = new(2F, 6F, .06F, .15F, .1F);
        Assert.Equal(.31F, blinks.Seconds, 5);
        Assert.Equal([0F, .5F, 1F, 1F, 1F, .5F, 0F, 0F], new[] { -.01F, .03F, .06F, .1F, .2F, .26F, .31F, 1F }.Select(at => blinks.Closure(at)), new FloatTolerance(1e-5F));
        // A clock's blinks are its seed's: two clocks of one seed agree, another seed's differ.
        float[] Run(int seed)
        {
            SplatBlinkClock clock = blinks.Clock(seed);
            return [.. Enumerable.Range(0, 3000).Select(_ => clock.Step(.01F))];
        }
        float[] one = Run(11);
        Assert.Equal(one, Run(11));
        Assert.NotEqual(one, Run(12));
        // The first blink starts within the longest interval; each later one 2 to 6 seconds after the last ends.
        int[] starts = [.. Enumerable.Range(1, one.Length - 1).Where(at => one[at] > 0 && one[at - 1] == 0)];
        Assert.True(starts[0] * .01F <= 6F + .01F, $"first at {starts[0] * .01F}");
        Assert.All(starts.Zip(starts.Skip(1)), pair => Assert.InRange((pair.Second - pair.First) * .01F, 2F + .31F - .02F, 6F + .31F + .02F));
        Assert.InRange(starts.Length, 4, 15);

        Assert.Throws<ArgumentException>(() => new SplatBlinks(0F, 6F, .06F, .15F, .1F).Validate());
        Assert.Throws<ArgumentException>(() => new SplatBlinks(6F, 2F, .06F, .15F, .1F).Validate());
        Assert.Throws<ArgumentException>(() => new SplatBlinks(2F, 6F, 0F, 0F, 0F).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => blinks.Clock(1).Step(-1F));
    }

    private sealed class FloatTolerance(float tolerance) : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => MathF.Abs(x - y) <= tolerance;
        public int GetHashCode(float value) => 0;
    }

    /// <summary>What the site appearance does with the snapshot the crowd showed walkers in.</summary>
    private static void Publish(SplatCrowd crowd)
    {
        crowd.Publishing();
        crowd.Settle();
    }

    /// <summary>Where seed 0's walkers start their clips, as the recorder writes it.</summary>
    private static readonly string At0 = $" at {Figure("385", 1).Gait(0, 0F).Phase:0.##}";

    private static SplatFigure Figure(string name, ulong appearance) =>
        new(name, new Appearance(new AppearanceHandle(appearance), static () => { }), [new("walk", 1F, .9F)], ["idle"], .25F);

    /// <summary>Records the animation calls a crowd makes, in order.</summary>
    public class AnimationRecorder : DispatchProxy
    {
        internal List<string> Calls { get; } = [];
        internal IAnimationService Service { get; private set; } = null!;
        /// <summary>The joints every instance is sampled in.</summary>
        internal JointPose[] Pose { get; set; } = [];
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
                    Calls.Add($"play {play.Instance.Handle.Value} {play.Clip} speed {play.Speed:0.##}{(play.Restart ? " restart" : "")} fade {(play.HasFade ? play.FadeSeconds : 0):0.##} at {play.NormalizedTime:0.##}");
                    return null;
                case nameof(IAnimationService.SampleJointPose):
                    AnimationPoseSampleRequest sample = (AnimationPoseSampleRequest)args![0]!;
                    Calls.Add($"sample {sample.Instance.Handle.Value}{(sample.UseCurrentPlayback ? " now" : "")}{(sample.ApplyPoseControls ? "" : " without controls")}");
                    return new ReadOnlyMemory<JointPose>(Pose);
                case nameof(IAnimationService.SetSplatGroupTint):
                    SplatGroupTintRequest tint = (SplatGroupTintRequest)args![0]!;
                    Calls.Add($"tint {tint.Instance.Handle.Value} group {tint.Group} to {tint.Target.R:0.##},{tint.Target.G:0.##},{tint.Target.B:0.##} by {tint.Weight:0.###}");
                    return null;
                case nameof(IAnimationService.SetPose):
                    AnimationPoseRequest pose = (AnimationPoseRequest)args![0]!;
                    Calls.Add($"pose {pose.Instance.Handle.Value} places {pose.Overrides.Length} in {string.Join(",", pose.Overrides.ToArray().Select(o => o.Space).Distinct())}");
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
