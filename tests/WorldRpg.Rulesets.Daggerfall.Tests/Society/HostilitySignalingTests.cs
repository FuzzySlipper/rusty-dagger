using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11164: Non-blocking NPC hostility signaling (male/female Kokoro TTS audio barks + near-actor comic speech bubbles):
/// - Authored TTS voice-bark manifest in <c>daggerfall.people/audio/hostility/hostility-barks.json</c> carries male &amp; female
///   clips for all 4 vendetta hostility kinds with verified SHA-256 digests and real .ogg audio files.
/// - <see cref="DaggerfallHostilityAudioBarks"/> selects gender- &amp; kind-matched spatialized 3D voices, tracks moving speakers,
///   and throttles simultaneous shouts via global anti-overlap cooldown.
/// - <see cref="DaggerfallSpeechBubbles"/> projects speakers in front, to the flank, and behind into normalized HUD viewport
///   coordinates with vertical de-overlap stacking, fade-out, and zero gameplay pause.
/// - <see cref="DaggerfallSession"/> hooks <see cref="DaggerfallFeudVendetta.HostilitySignaled"/> into both paths and exposes
///   <c>social.hostility-signal</c> for live mode switching and playtesting.
/// </summary>
public sealed class HostilitySignalingTests
{
    [Fact]
    public void Authored_hostility_tts_bark_catalog_covers_male_and_female_across_all_vendetta_kinds_with_valid_ogg_files()
    {
        DaggerfallHostilityBarkCatalog catalog = TestPayload.Definitions.People.HostilityBarks;
        Assert.True(catalog.Clips.Count >= 16, $"Expected at least 16 male/female hostility TTS clips, found {catalog.Clips.Count}.");

        string[] kinds = ["kin-vendetta", "hired-tough", "bounty-hunter", "assassin"];
        string[] genders = ["male", "female"];
        foreach (string kind in kinds)
        {
            foreach (string gender in genders)
            {
                DaggerfallHostilityBarkClip? selected = catalog.Select(kind, gender, 12345L);
                Assert.NotNull(selected);
                Assert.Equal(kind, selected!.Kind);
                Assert.Equal(gender, selected.Gender);
                Assert.True(selected.DurationSeconds > 0.5f);
            }
        }

        // Every clip path in the manifest exists on disk and matches its SHA-256 digest.
        foreach (DaggerfallHostilityBarkClip clip in catalog.Clips)
        {
            string fullPath = Path.Combine(TestData.RepositoryRoot, "content", clip.Path);
            Assert.True(File.Exists(fullPath), $"Missing hostility TTS audio file '{fullPath}'.");
            byte[] bytes = File.ReadAllBytes(fullPath);
            Assert.True(bytes.Length > 4096, $"Hostility TTS audio file '{clip.Id}' is unexpectedly small ({bytes.Length} bytes).");
            string actualSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Assert.Equal(clip.Sha256, DaggerfallContentHash.Parse(actualSha, clip.Id));
        }
    }

    [Fact]
    public void Speech_bubbles_project_front_flank_and_behind_speakers_stack_overlaps_and_fade_without_pausing()
    {
        DaggerfallSpeechBubbles bubbles = new();
        ActorsState actors = new();
        FirstPersonCameraTuning camera = DaggerfallTuning.Defaults.Camera;
        // Player at origin facing -Z (yaw = 0, pitch = 0).
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);

        // Front speaker directly ahead at (0, 0, -6), right-flank at (8, 0, -2), behind-left at (-4, 0, 5).
        bubbles.Show(101L, "Baren Hawkwing", "kin-vendetta", "You spilled our blood!", new WorldPoint(0f, 0f, -6f), durationSeconds: 4.0f);
        bubbles.Show(102L, "Hired Tough", "hired-tough", "They paid me to collect!", new WorldPoint(8f, 0f, -2f), durationSeconds: 4.0f);
        bubbles.Show(103L, "Assassin", "assassin", "The Brotherhood collects.", new WorldPoint(-4f, 0f, 5f), durationSeconds: 4.0f);

        IReadOnlyList<DaggerfallSpeechBubbleView> views = bubbles.Read(player, camera, actors);
        Assert.Equal(3, views.Count);

        DaggerfallSpeechBubbleView front = Assert.Single(views, v => v.ActorId == 101L);
        Assert.Equal("none", front.Offscreen);
        Assert.InRange(front.ScreenX, 0.45f, 0.55f);
        Assert.InRange(front.ScreenY, 0.12f, 0.72f);
        Assert.Equal(1f, front.Opacity);

        DaggerfallSpeechBubbleView flank = Assert.Single(views, v => v.ActorId == 102L);
        Assert.Equal("right", flank.Offscreen);
        Assert.Equal(0.88f, flank.ScreenX);

        DaggerfallSpeechBubbleView behind = Assert.Single(views, v => v.ActorId == 103L);
        Assert.Equal("behind-left", behind.Offscreen);
        Assert.Equal(0.12f, behind.ScreenX);

        // Overlapping speakers in the same spot get vertically separated.
        bubbles.Clear();
        bubbles.Show(201L, "Kin A", "kin-vendetta", "Line A", new WorldPoint(0f, 0f, -6f), durationSeconds: 2.0f);
        bubbles.Show(202L, "Kin B", "kin-vendetta", "Line B", new WorldPoint(0.2f, 0f, -6f), durationSeconds: 2.0f);
        IReadOnlyList<DaggerfallSpeechBubbleView> stacked = bubbles.Read(player, camera, actors);
        Assert.Equal(2, stacked.Count);
        Assert.True(MathF.Abs(stacked[0].ScreenY - stacked[1].ScreenY) >= 0.08f, "Simultaneous nearby bubbles should stack vertically.");

        // Fade-out near end of lifetime, then automatic expiry.
        bubbles.Step(1.6f, actors); // 0.4s remaining (< 0.8s FadeOutSeconds)
        IReadOnlyList<DaggerfallSpeechBubbleView> fading = bubbles.Read(player, camera, actors);
        Assert.All(fading, b => Assert.InRange(b.Opacity, 0.45f, 0.55f));

        bubbles.Step(0.5f, actors);
        Assert.Equal(0, bubbles.ActiveCount);
        Assert.Empty(bubbles.Read(player, camera, actors));
    }

    [Fact]
    public void Audio_bark_director_matches_gender_and_throttles_overlapping_simultaneous_shouts()
    {
        List<string> releases = [];
        ContentFake content = new(releases);
        foreach (DaggerfallHostilityBarkClip clip in TestPayload.Definitions.People.HostilityBarks.Clips)
            content.Add(clip.Path, clip.Sha256, [0x4F, 0x67, 0x67, 0x53]);
        AudioRecorder audio = AudioRecorder.Create();
        ActorsState actors = new();

        using DaggerfallHostilityAudioBarks director = new(content, audio.Service, TestPayload.Definitions.People.HostilityBarks, globalCooldownSeconds: 2.2f);

        // First shout (female kin) plays immediately.
        Assert.True(director.TryPlay(501L, "kin-vendetta", "Female", new WorldPoint(2f, 0f, -5f)));
        Assert.Equal(1, director.PlayedCount);
        Assert.Contains("kin-vendetta-female", director.LastPlayedClipId!);
        Assert.Equal(1, director.ActiveVoiceCount);

        // Second shout 0s later is throttled to prevent garbled overlapping audio chorus.
        Assert.False(director.TryPlay(502L, "kin-vendetta", "Male", new WorldPoint(-2f, 0f, -5f)));
        Assert.Equal(1, director.PlayedCount);
        Assert.Equal(1, director.ThrottledCount);

        // After advancing past the global cooldown, a male assassin shout plays cleanly.
        director.Step(3.5f, actors);
        Assert.True(director.TryPlay(503L, "assassin", "Male", new WorldPoint(0f, 0f, -6f)));
        Assert.Equal(2, director.PlayedCount);
        Assert.Contains("assassin-male", director.LastPlayedClipId!);
    }

    [Fact]
    public void Session_signals_hostility_without_pausing_and_developer_command_switches_modes_and_fires_demo()
    {
        List<string> releases = [];
        DaggerfallSiteProfile profile = ReadInputs(TestData.RepositoryRoot);
        ContentFake content = new(releases);
        PopulateContent(content, profile);
        foreach (DaggerfallHostilityBarkClip clip in TestPayload.Definitions.People.HostilityBarks.Clips)
            content.Add(clip.Path, clip.Sha256, [0x4F, 0x67, 0x67, 0x53]);
        SpatialFake spatial = SpatialFake.Create(profile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, profile, DaggerfallTuning.Defaults));

        IDeveloperSocialCommands socialCmds = session.DebugCommands.OfType<IDeveloperSocialCommands>().Single();

        // Trigger a live non-blocking vendetta demo signal in Both mode.
        DebugCommandResult demo = socialCmds.HostilitySignal("demo-kin");
        Assert.True(demo.Succeeded, demo.Message);
        using (JsonDocument doc = JsonDocument.Parse(demo.Message))
        {
            Assert.Equal("Both", doc.RootElement.GetProperty("mode").GetString());
            Assert.True(doc.RootElement.GetProperty("catalogClips").GetInt32() >= 16);
            Assert.Equal(1, doc.RootElement.GetProperty("activeBubbles").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("audioPlayedCount").GetInt32());
            Assert.Single(doc.RootElement.GetProperty("bubbles").EnumerateArray());
        }
        // Must NEVER open a game-stopping modal (Law / Dialogue / DungeonText).
        Assert.False(session.Law.ModalOpen);
        Assert.Null(session.DungeonText.Projection);

        // Switching to bubbles-only immediately clears active voices and suppresses further audio barks while still emitting bubbles.
        Assert.True(socialCmds.HostilitySignal("bubbles").Succeeded);
        Assert.Equal(0, session.HostilityAudioBarks.ActiveVoiceCount);
        DebugCommandResult bubblesOnly = socialCmds.HostilitySignal("demo-assassin");
        Assert.True(bubblesOnly.Succeeded, bubblesOnly.Message);
        using (JsonDocument doc = JsonDocument.Parse(bubblesOnly.Message))
        {
            Assert.Equal("SpeechBubbles", doc.RootElement.GetProperty("mode").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("activeBubbles").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("audioPlayedCount").GetInt32()); // unchanged!
        }

        // Switching to audio-only immediately clears active bubbles and suppresses bubbles while emitting audio barks.
        Assert.True(socialCmds.HostilitySignal("audio").Succeeded);
        Assert.Equal(0, session.SpeechBubbles.ActiveCount);
        DebugCommandResult audioOnly = socialCmds.HostilitySignal("demo-hunter");
        Assert.True(audioOnly.Succeeded, audioOnly.Message);
        using (JsonDocument doc = JsonDocument.Parse(audioOnly.Message))
        {
            Assert.Equal("AudioBarks", doc.RootElement.GetProperty("mode").GetString());
            Assert.Equal(0, doc.RootElement.GetProperty("activeBubbles").GetInt32());
            Assert.Equal(2, doc.RootElement.GetProperty("audioPlayedCount").GetInt32());
            Assert.Contains("bounty-hunter", doc.RootElement.GetProperty("lastClipId").GetString()!);
        }

        // Switching to off clears active bubbles and voices.
        DebugCommandResult off = socialCmds.HostilitySignal("off");
        Assert.True(off.Succeeded, off.Message);
        Assert.Equal(0, session.SpeechBubbles.ActiveCount);
        Assert.Equal(0, session.HostilityAudioBarks.ActiveVoiceCount);
    }
}
