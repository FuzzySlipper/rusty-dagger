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
/// The viewmodel plays the one fixed strike for every attack motion, and art that cannot reach the attack's
/// hit frame is named rather than played. The rules' clock delivers the blow.
/// </summary>
public sealed class WeaponStrikeDirectionTests
{
    /// <summary>
    /// The reproduction of #9700: the presentation used to draw any of the six strikes for a bow, and
    /// the bow's four-frame up strike could not reach the bow hit frame. Every shot now plays the one fixed strike.
    /// </summary>
    [Fact]
    public void A_bow_draws_the_one_fixed_strike_up_to_its_hit_frame_and_looses_from_there()
    {
        using Stage stage = new(BowWeapon(), "short-bow");

        stage.Presentation.BeginAdmittedUpdate();
        stage.Presentation.React(new PlayerAttackMotionFact(DaggerfallAttackIds.ShotDraw, new(1.25d, 0d, 0d), 2, 3));
        stage.Presentation.CompleteAdmittedUpdate();

        SpritePlaybackCreateRequest draw = stage.Appearance.PlaybackRequests.Last();
        Assert.Equal(DaggerfallFormulaPolicy.BowWeaponHitFrame, draw.Frames.Length); // of the bow's strikeDown, never its four-frame strikeUp
        Assert.All(draw.Frames.Span.ToArray(), frame => Assert.Equal(1.25d / DaggerfallFormulaPolicy.BowWeaponHitFrame, frame.DurationSeconds, 6));

        stage.Presentation.BeginAdmittedUpdate();
        stage.Presentation.React(new PlayerAttackMotionFact(DaggerfallAttackIds.Shot, new(.25d, .25d, .25d), 3, 4));
        stage.Presentation.CompleteAdmittedUpdate();

        // The last drawn frame through the windup, then the hit frame and those after it.
        SpritePlaybackCreateRequest loose = stage.Appearance.PlaybackRequests.Last();
        Assert.Equal(7 - DaggerfallFormulaPolicy.BowWeaponHitFrame + 1, loose.Frames.Length);
        Assert.Equal(.25d, loose.Frames.Span[0].DurationSeconds, 6);
    }

    [Fact]
    public void A_swing_plays_the_one_fixed_strike()
    {
        using Stage stage = new(DistinctStrikes(), "iron-dagger");

        stage.Presentation.React(new PlayerAttackMotionFact("fastA", new(.2d, .1d, .1d), 2, 3));

        SpritePlaybackCreateRequest strike = stage.Appearance.PlaybackRequests.Last();
        Assert.Equal(NormalizedClassicWeapon.StrikeActions.ToList().IndexOf(NormalizedClassicWeapon.PlayedStrike) + 3, strike.Frames.Length);
    }

    [Fact]
    public void A_strike_that_ends_before_the_hit_frame_is_named_rather_than_played()
    {
        // Admission refuses such art; a composition that still reaches here names the broken strike.
        using Stage stage = new(ShortStrike(), "short-bow");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => stage.Presentation.React(
            new PlayerAttackMotionFact(DaggerfallAttackIds.Shot, new(1d, .25d, .25d), 2, 3)));
        Assert.Contains("'weapon.bow' strike 'strikeDown' plays 4 frames", failure.Message, StringComparison.Ordinal);
    }

    private sealed class Stage : IDisposable
    {
        internal Stage(NormalizedClassicPresentation classic, string item)
        {
            List<string> releases = [];
            ContentFake content = MediaContent(releases);
            foreach (NormalizedClassicWeapon weapon in classic.Weapons.Values) content.Add(weapon.TexturePath, Hash);
            Appearance = new(releases);
            Presentation = new(content, Appearance, MediaInputs(classic: classic));
            Presentation.UpdateRightHandEquipment(RightHand(item), weaponDrawn: true);
        }

        internal AppearanceFake Appearance { get; }
        internal DaggerfallSiteAppearance Presentation { get; }
        public void Dispose() => Presentation.Dispose();
    }

    /// <summary>The classic bow's shape: a four-frame up strike and seven-frame others.</summary>
    private static NormalizedClassicPresentation BowWeapon() =>
        Weapon("weapon.bow", "weapon/bow.png", "short-bow", name => name == "strikeUp" ? 4 : 7);

    /// <summary>A bow whose played strike ends before the bow hit frame.</summary>
    private static NormalizedClassicPresentation ShortStrike() =>
        Weapon("weapon.bow", "weapon/bow.png", "short-bow", name => name == NormalizedClassicWeapon.PlayedStrike ? 4 : 7);

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
}
