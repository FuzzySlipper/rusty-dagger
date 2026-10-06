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

/// <summary>Keep deterministic minimum gameplay rolls while allowing source Place retries to explore the catalog.</summary>
internal class QuestPlaceRandomMinimum : RandomMinimum
{
    private readonly IRandomService _places = SummonRandom.Create();
    internal new static IRandomService Create() => DispatchProxy.Create<IRandomService, QuestPlaceRandomMinimum>();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) =>
        method?.Name == nameof(IRandomService.DrawKeyed) && arguments![0] is KeyedRngRequest request
            && request.ToString().Contains("daggerfall.quest.place", StringComparison.Ordinal)
            ? _places.DrawKeyed(request) : base.Invoke(method, arguments);
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

/// <summary>
/// Gives any test random the Engine's scoped streams. The Engine owns real streams; a test needs only
/// a stable sequence per seed and scope, so streams here are a managed SplitMix64 and every other
/// draw goes to the wrapped fake unchanged.
/// </summary>
internal class ScopedStreamRandom : DispatchProxy
{
    private IRandomService _inner = null!;
    private readonly Dictionary<ulong, ulong> _states = [];
    private ulong _nextHandle;

    internal static IRandomService Wrap(IRandomService inner)
    {
        if ((object)inner is ScopedStreamRandom) return inner;
        IRandomService service = DispatchProxy.Create<IRandomService, ScopedStreamRandom>();
        ((ScopedStreamRandom)(object)service)._inner = inner;
        return service;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        switch (method?.Name)
        {
            case nameof(IRandomService.CreateScoped):
            {
                ScopedRngCreateRequest request = (ScopedRngCreateRequest)arguments![0]!;
                ulong handle = ++_nextHandle;
                ulong scope = 14695981039346656037UL;
                foreach (char value in request.Scope) scope = unchecked((scope ^ value) * 1099511628211UL);
                _states[handle] = request.Seed ^ scope;
                return new Rng(new RngHandle(handle), () => _states.Remove(handle));
            }
            case nameof(IRandomService.NextU64):
                return new RngValue(Next(((Rng)arguments![0]!).Handle.Value));
            case nameof(IRandomService.NextBoundedU32):
            {
                ScopedRngBoundedRequest request = (ScopedRngBoundedRequest)arguments![0]!;
                return new RngValue(Next(request.Stream.Handle.Value) % request.Upper);
            }
            case nameof(IRandomService.NextBool):
                return new RngValue(Next(((Rng)arguments![0]!).Handle.Value) & 1UL);
            default:
                try { return method!.Invoke(_inner, arguments); }
                catch (TargetInvocationException failure) when (failure.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure.InnerException);
                    throw;
                }
        }
    }

    private ulong Next(ulong handle)
    {
        ulong state = unchecked(_states[handle] + 0x9E3779B97F4A7C15UL);
        _states[handle] = state;
        ulong value = state;
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
