using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Host;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Host.Tests;

/// <summary>
/// Ordinary menu save/load requests: a failure while adopting a loaded session keeps the
/// current session and reports honestly instead of throwing out of the product update.
/// </summary>
public sealed class WorldRpgProductSaveLoadTests
{
    [Fact]
    public void A_load_whose_previous_session_fails_disposal_keeps_the_current_session_and_reports()
    {
        InMemoryPersistenceService persistence = new();
        LoadTestRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(persistence), ruleset, new GameBundleId("test.bundle"));
        using WorldRpgSaveStore store = new(Context(persistence).Engine, "worldrpg.saves");
        Assert.Equal(PersistenceSaveOutcome.Saved, product.Save(store, "slot").Outcome);
        product.Start();

        LoadTestSession previous = ruleset.RequireCurrent();
        previous.ThrowOnDispose = true;
        previous.ArmLoadRequest();
        ProductUpdateResult result = product.Update(Update(1));

        Assert.Equal(ProductUpdateResult.None, result);
        Assert.Same(previous, ruleset.Current);
        Assert.True(previous.Disposed);
        Assert.NotNull(ruleset.Replacement);
        Assert.True(ruleset.Replacement.Disposed);
        Assert.StartsWith("Load failed:", previous.Outcome, StringComparison.Ordinal);
        Assert.Contains("boom", previous.Outcome, StringComparison.Ordinal);
        previous.ThrowOnDispose = false;
    }

    private static ProductUpdate Update(ulong step) =>
        new(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, step, step, step, step, 60, 1, 0, 1d / 60d), ReadOnlySpan<ProductInputEvent>.Empty);

    private static ProductCreateContext Context(IPersistenceService persistence) =>
        new(Engine(persistence), Content(), EmptyInput());

    private static ProductContent Content() => new(new ReadOnlyMemory<ProductContentFile>(
    [
        new ProductContentFile(System.Text.Encoding.UTF8.GetBytes("worldrpg/bundles/test.bundle.json"), System.Text.Encoding.UTF8.GetBytes("""{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.pack"}],"tuning":{"id":"test.tuning"}}""")),
        new ProductContentFile(System.Text.Encoding.UTF8.GetBytes("worldrpg/content-packs/test.pack.json"), System.Text.Encoding.UTF8.GetBytes("""{"kind":"worldrpg.content-pack","id":"test.pack","ruleset":"test","dependencies":[],"payload":"payload/pack.json"}""")),
        new ProductContentFile(System.Text.Encoding.UTF8.GetBytes("worldrpg/tuning/test.tuning.json"), System.Text.Encoding.UTF8.GetBytes("""{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/tuning.json"}""")),
        new ProductContentFile(System.Text.Encoding.UTF8.GetBytes("payload/pack.json"), System.Text.Encoding.UTF8.GetBytes("{}")),
        new ProductContentFile(System.Text.Encoding.UTF8.GetBytes("payload/tuning.json"), System.Text.Encoding.UTF8.GetBytes("{}")),
    ]));

    private static ProductInputConfiguration EmptyInput() => new(
        new InputBinding(1, 1, 1),
        new InputContext("test"u8.ToArray()),
        Array.Empty<ProductInputDescriptor>(), Array.Empty<ProductInputMapping>());

    private static IEngineContext Engine(IPersistenceService persistence)
    {
        IEngineContext context = DispatchProxy.Create<IEngineContext, PersistenceContextProxy>();
        ((PersistenceContextProxy)(object)context).PersistenceService = persistence;
        return context;
    }

    private sealed class LoadTestRuleset : ISaveableGameRuleset
    {
        public RulesetId Id => new("test");

        internal LoadTestSession? Current { get; private set; }

        internal LoadTestSession? Replacement { get; private set; }

        public IGameSession CreateSession(GameSessionContext context) => Track(new LoadTestSession());

        public IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved) =>
            Track(new LoadTestSession(), replacement: true);

        internal LoadTestSession RequireCurrent() =>
            Current ?? throw new InvalidOperationException("The ruleset did not create a session.");

        private LoadTestSession Track(LoadTestSession session, bool replacement = false)
        {
            if (replacement) Replacement = session;
            else Current = session;
            return session;
        }
    }

    private sealed class LoadTestSession : ISaveableGameSession, ISaveRequestingGameSession, IModeAwareGameSession
    {
        private bool _loadRequested;

        internal bool ThrowOnDispose { get; set; }

        internal bool Disposed { get; private set; }

        internal string? Outcome { get; private set; }

        internal void ArmLoadRequest() => _loadRequested = true;

        public void PublishInitial()
        {
        }

        public ProductUpdateResult Update(ProductUpdate update) => ProductUpdateResult.None;

        public RulesetSavePayload CaptureSave() => new(new RulesetId("test"), [1, 2, 3]);

        public bool TakeSaveRequest() => false;

        public bool TakeLoadRequest()
        {
            if (!_loadRequested) return false;
            _loadRequested = false;
            return true;
        }

        public void ReportSaveOutcome(string message) => Outcome = message;

        public ProductMode? PendingModeRequest => null;

        public bool PendingModeRequestClosesModal => false;

        public void ApplyProductMode(ProductMode mode)
        {
        }

        public void Dispose()
        {
            Disposed = true;
            if (ThrowOnDispose) throw new InvalidOperationException("boom");
        }
    }

    private class PersistenceContextProxy : DispatchProxy
    {
        internal IPersistenceService? PersistenceService { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Persistence" => PersistenceService,
            _ => throw new NotSupportedException($"Unexpected Engine service member '{targetMethod?.Name}'."),
        };
    }

    private sealed class InMemoryPersistenceService : IPersistenceService
    {
        private readonly Dictionary<(string Scope, string Key), Entry> _values = [];
        private readonly Dictionary<ulong, Entry?> _blobs = [];
        private ulong _nextBlob;

        public PersistenceStore OpenStore(PersistenceOpenRequest request) => new(new PersistenceStoreHandle(1), static () => { });
        public PersistenceSaveReceipt Save(PersistenceSaveRequest request)
        {
            ulong revision = _values.TryGetValue((request.Store.Handle.Value.ToString(), request.Key), out Entry? existing)
                ? checked(existing!.Revision + 1) : 1;
            _values[(request.Store.Handle.Value.ToString(), request.Key)] = new(revision, request.Payload.ToArray());
            return new PersistenceSaveReceipt(revision);
        }
        public PersistenceBlob Load(PersistenceLoadRequest request)
        {
            Entry? value = _values.TryGetValue((request.Store.Handle.Value.ToString(), request.Key), out Entry? found) ? found : null;
            ulong handle = ++_nextBlob;
            _blobs.Add(handle, value);
            return new(new PersistenceBlobHandle(handle), static () => { });
        }
        public PersistenceBlobInfo DescribeBlob(PersistenceBlob blob)
        {
            Entry? value = Require(blob);
            return value is null ? new(false, 0, 0) : new(true, value.Revision, checked((nuint)value.Payload.Length));
        }
        public void CopyBlob(PersistenceCopyBlobRequest request) => Require(request.Blob)?.Payload.CopyTo(request.Destination);
        public ReadOnlyMemory<byte> ReadBlobBytes(PersistenceBlob blob) => Require(blob)?.Payload.ToArray() ?? [];
        private Entry? Require(PersistenceBlob blob) => _blobs.TryGetValue(blob.Handle.Value, out Entry? value)
            ? value : throw new InvalidOperationException("Unknown persistence blob.");
        private sealed record Entry(ulong Revision, byte[] Payload);
    }
}
