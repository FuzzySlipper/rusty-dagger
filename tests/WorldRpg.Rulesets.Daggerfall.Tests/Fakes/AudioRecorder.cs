using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal class AudioRecorder : DispatchProxy
{
    internal IAudioService Service { get; private set; } = null!;
    internal List<AudioEmitRequest> Emits { get; } = [];
    internal List<AudioSignalHandle> EmittedSignals { get; } = [];
    internal List<AudioSignalHandle> RetiredSignals { get; } = [];
    internal List<AudioRealizationFact> RealizationFacts { get; } = [];
    internal int ReleasedClips { get; private set; }
    internal int ReleasedVoices { get; private set; }
    internal List<AudioSourceDescriptor> Voices { get; } = [];
    internal List<AudioVoiceControlRequest> Controls { get; } = [];
    private ulong nextHandle = 1;

    internal static AudioRecorder Create()
    {
        IAudioService service = DispatchProxy.Create<IAudioService, AudioRecorder>();
        AudioRecorder recorder = (AudioRecorder)(object)service;
        recorder.Service = service;
        return recorder;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
    {
        nameof(IAudioService.OpenClip) => new AudioClip(new AudioClipHandle(nextHandle++), () => ReleasedClips++),
        nameof(IAudioService.Emit) => Emit((AudioEmitRequest)arguments![0]!),
        nameof(IAudioService.RetireOneShot) => Retire((AudioSignalHandle)arguments![0]!),
        nameof(IAudioService.ReadRealization) => new AudioRealizationResult(RealizationFacts.ToArray(), 0),
        nameof(IAudioService.CreateVoice) => Voice((AudioSourceDescriptor)arguments![0]!),
        nameof(IAudioService.ControlVoice) => Control((AudioVoiceControlRequest)arguments![0]!),
        _ => throw new NotSupportedException(method?.Name),
    };

    private AudioVoice Voice(AudioSourceDescriptor descriptor)
    {
        Voices.Add(descriptor);
        return new(new AudioVoiceHandle(nextHandle++), () => ReleasedVoices++);
    }
    private object? Control(AudioVoiceControlRequest request) { Controls.Add(request); return null; }

    private object? Retire(AudioSignalHandle signal) { RetiredSignals.Add(signal); return null; }

    private AudioSignalHandle Emit(AudioEmitRequest request)
    {
        Emits.Add(request);
        AudioSignalHandle signal = new(nextHandle++);
        EmittedSignals.Add(signal);
        return signal;
    }
}
