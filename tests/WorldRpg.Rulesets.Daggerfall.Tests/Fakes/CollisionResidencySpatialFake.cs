using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Spatial service for owners that only apply collision residency: it records every residency request, numbers
/// deferred ones after the readout's batches as the Engine does, reports the readout a test sets, and answers any
/// other call with a default value, or refuses one whose result is a reference.
/// </summary>
internal class CollisionResidencySpatialFake : DispatchProxy
{
    internal ISpatialService Service { get; private set; } = null!;
    internal List<CollisionResidencyRequest> Requests { get; } = [];
    /// <summary>What a read of the session's deferred batches reports.</summary>
    internal CollisionResidencyReadout Readout { get; set; }

    internal static CollisionResidencySpatialFake Create()
    {
        ISpatialService service = DispatchProxy.Create<ISpatialService, CollisionResidencySpatialFake>();
        CollisionResidencySpatialFake proxy = (CollisionResidencySpatialFake)(object)service;
        proxy.Service = service;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method?.Name == nameof(ISpatialService.ApplyCollisionResidency))
        {
            CollisionResidencyRequest request = (CollisionResidencyRequest)arguments![0]!;
            Requests.Add(request);
            // As the Engine numbers them: the next after every batch done or pending.
            return request.Deferred
                ? new CollisionReplaceReceipt(0, 0, 0, 0, 0, Readout.CompletedBatch + Readout.PendingBatches + 1)
                : new CollisionReplaceReceipt();
        }
        if (method?.Name == nameof(ISpatialService.ReadCollisionResidency)) return Readout;
        if (method?.ReturnType == typeof(void)) return null;
        if (method?.ReturnType.IsValueType == true) return Activator.CreateInstance(method.ReturnType);
        throw new NotSupportedException(method?.Name);
    }
}
