using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Records the animated meshes a presentation opens by content path and the appearances it makes of them.</summary>
internal class AnimationRecorder : DispatchProxy
{
    internal IAnimationService Service { get; private set; } = null!;
    internal List<string?> OpenedPaths { get; } = [];
    internal List<Appearance> Created { get; } = [];
    internal int ReleasedAppearances { get; private set; }
    internal static AnimationRecorder Create()
    {
        var service = Create<IAnimationService, AnimationRecorder>();
        var recorder = (AnimationRecorder)(object)service;
        recorder.Service = service; return recorder;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        nameof(IAnimationService.OpenAnimatedMeshFromContent) => Open((AnimationContentRequest)args![0]!),
        nameof(IAnimationService.CreateAnimatedMeshAppearance) => NewAppearance(),
        _ => throw new NotSupportedException(method?.Name),
    };
    private RenderResource Open(AnimationContentRequest request)
    {
        OpenedPaths.Add(TestContentReferences.PathOf(request.Content));
        return new(new RenderResourceHandle(9000 + (ulong)OpenedPaths.Count), () => { });
    }
    private Appearance NewAppearance()
    {
        Appearance value = new(new AppearanceHandle(8000 + (ulong)Created.Count), () => ReleasedAppearances++);
        Created.Add(value);
        return value;
    }
}
