using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Non-blocking spatialized TTS voice-bark director for active hostile/vendetta NPC confrontations (Den #11164).
/// Opens authored male/female voice clips from <see cref="DaggerfallHostilityBarkCatalog"/> via
/// <see cref="IContentService"/> and plays them as 3D voices anchored to the speaking actor, with a global
/// anti-overlap cooldown so simultaneous kin shouts never collapse into an unintelligible chorus.
/// </summary>
internal sealed class DaggerfallHostilityAudioBarks(
    IContentService content,
    IAudioService? audio,
    DaggerfallHostilityBarkCatalog catalog,
    float globalCooldownSeconds = 2.2f,
    float maxDistanceMeters = 24f,
    float volume = 0.95f) : IDisposable
{
    private sealed record ActiveBarkVoice(AudioVoice Voice, AudioSourceDescriptor Descriptor, long ActorId, string ClipId, float RemainingSeconds);

    private readonly Dictionary<string, AudioClip> _clips = new(StringComparer.Ordinal);
    private readonly Dictionary<AudioVoiceHandle, ActiveBarkVoice> _activeVoices = [];
    private readonly float _globalCooldownSeconds = MathF.Max(0f, globalCooldownSeconds);
    private readonly float _maxDistanceMeters = MathF.Max(4f, maxDistanceMeters);
    private readonly float _volume = Math.Clamp(volume, 0f, 1f);
    private float _cooldownRemainingSeconds;
    private bool _disposed;

    internal int PlayedCount { get; private set; }
    internal int ThrottledCount { get; private set; }
    internal int FailedOpenCount { get; private set; }
    internal string? LastPlayedClipId { get; private set; }
    internal int ActiveVoiceCount => _activeVoices.Count;
    internal float CooldownRemainingSeconds => _cooldownRemainingSeconds;

    /// <summary>
    /// Attempts to play one spatialized hostility bark for <paramref name="actorId"/>.
    /// Returns <see langword="true"/> when a clip was emitted, or <see langword="false"/> if throttled,
    /// muted, or unavailable.
    /// </summary>
    internal bool TryPlay(long actorId, string kindSlug, string? gender, WorldPoint? position)
    {
        if (_disposed || audio is null || catalog.Clips.Count == 0)
            return false;

        if (_cooldownRemainingSeconds > 0f)
        {
            ThrottledCount++;
            return false;
        }

        DaggerfallHostilityBarkClip? entry = catalog.Select(kindSlug, gender, actorId);
        if (entry is null)
            return false;

        if (!_clips.TryGetValue(entry.Id, out AudioClip? clip))
        {
            try
            {
                clip = audio.OpenClip(content, entry.Path, entry.Sha256);
                _clips[entry.Id] = clip;
            }
            catch (InvalidOperationException)
            {
                FailedOpenCount++;
                return false;
            }
            catch (EngineCallException)
            {
                FailedOpenCount++;
                return false;
            }
        }

        bool spatial = position is not null;
        var descriptor = new AudioSourceDescriptor(
            clip,
            AudioBus.Sfx,
            _volume,
            1f,
            false,
            spatial ? 1f : 0f,
            _maxDistanceMeters,
            AudioRolloff.Linear,
            0f,
            spatial ? AudioEmitterKind.World3d : AudioEmitterKind.Global2d,
            position?.ToVector() ?? Vector3.Zero,
            0,
            Vector3.Zero);

        AudioVoice voice = audio.CreateVoice(descriptor);
        float voiceLifetime = MathF.Max(1.0f, entry.DurationSeconds + 0.35f);
        _activeVoices[voice.Handle] = new ActiveBarkVoice(voice, descriptor, actorId, entry.Id, voiceLifetime);
        _cooldownRemainingSeconds = MathF.Max(_globalCooldownSeconds, MathF.Min(entry.DurationSeconds * 0.75f, 2.8f));
        PlayedCount++;
        LastPlayedClipId = entry.Id;
        return true;
    }

    /// <summary>Advances anti-overlap cooldown, tracks moving speakers in 3D, and retires completed or defeated voices.</summary>
    internal void Step(float deltaSeconds, ActorsState actors)
    {
        if (_disposed)
            return;
        float dt = MathF.Max(0f, deltaSeconds);
        if (_cooldownRemainingSeconds > 0f)
            _cooldownRemainingSeconds = MathF.Max(0f, _cooldownRemainingSeconds - dt);

        if (audio is null || _activeVoices.Count == 0)
            return;

        foreach (var (handle, entry) in _activeVoices.ToArray())
        {
            float remaining = entry.RemainingSeconds - dt;
            if (remaining <= 0f || !actors.TryGet(entry.ActorId, out ActorState? speaker) || speaker.IsDefeated)
            {
                entry.Voice.Dispose();
                _activeVoices.Remove(handle);
                continue;
            }

            Vector3 livePosition = speaker.Position.ToVector() + (Vector3.UnitY * 1.6f);
            AudioSourceDescriptor nextDescriptor = entry.Descriptor.EmitterKind == AudioEmitterKind.World3d
                ? entry.Descriptor with { Position = livePosition }
                : entry.Descriptor;
            if (nextDescriptor != entry.Descriptor)
                audio.UpdateVoice(new AudioVoiceUpdateRequest(entry.Voice, nextDescriptor));
            _activeVoices[handle] = entry with { Descriptor = nextDescriptor, RemainingSeconds = remaining };
        }
    }

    /// <summary>Stops active voices on scene transition while keeping opened clip buffers reusable.</summary>
    internal void ClearActiveVoices()
    {
        foreach (ActiveBarkVoice entry in _activeVoices.Values)
            entry.Voice.Dispose();
        _activeVoices.Clear();
        _cooldownRemainingSeconds = 0f;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ClearActiveVoices();
        foreach (AudioClip clip in _clips.Values)
            clip.Dispose();
        _clips.Clear();
    }
}
