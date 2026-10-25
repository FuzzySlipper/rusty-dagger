using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A real Engine content service with one installed container laid over it. The Engine test host is given loose
/// files, so the container's files (those below one root) are admitted at their content paths and this serves the
/// container's listing and opens each of its files as the real, live content reference of that path; every other
/// call goes to the Engine.
/// </summary>
internal class EngineBundleContent : DispatchProxy
{
    private IContentService _engine = null!;
    private string _bundle = null!;
    private string _root = null!;
    private ContentReferenceInfo[] _files = [];
    private readonly HashSet<ulong> _open = [];
    private ulong _next = 1;

    /// <summary>The service, and the files admitted to the Engine host that the container carries: those below the root.</summary>
    internal static IContentService Create(IContentService engine, string container, string root, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> admitted)
    {
        IContentService service = Create<IContentService, EngineBundleContent>();
        EngineBundleContent content = (EngineBundleContent)(object)service;
        content._engine = engine;
        content._bundle = container;
        content._root = root;
        content._files = [.. admitted.Where(entry => entry.Key.StartsWith(root + "/", StringComparison.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ContentReferenceInfo(entry.Key, TestSessions.Digest(entry.Value.Span), checked((ulong)entry.Value.Length)))];
        return service;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        switch (method.Name)
        {
            case nameof(IContentService.OpenContainer):
                if (((ContentContainerOpenRequest)args![0]!).Path != _bundle) break;
                ulong handle = _next++;
                _open.Add(handle);
                return new ContentBundle(new ContentBundleHandle(handle), () => _open.Remove(handle));
            case nameof(IContentService.ReadBundleFiles):
                Require(((ContentBundle)args![0]!).Handle.Value);
                return (ReadOnlyMemory<ContentReferenceInfo>)_files;
            case nameof(IContentService.ReadBundleIdentity):
                Require(((ContentBundle)args![0]!).Handle.Value);
                return TestSessions.Digest(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",
                    _files.Select(entry => $"{entry.Path}:{entry.Sha256}:{entry.ByteLength}"))));
            case nameof(IContentService.OpenBundleReference):
                ContentBundleReferenceRequest request = (ContentBundleReferenceRequest)args![0]!;
                Require(request.Bundle.Handle.Value);
                return _engine.OpenReference(new ContentOpenRequest(request.Path));
        }

        try { return method.Invoke(_engine, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private void Require(ulong handle)
    {
        if (!_open.Contains(handle)) throw new InvalidOperationException($"Container '{_bundle}' is not open under handle {handle}.");
    }
}
