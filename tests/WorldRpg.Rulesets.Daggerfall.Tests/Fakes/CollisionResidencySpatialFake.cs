using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Spatial service for owners that only apply collision residency: it records every residency request and
/// answers any other call with a default value, or refuses one whose result is a reference.
/// </summary>
internal class CollisionResidencySpatialFake : DispatchProxy
{
    internal ISpatialService Service { get; private set; } = null!;
    internal List<CollisionResidencyRequest> Requests { get; } = [];

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
            Requests.Add((CollisionResidencyRequest)arguments![0]!);
            return new CollisionReplaceReceipt();
        }
        if (method?.ReturnType == typeof(void)) return null;
        if (method?.ReturnType.IsValueType == true) return Activator.CreateInstance(method.ReturnType);
        throw new NotSupportedException(method?.Name);
    }
}
