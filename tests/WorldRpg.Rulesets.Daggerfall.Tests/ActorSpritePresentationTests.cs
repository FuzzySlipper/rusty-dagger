using WorldRpg.Kit.Combat;
using System.Numerics;
using System.Reflection;
using System.Text;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Actor sprite presentation: directional sectors, rest states, attack alternates, markers, hit variants and blood effects.</summary>
public sealed class ActorSpritePresentationTests
{
    [Fact]
    public void Normalized_actor_states_preserve_all_directional_sectors_and_select_an_explicit_sector()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        NormalizedSpriteState state = inputs.ActorSprites.Values
            .SelectMany(sprite => sprite.States.Values)
            .First(value => value.Orientations.Count == 8);

        Assert.Equal(Enumerable.Range(0, 8), state.Orientations.Keys.OrderBy(key => key));
        foreach ((int sector, IReadOnlyList<uint> frames) in state.Orientations)
        {
            Assert.NotEmpty(frames);
            Assert.Equal(frames, state.SelectOrientation(sector));
        }

        NormalizedSpriteState sparse = new("idle", [10], 8F, true)
        {
            Orientations = new Dictionary<int, IReadOnlyList<uint>>
            {
                [0] = [10],
                [4] = [40],
            },
        };
        Assert.Equal([40u], sparse.SelectOrientation(4));
        Assert.Throws<InvalidOperationException>(() => sparse.SelectOrientation(1));
    }

    [Fact]
    public void Authored_actor_presentation_resolves_rest_state_and_effective_playback_without_mobile_specific_runtime_policy()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        string scenario = File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        DaggerfallSiteProfile ratInputs = DaggerfallSiteContent.Read(ImportContent(root), Encoding.UTF8.GetBytes(scenario), definitions);
        NormalizedActorSprite rat = SpriteFor(ratInputs, "rat");
        Assert.Equal("ratIdle", rat.PreferredRestState);

        DaggerfallSiteProfile impInputs = DaggerfallSiteContent.Read(ImportContent(root), Encoding.UTF8.GetBytes(scenario.Replace("\"actor\": \"rat\"", "\"actor\": \"imp\"", StringComparison.Ordinal)), definitions);
        Assert.Equal("move", SpriteFor(impInputs, "imp").PreferredRestState);
        Assert.Equal(10F, SpriteFor(impInputs, "imp").States["move"].EffectiveFramesPerSecond);
        Assert.DoesNotContain("idle", SpriteFor(impInputs, "imp").States.Keys);

        DaggerfallSiteProfile batInputs = DaggerfallSiteContent.Read(ImportContent(root), Encoding.UTF8.GetBytes(scenario.Replace("\"actor\": \"rat\"", "\"actor\": \"giant-bat\"", StringComparison.Ordinal)), definitions);
        Assert.Equal("move", SpriteFor(batInputs, "giant-bat").PreferredRestState);
        Assert.Equal(10F, SpriteFor(batInputs, "giant-bat").States["move"].EffectiveFramesPerSecond);
        Assert.DoesNotContain("idle", SpriteFor(batInputs, "giant-bat").States.Keys);

        NormalizedActorSprite ordinary = SpriteFor(ratInputs, "skeletal-warrior");
        Assert.Equal("idle", ordinary.PreferredRestState);
        Assert.All(ordinary.States.Values, state => Assert.Equal(state.FramesPerSecond, state.EffectiveFramesPerSecond));
    }

    [Fact]
    public void Directional_sprite_sectors_follow_actor_heading_with_classic_octant_boundaries()
    {
        Assert.Equal(0, DaggerfallSiteAppearance.RelativeSector(0f, 0f, -1f));
        Assert.Equal(4, DaggerfallSiteAppearance.RelativeSector(0f, 0f, 1f));
        Assert.Equal(6, DaggerfallSiteAppearance.RelativeSector(0f, 1f, 0f));
        Assert.Equal(2, DaggerfallSiteAppearance.RelativeSector(0f, -1f, 0f));
        Assert.Equal(0, DaggerfallSiteAppearance.RelativeSector(MathF.PI / 2f, 1f, 0f));
        Assert.Equal(0, DaggerfallSiteAppearance.RelativeSector(0f, 0f, 0f));

        float twentyTwo = 22f * MathF.PI / 180f;
        float twentyThree = 23f * MathF.PI / 180f;
        float halfSector = MathF.PI / 8f;
        Assert.Equal(0, DaggerfallSiteAppearance.RelativeSector(0f, MathF.Sin(twentyTwo), -MathF.Cos(twentyTwo)));
        Assert.Equal(7, DaggerfallSiteAppearance.RelativeSector(0f, MathF.Sin(halfSector), -MathF.Cos(halfSector)));
        Assert.Equal(1, DaggerfallSiteAppearance.RelativeSector(0f, -MathF.Sin(halfSector), -MathF.Cos(halfSector)));
        Assert.Equal(7, DaggerfallSiteAppearance.RelativeSector(0f, MathF.Sin(twentyThree), -MathF.Cos(twentyThree)));
        Assert.Equal(1, DaggerfallSiteAppearance.RelativeSector(0f, -MathF.Sin(twentyThree), -MathF.Cos(twentyThree)));
    }

    [Fact]
    public void Enemy_idle_transition_returns_to_the_authored_preferred_rest_state()
    {
        List<string> releases = [];
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), new AppearanceFake(releases), MediaInputs(preferredRestState: "ratIdle"));

        presentation.React(new EnemyBehaviorTransitionFact(11, EnemyBehaviorState.Idle, EnemyBehaviorState.Chase, 1, 1));
        Assert.Equal("move", Visual(presentation).State);
        presentation.React(new EnemyBehaviorTransitionFact(11, EnemyBehaviorState.Chase, EnemyBehaviorState.Idle, 1, 2));

        Assert.Equal("ratIdle", Visual(presentation).State);
    }

    [Fact]
    public void Direction_change_maps_the_current_idle_and_attack_frame_without_recreating_playback()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(directional: true));
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new WorldPoint(0f, 0f, 0f));

        presentation.UpdateDirections(actors, new WorldPoint(0f, 0f, -1f));
        Assert.Equal(0u, appearance.SetFrameRequests.Last().FrameId);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 1, 1));
        int playbackCount = appearance.PlaybackRequests.Count;
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Playing, 0d, 0, 1, false),
            true));
        presentation.Advance(OuterUpdate(1));
        presentation.UpdateDirections(actors, new WorldPoint(1f, 0f, 0f));

        Assert.Equal(playbackCount, appearance.PlaybackRequests.Count);
        Assert.Equal(3u, appearance.SetFrameRequests.Last().FrameId);
    }

    [Fact]
    public void Direction_change_skips_a_source_frame_absent_from_a_shorter_direction()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), appearance, MediaInputs(primaryFrames: [1], directional: true, shortAttackDirection: true));
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new WorldPoint(0f, 0f, 0f));

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 1, 1));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Playing, 0d, 3, 1, false),
            true));
        presentation.Advance(OuterUpdate(1));
        int frameUpdates = appearance.SetFrameRequests.Count;
        presentation.UpdateDirections(actors, new WorldPoint(1f, 0f, 0f));

        Assert.Equal(frameUpdates, appearance.SetFrameRequests.Count);
    }

    [Fact]
    public void Attack_alternate_selection_is_keyed_by_stable_event_identity_and_respects_authored_weights()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        KeyedRandomFake random = KeyedRandomFake.Create(40);
        DaggerfallSiteProfile favoredAlternate = MediaInputs(primaryChance: 60);

        using (DaggerfallSiteAppearance first = new(content, appearance, favoredAlternate, random: random.Service))
        {
            first.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));
            SpritePlaybackCreateRequest selected = appearance.PlaybackRequests.Last();
            Assert.Equal([3u], selected.Frames.Span.ToArray().Select(frame => frame.FrameId));

            int beforeDuplicate = appearance.PlaybackRequests.Count;
            first.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));
            Assert.Equal(beforeDuplicate, appearance.PlaybackRequests.Count);
        }

        AppearanceFake secondAppearance = new(releases);
        using (DaggerfallSiteAppearance second = new(content, secondAppearance, MediaInputs(primaryChance: 20), random: KeyedRandomFake.Create(40).Service))
        {
            second.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));
            Assert.Equal([2u], secondAppearance.PlaybackRequests.Last().Frames.Span.ToArray().Select(frame => frame.FrameId));
        }

        Assert.Equal("daggerfall.media.attack-alternate.v1", random.Requests[0].Scope);
        Assert.Single(random.Requests); // A start cannot choose the hit cue before an applied result.
    }

    [Fact]
    public void A_ranged_mobile_plays_its_published_ranged_state_for_every_attack_without_the_melee_roll()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        KeyedRandomFake random = KeyedRandomFake.Create(40);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(rangedFrames: [3, 2, 0, 0, 0, -1, 1, 1, 2, 3]), random: random.Service);

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));

        // The donor plays a ranged mobile's ranged animation for every attack, so the published
        // ranged sequence replaces the melee alternate pool instead of joining it: its authored
        // frame order plays verbatim, and the marker step becomes the playback marker the donor
        // launches its missile on, with no alternate roll drawn.
        Assert.Equal("rangedAttack1", Visual(presentation).State);
        SpritePlaybackCreateRequest request = appearance.PlaybackRequests.Last();
        Assert.Equal(new uint[] { 3, 2, 0, 0, 0, 1, 1, 2, 3 }, request.Frames.Span.ToArray().Select(frame => frame.FrameId).ToArray());
        Assert.Equal([new SpritePlaybackMarker(6, 5)], request.Markers.Span.ToArray());
        Assert.DoesNotContain(random.Requests, request => request.Scope == "daggerfall.media.attack-alternate.v1");
    }

    [Fact]
    public void A_mobile_without_a_ranged_declaration_keeps_playing_its_melee_pool()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1], includeAlternate: false));

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));

        Assert.Equal("primaryAttack", Visual(presentation).State);
        SpritePlaybackCreateRequest request = appearance.PlaybackRequests.Last();
        Assert.Equal(new uint[] { 2, 3 }, request.Frames.Span.ToArray().Select(frame => frame.FrameId).ToArray());
        Assert.Equal([new SpritePlaybackMarker(2, 1)], request.Markers.Span.ToArray());
    }

    [Fact]
    public void Marker_crossings_are_consumed_once_without_emitting_a_second_combat_presentation()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            new[] { new SpritePlaybackMarkerCrossing(2, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 1, false),
            true));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            new[] { new SpritePlaybackMarkerCrossing(2, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 2, false),
            true));

        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 3, 4));
        int playbacksBefore = appearance.PlaybackRequests.Count;
        int audioBefore = audio.Emits.Count;

        presentation.Advance(OuterUpdate(1));
        presentation.Advance(OuterUpdate(2));

        Assert.Equal(playbacksBefore, appearance.PlaybackRequests.Count);
        // The marker admits one strike. No hit is audible until the ruleset applies it.
        Assert.Equal(audioBefore, audio.Emits.Count);
        AttackImpactNotice impact = Assert.Single(presentation.TakeAttackImpacts());
        Assert.False(impact.Expired);
        Assert.Equal(11, impact.AttackerId);
        Assert.Equal(12, impact.TargetId);
        Assert.Empty(presentation.TakeAttackImpacts());
        FieldInfo actorsField = typeof(DaggerfallSiteAppearance).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        System.Collections.IDictionary visuals = (System.Collections.IDictionary)actorsField.GetValue(presentation)!;
        object visual = visuals[11L]!;
        FieldInfo crossingField = visual.GetType().GetField("<LastMarkerCrossing>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ulong consumed = (ulong)crossingField.GetValue(visual)!;
        Assert.Equal((ulong)1, consumed);
    }

    [Fact]
    public void Duplicate_attack_delivery_does_not_restart_playback_or_duplicate_tuned_audio()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        DaggerfallPresentationAudioTuning tuning = new(.25F, 1.5F, .75F, 12F) { ContactPitch = 1.5F };
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(), audio.Service, tuning);
        EnemyAttackStartedFact hit = new(11, 12, true, 7, 9);

        presentation.React(hit);
        int playbackCount = appearance.PlaybackRequests.Count;
        presentation.React(hit);

        Assert.Equal(playbackCount, appearance.PlaybackRequests.Count);
        Assert.Empty(audio.Emits);
        AttackHitFact applied = new(11, 12, 3, 3, 0, true, 7, 9);
        presentation.React(applied);
        presentation.React(applied);
        AudioEmitRequest emitted = Assert.Single(audio.Emits);
        Assert.Equal(.25F, emitted.Descriptor.Volume);
        Assert.Equal(1.5F, emitted.Descriptor.Pitch);
        Assert.Equal(.75F, emitted.Descriptor.SpatialBlend);
        Assert.Equal(12F, emitted.Descriptor.MaxDistance);
        Assert.Equal(AudioRolloff.Linear, emitted.Descriptor.Rolloff);
        Assert.True(float.IsFinite(emitted.Descriptor.MaxDistance));
        Assert.True(emitted.Descriptor.MaxDistance > 0F);
    }

    [Fact]
    public void Completed_one_shot_returns_to_rest_on_the_next_distinct_outer_update_even_when_engine_does_not_advance()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs());
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));
        int beforeCompletion = appearance.PlaybackRequests.Count;
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Completed, 0D, 0, 1, true),
            true));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Completed, 0D, 0, 2, true),
            false));

        presentation.Advance(OuterUpdate(1));
        Assert.Equal(beforeCompletion, appearance.PlaybackRequests.Count);

        presentation.Advance(OuterUpdate(2));
        Assert.Equal(beforeCompletion + 1, appearance.PlaybackRequests.Count);
        Assert.Equal([0u], appearance.PlaybackRequests.Last().Frames.Span.ToArray().Select(frame => frame.FrameId));
        int advancesAfterRest = appearance.AdvanceRequests.Count;

        presentation.Advance(OuterUpdate(2));
        Assert.Equal(advancesAfterRest, appearance.AdvanceRequests.Count);
        Assert.Equal(beforeCompletion + 1, appearance.PlaybackRequests.Count);
    }

    [Fact]
    public void Trailing_attack_marker_is_rejected_before_engine_playback_creation()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        DaggerfallSiteProfile inputs = MediaInputs(primaryFrames: [0, -1]);
        using DaggerfallSiteAppearance presentation = new(content, appearance, inputs);
        int before = appearance.PlaybackRequests.Count;

        Assert.Throws<InvalidOperationException>(() => presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9)));
        Assert.Equal(before, appearance.PlaybackRequests.Count);
    }

    [Fact]
    public void Actor_atlas_frames_preserve_normalized_per_crop_world_geometry()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        IReadOnlyList<NormalizedAtlasFrame> crops =
        [
            new NormalizedAtlasFrame(0, 0, 0, 4, 8, new Vector2(1.5F, 3F)),
            new NormalizedAtlasFrame(1, 4, 0, 6, 12, new Vector2(2.25F, 4.5F)),
            new NormalizedAtlasFrame(2, 10, 0, 10, 5, new Vector2(3.75F, 1.875F)),
            new NormalizedAtlasFrame(3, 20, 0, 12, 16, new Vector2(4.5F, 6F)),
        ];
        using DaggerfallSiteAppearance presentation = new(MediaContent(releases), appearance, MediaInputs(actorFrames: crops));

        SpriteAtlasFrame[] frames = appearance.AtlasRequests.Single().Frames.Span.ToArray();
        Assert.All(frames, frame => Assert.True(frame.HasSize));
        Assert.Equal(crops.Select(crop => crop.DisplaySize), frames.Select(frame => (Vector2?)frame.Size));
        Assert.Equal(Vector2.One, appearance.SpriteRequests.Single().Size);
    }

    [Fact]
    public void Non_advanced_receipts_cannot_consume_markers_or_complete_a_one_shot()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 2, 3));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            new[] { new SpritePlaybackMarkerCrossing(2, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Completed, 0D, 0, 1, true),
            false));

        presentation.Advance(OuterUpdate(1));

        DaggerfallSiteAppearance.ActorVisual visual = Visual(presentation);
        Assert.Equal((ulong)0, visual.LastMarkerCrossing);
        Assert.False(visual.CompletedOuterUpdate);
        Assert.Empty(audio.Emits);
    }

    [Fact]
    public void Missed_attack_markers_never_emit_hit_variants()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, false, 2, 3));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            new[] { new SpritePlaybackMarkerCrossing(2, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 1, false),
            true));

        presentation.Advance(OuterUpdate(1));

        Assert.DoesNotContain(audio.Emits, emitted => emitted.SignalId.Contains("hit", StringComparison.Ordinal));
    }

    [Fact]
    public void Hit_variant_is_keyed_by_event_identity_and_can_select_beyond_the_first_classic_hit_clip()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AttackHitFact hit = new(11, 12, 3, 3, 0, true, 8, 13) { Feedback = new(true, "swing") };
        AudioRecorder firstAudio = AudioRecorder.Create();
        AppearanceFake firstAppearance = new(releases);
        DaggerfallSiteProfile hitInputs = MediaInputs(includeAlternate: false);
        using (DaggerfallSiteAppearance first = new(content, firstAppearance, hitInputs, firstAudio.Service, random: KeyedRandomFake.Create(5).Service))
        {
            first.React(hit);
            AudioEmitRequest emitted = Assert.Single(firstAudio.Emits);
            Assert.Equal((ulong)6, emitted.Descriptor.Clip.Handle.Value);
        }

        // The clips an appearance opens are its own now, so closing it releases every one of them.
        Assert.Equal(hitInputs.Audio.Count, firstAudio.ReleasedClips);

        AudioRecorder secondAudio = AudioRecorder.Create();
        using (DaggerfallSiteAppearance second = new(content, new AppearanceFake(releases), MediaInputs(includeAlternate: false), secondAudio.Service, random: KeyedRandomFake.Create(5).Service))
        {
            second.React(hit);
        }

        Assert.Equal(firstAudio.Emits.Single().SignalId, secondAudio.Emits.Single().SignalId);
        Assert.Equal(firstAudio.Emits.Single().Descriptor.Clip.Handle, secondAudio.Emits.Single().Descriptor.Clip.Handle);
    }

    [Fact]
    public void Hit_variant_selection_uses_the_authored_contiguous_catalog_cardinality()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AudioRecorder audio = AudioRecorder.Create();
        IReadOnlyList<NormalizedAudioClip> authoredAudio =
        [
            new NormalizedAudioClip("swing", "audio/swing.wav", Hash),
            new NormalizedAudioClip("hit1", "audio/hit.wav", Hash),
            new NormalizedAudioClip("hit2", "audio/hit2.wav", Hash),
        ];

        using DaggerfallSiteAppearance presentation = new(content, new AppearanceFake(releases), MediaInputs(includeAlternate: false, audio: authoredAudio), audio.Service, random: KeyedRandomFake.Create(2).Service);
        presentation.React(new AttackHitFact(11, 12, 1, 1, 0, true, 8, 13) { Feedback = new(true, "swing") });

        Assert.Equal((ulong)3, Assert.Single(audio.Emits).Descriptor.Clip.Handle.Value);
    }

    [Fact]
    public void Classic_blood_effect_is_keyed_to_the_player_hit_and_retires_after_its_final_frame()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicEffects()));
        WorldPoint targetPosition = new(7F, 2F, -3F);
        using ActorsState actors = ActorsAt(new WorldPoint(1F, 1F, 1F));
        presentation.Publish(actors);
        actors.Get(12).ApplyPose(new ActorPose(targetPosition, 0F));
        AttackHitFact hit = new(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1d, 0, false, 5, 8);

        presentation.React(hit, actors);
        presentation.React(hit, actors);

        Assert.Equal(2, appearance.PlaybackRequests.Count);
        Assert.Equal(1, EffectCount(presentation));
        Assert.Equal(targetPosition, Effect(presentation).Position);
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 1, true), true));
        presentation.Advance(OuterUpdate(1));
        Assert.Equal(1, EffectCount(presentation));
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 2, true), true));
        presentation.Advance(OuterUpdate(2));
        Assert.Equal(0, EffectCount(presentation));
    }

    [Fact]
    public void Classic_effect_atlas_frames_do_not_override_varied_authored_display_geometry()
    {
        Vector2[] expectedSizes = [new(.25F, .5F), new(.75F, .3F), new(.4F, .9F)];
        for (int ordinal = 0; ordinal < expectedSizes.Length; ordinal++)
        {
            List<string> releases = [];
            ContentFake content = MediaContent(releases);
            AppearanceFake appearance = new(releases);
            NormalizedClassicPresentation classic = ClassicEffects(expectedSizes);
            DaggerfallSiteProfile inputs = MediaInputs(classic: classic, spriteActorId: 12,
                feedback: new(0, "swing", "swing", "swing", false, ordinal));
            using DaggerfallSiteAppearance presentation = new(content, appearance, inputs, random: KeyedRandomFake.Create(2 - ordinal).Service);
            using ActorsState actors = ActorsAt(new WorldPoint(2F, 0F, 3F));

            presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1d, ordinal, false, 2, 3), actors);

            Assert.All(appearance.AtlasRequests.Last().Frames.Span.ToArray(), frame => Assert.False(frame.HasSize));
            Assert.Equal(expectedSizes[ordinal], appearance.SpriteRequests.Last().Size);
            presentation.Publish(actors);
            Assert.All(appearance.Snapshots.Last(), fact => Assert.InRange(fact.ObjectId, 1UL, (1UL << 53) - 1));
            Assert.Equal(appearance.Snapshots.Last().Count(), appearance.Snapshots.Last().Select(fact => fact.ObjectId).Distinct().Count());
        }
    }

}
