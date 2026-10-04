using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal class PresentationRecorder : DispatchProxy
{
    internal IPresentationService Service { get; private set; } = null!;
    internal List<PresentationParticleDescriptor> Created { get; } = [];
    internal List<PresentationParticleDescriptor> Updated { get; } = [];
    internal int Released { get; private set; }
    internal static PresentationRecorder Create()
    {
        var service = Create<IPresentationService, PresentationRecorder>();
        var recorder = (PresentationRecorder)(object)service;
        recorder.Service = service; return recorder;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        nameof(IPresentationService.CreateEmitter) => NewEmitter((PresentationParticleDescriptor)args![0]!),
        nameof(IPresentationService.UpdateEmitter) => Update((PresentationParticleDescriptor)args![1]!),
        _ => throw new NotSupportedException(method?.Name),
    };
    private PresentationEmitter NewEmitter(PresentationParticleDescriptor descriptor)
    {
        Created.Add(descriptor);
        return new(new PresentationEmitterHandle((ulong)Created.Count), () => Released++);
    }
    private object? Update(PresentationParticleDescriptor descriptor) {Updated.Add(descriptor); return null;}
}
