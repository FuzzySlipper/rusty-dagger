using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Small persistence service that can be shared across fresh EngineContext fakes, as in the host
/// close/reopen tests. Each open hands out its own store handle bound to the scope it named, and values
/// live under that scope, so two scopes never see each other's keys while two opens of one scope do.
/// </summary>
internal sealed class InMemoryPersistenceService : IPersistenceService
{
    private readonly Dictionary<(string Scope, string Key), Entry> _values = [];
    private readonly Dictionary<ulong, Entry?> _blobs = [];
    private readonly Dictionary<ulong, string> _stores = [];
    private ulong _nextBlob;
    private ulong _nextStore;

    /// <summary>Every scope a store was opened for, in the order they were opened.</summary>
    internal List<string> OpenedScopes { get; } = [];

    /// <summary>The keys currently stored under one scope, in ordinal order.</summary>
    internal IReadOnlyList<string> Keys(string scope) => _values.Keys
        .Where(entry => string.Equals(entry.Scope, scope, StringComparison.Ordinal))
        .Select(entry => entry.Key)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public PersistenceStore OpenStore(PersistenceOpenRequest request)
    {
        ulong handle = ++_nextStore;
        _stores.Add(handle, request.Scope);
        OpenedScopes.Add(request.Scope);
        return new(new PersistenceStoreHandle(handle), () => _stores.Remove(handle));
    }

    private string Scope(PersistenceStore store) => _stores.TryGetValue(store.Handle.Value, out string? scope)
        ? scope : throw new InvalidOperationException($"Persistence store {store.Handle.Value} is not open.");
    public PersistenceSaveReceipt Save(PersistenceSaveRequest request)
    {
        (string Scope, string Key) key = (Scope(request.Store), request.Key);
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
        (string Scope, string Key) key = (Scope(request.Store), request.Key);
        _values.TryGetValue(key, out Entry? existing);
        bool matches = request.RevisionGuard switch
        {
            PersistenceRevisionGuard.Any => true,
            PersistenceRevisionGuard.Exact => existing is not null && existing.Revision == request.ExpectedRevision,
            PersistenceRevisionGuard.Absent => existing is null,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        if (!matches) return new(PersistenceDeleteOutcome.RevisionConflict, existing?.Revision ?? 0);
        if (existing is null) return new(PersistenceDeleteOutcome.Missing, 0);
        _values.Remove(key);
        return new(PersistenceDeleteOutcome.Deleted, existing.Revision);
    }
    public PersistenceBlob Load(PersistenceLoadRequest request)
    {
        Entry? value = _values.TryGetValue((Scope(request.Store), request.Key), out Entry? found) ? found : null;
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
