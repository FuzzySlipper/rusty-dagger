using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Records the Dynamics worlds, bodies, ragdolls and steps a presentation asks for, and which of them it releases.</summary>
internal class DynamicsRecorder : DispatchProxy
{
    internal IDynamicsService Service { get; private set; } = null!;
    internal int Worlds { get; private set; }
    internal int ReleasedWorlds { get; private set; }
    internal int CollisionBindings { get; private set; }
    internal List<DynamicsCreateCapsuleBodyRequest> Capsules { get; } = [];
    internal int ReleasedBodies { get; private set; }
    internal List<DynamicsBodyKinematicRequest> KinematicMoves { get; } = [];
    internal List<DynamicsRagdollRequest> Ragdolls { get; } = [];
    internal int ReleasedRagdolls { get; private set; }
    internal List<float> Blends { get; } = [];
    internal List<DynamicsStepRequest> Steps { get; } = [];

    internal static DynamicsRecorder Create()
    {
        var service = Create<IDynamicsService, DynamicsRecorder>();
        var recorder = (DynamicsRecorder)(object)service;
        recorder.Service = service;
        return recorder;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        nameof(IDynamicsService.CreateWorld) => new DynamicsWorld(new DynamicsWorldHandle((ulong)++Worlds), () => ReleasedWorlds++),
        nameof(IDynamicsService.BindWorldCollision) => Count(() => CollisionBindings++),
        nameof(IDynamicsService.CreateCapsuleBody) => Capsule((DynamicsCreateCapsuleBodyRequest)args![0]!),
        nameof(IDynamicsService.MoveBodyKinematic) => Count(() => KinematicMoves.Add((DynamicsBodyKinematicRequest)args![0]!)),
        nameof(IDynamicsService.CreateRagdoll) => Ragdoll((DynamicsRagdollRequest)args![0]!),
        nameof(IDynamicsService.SetRagdollBlend) => Count(() => Blends.Add(((DynamicsRagdollBlendRequest)args![0]!).Blend)),
        nameof(IDynamicsService.Step) => Step((DynamicsStepRequest)args![0]!),
        _ => throw new NotSupportedException(method?.Name),
    };

    private static object? Count(Action record)
    {
        record();
        return null;
    }

    private DynamicsBody Capsule(DynamicsCreateCapsuleBodyRequest request)
    {
        Capsules.Add(request);
        return new DynamicsBody(new DynamicsBodyHandle((ulong)Capsules.Count), () => ReleasedBodies++);
    }

    private DynamicsRagdoll Ragdoll(DynamicsRagdollRequest request)
    {
        Ragdolls.Add(request);
        return new DynamicsRagdoll(new DynamicsRagdollHandle((ulong)Ragdolls.Count), () => ReleasedRagdolls++);
    }

    private DynamicsStepReceipt Step(DynamicsStepRequest request)
    {
        Steps.Add(request);
        return default;
    }
}
