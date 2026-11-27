using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// One projected non-blocking comic speech bubble anchored near a hostile or shouting actor (Den #11164).
/// Normalized viewport coordinates (<c>0..1</c>) and edge clamp flags are computed authoritatively in C#.
/// </summary>
internal sealed record DaggerfallSpeechBubbleView(
    long ActorId,
    string SpeakerName,
    string Kind,
    string Text,
    float ScreenX,
    float ScreenY,
    float Opacity,
    string Offscreen,
    float DistanceMeters);

/// <summary>
/// Non-blocking near-actor comic speech-bubble owner (Den #11164).
/// Projects active shouting/vendetta NPCs from live 3D world poses into normalized screen-space anchors each update
/// without pausing simulation, with bounded concurrency, fade-out, and vertical de-overlap stacking.
/// </summary>
internal sealed class DaggerfallSpeechBubbles
{
    internal const int MaxSimultaneousBubbles = 3;
    internal const float DefaultDurationSeconds = 4.5f;
    internal const float FadeOutSeconds = 0.8f;
    internal const float SpeakerHeadHeightMeters = 2.05f;
    internal const float MaxDisplayDistanceMeters = 30.0f;
    private const float ReferenceAspectRatio = 16f / 9f;
    private const float MinScreenX = 0.12f;
    private const float MaxScreenX = 0.88f;
    private const float MinScreenY = 0.13f;
    private const float MaxScreenY = 0.72f;
    private const float OverlapThresholdX = 0.20f;
    private const float OverlapThresholdY = 0.10f;
    private const float OverlapStepY = 0.11f;

    private sealed class ActiveBubble(
        long actorId,
        string speakerName,
        string kind,
        string text,
        float durationSeconds,
        WorldPoint? fallbackPosition)
    {
        internal long ActorId { get; } = actorId;
        internal string SpeakerName { get; set; } = speakerName;
        internal string Kind { get; set; } = kind;
        internal string Text { get; set; } = text;
        internal float RemainingSeconds { get; set; } = durationSeconds;
        internal WorldPoint? LastKnownPosition { get; set; } = fallbackPosition;
    }

    private readonly List<ActiveBubble> _active = [];

    internal int ActiveCount => _active.Count;

    /// <summary>Publishes or refreshes a timed non-blocking comic speech bubble for <paramref name="actorId"/>.</summary>
    internal void Show(
        long actorId,
        string speakerName,
        string kind,
        string text,
        WorldPoint? position,
        float durationSeconds = DefaultDurationSeconds)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        float clampedDuration = MathF.Max(0.5f, durationSeconds);
        string safeSpeaker = string.IsNullOrWhiteSpace(speakerName) ? "Attacker" : speakerName.Trim();
        string safeKind = string.IsNullOrWhiteSpace(kind) ? "kin-vendetta" : kind.Trim().ToLowerInvariant();
        string safeText = text.Trim();

        ActiveBubble? existing = _active.FirstOrDefault(b => b.ActorId == actorId);
        if (existing is not null)
        {
            existing.SpeakerName = safeSpeaker;
            existing.Kind = safeKind;
            existing.Text = safeText;
            existing.RemainingSeconds = clampedDuration;
            if (position is not null)
                existing.LastKnownPosition = position;
            return;
        }

        while (_active.Count >= MaxSimultaneousBubbles)
            _active.RemoveAt(0);

        _active.Add(new ActiveBubble(actorId, safeSpeaker, safeKind, safeText, clampedDuration, position));
    }

    /// <summary>Advances bubble lifetimes and prunes expired or defeated speakers.</summary>
    internal void Step(float deltaSeconds, ActorsState actors)
    {
        float dt = MathF.Max(0f, deltaSeconds);
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            ActiveBubble entry = _active[i];
            entry.RemainingSeconds -= dt;
            if (entry.RemainingSeconds <= 0f)
            {
                _active.RemoveAt(i);
                continue;
            }
            if (actors.TryGet(entry.ActorId, out ActorState? actor))
            {
                if (actor.IsDefeated)
                {
                    _active.RemoveAt(i);
                    continue;
                }
                entry.LastKnownPosition = actor.Position;
            }
        }
    }

    /// <summary>Clears all transient bubbles (e.g. on scene transition or load).</summary>
    internal void Clear() => _active.Clear();

    /// <summary>Projects all active speech bubbles into normalized HUD viewport coordinates using the active camera.</summary>
    internal IReadOnlyList<DaggerfallSpeechBubbleView> Read(
        PlayerControlState player,
        FirstPersonCameraTuning cameraTuning,
        ActorsState actors)
    {
        if (_active.Count == 0 || player.Position is not WorldPoint playerFeet)
            return [];

        Vector3 eye = playerFeet.ToVector() + (Vector3.UnitY * cameraTuning.EyeHeight);
        (float sinYaw, float cosYaw) = MathF.SinCos(player.YawRadians);
        (float sinPitch, float cosPitch) = MathF.SinCos(player.PitchRadians);
        Vector3 forward = new(sinYaw * cosPitch, sinPitch, -cosYaw * cosPitch);
        Vector3 right = new(cosYaw, 0f, sinYaw);
        Vector3 up = Vector3.Cross(right, forward);

        float fovYRad = (float)(cameraTuning.FieldOfViewYDegrees * (Math.PI / 180.0));
        float tanHalfY = MathF.Tan(fovYRad * 0.5f);
        float tanHalfX = tanHalfY * ReferenceAspectRatio;

        List<DaggerfallSpeechBubbleView> projected = [];
        foreach (ActiveBubble entry in _active)
        {
            WorldPoint? worldPos = actors.TryGet(entry.ActorId, out ActorState? actor)
                ? actor.Position
                : entry.LastKnownPosition;
            if (worldPos is not WorldPoint pos)
                continue;

            Vector3 head = pos.ToVector() + (Vector3.UnitY * SpeakerHeadHeightMeters);
            Vector3 delta = head - eye;
            float distance = delta.Length();
            if (distance > MaxDisplayDistanceMeters)
                continue;

            float depth = Vector3.Dot(delta, forward);
            float xCam = Vector3.Dot(delta, right);
            float yCam = Vector3.Dot(delta, up);

            float screenX;
            float screenY;
            string offscreen;
            if (depth <= 0.15f)
            {
                // Speaker is behind or directly beside the player: anchor to the matching left/right edge.
                offscreen = xCam < 0f ? "behind-left" : "behind-right";
                screenX = xCam < 0f ? MinScreenX : MaxScreenX;
                screenY = 0.34f;
            }
            else
            {
                float ndcX = xCam / (depth * tanHalfX);
                float ndcY = yCam / (depth * tanHalfY);
                float rawX = 0.5f + (0.5f * ndcX);
                float rawY = 0.5f - (0.5f * ndcY);

                if (rawX < MinScreenX)
                {
                    offscreen = "left";
                    screenX = MinScreenX;
                }
                else if (rawX > MaxScreenX)
                {
                    offscreen = "right";
                    screenX = MaxScreenX;
                }
                else
                {
                    offscreen = "none";
                    screenX = rawX;
                }
                screenY = Math.Clamp(rawY, MinScreenY, MaxScreenY);
            }

            // De-overlap stacked bubbles when multiple kin shout near the same viewport region.
            foreach (DaggerfallSpeechBubbleView placed in projected)
            {
                if (MathF.Abs(placed.ScreenX - screenX) < OverlapThresholdX
                    && MathF.Abs(placed.ScreenY - screenY) < OverlapThresholdY)
                {
                    screenY = Math.Clamp(placed.ScreenY - OverlapStepY, MinScreenY, MaxScreenY);
                    if (MathF.Abs(placed.ScreenY - screenY) < OverlapThresholdY * 0.5f)
                        screenY = Math.Clamp(placed.ScreenY + OverlapStepY, MinScreenY, MaxScreenY);
                }
            }

            float opacity = entry.RemainingSeconds < FadeOutSeconds
                ? Math.Clamp(entry.RemainingSeconds / FadeOutSeconds, 0f, 1f)
                : 1f;

            projected.Add(new DaggerfallSpeechBubbleView(
                entry.ActorId,
                entry.SpeakerName,
                entry.Kind,
                entry.Text,
                MathF.Round(screenX, 4),
                MathF.Round(screenY, 4),
                MathF.Round(opacity, 3),
                offscreen,
                MathF.Round(distance, 1)));
        }
        return projected;
    }
}
