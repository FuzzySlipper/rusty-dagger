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
    internal int Instances { get; private set; }
    internal List<AnimatedMeshMaterialFactorsRequest> FactorUpdates { get; } = [];
    /// <summary>The joints and clips an opened model reports.</summary>
    internal AnimationJointInfo[] Joints { get; set; } = [];
    internal AnimationClipInfo[] Clips { get; set; } = [];
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
        nameof(IAnimationService.ReadJoints) => new ReadOnlyMemory<AnimationJointInfo>(Joints),
        nameof(IAnimationService.ReadClips) => new ReadOnlyMemory<AnimationClipInfo>(Clips),
        nameof(IAnimationService.SampleModelJointPose) or nameof(IAnimationService.SampleJointPose) => new ReadOnlyMemory<JointPose>(new JointPose[Joints.Length]),
        nameof(IAnimationService.CreateInstance) => new AnimationInstance(new AnimationInstanceHandle(7000 + (ulong)++Instances), () => { }),
        nameof(IAnimationService.UpdateAnimatedMeshMaterialFactors) => Factors((AnimatedMeshMaterialFactorsRequest)args![0]!),
        nameof(IAnimationService.ReadJointPose) => Activator.CreateInstance(method.ReturnType),
        nameof(IAnimationService.SetPlayback) or nameof(IAnimationService.SetPose) or nameof(IAnimationService.SetLayers) => null,
        _ => throw new NotSupportedException(method?.Name),
    };
    private object? Factors(AnimatedMeshMaterialFactorsRequest request)
    {
        FactorUpdates.Add(request);
        return null;
    }
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
