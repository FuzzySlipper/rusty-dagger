using WorldRpg.Kit.Combat;
using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The player weapon viewmodel: art selection, swing playback and viewport placement.</summary>
public sealed class PlayerViewmodelTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Vampire_voice_sounds_once_at_the_start_of_the_attack(bool targeted)
    {
        List<string> releases = [];
        var content = MediaContent(releases); content.Add("weapon/dagger.png", Hash);
        content.Add("audio/hit.wav", Hash); content.Add("audio/vampire.wav", Hash);
        var appearance = new AppearanceFake(releases);
        var audio = AudioRecorder.Create();
        using var presentation = new DaggerfallSiteAppearance(content, appearance,
            MediaInputs(classic: ClassicWeapon(), audio: [
                new NormalizedAudioClip("hit1", "audio/hit.wav", Hash),
                new NormalizedAudioClip("sound.205", "audio/vampire.wav", Hash)]), audio.Service,
            DaggerfallTuning.Defaults.PresentationAudio with { VampireAttackChancePercent = 100, VampireBarkChancePercent = 100 });
        presentation.UsePlayerVampireGender(() => false);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        PlayerAttackStartedFact started = new(2, 3, TargetId: targeted ? 12 : null) { Feedback = new(false, "") };

        presentation.React(started);
        Assert.Single(audio.EmittedSignals);
        presentation.React(started);
        Assert.Single(audio.EmittedSignals);
    }

    [Fact]
    public void A_site_without_its_own_classic_selection_presents_held_weapons_from_the_session()
    {
        // Ships and shop interiors publish no classic selection of their own; the session's start site
        // supplies the player's held-item presentation wherever the player stands.
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        DaggerfallSiteProfile site = MediaInputs();
        Assert.Null(site.ClassicPresentation.Viewmodel);
        using DaggerfallSiteAppearance presentation = new(content, appearance, site,
            sessionPresentation: MediaInputs(classic: ClassicWeapon()).ClassicPresentation);

        // The session's selection already gives the bare hands a viewmodel; the dagger adds its own.
        int before = appearance.PlaybackRequests.Count;
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        Assert.Equal(before + 1, appearance.PlaybackRequests.Count);
        presentation.Publish(EmptyActors());
        Assert.Contains(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
    }

    [Fact]
    public void The_sprite_viewmodel_takes_the_view_light_through_a_held_tween_while_its_playback_runs()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        TweenRecorder tween = TweenRecorder.Create();
        DaggerfallViewLight light = new(DaggerfallTuning.Defaults.SiteLighting);
        presentation.UseViewLight(tween.Service, light);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);

        presentation.Publish(EmptyActors());
        Assert.Empty(tween.Started);

        light.Update(() => new Vector3(DaggerfallTuning.Defaults.SiteLighting.ViewFull / 2));
        presentation.Publish(EmptyActors());
        presentation.Publish(EmptyActors());

        TweenStartRequest dimmed = Assert.Single(tween.Started);
        Assert.Equal(Assert.Single(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel).ObjectId, dimmed.ObjectId);
        TweenSegment segment = Assert.Single(dimmed.Segments.ToArray());
        Assert.Equal(TweenChannel.Tint, segment.Channel);
        Assert.Equal(new Vector4(.5F, .5F, .5F, 1), segment.From);
        Assert.Equal(segment.From, segment.To);
        Assert.Equal(TweenControl.Pause, Assert.Single(tween.Controls).Control);
    }

    [Fact]
    public void Compatible_right_hand_creates_a_viewmodel_and_uses_one_shot_strike_playback()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));

        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"), weaponDrawn: true);
        Assert.Single(appearance.PlaybackRequests);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        Assert.Equal(2, appearance.PlaybackRequests.Count);
        Assert.All(appearance.AtlasRequests.Last().Frames.Span.ToArray(), frame => Assert.False(frame.HasSize));
        Assert.Equal(new Vector2(320, 200), appearance.SpriteRequests.Last().Size);
        presentation.Publish(EmptyActors());
        Assert.Contains(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);

        presentation.React(new PlayerAttackMotionFact("fastA", new(.2d, .1d, .1d), 3, 4));
        Assert.Equal(3, appearance.PlaybackRequests.Count);
        Assert.Equal(SpritePlaybackLoopMode.OneShot, appearance.PlaybackRequests.Last().LoopMode);
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 1, true), true));
        presentation.Advance(OuterUpdate(1));
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 2, true), true));
        presentation.Advance(OuterUpdate(2));
        Assert.Equal(3, appearance.PlaybackRequests.Count); // A completed strike holds until the rules rest it.
        presentation.React(new PlayerAttackRestFact(3, 5));
        Assert.Equal(4, appearance.PlaybackRequests.Count);

        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"), weaponDrawn: true);
        presentation.Publish(EmptyActors());
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
    }

    [Fact]
    public void What_a_developer_move_retires_between_updates_outlives_the_snapshot_that_still_holds_it()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.BeginAdmittedUpdate();
        presentation.Publish(EmptyActors());
        presentation.CompleteAdmittedUpdate();
        int before = releases.Count;

        presentation.SuspendLocationResources();
        presentation.BeginAdmittedUpdate();
        Assert.Equal(before, releases.Count);

        presentation.Publish(EmptyActors());
        presentation.CompleteAdmittedUpdate();
        presentation.BeginAdmittedUpdate();
        Assert.True(releases.Count > before, "the retired location resources are released once a snapshot has dropped them");
    }

    [Fact]
    public void A_motion_plays_the_strike_rescaled_so_its_hit_frame_comes_as_the_windup_ends_and_delivers_nothing()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);

        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackMotionFact("fastA", new(.5d, .2d, .3d), 2, 3));
        presentation.CompleteAdmittedUpdate();

        // A windup of .5 s up to the hit on the classic frame 2: the two frames before it last .25 s each.
        SpritePlaybackCreateRequest strike = appearance.PlaybackRequests.Last();
        Assert.All(strike.Frames.Span[..NormalizedClassicWeapon.MeleeWeaponHitFrame].ToArray(), frame => Assert.Equal(.25d, frame.DurationSeconds, 6));

        // The rules' clock delivers the blow: the playback carries no marker, and any frame it reports delivers nothing.
        Assert.True(strike.Markers.IsEmpty);
        appearance.AdvanceReceiptForAll = WeaponHitReading(NormalizedClassicWeapon.MeleeWeaponHitFrame, (uint)NormalizedClassicWeapon.MeleeWeaponHitFrame);
        presentation.Advance(OuterUpdate(1));
        Assert.Empty(presentation.TakeAttackImpacts());
    }

    [Fact]
    public void A_power_attack_plays_the_frames_before_the_hit_its_hold_keeps_them_and_its_release_plays_the_rest()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        int played = ClassicWeapon().Weapons["weapon.dagger.steel"].Actions[NormalizedClassicWeapon.PlayedStrike].PlayedFrameCount;
        const int hit = NormalizedClassicWeapon.MeleeWeaponHitFrame;

        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackMotionFact(DaggerfallAttackIds.PowerIn, new(.5d, 0d, 0d), 2, 3));
        presentation.CompleteAdmittedUpdate();
        SpritePlaybackCreateRequest charge = appearance.PlaybackRequests.Last();
        Assert.Equal(hit, charge.Frames.Length);
        Assert.All(charge.Frames.Span.ToArray(), frame => Assert.Equal(.25d, frame.DurationSeconds, 6));

        int requests = appearance.PlaybackRequests.Count;
        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackMotionFact(DaggerfallAttackIds.PowerHold, null, 2, 4));
        presentation.CompleteAdmittedUpdate();
        Assert.Equal(requests, appearance.PlaybackRequests.Count);

        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackMotionFact(DaggerfallAttackIds.PowerOut, new(.1d, .1d, .1d), 2, 5));
        presentation.CompleteAdmittedUpdate();
        SpritePlaybackCreateRequest release = appearance.PlaybackRequests.Last();
        // The release stays on the charged pose through its windup, so the hit frame begins as the rules deliver the blow.
        Assert.Equal(played - hit + 1, release.Frames.Length);
        Assert.Equal(.1d, release.Frames.Span[0].DurationSeconds, 6);
        Assert.All(release.Frames.Span[1..].ToArray(), frame => Assert.Equal(.2d / (played - hit), frame.DurationSeconds, 6));
    }

    [Fact]
    public void A_fast_attacks_hit_frame_begins_as_the_windup_ends_and_its_frames_fill_the_motion()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);

        DaggerfallAttackTiming timing = new(.2d, .1d, .3d);
        presentation.React(new PlayerAttackMotionFact("fastA", timing, 1, 1));

        SpritePlaybackFrame[] frames = appearance.PlaybackRequests.Last().Frames.ToArray();
        Assert.Equal(timing.Windup, frames.Take(NormalizedClassicWeapon.MeleeWeaponHitFrame).Sum(frame => frame.DurationSeconds), 6);
        Assert.Equal(timing.Duration, frames.Sum(frame => frame.DurationSeconds), 6);
    }

    [Fact]
    public void The_weapon_returns_to_idle_on_the_rules_rest_not_on_the_strikes_completion()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        presentation.React(new PlayerAttackMotionFact("fastA", new(.2d, .1d, .1d), 1, 1));
        Assert.True(Viewmodel(presentation).Strike);

        var completed = new SpritePlaybackReadout(1, 1, SpritePlaybackState.Completed, 0d, 0, 1, true);
        appearance.AdvanceReceiptForAll = new SpritePlaybackAdvanceResult(default, completed, true);
        presentation.Advance(OuterUpdate(1));
        appearance.AdvanceReceiptForAll = new SpritePlaybackAdvanceResult(default, completed, false);
        presentation.Advance(OuterUpdate(2));
        Assert.True(Viewmodel(presentation).Strike);

        presentation.React(new PlayerAttackRestFact(1, 2));
        Assert.False(Viewmodel(presentation).Strike);
        presentation.React(new PlayerAttackMotionFact("fastA", new(.2d, .1d, .1d), 1, 3));
        Assert.True(Viewmodel(presentation).Strike); // A second ordinary attack can start.
    }

    [Fact]
    public void Drawn_weapon_swaps_and_empty_hands_replace_art_and_sheathing_retires_it()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        content.Add("weapon/sword.png", Hash);
        content.Add("weapon/unarmed.png", Hash);
        NormalizedClassicPresentation original = ClassicWeapon();
        NormalizedClassicWeapon dagger = original.Weapons["weapon.dagger.steel"];
        NormalizedClassicPresentation classic = original with
        {
            Weapons = new Dictionary<string, NormalizedClassicWeapon>
            {
                [dagger.ResourceId] = dagger,
                ["weapon.longblade"] = dagger with { ResourceId = "weapon.longblade", TexturePath = "weapon/sword.png" },
                ["weapon.unarmed"] = dagger with { ResourceId = "weapon.unarmed", TexturePath = "weapon/unarmed.png" },
            },
            CompatibleItemVisuals = new Dictionary<string, string> { ["iron-dagger"] = dagger.ResourceId, ["iron-longsword"] = "weapon.longblade" },
            UnarmedVisual = "weapon.unarmed",
        };
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: classic));
        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"), weaponDrawn: true);
        Assert.Equal("weapon.longblade", Viewmodel(presentation).Weapon.ResourceId);
        presentation.React(new PlayerAttackMotionFact("fastA", new(.2d, .1d, .1d), 1, 1));
        Assert.True(Viewmodel(presentation).Strike);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        Assert.Equal(dagger.ResourceId, Viewmodel(presentation).Weapon.ResourceId);
        Assert.False(Viewmodel(presentation).Strike);
        presentation.UpdateRightHandEquipment(RightHand("gold"), weaponDrawn: true);
        Assert.Equal("weapon.unarmed", Viewmodel(presentation).Weapon.ResourceId);
        // The session holds the weapon sheathed: the viewmodel shows no art. Refusing the attack itself
        // is the session's (see SessionPersistenceTests and the playtest inspection).
        presentation.UpdateRightHandEquipment(RightHand("gold"), weaponDrawn: false);
        using ActorsState actors = EmptyActors();
        presentation.Publish(actors);
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
    }

    [Fact]
    public void A_cast_draws_the_elements_hand_over_the_resting_weapon_until_the_rules_rest()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        content.Add("weapon/fire.png", Hash);
        NormalizedClassicPresentation original = ClassicWeapon();
        NormalizedClassicWeapon dagger = original.Weapons["weapon.dagger.steel"];
        NormalizedClassicPresentation classic = original with
        {
            Weapons = new Dictionary<string, NormalizedClassicWeapon>
            {
                [dagger.ResourceId] = dagger,
                ["hand.fire"] = dagger with { ResourceId = "hand.fire", TexturePath = "weapon/fire.png" },
            },
        };
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: classic));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        using ActorsState actors = EmptyActors();

        presentation.React(new PlayerCastMotionFact(0, new(.2d, .02d, .2d), 1, 1));
        presentation.Publish(actors);
        Assert.Equal(2, appearance.Snapshots.Last().Count(fact => fact.Layer == RenderLayer.Viewmodel));
        Assert.False(Viewmodel(presentation).Strike); // The weapon rests in the right hand.

        presentation.React(new PlayerAttackRestFact(1, 2));
        presentation.Publish(actors);
        Assert.Equal(1, appearance.Snapshots.Last().Count(fact => fact.Layer == RenderLayer.Viewmodel));
    }

    [Fact]
    public void A_cast_cutting_into_a_swings_recovery_rests_the_weapon_while_the_hand_plays()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        presentation.React(new PlayerAttackMotionFact("fastA", new(.2d, .1d, .1d), 1, 1));
        Assert.True(Viewmodel(presentation).Strike);

        presentation.React(new PlayerCastMotionFact(0, new(.2d, .02d, .2d), 1, 2));

        Assert.False(Viewmodel(presentation).Strike);
    }

    [Fact]
    public void A_cast_with_no_hand_in_the_data_draws_nothing_and_breaks_nothing()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        using ActorsState actors = EmptyActors();

        presentation.React(new PlayerCastMotionFact(0, new(.2d, .02d, .2d), 1, 1));
        presentation.Publish(actors);

        Assert.Equal(1, appearance.Snapshots.Last().Count(fact => fact.Layer == RenderLayer.Viewmodel));
    }

    [Fact]
    public void A_raised_guard_lifts_the_resting_weapon_toward_the_centre_and_a_lowered_one_returns_it()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        using ActorsState actors = EmptyActors();
        presentation.Publish(actors);
        SpriteViewportUpdateRequest rest = Assert.Single(appearance.ViewportRequests);

        presentation.React(new PlayerGuardChangedFact(true, 1, 1));
        SpriteViewportUpdateRequest guarded = appearance.ViewportRequests[^1];
        // The right-aligned dagger lifts and draws toward the centre: the canvas moves left.
        Assert.Equal(new Vector2(-.15F, .12F), guarded.Minimum);
        Assert.Equal(rest.Size, guarded.Size);

        presentation.React(new PlayerGuardChangedFact(false, 1, 2));
        Assert.Equal(rest.Minimum, appearance.ViewportRequests[^1].Minimum);
    }

    [Fact]
    public void Viewmodel_uses_stable_viewport_placement()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"), weaponDrawn: true);
        using ActorsState actors = EmptyActors();

        presentation.Publish(actors);
        AppearanceFact first = Assert.Single(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
        Assert.InRange(first.ObjectId, 2UL, (1UL << 53) - 1);
        Assert.Equal(Vector3.Zero, first.Transform.Translation);
        // The imported 320x200 canvas already stands right-aligned at its 0.1 offset, so the view stretches the
        // whole canvas over the screen, as FPSWeapon scales its display, and applies no second offset.
        SpriteViewportUpdateRequest placement = Assert.Single(appearance.ViewportRequests);
        Assert.Equal(SpriteViewportFit.Stretch, placement.Fit);
        Assert.Equal((Vector2.Zero, Vector2.One), (placement.Minimum, placement.Size));
        // Every action, whatever its alignment and offset, keeps that one placement: a strike at a .4 offset too.
        presentation.React(new PlayerAttackMotionFact("fastA", new(.2d, .1d, .1d), 1, 1));
        Assert.All(appearance.ViewportRequests, request => Assert.Equal((Vector2.Zero, Vector2.One), (request.Minimum, request.Size)));
        Assert.Equal(Quaternion.Identity, first.Transform.Rotation);
        Assert.All([first.Transform.Translation.X, first.Transform.Translation.Y, first.Transform.Translation.Z], coordinate => Assert.InRange(coordinate, -16F, 16F));
        presentation.Publish(actors);
        Assert.Equal(first.Transform, Assert.Single(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel).Transform);
        Assert.Equal(first.Transform, Viewmodel(presentation).Transform);
    }
}
