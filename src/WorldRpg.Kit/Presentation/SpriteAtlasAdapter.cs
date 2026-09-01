using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>One normalized pixel-space atlas frame before Engine realization.</summary>
public readonly record struct NormalizedSpriteFrame(uint Id, int X, int Y, int Width, int Height, Vector2? DisplaySize = null);
public readonly record struct SpritePlaybackStep(uint? FrameId);
public readonly record struct SpritePlaybackPlan(SpritePlaybackFrame[] Frames, SpritePlaybackMarker[] Markers);

/// <summary>Maps normalized pixel rectangles and sequence timing into Engine-owned sprite requests.</summary>
public static class SpriteAtlasAdapter
{
    public static SpriteAtlasFrame[] ToAtlasFrames(int atlasWidth, int atlasHeight, IReadOnlyList<NormalizedSpriteFrame> frames)
    {
        if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("A sprite atlas needs at least one frame.", nameof(frames));

        HashSet<uint> ids = [];
        return frames.Select(frame =>
        {
            if (frame.Width <= 0 || frame.Height <= 0 || frame.X < 0 || frame.Y < 0
                || checked(frame.X + frame.Width) > atlasWidth || checked(frame.Y + frame.Height) > atlasHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(frames), "A sprite frame is outside the normalized atlas bounds.");
            }
            if (!ids.Add(frame.Id)) throw new ArgumentException("Sprite frame IDs must be unique.", nameof(frames));
            Vector2 size = frame.DisplaySize ?? Vector2.Zero;
            if (frame.DisplaySize is { } requested && (requested.X <= 0F || requested.Y <= 0F || !float.IsFinite(requested.X) || !float.IsFinite(requested.Y)))
                throw new ArgumentOutOfRangeException(nameof(frames), "An optional sprite frame display size must be finite and positive.");
            return new SpriteAtlasFrame(frame.Id,
                new Vector2((float)frame.X / atlasWidth, (float)frame.Y / atlasHeight),
                new Vector2((float)(frame.X + frame.Width) / atlasWidth, (float)(frame.Y + frame.Height) / atlasHeight),
                frame.DisplaySize is not null, size);
        }).ToArray();
    }

    public static SpritePlaybackFrame[] ToPlaybackFrames(IReadOnlyList<uint> sequence, double framesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (sequence.Count == 0) throw new ArgumentException("A sprite playback needs at least one frame.", nameof(sequence));
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0D) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        return ToPlaybackPlan(sequence.Select(frame => new SpritePlaybackStep(frame)).ToArray(), framesPerSecond).Frames;
    }

    public static SpritePlaybackPlan ToPlaybackPlan(IReadOnlyList<SpritePlaybackStep> sequence, double framesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (sequence.Count == 0) throw new ArgumentException("A sprite playback needs at least one step.", nameof(sequence));
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0D) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        double duration = 1D / framesPerSecond;
        List<SpritePlaybackFrame> frames = [];
        List<SpritePlaybackMarker> markers = [];
        for (int index = 0; index < sequence.Count; index++)
        {
            if (sequence[index].FrameId is { } frame) frames.Add(new SpritePlaybackFrame(frame, duration));
            else markers.Add(new SpritePlaybackMarker(checked((ulong)index + 1), checked((uint)frames.Count)));
        }
        if (frames.Count == 0 || markers.Any(marker => marker.FrameIndex >= frames.Count))
            throw new InvalidOperationException("Sprite marker steps require a following visible frame.");
        return new(frames.ToArray(), markers.ToArray());
    }
}
