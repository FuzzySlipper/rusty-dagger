using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Host;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Host.Tests;

public sealed class WorldRpgSaveStoreTests
{
    [Fact]
    public void Store_roundtrips_a_typed_envelope_and_honors_the_engine_revision_guard()
    {
        InMemoryPersistenceService persistence = new();
        GameSaveEnvelope saved = Envelope();
        PersistenceSaveReceipt first;
        using (WorldRpgSaveStore store = new(Engine(persistence), "worldrpg-test"))
        {
            first = store.Save("slot", saved, PersistenceRevisionGuard.Absent);
        }

        // The next host lifetime gets a new ProductStateStore and decodes the persisted current DTO.
        using WorldRpgSaveStore reopened = new(Engine(persistence), "worldrpg-test");
        ProductStateLoad<GameSaveEnvelope> loaded = reopened.Load("slot");

        Assert.True(loaded.Present);
        Assert.Equal(first.Revision, loaded.Revision);
        Assert.Equal("test", loaded.State!.Payload.Ruleset.Value);
        Assert.Equal([1, 2, 3], loaded.State.Payload.Bytes.ToArray());
        PersistenceSaveReceipt conflict = reopened.Save("slot", saved, PersistenceRevisionGuard.Exact, first.Revision + 1);
        Assert.Equal(PersistenceSaveOutcome.RevisionConflict, conflict.Outcome);
        Assert.Equal(first.Revision, conflict.Revision);
        Assert.Equal(first.Revision, reopened.Load("slot").Revision);
    }

    [Fact]
    public void Store_rejects_corrupt_envelopes_as_a_typed_format_failure()
    {
        InMemoryPersistenceService persistence = new();
        persistence.Put("worldrpg-test", "slot", "not-json"u8.ToArray());
        using WorldRpgSaveStore store = new(Engine(persistence), "worldrpg-test");

        Assert.Throws<WorldRpgSaveFormatException>(() => store.Load("slot"));
    }

    [Theory]
    [MemberData(nameof(InvalidEnvelopeJson))]
    public void Store_rejects_structurally_incomplete_envelopes(string payload)
    {
        InMemoryPersistenceService persistence = new();
        persistence.Put("worldrpg-test", "slot", System.Text.Encoding.UTF8.GetBytes(payload));
        using WorldRpgSaveStore store = new(Engine(persistence), "worldrpg-test");

        Assert.Throws<WorldRpgSaveFormatException>(() => store.Load("slot"));
    }

    [Fact]
    public void Resume_reports_corrupt_storage_without_selecting_or_constructing_a_session()
    {
        InMemoryPersistenceService persistence = new();
        persistence.Put("worldrpg-test", "slot", System.Text.Encoding.UTF8.GetBytes("{\"Ruleset\":null,\"Payload\":[]}"));
        IEngineContext engine = Engine(persistence);
        using WorldRpgSaveStore store = new(engine, "worldrpg-test");
        ProductCreateContext context = new(engine, new ProductContent(Array.Empty<ProductContentFile>()), EmptyInput());

        WorldRpgResumeResult result = WorldRpgProduct.TryResume(context, store, "slot");

        Assert.False(result.IsResumed);
        Assert.Null(result.Product);
        Assert.Contains(result.Diagnostics, value => value.Code == "corrupt");
    }

    [Fact]
    public void Resume_reports_missing_selected_bundle_without_throwing()
    {
        InMemoryPersistenceService persistence = new();
        IEngineContext engine = Engine(persistence);
        using WorldRpgSaveStore store = new(engine, "worldrpg-test");
        store.Save("slot", Envelope());

        WorldRpgResumeResult result = WorldRpgProduct.TryResume(new ProductCreateContext(engine, new ProductContent(Array.Empty<ProductContentFile>()), EmptyInput()), store, "slot");

        Assert.False(result.IsResumed);
        Assert.Contains(result.Diagnostics, value => value.Code == "selection");
    }

    [Fact]
    public void Resume_reports_unknown_built_in_ruleset_without_throwing()
    {
        InMemoryPersistenceService persistence = new();
        IEngineContext engine = Engine(persistence);
        using WorldRpgSaveStore store = new(engine, "worldrpg-test");
        store.Save("slot", Envelope());
        ProductContent content = Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"unknown","contentPacks":[],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"unknown","payload":"payload/tuning.json"}"""),
            ("payload/tuning.json", "{}"));

        WorldRpgResumeResult result = WorldRpgProduct.TryResume(new ProductCreateContext(engine, content, EmptyInput()), store, "slot", bundle: new GameBundleId("test.bundle"));

        Assert.False(result.IsResumed);
        Assert.Contains(result.Diagnostics, value => value.Code == "selection");
    }

    [Fact]
    public void Resume_constructs_a_fresh_session_from_a_current_state_payload()
    {
        InMemoryPersistenceService persistence = new();
        IEngineContext engine = Engine(persistence);
        using WorldRpgSaveStore store = new(engine, "worldrpg-test");
        ProductContent content = Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.pack"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/content-packs/test.pack.json", """{"kind":"worldrpg.content-pack","id":"test.pack","ruleset":"test","dependencies":[],"payload":"payload/pack.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/tuning.json"}"""),
            ("payload/pack.json", "{}"),
            ("payload/tuning.json", "{}"));
        store.Save("slot", new GameSaveEnvelope(new RulesetSavePayload(new RulesetId("test"), [1, 2, 3])));

        WorldRpgResumeResult result = WorldRpgProduct.TryResume(
            new ProductCreateContext(engine, content, EmptyInput()),
            store,
            "slot",
            ruleset: new ReportingRuleset(),
            bundle: new GameBundleId("test.bundle"));

        Assert.True(result.IsResumed, string.Join("; ", result.Diagnostics.Select(value => $"{value.Code}: {value.Message}")));
        Assert.True(result.IsComplete);
        Assert.Empty(result.Diagnostics);
        using WorldRpgProduct? product = result.Product;
        Assert.NotNull(product);

        // A resumed product has already been past the entry screen: starting it shows the world the save
        // restored rather than a screen offering to begin a run that is already in progress.
        product.Start();
        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal("the resumed product started in the world it restored", product.ModeHistory[^1].Reason);
    }

    private sealed class ReportingRuleset : ISaveableGameRuleset
    {
        public RulesetId Id => new("test");

        public IGameSession CreateSession(GameSessionContext context) => new ReportingSession();

        public IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved) =>
            new ReportingSession();
    }

    private sealed class ReportingSession : IGameSession
    {
        public void PublishInitial()
        {
        }

        public ProductUpdateResult Update(ProductUpdate update) => ProductUpdateResult.None;

        public void Dispose()
        {
        }
    }

    public static IEnumerable<object[]> InvalidEnvelopeJson =>
    [
        ["{}"],
        ["null"],
        ["""{"Ruleset":"test"}"""],
        ["""{"Ruleset":null,"Payload":[]}"""],
        ["""{"Ruleset":"","Payload":[]}"""],
        ["""{"Ruleset":"test","Payload":null}"""],
        ["""{"Ruleset":"test","Payload":[]}"""],
    ];

    private static GameSaveEnvelope Envelope() => new(
        new RulesetSavePayload(new RulesetId("test"), [1, 2, 3]));

    private static IEngineContext Engine(IPersistenceService persistence)
    {
        IEngineContext context = DispatchProxy.Create<IEngineContext, PersistenceContextProxy>();
        PersistenceContextProxy proxy = (PersistenceContextProxy)(object)context;
        proxy.PersistenceService = persistence;
        return context;
    }

    private static ProductInputConfiguration EmptyInput() => new(
        new InputBinding(1, 1, 1),
        new InputContext("test"u8.ToArray()),
        Array.Empty<ProductInputDescriptor>(), Array.Empty<ProductInputMapping>());

    private static ProductContent Content(params (string Path, string Value)[] files) => new(files.Select(file => new ProductContentFile(System.Text.Encoding.UTF8.GetBytes(file.Path), System.Text.Encoding.UTF8.GetBytes(file.Value))).ToArray());

    private class PersistenceContextProxy : DispatchProxy
    {
        internal IPersistenceService? PersistenceService { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Persistence" => PersistenceService,
            _ => throw new NotSupportedException($"Unexpected Engine service member '{targetMethod?.Name}'."),
        };
    }

    /// <summary>Bounded test double for the generated persistence contract; it does not model product storage semantics.</summary>
    private sealed class InMemoryPersistenceService : IPersistenceService
    {
        private readonly Dictionary<(string Scope, string Key), Entry> _values = [];
        private readonly Dictionary<ulong, Entry?> _blobs = [];
        private ulong _nextBlob;

        internal void Put(string scope, string key, byte[] payload) => _values[(scope, key)] = new(1, payload.ToArray());
        public PersistenceStore OpenStore(PersistenceOpenRequest request) => new(new PersistenceStoreHandle(1), static () => { });
        public PersistenceSaveReceipt Save(PersistenceSaveRequest request)
        {
            string scope = "worldrpg-test";
            (string Scope, string Key) key = (scope, request.Key);
            bool present = _values.TryGetValue(key, out Entry? existing);
            if ((request.RevisionGuard == PersistenceRevisionGuard.Absent && present)
                || (request.RevisionGuard == PersistenceRevisionGuard.Exact && (!present || existing!.Revision != request.ExpectedRevision)))
                return new PersistenceSaveReceipt(PersistenceSaveOutcome.RevisionConflict, existing?.Revision ?? 0);
            ulong revision = present ? checked(existing!.Revision + 1) : 1;
            _values[key] = new(revision, request.Payload.ToArray());
            return new PersistenceSaveReceipt(revision);
        }
        public PersistenceDeleteReceipt Delete(PersistenceDeleteRequest request)
        {
            (string Scope, string Key) key = ("worldrpg-test", request.Key);
            bool present = _values.TryGetValue(key, out Entry? existing);
            if ((request.RevisionGuard == PersistenceRevisionGuard.Absent && present)
                || (request.RevisionGuard == PersistenceRevisionGuard.Exact && (!present || existing!.Revision != request.ExpectedRevision)))
                return new(PersistenceDeleteOutcome.RevisionConflict, existing?.Revision ?? 0);
            if (!present) return new(PersistenceDeleteOutcome.Missing, 0);
            _values.Remove(key);
            return new(PersistenceDeleteOutcome.Deleted, existing!.Revision);
        }
        public PersistenceBlob Load(PersistenceLoadRequest request)
        {
            Entry? value = _values.TryGetValue(("worldrpg-test", request.Key), out Entry? found) ? found : null;
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
        private Entry? Require(PersistenceBlob blob) => _blobs.TryGetValue(blob.Handle.Value, out Entry? value) ? value : throw new InvalidOperationException("Unknown persistence blob.");
        private sealed record Entry(ulong Revision, byte[] Payload);
    }
}
