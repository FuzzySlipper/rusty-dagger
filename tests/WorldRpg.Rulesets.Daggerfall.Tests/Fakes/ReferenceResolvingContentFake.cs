using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Content service that resolves any reference to one handle and refuses every other call.</summary>
internal class ReferenceResolvingContentFake : DispatchProxy
{
    internal IContentService Service { get; private set; } = null!;

    internal static ReferenceResolvingContentFake Create()
    {
        IContentService service = DispatchProxy.Create<IContentService, ReferenceResolvingContentFake>();
        ReferenceResolvingContentFake proxy = (ReferenceResolvingContentFake)(object)service;
        proxy.Service = service;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IContentService.ResolveReference)
        ? new ContentReference(new ContentReferenceHandle(1), static () => { })
        : throw new NotSupportedException(method?.Name);
}
