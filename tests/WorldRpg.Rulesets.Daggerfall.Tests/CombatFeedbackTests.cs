using System.Numerics;
using SourceSoundNames = global::Daggerfall.Import.Arena2.DaggerfallSoundNames;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class CombatFeedbackTests
{
    [Theory]
    [InlineData(113, "swing")]
    [InlineData(114, "swing")]
    [InlineData(115, "sound.347")]
    [InlineData(116, "swing")]
    [InlineData(117, "sound.347")]
    [InlineData(118, "sound.347")]
    [InlineData(119, "sound.347")]
    [InlineData(120, "sound.347")]
    [InlineData(121, "sound.105")]
    [InlineData(122, "sound.105")]
    [InlineData(123, "sound.105")]
    [InlineData(124, "sound.347")]
    [InlineData(125, "sound.105")]
    [InlineData(126, "sound.105")]
    [InlineData(127, "sound.105")]
    [InlineData(128, "sound.347")]
    [InlineData(129, "sound.3")]
    [InlineData(130, "sound.3")]
    public void Every_retained_weapon_template_uses_its_donor_sound_family(int template, string cue)
    {
        Assert.Equal(new DaggerfallStrikeFeedback(true, cue), DaggerfallCombatFeedbackPolicy.ForWeaponTemplate(template));
    }

    [Fact]
    public void Published_mobile_feedback_matches_source_links_and_every_cue_is_admitted()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        Assert.NotEmpty(inputs.MobileSprites);
        foreach ((int mobileId, NormalizedActorSprite sprite) in inputs.MobileSprites)
        {
            Assert.True(global::Daggerfall.Import.Arena2.MobileSourceMetadata.TryGet(new(checked((byte)mobileId)), out var source));
            DaggerfallActorFeedback feedback = Assert.IsType<DaggerfallActorFeedback>(sprite.Feedback);
            Assert.Equal(mobileId, feedback.MobileId);
            Assert.Equal(source!.Links.BloodIndex, feedback.BloodIndex);
            Assert.Equal(source.Links.ParrySounds, feedback.ParrySounds);
            foreach ((string name, string cue) in new[] { (source.Links.MoveSoundCue, feedback.MoveCue),
                (source.Links.BarkSoundCue, feedback.BarkCue), (source.Links.AttackSoundCue, feedback.AttackCue) })
            {
                Assert.Equal($"sound.{SourceSoundNames.ForName(name)}", cue);
                Assert.Contains(inputs.Audio, audio => audio.Id == cue);
            }
        }
    }

    [Fact]
    public void Unarmed_hit_uses_only_natural_contact_clips_and_actual_live_target_position()
    {
        List<string> releases = [];
        AudioRecorder audio = AudioRecorder.Create();
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3));
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), new AppearanceFake(releases),
            MediaInputs(), audio.Service, random: KeyedRandomFake.Create(100).Service);
        AttackHitFact applied = new(1, 11, 5, 5, 0, false, 1, 2);
        presentation.React(applied, actors);
        presentation.React(applied, actors);
        AudioEmitRequest cue = Assert.Single(audio.Emits);
        Assert.EndsWith(".hit4", cue.SignalId, StringComparison.Ordinal);
        Assert.Equal(AudioEmitterKind.World3d, cue.Descriptor.EmitterKind);
        Assert.Equal(new Vector3(2, 0, 3), cue.Descriptor.Position);
        Assert.Equal(1F, cue.Descriptor.SpatialBlend);
        Assert.Equal(1.1F, cue.Descriptor.Pitch);
    }

    [Fact]
    public void A_parrying_mobile_uses_the_parry_family_for_weapon_contact_without_health_loss()
    {
        List<string> releases = [];
        AudioRecorder audio = AudioRecorder.Create();
        DaggerfallSiteProfile inputs = FeedbackInputs(0, parry: true);
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3));
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), new AppearanceFake(releases), inputs,
            audio.Service, random: KeyedRandomFake.Create(8).Service);
        AttackHitFact blocked = new(1, 11, 0, 0, 0, false, 1, 2) { Feedback = new(true, "swing") };
        presentation.React(blocked, actors);
        presentation.React(blocked, actors);
        Assert.EndsWith(".sound.436", Assert.Single(audio.Emits).SignalId, StringComparison.Ordinal);
        Assert.Equal(100, actors.Get(11).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).ValueInt64);
    }

    [Fact]
    public void Interrupted_and_multiframe_playback_cannot_claim_an_unapplied_hit()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), appearance,
            MediaInputs(primaryFrames: [0, -1, 1, -1, 0], includeAlternate: false), audio.Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 1, 1));
        presentation.React(new EnemyBehaviorTransitionFact(11, EnemyBehaviorState.Attack, EnemyBehaviorState.Idle, 1, 2));
        Assert.Null(Visual(presentation).ActiveAttack);
        appearance.AdvanceReceiptForAll = new(new[] { new SpritePlaybackMarkerCrossing(2, 3, 1, 0, 1), new SpritePlaybackMarkerCrossing(4, 3, 1, 0, 2) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0, 0, 2, false), true);
        presentation.Advance(OuterUpdate(3));
        Assert.Empty(audio.Emits);
        Assert.Empty(presentation.TakeAttackImpacts());
    }

    [Theory]
    [InlineData("sound.347", "sound.347")]
    [InlineData("sound.3", "sound.436")]
    public void Enemy_melee_misses_swing_while_bow_misses_can_parry(string acceptedCue, string emittedCue)
    {
        List<string> releases = [];
        AudioRecorder audio = AudioRecorder.Create();
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), new AppearanceFake(releases),
            FeedbackInputs(0, parry: true), audio.Service, random: KeyedRandomFake.Create(8).Service);
        AttackMissedFact missed = new(12, 11, 100, 50, true, 1, 2) { Feedback = new(true, acceptedCue) };
        presentation.React(missed);
        presentation.React(missed);
        Assert.EndsWith($".{emittedCue}", Assert.Single(audio.Emits).SignalId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(128, true, 0)]
    [InlineData(128, false, 1)]
    [InlineData(146, true, 1)]
    public void Mobile_attack_sounds_follow_mute_policy_and_duplicate_start_is_quiet(int mobile, bool mute, int emitted)
    {
        List<string> releases = [];
        AudioRecorder audio = AudioRecorder.Create();
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), new AppearanceFake(releases), FeedbackInputs(mobile), audio.Service,
            DaggerfallTuning.Defaults.PresentationAudio with { MuteHumanSounds = mute, AttackCueChancePercent = 100 });
        EnemyAttackStartedFact started = new(11, 12, true, 1, 2);
        presentation.React(started);
        presentation.React(started);
        Assert.Equal(emitted, audio.Emits.Count);
        if (emitted != 0) Assert.EndsWith(".sound.12", Assert.Single(audio.Emits).SignalId, StringComparison.Ordinal);
    }

    [Fact]
    public void Attract_time_advances_outside_radius_but_held_and_duplicate_updates_add_no_time()
    {
        List<string> releases = [];
        AudioRecorder audio = AudioRecorder.Create();
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new(20, 0, 0));
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), new AppearanceFake(releases), FeedbackInputs(0), audio.Service,
            DaggerfallTuning.Defaults.PresentationAudio with { AttractMinimumDelaySeconds = 1, AttractMaximumDelaySeconds = 1, AttractMoveChancePercent = 0 });
        ProductUpdateFacts update = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 120, 0, 1d / 60);
        presentation.AdvanceMobileFeedback(update, actors, new(0, 0, 0), (_, _) => false);
        Assert.Empty(audio.Emits);
        actors.Get(11).ApplyPose(new(new(2, 0, 0), 0));
        presentation.AdvanceMobileFeedback(update, actors, new(0, 0, 0), (_, _) => false);
        Assert.Empty(audio.Emits);
        ProductUpdateFacts held = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 2, 2, 60, 0, 0, 1d / 60);
        presentation.AdvanceMobileFeedback(held, actors, new(0, 0, 0), (_, _) => false);
        Assert.Empty(audio.Emits);
        presentation.AdvanceMobileFeedback(OuterUpdate(3), actors, new(0, 0, 0), (_, _) => true);
        presentation.AdvanceMobileFeedback(OuterUpdate(3), actors, new(0, 0, 0), (_, _) => true);
        AudioEmitRequest cue = Assert.Single(audio.Emits);
        Assert.EndsWith(".sound.11", cue.SignalId, StringComparison.Ordinal);
        Assert.Equal(.25F, cue.Descriptor.Volume);
        Assert.Equal(AudioEmitterKind.World3d, cue.Descriptor.EmitterKind);
    }

    [Fact]
    public void Completed_effect_outcomes_use_the_existing_sparkle_path_and_restore_does_not_replay_it()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3));
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), appearance, MediaInputs(classic: ClassicEffects()));
        DaggerfallEffectCatalog catalog = new([
            new("sparkle", "sparkle", DaggerfallEffectStacking.Stack, 1, 1, Feedback: DaggerfallEffectFeedback.MagicSparkle)]);
        using DaggerfallEffectLifecycle effects = new(actors, catalog);
        List<DaggerfallEffectOutcome> outcomes = [];
        effects.Completed += outcome => { outcomes.Add(outcome); presentation.ReactEffectOutcome(outcome, actors, null, 1, 2); };
        DaggerfallEffectRequest request = new("sparkle-a", "sparkle", "spell-a", 1, 11, "settings", "magic", null, 1, 2, JsonSerializer.SerializeToElement(new { }));
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, effects.Start(request));
        presentation.ReactEffectOutcome(Assert.Single(outcomes), actors, null, 1, 2);
        Assert.Equal(1, EffectCount(presentation));
        Assert.Equal(new WorldPoint(2, 0, 3), Effect(presentation).Position);
        var saved = effects.Capture();
        using ActorsState restoredActors = ActorsWithNpc(11, HealthyMechanics(), new(2, 0, 3));
        using DaggerfallSiteAppearance restoredPresentation = new(MediaContent(releases), new AppearanceFake(releases), MediaInputs(classic: ClassicEffects()));
        using DaggerfallEffectLifecycle restoredEffects = new(restoredActors, catalog);
        restoredEffects.Completed += outcome => restoredPresentation.ReactEffectOutcome(outcome, restoredActors, null, 1, 2);
        restoredEffects.Restore(saved);
        Assert.Single(restoredEffects.Active);
        Assert.Equal(0, EffectCount(restoredPresentation));
        Assert.Single(outcomes);
        Assert.Equal(1, EffectCount(presentation));
        Assert.True(effects.Cancel(Rusty.Engine.Mechanics.EffectInstanceId.Parse("sparkle-a")));
        Assert.Equal(0, EffectCount(presentation));
    }

    private static DaggerfallSiteProfile FeedbackInputs(int mobile, bool parry = false) => MediaInputs(
        feedback: new(mobile, "sound.10", "sound.11", "sound.12", parry, 0), audio: [
            new("swing", "audio/swing.wav", Hash), new("hit1", "audio/hit.wav", Hash), new("hit2", "audio/hit2.wav", Hash), new("hit3", "audio/hit3.wav", Hash), new("hit4", "audio/hit4.wav", Hash),
            new("sound.10", "audio/swing.wav", Hash), new("sound.11", "audio/swing.wav", Hash), new("sound.12", "audio/swing.wav", Hash),
            new("sound.347", "audio/swing.wav", Hash), new("sound.3", "audio/swing.wav", Hash),
            new("sound.436", "audio/swing.wav", Hash)]);
}
