using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

internal class RandomMaximum : DispatchProxy
{
    internal static IRandomService Create() => DispatchProxy.Create<IRandomService, RandomMaximum>();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
        ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Maximum)
        : throw new NotSupportedException(method?.Name);
}

internal class RandomMinimum : DispatchProxy
{
    internal IRandomService Service { get; private set; } = null!;

    internal static IRandomService Create()
    {
        IRandomService service = DispatchProxy.Create<IRandomService, RandomMinimum>();
        ((RandomMinimum)(object)service).Service = service;
        return service;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method?.Name == nameof(IRandomService.DrawKeyed))
            return new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum);
        if (method?.Name == nameof(IRandomService.DrawLcg15))
        {
            Lcg15Request request = (Lcg15Request)arguments![0]!;
            uint state = unchecked(request.State * 1103515245u + 12345u);
            return new Lcg15Receipt(state, ((state >> 16) & 0x7fffu) % request.UpperExclusive);
        }
        throw new NotSupportedException(method?.Name);
    }
}

internal class KeyedRandomFake : DispatchProxy
{
    internal IRandomService Service { get; private set; } = null!;
    internal List<KeyedRngRequest> Requests { get; } = [];
    private long value;

    internal static KeyedRandomFake Create(long returnedValue)
    {
        IRandomService service = DispatchProxy.Create<IRandomService, KeyedRandomFake>();
        KeyedRandomFake fake = (KeyedRandomFake)(object)service;
        fake.Service = service;
        fake.value = returnedValue;
        return fake;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
        KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
        Requests.Add(request);
        return new KeyedRngReceipt(Math.Clamp(value, request.Minimum, request.Maximum));
    }
}
