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
    public void Named_slot_actions_save_select_overwrite_load_and_delete_real_payloads()
    {
        InMemoryPersistenceService persistence = new();
        LoadTestRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(persistence), ruleset, new GameBundleId("test.bundle"));
        product.Start();
        LoadTestSession session = ruleset.RequireCurrent();

        session.SaveValue = 11;
        session.ArmSlotRequest(new(SaveSlotOperation.Save, Label: "Before the dungeon"));
        product.Update(Update(1));
        Assert.Contains("Before the dungeon", session.Outcome, StringComparison.Ordinal);
        SaveSlotSummary first = Assert.Single(session.SaveSlots);
        Assert.Equal("slot-1", first.Key);

        session.SaveValue = 22;
        session.ArmSlotRequest(new(SaveSlotOperation.Save, Label: "After the dungeon"));
        product.Update(Update(2));
        Assert.Equal(2, session.SaveSlots.Count);
        SaveSlotSummary second = Assert.Single(session.SaveSlots, value => value.Key == "slot-2");

        session.SaveValue = 33;
        session.ArmSlotRequest(new(SaveSlotOperation.Save, first.Key, "Revisited dungeon"));
        product.Update(Update(3));
        Assert.Contains(first.Key, session.Outcome, StringComparison.Ordinal);
        Assert.Equal("Before the dungeon", Assert.Single(session.SaveSlots, value => value.Key == first.Key).Label);

        session.ArmSlotRequest(new(SaveSlotOperation.Save, first.Key, "Revisited dungeon", Confirm: true));
        product.Update(Update(4));
        Assert.Equal("Revisited dungeon", Assert.Single(session.SaveSlots, value => value.Key == first.Key).Label);

        session.ArmSlotRequest(new(SaveSlotOperation.Load, second.Key));
        product.Update(Update(5));
        LoadTestSession restored = Assert.IsType<LoadTestSession>(ruleset.Replacement);
        Assert.Equal((byte)22, restored.SaveValue);
        Assert.Contains("loaded", restored.Outcome, StringComparison.OrdinalIgnoreCase);

        restored.ArmSlotRequest(new(SaveSlotOperation.Delete, second.Key));
        product.Update(Update(6));
        Assert.Contains(second.Key, restored.Outcome, StringComparison.Ordinal);
        restored.ArmSlotRequest(new(SaveSlotOperation.Delete, second.Key, Confirm: true));
        product.Update(Update(7));
        Assert.Single(restored.SaveSlots);
        using WorldRpgSaveSlots reopened = new(Context(persistence).Engine, "worldrpg.saves");
        (GameSaveEnvelope? deleted, WorldRpgSlotLoadDiagnostic? diagnostic) = reopened.LoadSlot(second.Key, "test");
        Assert.Null(deleted);
        Assert.Equal("missing", diagnostic!.Kind);
    }

    [Fact]
    public void Named_slot_load_and_catalog_failures_keep_the_live_session_and_publish_diagnostics()
    {
        InMemoryPersistenceService persistence = new();
        LoadTestRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(persistence), ruleset, new GameBundleId("test.bundle"));
        product.Start();
        LoadTestSession session = ruleset.RequireCurrent();
        session.ArmSlotRequest(new(SaveSlotOperation.Save, Label: "Before the dungeon"));
        product.Update(Update(1));

        persistence.Put(WorldRpgSaveSlots.IndexKey, System.Text.Encoding.UTF8.GetBytes(
            """[{"Key":"slot-1","Label":"Before the dungeon","SavedAtUtc":"2026-09-22T00:00:00Z","Ruleset":"test","Payload":"","Revision":1}]"""));
        session.ArmSlotRequest(new(SaveSlotOperation.Load, "slot-1"));
        product.Update(Update(2));
        Assert.Same(session, ruleset.RequireCurrent());
        Assert.Null(ruleset.Replacement);
        Assert.StartsWith("Load failed:", session.Outcome, StringComparison.Ordinal);

        persistence.Put(WorldRpgSaveSlots.IndexKey, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes("not-json"));
        session.ArmSlotRequest(new(SaveSlotOperation.List));
        product.Update(Update(3));
        Assert.Same(session, ruleset.RequireCurrent());
        Assert.Empty(session.SaveSlots);
        Assert.Contains("slot catalog", session.SaveSlotDiagnostic, StringComparison.Ordinal);
        Assert.StartsWith("Save slots unavailable:", session.Outcome, StringComparison.Ordinal);
    }


    [Fact]
    public void Player_preferences_apply_before_publication_and_survive_restart_and_load()
    {
        InMemoryPersistenceService persistence = new();
        LoadTestRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(persistence), ruleset, new GameBundleId("test.bundle"));
        LoadTestSession initial = ruleset.RequireCurrent();
        Assert.Null(initial.PreferencesAtInitialPublication);

        product.Start();
        product.Begin();
        initial.ArmPlayerPreferencesSave("look:inverted");
        product.Update(Update(1));
        Assert.Equal("look:inverted", initial.ActivePreferences);
        Assert.Contains("saved", initial.PlayerPreferencesOutcome, StringComparison.OrdinalIgnoreCase);

        product.Restart();
        LoadTestSession restarted = ruleset.RequireCurrent();
        Assert.Equal("look:inverted", restarted.ActivePreferences);
        Assert.Equal("look:inverted", restarted.PreferencesAtInitialPublication);

        restarted.ArmSlotRequest(new(SaveSlotOperation.Save, Label: "For preference load"));
        product.Update(Update(2));
        restarted.ArmSlotRequest(new(SaveSlotOperation.Load, "slot-1"));
        product.Update(Update(3));
        LoadTestSession loaded = Assert.IsType<LoadTestSession>(ruleset.Replacement);
        Assert.Equal("look:inverted", loaded.ActivePreferences);
        Assert.Equal("look:inverted", loaded.PreferencesAtInitialPublication);

        product.QuitToTitle();
        LoadTestSession title = ruleset.RequireCurrent();
        Assert.Equal("look:inverted", title.ActivePreferences);
        Assert.Equal("look:inverted", title.PreferencesAtInitialPublication);
    }

    [Fact]
    public void Corrupt_or_unwritten_player_preferences_keep_defaults_or_the_active_session_binding()
    {
        InMemoryPersistenceService persistence = new();
        persistence.Put("test", "not-json"u8.ToArray());
        LoadTestRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(persistence), ruleset, new GameBundleId("test.bundle"));
        LoadTestSession session = ruleset.RequireCurrent();
        Assert.Null(session.ActivePreferences);
        Assert.Null(session.PreferencesAtInitialPublication);
        Assert.Contains("defaults are active", session.PlayerPreferencesOutcome, StringComparison.Ordinal);

        product.Start();
        session.ArmPlayerPreferencesSave("look:normal");
        persistence.ThrowOnNextSave = true;
        product.Update(Update(1));
        Assert.Equal("look:normal", session.ActivePreferences);
        Assert.Contains("active but were not saved", session.PlayerPreferencesOutcome, StringComparison.Ordinal);
    }

    [Fact]
    public void A_load_whose_previous_session_fails_disposal_keeps_the_current_session_and_reports()
    {
        InMemoryPersistenceService persistence = new();
        LoadTestRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(persistence), ruleset, new GameBundleId("test.bundle"));
        product.Start();

        LoadTestSession previous = ruleset.RequireCurrent();
        previous.ArmSlotRequest(new(SaveSlotOperation.Save, Label: "Before the failure"));
        product.Update(Update(1));
        previous.ThrowOnDispose = true;
        previous.ArmSlotRequest(new(SaveSlotOperation.Load, "slot-1"));
        ProductUpdateResult result = product.Update(Update(2));

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
            Track(new LoadTestSession { SaveValue = saved.Bytes.Span[0] }, replacement: true);

        internal LoadTestSession RequireCurrent() =>
            Current ?? throw new InvalidOperationException("The ruleset did not create a session.");

        private LoadTestSession Track(LoadTestSession session, bool replacement = false)
        {
            if (replacement) Replacement = session;
            else Current = session;
            return session;
        }
    }

    private sealed class LoadTestSession : ISaveableGameSession, ISaveRequestingGameSession, IModeAwareGameSession, IPlayerPreferencesSession
    {
        private SaveSlotRequest? _saveSlotRequest;
        private string? _playerPreferencesSave;

        internal bool ThrowOnDispose { get; set; }

        internal bool Disposed { get; private set; }

        internal string? Outcome { get; private set; }

        internal byte SaveValue { get; set; } = 1;

        internal IReadOnlyList<SaveSlotSummary> SaveSlots { get; private set; } = [];

        internal string? SaveSlotDiagnostic { get; private set; }

        internal string? ActivePreferences { get; private set; }

        internal string? PreferencesAtInitialPublication { get; private set; }

        internal string? PlayerPreferencesOutcome { get; private set; }

        internal void ArmPlayerPreferencesSave(string serialized)
        {
            ActivePreferences = serialized;
            _playerPreferencesSave = serialized;
        }

        internal void ArmSlotRequest(SaveSlotRequest request) => _saveSlotRequest = request;

        public void PublishInitial()
        {
            PreferencesAtInitialPublication = ActivePreferences;
        }

        public string CapturePlayerPreferences() => ActivePreferences ?? string.Empty;

        public void ApplyPlayerPreferences(string? serialized) => ActivePreferences = serialized;

        public string? TakePlayerPreferencesSave()
        {
            string? save = _playerPreferencesSave;
            _playerPreferencesSave = null;
            return save;
        }

        public void ReportPlayerPreferencesOutcome(string message) => PlayerPreferencesOutcome = message;

        public ProductUpdateResult Update(ProductUpdate update) => ProductUpdateResult.None;

        public RulesetSavePayload CaptureSave() => new(new RulesetId("test"), [SaveValue]);

        public SaveSlotRequest? TakeSaveSlotRequest()
        {
            SaveSlotRequest? request = _saveSlotRequest;
            _saveSlotRequest = null;
            return request;
        }

        public void ReportSaveOutcome(string message) => Outcome = message;

        public void ReportSaveSlots(IReadOnlyList<SaveSlotSummary> slots, string? diagnostic)
        {
            SaveSlots = slots.ToArray();
            SaveSlotDiagnostic = diagnostic;
        }

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

        internal bool ThrowOnNextSave { get; set; }

        internal void Put(string key, byte[] payload) => _values[("1", key)] = new(1, payload.ToArray());

        public PersistenceStore OpenStore(PersistenceOpenRequest request) => new(new PersistenceStoreHandle(1), static () => { });
        public PersistenceSaveReceipt Save(PersistenceSaveRequest request)
        {
            if (ThrowOnNextSave)
            {
                ThrowOnNextSave = false;
                throw new InvalidOperationException("Injected persistence save fault.");
            }

            ulong revision = _values.TryGetValue((request.Store.Handle.Value.ToString(), request.Key), out Entry? existing)
                ? checked(existing!.Revision + 1) : 1;
            _values[(request.Store.Handle.Value.ToString(), request.Key)] = new(revision, request.Payload.ToArray());
            return new PersistenceSaveReceipt(revision);
        }
        public PersistenceDeleteReceipt Delete(PersistenceDeleteRequest request)
        {
            (string Scope, string Key) key = (request.Store.Handle.Value.ToString(), request.Key);
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
