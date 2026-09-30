using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Host;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Host.Tests;

/// <summary>
/// Save slots: metadata indexing, missing/incompatible/corrupt outcomes, overwrite guards and
/// deletion, and an old-or-new complete slot set after a fault at any payload or catalog write.
/// </summary>
public sealed class WorldRpgSaveSlotsTests
{
    [Fact]
    public void Slots_index_metadata_and_guard_overwrites()
    {
        InMemoryPersistenceService persistence = new();
        using WorldRpgSaveSlots slots = new(Engine(persistence), "worldrpg-test");
        Assert.Empty(slots.List());

        WorldRpgSaveSlotEntry entry = slots.SaveSlot("slot-a", "Before the dungeon", Envelope("daggerfall"));
        Assert.Equal("slot-a", entry.Key);
        Assert.Equal("daggerfall", entry.Ruleset);
        Assert.Single(slots.List());

        // A refused payload write must not create metadata for a save that never happened.
        PersistenceRevisionGuard guard = PersistenceRevisionGuard.Exact;
        byte[] originalCatalog = persistence.Get("worldrpg-test/slots", WorldRpgSaveSlots.IndexKey);
        InvalidOperationException conflict = Assert.Throws<InvalidOperationException>(
            () => slots.SaveSlot("slot-a", "Refused overwrite", Envelope("daggerfall"), guard, entry.Revision + 1));
        Assert.Contains("RevisionConflict", conflict.Message, StringComparison.Ordinal);
        Assert.Equal("Before the dungeon", slots.List()[0].Label);
        Assert.Equal(originalCatalog, persistence.Get("worldrpg-test/slots", WorldRpgSaveSlots.IndexKey));

        WorldRpgSaveSlotEntry second = slots.SaveSlot("slot-a", "After the dungeon", Envelope("daggerfall"));
        Assert.Equal("After the dungeon", slots.List()[0].Label);
        Assert.NotEqual(entry.Revision, second.Revision);
    }

    [Fact]
    public void Loads_name_missing_incompatible_and_corrupt_slots()
    {
        InMemoryPersistenceService persistence = new();
        using WorldRpgSaveSlots slots = new(Engine(persistence), "worldrpg-test");
        slots.SaveSlot("slot-a", "A", Envelope("daggerfall"));

        (GameSaveEnvelope? missing, WorldRpgSlotLoadDiagnostic? missingDiagnostic) = slots.LoadSlot("nope", "daggerfall");
        Assert.Null(missing);
        Assert.Equal("missing", missingDiagnostic!.Kind);

        (GameSaveEnvelope? wrong, WorldRpgSlotLoadDiagnostic? wrongDiagnostic) = slots.LoadSlot("slot-a", "other");
        Assert.Null(wrong);
        Assert.Equal("incompatible", wrongDiagnostic!.Kind);
        Assert.Contains("daggerfall", wrongDiagnostic.Message, StringComparison.Ordinal);

        (GameSaveEnvelope? ok, WorldRpgSlotLoadDiagnostic? okDiagnostic) = slots.LoadSlot("slot-a", "daggerfall");
        Assert.NotNull(ok);
        Assert.Null(okDiagnostic);

        // A catalog entry whose revision has no stored payload is corrupt, not a load of another revision.
        persistence.Put("worldrpg-test/slots", WorldRpgSaveSlots.IndexKey, System.Text.Encoding.UTF8.GetBytes(
            """[{"Key":"slot-a","Label":"A","SavedAtUtc":"2026-09-22T00:00:00Z","Ruleset":"daggerfall","Revision":5}]"""));
        (GameSaveEnvelope? corrupt, WorldRpgSlotLoadDiagnostic? corruptDiagnostic) = slots.LoadSlot("slot-a", "daggerfall");
        Assert.Null(corrupt);
        Assert.Equal("corrupt", corruptDiagnostic!.Kind);
    }

    [Fact]
    public void Deletes_drop_slots_from_the_index()
    {
        InMemoryPersistenceService persistence = new();
        using WorldRpgSaveSlots slots = new(Engine(persistence), "worldrpg-test");
        slots.SaveSlot("slot-a", "A", Envelope("daggerfall"));
        Assert.True(slots.DeleteSlot("slot-a"));
        Assert.Empty(slots.List());
        using WorldRpgSaveSlots reopened = new(Engine(persistence), "worldrpg-test");
        Assert.Empty(reopened.List());
        Assert.Equal("missing", reopened.LoadSlot("slot-a", "daggerfall").Diagnostic!.Kind);
        Assert.False(slots.DeleteSlot("slot-a"));
    }


    [Fact]
    public void Listing_reads_only_the_catalog_and_payloads_live_under_their_slot_key()
    {
        InMemoryPersistenceService persistence = new();
        using WorldRpgSaveSlots slots = new(Engine(persistence), "worldrpg-test");
        WorldRpgSaveSlotEntry first = slots.SaveSlot("slot-a", "A", Envelope("daggerfall", [7]));
        Assert.Equal([7], System.Text.Json.JsonSerializer.Deserialize<byte[]>(persistence.Get("worldrpg-test/slots", WorldRpgSaveSlots.PayloadKey("slot-a", first.Revision))));
        Assert.DoesNotContain("\"Payload\"", System.Text.Encoding.UTF8.GetString(persistence.Get("worldrpg-test/slots", WorldRpgSaveSlots.IndexKey)), StringComparison.Ordinal);

        // An overwrite replaces the previous revision's payload; a delete removes the current one.
        WorldRpgSaveSlotEntry second = slots.SaveSlot("slot-a", "A again", Envelope("daggerfall", [8]));
        Assert.False(persistence.Contains("worldrpg-test/slots", WorldRpgSaveSlots.PayloadKey("slot-a", first.Revision)));
        Assert.True(slots.DeleteSlot("slot-a"));
        Assert.False(persistence.Contains("worldrpg-test/slots", WorldRpgSaveSlots.PayloadKey("slot-a", second.Revision)));

        // A listing never opens a payload, so a missing payload only surfaces when that slot loads.
        slots.SaveSlot("slot-b", "B", Envelope("daggerfall", [9]));
        persistence.Remove("worldrpg-test/slots", WorldRpgSaveSlots.PayloadKey("slot-b", 1));
        Assert.Equal("B", Assert.Single(slots.List()).Label);
        Assert.Equal("corrupt", slots.LoadSlot("slot-b", "daggerfall").Diagnostic!.Kind);
    }

    [Fact]
    public void A_fault_at_any_slot_write_leaves_a_complete_old_or_new_slot_set()
    {
        InMemoryPersistenceService persistence = new();
        persistence.FailNextSaveBeforeCommit();
        using (WorldRpgSaveSlots slots = new(Engine(persistence), "worldrpg-test"))
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => slots.SaveSlot("slot-a", "A", Envelope("daggerfall", [1])));
            Assert.Contains("Reload the catalog", failure.Message, StringComparison.Ordinal);
        }

        using (WorldRpgSaveSlots afterNewFailure = new(Engine(persistence), "worldrpg-test"))
        {
            Assert.Empty(afterNewFailure.List());
            Assert.Equal("missing", afterNewFailure.LoadSlot("slot-a", "daggerfall").Diagnostic!.Kind);
        }

        using (WorldRpgSaveSlots slots = new(Engine(persistence), "worldrpg-test"))
        {
            slots.SaveSlot("slot-a", "Before", Envelope("daggerfall", [2]));
            slots.SaveSlot("slot-b", "Unrelated", Envelope("daggerfall", [3]));
            // A payload write that commits and then faults leaves the catalog naming the old slot.
            persistence.FailNextSaveAfterCommit();
            InvalidOperationException payloadFailure = Assert.Throws<InvalidOperationException>(() => slots.SaveSlot("slot-a", "Lost", Envelope("daggerfall", [5])));
            Assert.Contains("Reload the catalog", payloadFailure.Message, StringComparison.Ordinal);
        }

        using (WorldRpgSaveSlots afterPayloadFailure = new(Engine(persistence), "worldrpg-test"))
        {
            Assert.Equal("Before", afterPayloadFailure.List().Single(entry => entry.Key == "slot-a").Label);
            Assert.Equal([2], afterPayloadFailure.LoadSlot("slot-a", "daggerfall").Envelope!.Payload.Bytes.ToArray());
            // A catalog write that commits and then faults leaves the new slot complete.
            persistence.FailNextSaveAfterCommit(WorldRpgSaveSlots.IndexKey);
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => afterPayloadFailure.SaveSlot("slot-a", "After", Envelope("daggerfall", [4])));
            Assert.Contains("Reload the catalog", failure.Message, StringComparison.Ordinal);
        }

        using (WorldRpgSaveSlots afterOverwriteFailure = new(Engine(persistence), "worldrpg-test"))
        {
            Assert.Equal("After", afterOverwriteFailure.List().Single(entry => entry.Key == "slot-a").Label);
            Assert.Equal([4], afterOverwriteFailure.LoadSlot("slot-a", "daggerfall").Envelope!.Payload.Bytes.ToArray());
            Assert.Equal([3], afterOverwriteFailure.LoadSlot("slot-b", "daggerfall").Envelope!.Payload.Bytes.ToArray());
            persistence.FailNextSaveAfterCommit();
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => afterOverwriteFailure.DeleteSlot("slot-a"));
            Assert.Contains("Reload the catalog", failure.Message, StringComparison.Ordinal);
        }

        using (WorldRpgSaveSlots afterDeleteFailure = new(Engine(persistence), "worldrpg-test"))
        {
            Assert.Equal("missing", afterDeleteFailure.LoadSlot("slot-a", "daggerfall").Diagnostic!.Kind);
            Assert.Equal("Unrelated", afterDeleteFailure.List().Single().Label);
            Assert.Equal([3], afterDeleteFailure.LoadSlot("slot-b", "daggerfall").Envelope!.Payload.Bytes.ToArray());
            Assert.True(afterDeleteFailure.DeleteSlot("slot-b"));
        }

        using WorldRpgSaveSlots afterNormalDelete = new(Engine(persistence), "worldrpg-test");
        Assert.Empty(afterNormalDelete.List());
    }

    [Theory]
    [MemberData(nameof(InvalidIndexValues))]
    public void Invalid_present_catalogs_report_failure_and_preserve_the_malformed_value(string description, string? indexValue)
    {
        InMemoryPersistenceService persistence = new();
        using WorldRpgSaveSlots slots = new(Engine(persistence), "worldrpg-test");
        slots.SaveSlot("slot-a", "Before the dungeon", Envelope("daggerfall"));
        byte[] originalCatalog = persistence.Get("worldrpg-test/slots", WorldRpgSaveSlots.IndexKey);
        byte[] malformedIndex = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(indexValue);
        persistence.Put("worldrpg-test/slots", WorldRpgSaveSlots.IndexKey, malformedIndex);

        WorldRpgSaveFormatException listFailure = Assert.Throws<WorldRpgSaveFormatException>(() => slots.List());
        Assert.True(listFailure.Message.Contains("slot catalog", StringComparison.Ordinal), description);
        Assert.Equal("corrupt", slots.LoadSlot("slot-a", "daggerfall").Diagnostic!.Kind);
        Assert.Throws<WorldRpgSaveFormatException>(() => slots.DeleteSlot("slot-a"));
        Assert.Throws<WorldRpgSaveFormatException>(() => slots.SaveSlot("slot-a", "After the dungeon", Envelope("daggerfall")));

        Assert.NotEqual(originalCatalog, malformedIndex);
        Assert.Equal(malformedIndex, persistence.Get("worldrpg-test/slots", WorldRpgSaveSlots.IndexKey));
    }

    public static IEnumerable<object?[]> InvalidIndexValues =>
    [
        ["present null", null],
        ["empty value", ""],
        ["malformed JSON", "not-json"],
        ["null index list", "null"],
        ["incomplete entry", "[{}]"],
        ["duplicate slot key", """[{"Key":"slot-a","Label":"A","SavedAtUtc":"2026-09-22T00:00:00Z","Ruleset":"daggerfall","Revision":1},{"Key":"slot-a","Label":"B","SavedAtUtc":"2026-09-22T01:00:00Z","Ruleset":"daggerfall","Revision":2}]"""],
    ];

    private static GameSaveEnvelope Envelope(string ruleset, byte[]? payload = null) =>
        new(new RulesetSavePayload(new RulesetId(ruleset), payload ?? [1, 2, 3]));

    private static IEngineContext Engine(IPersistenceService persistence)
    {
        IEngineContext context = DispatchProxy.Create<IEngineContext, PersistenceContextProxy>();
        PersistenceContextProxy proxy = (PersistenceContextProxy)(object)context;
        proxy.PersistenceService = persistence;
        return context;
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

    /// <summary>Bounded test double for the generated persistence contract; it does not model product storage semantics.</summary>
    private sealed class InMemoryPersistenceService : IPersistenceService
    {
        private readonly Dictionary<(string Scope, string Key), Entry> _values = [];
        private readonly Dictionary<ulong, Entry?> _blobs = [];
        private readonly Dictionary<ulong, string> _scopes = [];
        private ulong _nextHandle;
        private SaveFault _nextSaveFault;

        private string? _faultKey;

        internal void FailNextSaveBeforeCommit() => _nextSaveFault = SaveFault.BeforeCommit;

        /// <summary>Faults the next save after it commits, or the next save of <paramref name="key"/> when one is named.</summary>
        internal void FailNextSaveAfterCommit(string? key = null)
        {
            _nextSaveFault = SaveFault.AfterCommit;
            _faultKey = key;
        }

        internal bool Contains(string scope, string key) => _values.ContainsKey((scope, key));
        internal void Remove(string scope, string key) => _values.Remove((scope, key));
        internal void Put(string scope, string key, byte[] payload) => _values[(scope, key)] = new(1, payload.ToArray());
        internal byte[] Get(string scope, string key) => _values[(scope, key)].Payload.ToArray();
        public PersistenceStore OpenStore(PersistenceOpenRequest request)
        {
            ulong handle = ++_nextHandle;
            _scopes.Add(handle, request.Scope);
            return new(new PersistenceStoreHandle(handle), () => _scopes.Remove(handle));
        }
        public PersistenceSaveReceipt Save(PersistenceSaveRequest request)
        {
            SaveFault fault = _faultKey is null || _faultKey == request.Key ? _nextSaveFault : SaveFault.None;
            if (fault != SaveFault.None)
            {
                _nextSaveFault = SaveFault.None;
                _faultKey = null;
            }
            if (fault == SaveFault.BeforeCommit) throw new InvalidOperationException("Injected persistence save fault before commit.");

            string scope = _scopes[request.Store.Handle.Value];
            (string Scope, string Key) key = (scope, request.Key);
            bool present = _values.TryGetValue(key, out Entry? existing);
            if ((request.RevisionGuard == PersistenceRevisionGuard.Absent && present)
                || (request.RevisionGuard == PersistenceRevisionGuard.Exact && (!present || existing!.Revision != request.ExpectedRevision)))
                return new PersistenceSaveReceipt(PersistenceSaveOutcome.RevisionConflict, existing?.Revision ?? 0);
            ulong revision = present ? checked(existing!.Revision + 1) : 1;
            _values[key] = new(revision, request.Payload.ToArray());
            if (fault == SaveFault.AfterCommit) throw new InvalidOperationException("Injected persistence save fault after commit.");
            return new PersistenceSaveReceipt(revision);
        }
        public PersistenceDeleteReceipt Delete(PersistenceDeleteRequest request)
        {
            string scope = _scopes[request.Store.Handle.Value];
            (string Scope, string Key) key = (scope, request.Key);
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
            string scope = _scopes[request.Store.Handle.Value];
            Entry? value = _values.TryGetValue((scope, request.Key), out Entry? found) ? found : null;
            ulong handle = ++_nextHandle;
            _blobs.Add(handle, value);
            return new(new PersistenceBlobHandle(handle), () => _blobs.Remove(handle));
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
        private enum SaveFault { None, BeforeCommit, AfterCommit }
    }
}
