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
        using WorldRpgSaveStore store = new(Engine(persistence), "worldrpg-test");
        GameSaveEnvelope saved = Envelope();

        PersistenceSaveReceipt first = store.Save("slot", saved, PersistenceRevisionGuard.Absent);
        ProductStateLoad<GameSaveEnvelope> loaded = store.Load("slot");

        Assert.True(loaded.Present);
        Assert.Equal(first.Revision, loaded.Revision);
        Assert.Equal("test", loaded.State!.Payload.Ruleset.Value);
        Assert.Equal([1, 2, 3], loaded.State.Payload.Bytes.ToArray());
        Assert.Throws<InvalidOperationException>(() => store.Save("slot", saved, PersistenceRevisionGuard.Exact, first.Revision + 1));
    }

    [Fact]
    public void Store_rejects_corrupt_envelopes_as_a_typed_format_failure()
    {
        InMemoryPersistenceService persistence = new();
        persistence.Put("worldrpg-test", "slot", WorldRpgSaveStore.SchemaVersion, "not-json"u8.ToArray());
        using WorldRpgSaveStore store = new(Engine(persistence), "worldrpg-test");

        Assert.Throws<WorldRpgSaveFormatException>(() => store.Load("slot"));
    }

    [Theory]
    [MemberData(nameof(InvalidEnvelopeJson))]
    public void Store_rejects_structurally_incomplete_schema_one_envelopes(string payload)
    {
        InMemoryPersistenceService persistence = new();
        persistence.Put("worldrpg-test", "slot", WorldRpgSaveStore.SchemaVersion, System.Text.Encoding.UTF8.GetBytes(payload));
        using WorldRpgSaveStore store = new(Engine(persistence), "worldrpg-test");

        Assert.Throws<WorldRpgSaveFormatException>(() => store.Load("slot"));
    }

    [Fact]
    public void Resume_reports_corrupt_storage_without_selecting_or_constructing_a_session()
    {
        InMemoryPersistenceService persistence = new();
        persistence.Put("worldrpg-test", "slot", WorldRpgSaveStore.SchemaVersion, System.Text.Encoding.UTF8.GetBytes(NullComposition));
        IEngineContext engine = Engine(persistence);
        using WorldRpgSaveStore store = new(engine, "worldrpg-test");
        ProductCreateContext context = new(engine, new ProductContent(Array.Empty<ProductContentFile>()), EmptyInput());

        WorldRpgResumeResult result = WorldRpgProduct.TryResume(context, store, "slot");

        Assert.False(result.IsResumed);
        Assert.Null(result.Product);
        Assert.Contains(result.Diagnostics, value => value.Code == "corrupt");
    }

    [Fact]
    public void Resume_reports_an_unsupported_engine_storage_schema_before_selection()
    {
        InMemoryPersistenceService persistence = new();
        persistence.Put("worldrpg-test", "slot", WorldRpgSaveStore.SchemaVersion + 1, "{}"u8.ToArray());
        IEngineContext engine = Engine(persistence);
        using WorldRpgSaveStore store = new(engine, "worldrpg-test");

        WorldRpgResumeResult result = WorldRpgProduct.TryResume(new ProductCreateContext(engine, new ProductContent(Array.Empty<ProductContentFile>()), EmptyInput()), store, "slot");

        Assert.False(result.IsResumed);
        Assert.Contains(result.Diagnostics, value => value.Code == "storage-schema");
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
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","schemaVersion":1,"id":"test.bundle","version":1,"ruleset":"unknown","contentPacks":[],"tuning":{"id":"test.tuning","version":1}}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","schemaVersion":1,"id":"test.tuning","version":1,"ruleset":"unknown","payload":"payload/tuning.json"}"""),
            ("payload/tuning.json", "{}"));

        WorldRpgResumeResult result = WorldRpgProduct.TryResume(new ProductCreateContext(engine, content, EmptyInput()), store, "slot", bundle: new GameBundleId("test.bundle"));

        Assert.False(result.IsResumed);
        Assert.Contains(result.Diagnostics, value => value.Code == "selection");
    }

    public static IEnumerable<object[]> InvalidEnvelopeJson =>
    [
        [NullComposition],
        ["""{"Ruleset":"test","RulesetSchemaVersion":1,"Payload":[]}"""],
        [EnvelopeJson("null")],
        [EnvelopeJson("[null]")],
        [EnvelopeJson("[]", ruleset: "null")],
        [EnvelopeJson("[]", payload: "null")],
        [EnvelopeJson("[]", bundle: "null")],
        [EnvelopeJson("[]", tuning: "\"\"")],
    ];

    private const string NullComposition = """{"Composition":null,"Ruleset":"test","RulesetSchemaVersion":1,"Payload":[]}""";

    private static string EnvelopeJson(string packs, string ruleset = "\"test\"", string payload = "[]", string bundle = "\"bundle\"", string tuning = "\"tuning\"") =>
        $$"""{"Composition":{"Bundle":{{bundle}},"BundleSchemaVersion":1,"BundleVersion":1,"Ruleset":"test","ContentPacks":{{packs}},"Tuning":{{tuning}},"TuningSchemaVersion":1,"TuningVersion":1,"Fingerprint":"a","ContentFingerprint":"b","TuningFingerprint":"c"},"Ruleset":{{ruleset}},"RulesetSchemaVersion":1,"Payload":{{payload}}}""";

    private static GameSaveEnvelope Envelope() => new(
        new SaveCompositionIdentity(
            new ResolvedBundleIdentity(new GameBundleId("test.bundle"), 1, 1),
            new RulesetId("test"),
            [new ResolvedContentPackIdentity(new ContentPackId("test.pack"), 1, 1)],
            new ResolvedTuningIdentity(new TuningProfileId("test.tuning"), 1, 1),
            "fingerprint", "content", "tuning"),
        new RulesetSavePayload(new RulesetId("test"), 1, [1, 2, 3]));

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

        internal void Put(string scope, string key, uint schemaVersion, byte[] payload) => _values[(scope, key)] = new(schemaVersion, 1, payload.ToArray());
        public PersistenceStore OpenStore(PersistenceOpenRequest request) => new(new PersistenceStoreHandle(1), static () => { });
        public PersistenceSaveReceipt Save(PersistenceSaveRequest request)
        {
            string scope = "worldrpg-test";
            (string Scope, string Key) key = (scope, request.Key);
            bool present = _values.TryGetValue(key, out Entry? existing);
            if ((request.RevisionGuard == PersistenceRevisionGuard.Absent && present)
                || (request.RevisionGuard == PersistenceRevisionGuard.Exact && (!present || existing!.Revision != request.ExpectedRevision)))
                throw new InvalidOperationException("The persistence revision guard rejected the save.");
            ulong revision = present ? checked(existing!.Revision + 1) : 1;
            _values[key] = new(request.SchemaVersion, revision, request.Payload.ToArray());
            return new PersistenceSaveReceipt(revision, request.SchemaVersion);
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
            return value is null ? new(false, 0, 0, 0) : new(true, value.SchemaVersion, value.Revision, checked((nuint)value.Payload.Length));
        }
        public void CopyBlob(PersistenceCopyBlobRequest request) => Require(request.Blob)?.Payload.CopyTo(request.Destination);
        public ReadOnlyMemory<byte> ReadBlobBytes(PersistenceBlob blob) => Require(blob)?.Payload.ToArray() ?? [];
        private Entry? Require(PersistenceBlob blob) => _blobs.TryGetValue(blob.Handle.Value, out Entry? value) ? value : throw new InvalidOperationException("Unknown persistence blob.");
        private sealed record Entry(uint SchemaVersion, ulong Revision, byte[] Payload);
    }
}
