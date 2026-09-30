using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Engine video service a test drives: it records each playback the product starts and reports only the
/// realization facts the test appends, in the Engine's shape of one monotonically numbered fact per
/// observation. Nothing completes unless the test says so.
/// </summary>
internal class VideoRecorder : DispatchProxy
{
    private readonly List<VideoRealizationFact> facts = [];
    private ulong nextHandle;
    private ulong nextFact;

    internal IVideoService Service { get; private set; } = null!;

    /// <summary>Every playback the product started, in order.</summary>
    internal List<VideoPlaybackHandle> Played { get; } = [];

    internal int Stops { get; private set; }
    internal int Skips { get; private set; }

    internal static VideoRecorder Create()
    {
        IVideoService service = DispatchProxy.Create<IVideoService, VideoRecorder>();
        VideoRecorder recorder = (VideoRecorder)(object)service;
        recorder.Service = service;
        return recorder;
    }

    /// <summary>Reports that the Engine finished playing the given playback.</summary>
    internal void Complete(VideoPlaybackHandle playback) =>
        facts.Add(new VideoRealizationFact(VideoRealizationFactKind.Completed, ++nextFact, playback, VideoFailureCode.None));

    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
    {
        nameof(IVideoService.PlayFromContent) => Play(),
        nameof(IVideoService.ReadRealization) => new VideoRealizationResult(facts.ToArray(), 0),
        nameof(IVideoService.Stop) => Count(() => Stops++),
        nameof(IVideoService.Skip) => Count(() => Skips++),
        _ => throw new NotSupportedException(method?.Name),
    };

    private VideoPlaybackHandle Play()
    {
        VideoPlaybackHandle handle = new(++nextHandle);
        Played.Add(handle);
        return handle;
    }

    private static object? Count(Action count)
    {
        count();
        return null;
    }
}
