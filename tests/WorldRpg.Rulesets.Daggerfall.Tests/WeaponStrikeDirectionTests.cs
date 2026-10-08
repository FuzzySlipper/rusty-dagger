using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The viewmodel plays the strike the rules chose for a swing, so every admitted swing reaches the hit
/// frame that delivers its impact.
/// </summary>
public sealed class WeaponStrikeDirectionTests
{
    /// <summary>
    /// The reproduction of #9700: the presentation used to draw any of the six strikes for a bow, and
    /// the bow's four-frame up strike (draw 5) carried no marker for the bow hit frame, so the shot's
    /// arrow and stamina were spent and its impact retired unreported. The bow now always plays the
    /// strike the rules publish, whatever the presentation's draw would have chosen.
    /// </summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Every_bow_shot_reaches_the_bow_hit_frame_and_delivers_its_impact(long draw)
    {
        using Stage stage = new(BowWeapon(), "short-bow", draw);

        stage.Presentation.BeginAdmittedUpdate();
        stage.Presentation.React(new PlayerAttackStartedFact(2, 3, TargetId: 12, FrameSeconds: .25d,
            HitFrame: DaggerfallFormulaPolicy.BowWeaponHitFrame, Swing: DaggerfallFormulaPolicy.BowSwing));
        stage.Presentation.CompleteAdmittedUpdate();

        SpritePlaybackCreateRequest strike = stage.Appearance.PlaybackRequests.Last();
        Assert.Equal(7, strike.Frames.Length); // the bow's strikeDown, never its four-frame strikeUp
        SpritePlaybackMarker marker = Assert.Single(strike.Markers.Span.ToArray());
        Assert.Equal((uint)DaggerfallFormulaPolicy.BowWeaponHitFrame, marker.FrameIndex);

        stage.Appearance.AdvanceReceiptForAll = WeaponHitReading(0, (uint)DaggerfallFormulaPolicy.BowWeaponHitFrame);
        stage.Presentation.Advance(OuterUpdate(1));
        AttackImpactNotice impact = Assert.Single(stage.Presentation.TakeAttackImpacts());
        Assert.Equal(12, impact.TargetId);
        Assert.False(impact.Expired);
    }

    [Theory]
    [InlineData("StrikeDown", 3)] [InlineData("StrikeDownLeft", 4)] [InlineData("StrikeLeft", 5)]
    [InlineData("StrikeRight", 6)] [InlineData("StrikeDownRight", 7)] [InlineData("StrikeUp", 8)]
    public void A_drawn_swing_plays_the_strike_its_direction_names(string direction, int frames)
    {
        // Every draw would pick the first strike; a drawn direction ignores the draw.
        using Stage stage = new(DistinctStrikes(), "iron-dagger", draw: 0);

        stage.Presentation.React(new PlayerAttackStartedFact(2, 3, TargetId: 12,
            Swing: Enum.Parse<DaggerfallSwingDirection>(direction)));

        SpritePlaybackCreateRequest strike = stage.Appearance.PlaybackRequests.Last();
        Assert.Equal(frames, strike.Frames.Length);
        Assert.Equal((uint)DaggerfallFormulaPolicy.MeleeWeaponHitFrame, Assert.Single(strike.Markers.Span.ToArray()).FrameIndex);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void A_swing_without_a_gesture_plays_the_donors_click_attack_choice_among_the_six_strikes(long draw)
    {
        // WeaponManager's click attack draws uniformly among UpRight..DownRight, which FPSWeapon plays as
        // the six strike states.
        using Stage stage = new(DistinctStrikes(), "iron-dagger", draw);

        stage.Presentation.React(new PlayerAttackStartedFact(2, 3));

        Assert.Equal(checked((int)draw) + 3, stage.Appearance.PlaybackRequests.Last().Frames.Length);
    }

    [Fact]
    public void A_strike_that_ends_before_the_hit_frame_fails_instead_of_dropping_the_impact()
    {
        // Admission refuses such art; a composition that still reaches here names the broken strike
        // rather than spending the swing and retiring its impact unreported.
        using Stage stage = new(BowWeapon(), "short-bow", draw: 0);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => stage.Presentation.React(
            new PlayerAttackStartedFact(2, 3, TargetId: 12, HitFrame: DaggerfallFormulaPolicy.BowWeaponHitFrame,
                Swing: DaggerfallSwingDirection.StrikeUp)));
        Assert.Contains("'weapon.bow' strike 'strikeUp' plays 4 frames", failure.Message, StringComparison.Ordinal);
        Assert.Empty(stage.Presentation.TakeAttackImpacts());
    }

    private sealed class Stage : IDisposable
    {
        internal Stage(NormalizedClassicPresentation classic, string item, long draw)
        {
            List<string> releases = [];
            ContentFake content = MediaContent(releases);
            foreach (NormalizedClassicWeapon weapon in classic.Weapons.Values) content.Add(weapon.TexturePath, Hash);
            Appearance = new(releases);
            Presentation = new(content, Appearance, MediaInputs(classic: classic), random: StrikeRandom.Create(draw));
            Presentation.UpdateRightHandEquipment(RightHand(item), weaponDrawn: true);
        }

        internal AppearanceFake Appearance { get; }
        internal DaggerfallSiteAppearance Presentation { get; }
        public void Dispose() => Presentation.Dispose();
    }

    /// <summary>The classic bow's shape: a four-frame up strike and seven-frame others.</summary>
    private static NormalizedClassicPresentation BowWeapon() =>
        Weapon("weapon.bow", "weapon/bow.png", "short-bow", name => name == "strikeUp" ? 4 : 7);

    /// <summary>A weapon whose strikes each play a different length, so a playback names its strike.</summary>
    private static NormalizedClassicPresentation DistinctStrikes() =>
        Weapon("weapon.dagger.steel", "weapon/dagger.png", "iron-dagger",
            name => NormalizedClassicWeapon.StrikeActions.ToList().IndexOf(name) + 3);

    private static NormalizedClassicPresentation Weapon(string resource, string texture, string item, Func<string, int> strikeFrames)
    {
        IReadOnlyList<NormalizedAtlasFrame> frames = [new NormalizedAtlasFrame(0, 0, 0, 8, 8)];
        IReadOnlyDictionary<string, NormalizedClassicWeaponAction> actions = new[] { "idle" }.Concat(NormalizedClassicWeapon.StrikeActions)
            .Select((name, ordinal) => new NormalizedClassicWeaponAction(name, ordinal, 0, 1, "right", .4F, 10F, name == "idle", 0, 0)
            { Sequence = Enumerable.Repeat(0, name == "idle" ? 1 : strikeFrames(name)).ToArray() })
            .ToDictionary(action => action.Name);
        return new NormalizedClassicPresentation(new Dictionary<string, NormalizedClassicWeapon>
        {
            [resource] = new(resource, texture, Hash, 8, 8, frames, new(.5F, .5F), new(1F, 1F), [0], actions),
        }, [])
        {
            CompatibleItemVisuals = new Dictionary<string, string> { [item] = resource },
            Viewmodel = new ClassicViewmodelStyle(0),
        };
    }

    internal class StrikeRandom : DispatchProxy
    {
        private long _value;

        internal static IRandomService Create(long value)
        {
            IRandomService service = DispatchProxy.Create<IRandomService, StrikeRandom>();
            ((StrikeRandom)(object)service)._value = value;
            return service;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            return new KeyedRngReceipt(Math.Clamp(_value, request.Minimum, request.Maximum));
        }
    }
}
