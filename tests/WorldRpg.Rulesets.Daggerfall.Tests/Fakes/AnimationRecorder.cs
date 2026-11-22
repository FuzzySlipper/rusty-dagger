using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Records the animated meshes and clip packs a presentation opens by content path, the appearances and instances it
/// makes of them, how it binds and fits clips and draws splats, and the playback it asks for.
/// </summary>
internal class AnimationRecorder : DispatchProxy
{
    internal IAnimationService Service { get; private set; } = null!;
    internal List<string?> OpenedPaths { get; } = [];
    internal List<string?> OpenedPackPaths { get; } = [];
    /// <summary>Each clip pack bound by joint names, as (mesh, pack) handle values.</summary>
    internal List<(ulong Mesh, ulong Pack, string Producer, string License)> JointNameBindings { get; } = [];
    internal List<AnimationClipTranslationFitRequest> Fits { get; } = [];
    internal List<SplatAppearanceRequest> Splats { get; } = [];
    internal List<AnimationInstanceRequest> InstanceRequests { get; } = [];
    internal List<AnimationPlaybackRequest> Playbacks { get; } = [];
    internal List<SplatGroupTintRequest> Tints { get; } = [];
    internal List<Appearance> Created { get; } = [];
    internal int ReleasedAppearances { get; private set; }
    internal int Instances { get; private set; }
    internal List<AnimatedMeshMaterialFactorsRequest> FactorUpdates { get; } = [];
    /// <summary>The joints and clips an opened model reports.</summary>
    internal AnimationJointInfo[] Joints { get; set; } = [];
    /// <summary>Where each of <see cref="Joints"/> stands in its model at rest, which a model sample places in the scene; none puts every joint at its origin.</summary>
    internal System.Numerics.Vector3[] Rest { get; set; } = [];
    internal int ReleasedInstances { get; private set; }
    /// <summary>The pose every instance reports it was drawn in; none reports it unposed.</summary>
    internal JointPose[]? Live { get; set; }
    internal AnimationClipInfo[] Clips { get; set; } = [];
    /// <summary>The bounds an opened model reports: a metre cube standing on its floor, unless a test states its own.</summary>
    internal AnimatedMeshInfo MeshInfo { get; set; } = new(new(-.5F, 0, -.5F), new(.5F, 1, .5F), 0, 0, 0);
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
        nameof(IAnimationService.ReadMeshInfo) => MeshInfo,
        nameof(IAnimationService.SampleModelJointPose) => ModelPose(((AnimationModelPoseSampleRequest)args![0]!).Placement),
        nameof(IAnimationService.SampleJointPose) => new ReadOnlyMemory<JointPose>(new JointPose[Joints.Length]),
        nameof(IAnimationService.CreateInstance) => NewInstance((AnimationInstanceRequest)args![0]!),
        nameof(IAnimationService.ReplaceInstance) => Replace((AnimationInstance)args![0]!, (AnimationInstanceRequest)args[1]!),
        nameof(IAnimationService.OpenAnimationClipPackFromContent) => OpenPack((AnimationContentRequest)args![0]!),
        nameof(IAnimationService.AssociateAnimationClipPackByJointNames) => Bind((AnimationClipPackAssociationRequest)args![0]!),
        nameof(IAnimationService.FitAnimationClipTranslation) => Record(Fits, (AnimationClipTranslationFitRequest)args![0]!),
        nameof(IAnimationService.SetSplatAppearance) => Record(Splats, (SplatAppearanceRequest)args![0]!),
        nameof(IAnimationService.SetPlayback) => Record(Playbacks, (AnimationPlaybackRequest)args![0]!),
        nameof(IAnimationService.SetSplatGroupTint) => Record(Tints, (SplatGroupTintRequest)args![0]!),
        nameof(IAnimationService.UpdateAnimatedMeshMaterialFactors) => Factors((AnimatedMeshMaterialFactorsRequest)args![0]!),
        nameof(IAnimationService.ReadJointPose) => Live is { } live
            ? new AnimationJointPoseResult(live, ReadOnlyMemory<IkChainReach>.Empty, default, 0, true) : default(AnimationJointPoseResult),
        nameof(IAnimationService.SetPose) or nameof(IAnimationService.SetLayers) => null,
        _ => throw new NotSupportedException(method?.Name),
    };
    /// <summary>Its joints at rest, placed at <paramref name="placement"/>, as a model sample places them.</summary>
    internal ReadOnlyMemory<JointPose> ModelPose(Transform placement) => Joints.Select((_, joint) =>
    {
        System.Numerics.Vector3 local = joint < Rest.Length ? Rest[joint] : default;
        Transform model = new(local, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.One);
        System.Numerics.Vector3 world = placement.Translation + System.Numerics.Vector3.Transform(local * placement.Scale, placement.Rotation);
        return new JointPose(model, new Transform(world, placement.Rotation, placement.Scale));
    }).ToArray();

    private static object? Record<T>(List<T> list, T request)
    {
        list.Add(request);
        return null;
    }
    private AnimationInstance NewInstance(AnimationInstanceRequest request)
    {
        InstanceRequests.Add(request);
        return new AnimationInstance(new AnimationInstanceHandle(7000 + (ulong)++Instances), () => ReleasedInstances++);
    }
    private AnimationInstance Replace(AnimationInstance prior, AnimationInstanceRequest request)
    {
        prior.Dispose();
        return NewInstance(request);
    }
    private RenderResource OpenPack(AnimationContentRequest request)
    {
        OpenedPackPaths.Add(TestContentReferences.PathOf(request.Content));
        return new(new RenderResourceHandle(9500 + (ulong)OpenedPackPaths.Count), () => { });
    }
    private object? Bind(AnimationClipPackAssociationRequest request)
    {
        JointNameBindings.Add((request.PrimaryMesh.Handle.Value, request.ClipPack.Handle.Value, request.Producer, request.License));
        return null;
    }
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
