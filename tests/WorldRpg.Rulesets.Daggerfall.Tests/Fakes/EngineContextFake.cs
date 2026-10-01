using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal class EngineContextFake : DispatchProxy
{
    internal IEngineContext Context { get; private set; } = null!;
    internal int UiOpenCalls { get; private set; }
    internal int ClearedSkyBackgrounds => ((CameraServiceFake)(object)camera).ClearedSkyBackgrounds;
    /// <summary>The diagnostics the product published, in order, as 'source/code' with their message.</summary>
    internal IReadOnlyList<string> PublishedDiagnostics => ((DiagnosticsServiceFake)(object)diagnostics).Published
        .Select(entry => $"{entry.Source}/{entry.Code}: {entry.Message}")
        .ToArray();
    /// <summary>The retained audio voices started so far, in order.</summary>
    internal IReadOnlyList<AudioEmitRequest> EmittedAudio => ((AudioServiceFake)(object)audio).Emits;
    internal IReadOnlyList<string> StartedAudioVoices => ((AudioServiceFake)(object)audio).StartedVoices;
    /// <summary>How many of those voices the product released.</summary>
    internal int ReleasedAudioVoices => ((AudioServiceFake)(object)audio).ReleasedVoices;
    /// <summary>How many clips the product opened from content, which is how a cue really resolved.</summary>
    internal int OpenedContentClips => ((AudioServiceFake)(object)audio).OpenedContentClips;
    internal IReadOnlyList<Color> BackgroundColors => ((CameraServiceFake)(object)camera).BackgroundColors;

    /// <summary>Read one named field of the last published projection, or null when none was.</summary>
    internal string? PublishedField(string key) => ((UiServiceFake)(object)ui).Field(key);

    /// <summary>The whole published projection decoded into plain values, or null before one is published.</summary>
    internal object? Published() => ((UiServiceFake)(object)ui).Decoded();

    /// <summary>Every snapshot published so far, decoded in order: one admitted update can publish more than one.</summary>
    internal IReadOnlyList<object?> PublishedHistory() => ((UiServiceFake)(object)ui).History;

    /// <summary>Read one named field of a nested object of the last published projection.</summary>
    internal string? PublishedNested(string parent, string key) => ((UiServiceFake)(object)ui).Nested(parent, key);

    /// <summary>Read one named field of one element of a published array.</summary>
    internal string? PublishedArrayItem(string array, int index, string key) => ((UiServiceFake)(object)ui).ArrayItemField(array, index, key);
    internal readonly InputServiceFake PhysicalInput = new();
    private IContentService content = null!;
    private ISpatialService spatial = null!;
    private IGraphicsService appearance = null!;
    private IPerceptionService perception = null!;
    private ICameraViewService camera = null!;
    private IAudioService audio = null!;
    private IDiagnosticsService diagnostics = null!;
    private IVideoService video = null!;
    private IRandomService random = null!;
    private IUiService ui = null!;
    private IPersistenceService persistence = null!;

    internal static EngineContextFake Create(IContentService content, ISpatialService spatial, IGraphicsService appearance,
        IPerceptionService? perception = null, IPersistenceService? persistence = null, IRandomService? random = null,
        IVideoService? video = null)
    {
        IEngineContext context = DispatchProxy.Create<IEngineContext, EngineContextFake>();
        EngineContextFake fake = (EngineContextFake)(object)context;
        fake.Context = context;
        fake.content = content;
        fake.spatial = spatial;
        fake.appearance = appearance;
        fake.perception = perception ?? PerceptionFake.Create().Service;
        fake.camera = ServiceProxy<ICameraViewService, CameraServiceFake>.Create();
        fake.audio = ServiceProxy<IAudioService, AudioServiceFake>.Create();
        fake.diagnostics = ServiceProxy<IDiagnosticsService, DiagnosticsServiceFake>.Create();
        fake.video = video ?? ServiceProxy<IVideoService, VideoServiceFake>.Create();
        fake.random = random ?? RandomMinimum.Create();
        fake.ui = UiServiceFake.Create(fake);
        fake.persistence = persistence ?? new InMemoryPersistenceService();
        return fake;
    }

    internal void FailNextCameraUpdate() => ((CameraServiceFake)(object)camera).FailNextUpdate = true;

    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
    {
        "get_Input" => PhysicalInput,
        "get_Content" => content,
        "get_Spatial" => spatial,
        "get_Graphics" => appearance,
        "get_Perception" => perception,
        "get_CameraView" => camera,
        "get_Audio" => audio,
        "get_Diagnostics" => diagnostics,
        "get_Video" => video,
        "get_Random" => random,
        "get_Ui" => ui,
        "get_Persistence" => persistence,
        _ => throw new NotSupportedException(method?.Name),
    };

    private class CameraServiceFake : DispatchProxy
    {
        internal bool FailNextUpdate { get; set; }
        internal int ClearedSkyBackgrounds { get; private set; }
        internal List<Color> BackgroundColors { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == nameof(ICameraViewService.UpdateCamera) && FailNextUpdate)
            {
                FailNextUpdate = false;
                throw new InvalidOperationException("Rejected camera pose update.");
            }
            return method?.Name switch
            {
                nameof(ICameraViewService.CreateCamera) => new Camera(new CameraHandle(1), () => { }),
                nameof(ICameraViewService.ClearSkyBackground) => ClearSkyBackground(),
                nameof(ICameraViewService.SetBackgroundColor) => SetBackgroundColor(arguments),
                nameof(ICameraViewService.UpdateCamera) or nameof(ICameraViewService.SetActiveCamera) or nameof(ICameraViewService.ClearActiveCamera) or nameof(ICameraViewService.SetSkyBackground) => null,
                nameof(ICameraViewService.ReplaceCamera) => new Camera(new CameraHandle(1), () => { }),
                _ => throw new NotSupportedException(method?.Name),
            };
        }

        private object? ClearSkyBackground()
        {
            ClearedSkyBackgrounds++;
            return null;
        }

        private object? SetBackgroundColor(object?[]? arguments)
        {
            BackgroundColors.Add(((SetBackgroundColorRequest)arguments![0]!).Color);
            return null;
        }
    }

    /// <summary>
    /// Answers clips, emitted signals and retained voices, recording the voices a session starts.
    /// </summary>
    /// <remarks>
    /// A looping cue is a retained voice, so a session that starts music is visible here as a
    /// created voice and as a release when the loop ends. Emitted one-shots stay answered because
    /// ordinary presentation audio uses them.
    /// </remarks>
    private class AudioServiceFake : DispatchProxy
    {
        internal List<string> StartedVoices { get; } = [];
        internal List<AudioEmitRequest> Emits { get; } = [];
        internal int ReleasedVoices { get; private set; }
        /// <summary>How many clips the product opened from content, which is how a cue really resolves.</summary>
        internal int OpenedContentClips { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IAudioService.OpenClip) => OpenClip(),
            nameof(IAudioService.OpenClipFromContent) => OpenClipFromContent(),
            nameof(IAudioService.Emit) => Emit((AudioEmitRequest)arguments![0]!),
            nameof(IAudioService.CreateVoice) => CreateVoice(),
            _ => throw new NotSupportedException(method?.Name),
        };

        private AudioSignalHandle Emit(AudioEmitRequest request) { Emits.Add(request); return new AudioSignalHandle(1); }

        private AudioClip OpenClip() => new(new AudioClipHandle(1), static () => { });

        private AudioClip OpenClipFromContent()
        {
            OpenedContentClips++;
            return new AudioClip(new AudioClipHandle(1), static () => { });
        }

        private AudioVoice CreateVoice()
        {
            StartedVoices.Add($"voice-{StartedVoices.Count + 1}");
            return new AudioVoice(new AudioVoiceHandle(checked((ulong)StartedVoices.Count)), () => ReleasedVoices++);
        }
    }

    /// <summary>
    /// Records the diagnostics the product publishes, so a fact can read the change timeline it keeps.
    /// </summary>
    private class DiagnosticsServiceFake : DispatchProxy
    {
        internal List<(string Source, string Code, string Message)> Published { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IDiagnosticsService.Publish) => Publish((DiagnosticsPublishRequest)arguments![0]!),
            nameof(IDiagnosticsService.ReadRenderer) => ReadOnlyMemory<byte>.Empty,
            _ => throw new NotSupportedException(method?.Name),
        };

        private object? Publish(DiagnosticsPublishRequest request)
        {
            Published.Add((request.Source, request.Code, request.Message));
            return null;
        }
    }

    /// <summary>Safe terminal-free video stub for non-media session fixtures.</summary>
    private class VideoServiceFake : DispatchProxy
    {
        private ulong _nextHandle;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IVideoService.PlayFromContent) => new VideoPlaybackHandle(++_nextHandle),
            nameof(IVideoService.ReadRealization) => new VideoRealizationResult(ReadOnlyMemory<VideoRealizationFact>.Empty, 0),
            nameof(IVideoService.Stop) or nameof(IVideoService.Skip) => null,
            _ => throw new NotSupportedException(method?.Name),
        };
    }

    private class UiServiceFake : DispatchProxy
    {
        private EngineContextFake owner = null!;
        internal UiProjection? LastProjection { get; private set; }
        internal static IUiService Create(EngineContextFake parent)
        {
            IUiService service = DispatchProxy.Create<IUiService, UiServiceFake>();
            ((UiServiceFake)(object)service).owner = parent;
            return service;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IUiService.OpenStream) => Open(),
            nameof(IUiService.PublishProjection) => Publish(arguments),
            _ => throw new NotSupportedException(method?.Name),
        };
        private UiStream Open() { owner.UiOpenCalls++; return new UiStream(new UiStreamHandle(1), () => { }); }

        private object? Publish(object?[]? arguments)
        {
            LastProjection = arguments is [UiProjection projection, ..] ? projection : null;
            History.Add(LastProjection is { } published ? Decode(published, published.Value.Root) : null);
            return null;
        }

        /// <summary>Every snapshot this session published, decoded in order.</summary>
        internal List<object?> History { get; } = [];

        /// <summary>The string one named field of the published object carries, or null.</summary>
        internal string? Field(string key)
        {
            if (LastProjection is not { } projection) return null;
            foreach (uint edge in Edges(projection, projection.Value.Root))
            {
                StructuredValueNode node = projection.Value.Nodes.Span[checked((int)edge)];
                if (Key(projection, node) != key) continue;
                return node.Kind == StructuredValueKind.String ? Text(projection, node) : null;
            }

            return null;
        }

        /// <summary>The node index of one named field of the published object, or null.</summary>
        internal uint? Object(string key)
        {
            if (LastProjection is not { } projection) return null;
            foreach (uint edge in Edges(projection, projection.Value.Root))
            {
                if (Key(projection, projection.Value.Nodes.Span[checked((int)edge)]) == key) return edge;
            }

            return null;
        }

        /// <summary>The string one named field of one published array element carries, or null.</summary>
        internal string? ArrayItemField(string array, int index, string key)
        {
            if (LastProjection is not { } projection || Object(array) is not { } arrayIndex) return null;
            List<uint> items = [.. Edges(projection, arrayIndex)];
            if (index >= items.Count) return null;
            foreach (uint edge in Edges(projection, items[index]))
            {
                StructuredValueNode node = projection.Value.Nodes.Span[checked((int)edge)];
                if (Key(projection, node) == key && node.Kind == StructuredValueKind.String) return Text(projection, node);
            }

            return null;
        }

        /// <summary>The string one named field of one nested object carries, or null.</summary>
        internal string? Nested(string parent, string key)
        {
            if (LastProjection is not { } projection || Object(parent) is not { } parentIndex) return null;
            foreach (uint edge in Edges(projection, parentIndex))
            {
                StructuredValueNode node = projection.Value.Nodes.Span[checked((int)edge)];
                if (Key(projection, node) == key && node.Kind == StructuredValueKind.String) return Text(projection, node);
            }

            return null;
        }

        /// <summary>The whole published value decoded into objects, arrays, strings and numbers.</summary>
        internal object? Decoded() => LastProjection is { } projection ? Decode(projection, projection.Value.Root) : null;

        private static object? Decode(UiProjection projection, uint index)
        {
            StructuredValueNode node = projection.Value.Nodes.Span[checked((int)index)];
            return node.Kind switch
            {
                StructuredValueKind.Null => null,
                StructuredValueKind.String => Text(projection, node),
                StructuredValueKind.Number => node.NumberValue,
                StructuredValueKind.Bool => node.BoolValue != 0,
                StructuredValueKind.Array => Edges(projection, index).Select(edge => Decode(projection, edge)).ToArray(),
                StructuredValueKind.Object => Edges(projection, index).ToDictionary(
                    edge => Key(projection, projection.Value.Nodes.Span[checked((int)edge)]),
                    edge => Decode(projection, edge),
                    StringComparer.Ordinal),
                _ => (object?)node.Kind,
            };
        }

        private static IEnumerable<uint> Edges(UiProjection projection, uint index)
        {
            StructuredValueNode node = projection.Value.Nodes.Span[checked((int)index)];
            for (uint offset = 0; offset < node.ChildCount; offset++) yield return projection.Value.Edges.Span[checked((int)(node.FirstEdge + offset))];
        }

        private static string Key(UiProjection projection, StructuredValueNode node) =>
            System.Text.Encoding.UTF8.GetString(projection.Value.Utf8.Span[checked((int)node.KeyOffset)..checked((int)(node.KeyOffset + node.KeyLen))]);

        private static string Text(UiProjection projection, StructuredValueNode node) =>
            System.Text.Encoding.UTF8.GetString(projection.Value.Utf8.Span[checked((int)node.TextOffset)..checked((int)(node.TextOffset + node.TextLen))]);
    }
}

internal sealed class InputServiceFake : IInputService
{
    internal ProductInputMapping[] Mappings = [];
    internal InputMappingReplacementOutcome Outcome = InputMappingReplacementOutcome.Staged;
    public InputMappingReplacementOutcome ReplacePhysicalMappings(ReadOnlySpan<ProductInputMapping> mappings)
    {
        if (Outcome == InputMappingReplacementOutcome.Staged) Mappings = mappings.ToArray();
        return Outcome;
    }
}

internal class ServiceProxy<TService, TProxy> : DispatchProxy where TService : class where TProxy : DispatchProxy
{
    internal static TService Create()
    {
        return DispatchProxy.Create<TService, TProxy>();
    }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => throw new NotSupportedException(method?.Name);
}
