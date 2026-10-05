using WorldRpg.Kit.Combat;
using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The player weapon viewmodel: art selection, swing playback and viewport placement.</summary>
public sealed class PlayerViewmodelTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void Vampire_voice_waits_for_the_melee_frame_and_retirement_cancels_it(bool targeted, bool retire)
    {
        List<string> releases = [];
        var content = MediaContent(releases); content.Add("weapon/dagger.png", Hash);
        var appearance = new AppearanceFake(releases);
        var audio = AudioRecorder.Create();
        using var presentation = new DaggerfallSiteAppearance(content, appearance,
            MediaInputs(classic: ClassicWeapon(), audio: [
                new NormalizedAudioClip("hit1", "audio/hit.wav", Hash),
                new NormalizedAudioClip("sound.205", "audio/vampire.wav", Hash)]), audio.Service,
            DaggerfallTuning.Defaults.PresentationAudio with { VampireAttackChancePercent = 100, VampireBarkChancePercent = 100 });
        presentation.UsePlayerVampireGender(() => false);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        presentation.React(new PlayerAttackStartedFact(2, 3, TargetId: targeted ? 12 : null) { Feedback = new(false, "") });
        Assert.Empty(audio.EmittedSignals);
        Frame(1, 1);
        Assert.Empty(audio.EmittedSignals);
        if (retire) presentation.RetirePendingSwing();
        Frame(2, 2);
        Assert.Equal(retire ? 0 : 1, audio.EmittedSignals.Count);
        Frame(3, 3);
        Assert.Equal(retire ? 0 : 1, audio.EmittedSignals.Count);

        void Frame(ulong step, uint frame)
        {
            appearance.AdvanceReceipts.Enqueue(default); // The existing actor playback advances first.
            appearance.AdvanceReceipts.Enqueue(Reading(0, frame));
            presentation.Advance(OuterUpdate(step));
        }
    }

    [Fact]
    public void Compatible_right_hand_creates_a_viewmodel_and_uses_one_shot_strike_playback()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));

        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"));
        Assert.Single(appearance.PlaybackRequests);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        Assert.Equal(2, appearance.PlaybackRequests.Count);
        Assert.All(appearance.AtlasRequests.Last().Frames.Span.ToArray(), frame => Assert.False(frame.HasSize));
        Assert.Equal(new Vector2(8, 8), appearance.SpriteRequests.Last().Size);
        presentation.Publish(EmptyActors());
        Assert.Contains(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);

        PlayerAttackStartedFact miss = new(3, 4);
        presentation.React(miss);
        presentation.React(miss);
        Assert.Equal(3, appearance.PlaybackRequests.Count);
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 1, true), true));
        presentation.Advance(OuterUpdate(1));
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 2, true), true));
        presentation.Advance(OuterUpdate(2));
        Assert.Equal(4, appearance.PlaybackRequests.Count);

        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"));
        presentation.Publish(EmptyActors());
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
    }

    [Fact]
    public void A_targeted_swing_plays_at_its_published_tick_and_delivers_on_the_classic_hit_frame()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));

        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackStartedFact(2, 3, TargetId: 12, FrameSeconds: .25d));
        presentation.CompleteAdmittedUpdate();

        // The published tick time replaces the authored table rate, so every strike frame lasts it.
        SpritePlaybackCreateRequest strike = appearance.PlaybackRequests.Last();
        Assert.All(strike.Frames.Span.ToArray(), frame => Assert.Equal(.25d, frame.DurationSeconds, 6));

        // Frames before the classic hit frame decide nothing.
        appearance.AdvanceReceiptForAll = Reading(1, 1u);
        presentation.Advance(OuterUpdate(1));
        Assert.Empty(presentation.TakeAttackImpacts());

        // The hit frame delivers the admitted swing exactly once.
        appearance.AdvanceReceiptForAll = Reading(DaggerfallFormulaPolicy.MeleeWeaponHitFrame, (uint)DaggerfallFormulaPolicy.MeleeWeaponHitFrame);
        presentation.Advance(OuterUpdate(2));
        AttackImpactNotice impact = Assert.Single(presentation.TakeAttackImpacts());
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, impact.AttackerId);
        Assert.Equal(12, impact.TargetId);
        Assert.False(impact.Expired);

        // Later frames of the same swing deliver nothing more.
        appearance.AdvanceReceiptForAll = Reading(DaggerfallFormulaPolicy.MeleeWeaponHitFrame + 1u, (uint)DaggerfallFormulaPolicy.MeleeWeaponHitFrame);
        presentation.Advance(OuterUpdate(3));
        Assert.Empty(presentation.TakeAttackImpacts());
    }

    [Fact]
    public void A_completed_player_swing_returns_to_idle_when_one_shot_stops_advancing()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        presentation.React(new PlayerAttackStartedFact(1, 1));
        Assert.False(presentation.CanStartPlayerAttack);

        var completed = new SpritePlaybackReadout(1, 1, SpritePlaybackState.Completed, 0d, 0, 1, true);
        appearance.AdvanceReceiptForAll = new SpritePlaybackAdvanceResult(default, completed, true);
        presentation.Advance(OuterUpdate(1));
        Assert.False(presentation.CanStartPlayerAttack); // Preserve the final frame for this update.
        appearance.AdvanceReceiptForAll = new SpritePlaybackAdvanceResult(default, completed, false);
        presentation.Advance(OuterUpdate(2));
        Assert.True(presentation.CanStartPlayerAttack);
        presentation.React(new PlayerAttackStartedFact(1, 3));
        Assert.False(presentation.CanStartPlayerAttack); // A second ordinary attack can start.
    }

    [Fact]
    public void A_swing_completion_that_did_not_advance_does_not_end_the_players_swing()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackStartedFact(2, 3, TargetId: 12, FrameSeconds: .25d));
        presentation.CompleteAdmittedUpdate();
        Assert.Empty(presentation.TakeAttackImpacts());

        // A receipt that reports completion without advancing is not authoritative, so the swing stays
        // live for the frame that can still deliver its impact.
        appearance.AdvanceReceiptForAll = new SpritePlaybackAdvanceResult(
            default, new SpritePlaybackReadout(1, 1, SpritePlaybackState.Completed, 0d, 0, 0, true), false);
        presentation.Advance(OuterUpdate(1));
        Assert.Empty(presentation.TakeAttackImpacts());

        appearance.AdvanceReceiptForAll = Reading(2, 2);
        presentation.Advance(OuterUpdate(2));

        AttackImpactNotice impact = Assert.Single(presentation.TakeAttackImpacts());
        Assert.Equal(12, impact.TargetId);
        Assert.False(impact.Expired);
    }

    [Fact]
    public void A_swing_that_ends_before_its_hit_frame_expires_its_impact_instead_of_landing_late()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));

        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackStartedFact(2, 3, TargetId: 12, FrameSeconds: .25d));
        presentation.CompleteAdmittedUpdate();
        Assert.Empty(presentation.TakeAttackImpacts());

        appearance.AdvanceReceiptForAll = new SpritePlaybackAdvanceResult(
            default, new SpritePlaybackReadout(1, 1, SpritePlaybackState.Completed, 0d, 0, 0, true), true);
        presentation.Advance(OuterUpdate(1));

        AttackImpactNotice expired = Assert.Single(presentation.TakeAttackImpacts());
        Assert.Equal(12, expired.TargetId);
        Assert.True(expired.Expired);
    }

    [Fact]
    public void Drawn_weapon_swaps_and_empty_hands_replace_art_and_sheathing_suppresses_attacks()
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
        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"));
        Assert.Equal("weapon.longblade", Viewmodel(presentation).Weapon.ResourceId);
        presentation.React(new PlayerAttackStartedFact(1, 1));
        Assert.False(presentation.CanStartPlayerAttack);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        Assert.Equal(dagger.ResourceId, Viewmodel(presentation).Weapon.ResourceId);
        Assert.True(presentation.CanStartPlayerAttack);
        presentation.UpdateRightHandEquipment(RightHand("gold"));
        Assert.Equal("weapon.unarmed", Viewmodel(presentation).Weapon.ResourceId);
        presentation.ToggleWeaponDrawn();
        presentation.UpdateRightHandEquipment(RightHand("gold"));
        Assert.False(presentation.CanStartPlayerAttack);
        using ActorsState actors = EmptyActors();
        presentation.Publish(actors);
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
    }

    [Fact]
    public void Viewmodel_uses_stable_viewport_placement()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        using ActorsState actors = EmptyActors();

        presentation.Publish(actors);
        AppearanceFact first = Assert.Single(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
        Assert.InRange(first.ObjectId, 2UL, (1UL << 53) - 1);
        Assert.Equal(Vector3.Zero, first.Transform.Translation);
        Assert.Single(appearance.ViewportRequests);
        Assert.Equal(Quaternion.Identity, first.Transform.Rotation);
        Assert.All([first.Transform.Translation.X, first.Transform.Translation.Y, first.Transform.Translation.Z], coordinate => Assert.InRange(coordinate, -16F, 16F));
        presentation.Publish(actors);
        Assert.Equal(first.Transform, Assert.Single(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel).Transform);
        Assert.Equal(first.Transform, Viewmodel(presentation).Transform);
    }
}
