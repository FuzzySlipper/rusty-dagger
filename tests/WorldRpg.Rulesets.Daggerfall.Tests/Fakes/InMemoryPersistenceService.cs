using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Small persistence service shared across two fresh EngineContext fakes in the host close/reopen test.</summary>
internal sealed class InMemoryPersistenceService : IPersistenceService
{
    private readonly Dictionary<(string Scope, string Key), Entry> _values = [];
    private readonly Dictionary<ulong, Entry?> _blobs = [];
    private ulong _nextBlob;

    public PersistenceStore OpenStore(PersistenceOpenRequest request) => new(new PersistenceStoreHandle(1), static () => { });
    public PersistenceSaveReceipt Save(PersistenceSaveRequest request)
    {
        (string Scope, string Key) key = (request.Store.Handle.Value.ToString(), request.Key);
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
        (string Scope, string Key) key = (request.Store.Handle.Value.ToString(), request.Key);
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
