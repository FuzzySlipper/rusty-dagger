using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Explores ordinary draws like <see cref="SummonRandom"/>, but answers the provider work-offer draw with a
/// chosen pool index, so a test names the quest it prepares instead of depending on a draw's value.
/// </summary>
internal class WorkOfferRandom : DispatchProxy
{
    private readonly IRandomService _other = SummonRandom.Create();
    internal Func<long, long>? WorkOffer { get; set; }

    internal static (IRandomService Service, WorkOfferRandom Fake) Create()
    {
        IRandomService service = Create<IRandomService, WorkOfferRandom>();
        return (service, (WorkOfferRandom)(object)service);
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name == nameof(IRandomService.DrawKeyed) && args![0] is KeyedRngRequest request
            && request.ToString().Contains("daggerfall.quest.work-offer", StringComparison.Ordinal) && WorkOffer is { } select)
            return new KeyedRngReceipt(select(request.Maximum));
        return method!.Invoke(_other, args);
    }
}
