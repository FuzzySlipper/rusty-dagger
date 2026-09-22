using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Host;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Host.Tests;

/// <summary>
/// Save slots: metadata indexing, missing/incompatible/corrupt outcomes, overwrite guards and
/// deletion from the index.
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

        // Overwriting takes the guard: a stale revision is refused and the slot keeps its save.
        PersistenceRevisionGuard guard = PersistenceRevisionGuard.Exact;
        using WorldRpgSaveStore direct = new(Engine(persistence), "worldrpg-test");
        PersistenceSaveReceipt conflict = direct.Save("slot-a", Envelope("daggerfall"), guard, entry.Revision + 1);
        Assert.Equal(PersistenceSaveOutcome.RevisionConflict, conflict.Outcome);
        Assert.Equal("Before the dungeon", slots.List()[0].Label);

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

        persistence.Put("worldrpg-test", "slot-a", "not-json"u8.ToArray());
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
        Assert.False(slots.DeleteSlot("slot-a"));
    }

    private static GameSaveEnvelope Envelope(string ruleset) =>
        new(new RulesetSavePayload(new RulesetId(ruleset), [1, 2, 3]));

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
