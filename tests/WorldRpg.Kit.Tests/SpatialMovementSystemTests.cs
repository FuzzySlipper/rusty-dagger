using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class SpatialMovementSystemTests
{
    private static readonly ContentSha256 FirstHash = new(1, 2, 3, 4);
    private static readonly ContentSha256 SecondHash = new(5, 6, 7, 8);

    [Fact]
    public void Rejected_content_replacement_releases_only_the_candidate_and_keeps_the_source_reference_live()
    {
        SpatialDouble spatial = SpatialDouble.Create();
        ContentDouble content = ContentDouble.Create();
        using SpatialMovementSystem movement = new(spatial.Service, content.Service,
            new SpatialContentArtifact("spatial/source", FirstHash, 1), new SpatialTuning(.5, 8, 8, 1));

        spatial.RejectReplacement = true;
        Assert.Throws<InvalidOperationException>(() => movement.ReplaceContent(new SpatialContentArtifact("spatial/destination", SecondHash, 2)));

        Assert.Equal(1, content.Disposals.GetValueOrDefault("spatial/destination"));
        Assert.Equal(0, content.Disposals.GetValueOrDefault("spatial/source"));
    }

    private class ContentDouble : DispatchProxy
    {
        internal IContentService Service { get; private set; } = null!;
        internal Dictionary<string, int> Disposals { get; } = [];

        internal static ContentDouble Create()
        {
            IContentService service = DispatchProxy.Create<IContentService, ContentDouble>();
            ContentDouble result = (ContentDouble)(object)service;
            result.Service = service;
            return result;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IContentService.ResolveReference)) throw new NotSupportedException(method?.Name);
            string path = ((ContentResolveRequest)arguments![0]!).Path;
            return new ContentReference(new ContentReferenceHandle((ulong)(Disposals.Count + 1)), () =>
            {
                Disposals.TryGetValue(path, out int count);
                Disposals[path] = count + 1;
            });
        }
    }

    private class SpatialDouble : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal bool RejectReplacement { get; set; }

        internal static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble result = (SpatialDouble)(object)service;
            result.Service = service;
            return result;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.DefaultCharacterControllerConfig) => default(CharacterControllerConfig),
            nameof(ISpatialService.ValidateCharacterControllerConfig) => null,
            nameof(ISpatialService.CreateSession) => new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            nameof(ISpatialService.ReplaceContentArtifact) when RejectReplacement => throw new InvalidOperationException("Destination artifact is rejected."),
            nameof(ISpatialService.ReplaceContentArtifact) => new SpatialContentArtifactReplaceReceipt(),
            _ => throw new NotSupportedException(method?.Name),
        };
    }
}
