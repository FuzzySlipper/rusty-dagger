using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal class PerceptionFake : DispatchProxy
{
    internal IPerceptionService Service { get; private set; } = null!;
    internal List<PerceptionQueryRequest> Requests { get; } = [];
    internal PerceptionReadoutResult Receipt { get; set; }
    internal Func<PerceptionQueryRequest, PerceptionReadoutResult>? Responder { get; set; }

    internal static PerceptionFake Create()
    {
        IPerceptionService service = DispatchProxy.Create<IPerceptionService, PerceptionFake>();
        PerceptionFake proxy = (PerceptionFake)(object)service;
        proxy.Service = service;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method?.Name != nameof(IPerceptionService.QueryVisibility)) throw new NotSupportedException(method?.Name);
        PerceptionQueryRequest request = (PerceptionQueryRequest)arguments![0]!;
        Requests.Add(request);
        return Responder?.Invoke(request) ?? Receipt;
    }
}
