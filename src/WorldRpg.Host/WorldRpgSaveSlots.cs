using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Kit;

namespace WorldRpg.Host;

/// <summary>One save slot's metadata: what the slot list shows without opening the save.</summary>
/// <param name="Key">The slot key.</param>
/// <param name="Label">The player-visible label.</param>
/// <param name="SavedAtUtc">When the slot was written.</param>
/// <param name="Ruleset">The ruleset the save belongs to.</param>
/// <param name="Revision">The slot's monotonic revision.</param>
public sealed record WorldRpgSaveSlotEntry(string Key, string Label, DateTime SavedAtUtc, string Ruleset, ulong Revision);

/// <summary>Why a slot load did not produce a session.</summary>
/// <param name="Kind">missing, incompatible, corrupt or selection.</param>
/// <param name="Message">What the slot shows instead.</param>
public sealed record WorldRpgSlotLoadDiagnostic(string Kind, string Message);

/// <summary>
/// Save slots over one Engine-owned catalog value. Every entry carries its metadata and current
/// payload together, so a save or delete has one durable write rather than a payload/index pair.
/// Loading admits before any session is touched, so a rejected load keeps the current session.
/// </summary>
public sealed class WorldRpgSaveSlots : IDisposable
{
    /// <summary>The reserved key holding the complete current save-slot catalog.</summary>
    public const string IndexKey = "slots/index";

    private readonly ProductStateStore<List<WorldRpgPersistedSaveSlot>> _catalog;
    private bool _disposed;

    /// <summary>Creates slot workflows over a save scope.</summary>
    public WorldRpgSaveSlots(IEngineContext engine, string scope)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _catalog = new ProductStateStore<List<WorldRpgPersistedSaveSlot>>(
            engine,
            scope + "/slots",
            new JsonProductStateCodec<List<WorldRpgPersistedSaveSlot>>(WorldRpgSaveJsonContext.Default.ListWorldRpgPersistedSaveSlot));
    }

    /// <summary>Every indexed slot, newest first.</summary>
    public IReadOnlyList<WorldRpgSaveSlotEntry> List()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ReadCatalog().Entries
            .Select(entry => entry.ToEntry())
            .OrderByDescending(entry => entry.SavedAtUtc)
            .ToList();
    }

    /// <summary>
    /// Saves into a named slot. The public guard applies to that slot's monotonic revision while
    /// the catalog's Engine revision protects unrelated slots from a lost concurrent update.
    /// </summary>
    public WorldRpgSaveSlotEntry SaveSlot(string key, string label, GameSaveEnvelope value, PersistenceRevisionGuard guard = PersistenceRevisionGuard.Any, ulong expectedRevision = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (string.Equals(key, IndexKey, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Slot key '{key}' is reserved for the slot index.", nameof(key));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(value);

        CatalogSnapshot snapshot = ReadCatalog();
        WorldRpgPersistedSaveSlot? existing = snapshot.Entries.SingleOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
        EnsureSlotGuard(key, existing, guard, expectedRevision);

        ulong revision = existing is null ? 1 : checked(existing.Revision + 1);
        WorldRpgPersistedSaveSlot saved = new(
            key,
            label,
            DateTime.UtcNow,
            value.Payload.Ruleset.Value,
            value.Payload.Bytes.ToArray(),
            revision);
        snapshot.Entries.RemoveAll(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
        snapshot.Entries.Add(saved);
        WriteCatalog(snapshot);
        return saved.ToEntry();
    }

    /// <summary>
    /// Loads a slot's envelope without touching any session: missing, corrupt and ruleset-mismatch
    /// outcomes arrive named, and the current session is never mutated by a rejected load.
    /// </summary>
    public (GameSaveEnvelope? Envelope, WorldRpgSlotLoadDiagnostic? Diagnostic) LoadSlot(string key, string ruleset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleset);

        CatalogSnapshot snapshot;
        try
        {
            snapshot = ReadCatalog();
        }
        catch (WorldRpgSaveFormatException error)
        {
            return (null, new("corrupt", error.Message));
        }

        WorldRpgPersistedSaveSlot? entry = snapshot.Entries.SingleOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
        if (entry is null)
        {
            return (null, new("missing", $"No save is indexed under '{key}'."));
        }

        if (!string.Equals(entry.Ruleset, ruleset, StringComparison.Ordinal))
        {
            return (null, new("incompatible", $"Slot '{key}' belongs to ruleset '{entry.Ruleset}', not '{ruleset}'."));
        }

        try
        {
            return (entry.ToEnvelope(), null);
        }
        catch (WorldRpgSaveFormatException error)
        {
            return (null, new("corrupt", error.Message));
        }
    }

    /// <summary>
    /// Deletes a slot through one catalog write. If storage reports an I/O failure, callers reload
    /// the catalog to observe the documented old-or-new complete result.
    /// </summary>
    public bool DeleteSlot(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        CatalogSnapshot snapshot = ReadCatalog();
        int removed = snapshot.Entries.RemoveAll(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
        if (removed == 0) return false;

        WriteCatalog(snapshot);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _catalog.Dispose();
    }

    private CatalogSnapshot ReadCatalog()
    {
        ProductStateLoad<List<WorldRpgPersistedSaveSlot>> loaded;
        try
        {
            loaded = _catalog.Load(IndexKey);
        }
        catch (System.Text.Json.JsonException error)
        {
            throw MalformedCatalog("its stored JSON cannot be decoded", error);
        }
        catch (InvalidOperationException error) when (error is not WorldRpgSaveFormatException)
        {
            throw MalformedCatalog("its stored value is null", error);
        }

        if (!loaded.Present) return new([], false, 0);
        if (loaded.State is null) throw MalformedCatalog("it contains null instead of a slot list");
        ValidateCatalog(loaded.State);
        return new(loaded.State, true, loaded.Revision);
    }

    private void WriteCatalog(CatalogSnapshot snapshot)
    {
        PersistenceSaveReceipt receipt;
        try
        {
            receipt = _catalog.Save(
                IndexKey,
                snapshot.Entries,
                snapshot.Present ? PersistenceRevisionGuard.Exact : PersistenceRevisionGuard.Absent,
                snapshot.Revision);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                "The save-slot catalog write did not report a result. Reload the catalog to determine whether the previous or updated complete slot set is durable.",
                error);
        }

        if (receipt.Outcome != PersistenceSaveOutcome.Saved)
        {
            throw new InvalidOperationException(
                $"Save-slot catalog persistence returned {receipt.Outcome} at revision {receipt.Revision}; reload the catalog before retrying.");
        }
    }

    private static void EnsureSlotGuard(string key, WorldRpgPersistedSaveSlot? existing, PersistenceRevisionGuard guard, ulong expectedRevision)
    {
        bool conflict = guard switch
        {
            PersistenceRevisionGuard.Any => false,
            PersistenceRevisionGuard.Absent => existing is not null,
            PersistenceRevisionGuard.Exact => existing is null || existing.Revision != expectedRevision,
            _ => throw new ArgumentOutOfRangeException(nameof(guard), guard, "Unknown persistence revision guard."),
        };
        if (conflict)
        {
            ulong actual = existing?.Revision ?? 0;
            throw new InvalidOperationException($"Save slot '{key}' was not written because persistence returned RevisionConflict at revision {actual}.");
        }
    }

    private static void ValidateCatalog(List<WorldRpgPersistedSaveSlot> entries)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            WorldRpgPersistedSaveSlot? entry = entries[entryIndex];
            string location = $"entry {entryIndex + 1}";
            if (entry is null) throw MalformedCatalog($"{location} is null");
            if (string.IsNullOrWhiteSpace(entry.Key)) throw MalformedCatalog($"{location} has no key");
            if (string.Equals(entry.Key, IndexKey, StringComparison.Ordinal)) throw MalformedCatalog($"{location} uses the reserved index key");
            if (!keys.Add(entry.Key)) throw MalformedCatalog($"{location} duplicates key '{entry.Key}'");
            if (string.IsNullOrWhiteSpace(entry.Label)) throw MalformedCatalog($"{location} has no label");
            if (entry.SavedAtUtc == default) throw MalformedCatalog($"{location} has no save timestamp");
            if (string.IsNullOrWhiteSpace(entry.Ruleset)) throw MalformedCatalog($"{location} has no ruleset");
            if (entry.Payload is null) throw MalformedCatalog($"{location} has no current-state payload");
            if (entry.Revision == 0) throw MalformedCatalog($"{location} has no save revision");
        }
    }

    private static WorldRpgSaveFormatException MalformedCatalog(string detail, Exception? inner = null) =>
        inner is null
            ? new WorldRpgSaveFormatException($"The saved slot catalog is malformed: {detail}.")
            : new WorldRpgSaveFormatException($"The saved slot catalog is malformed: {detail}.", inner);

    private sealed record CatalogSnapshot(List<WorldRpgPersistedSaveSlot> Entries, bool Present, ulong Revision);
}

/// <summary>Current durable payload and metadata for one named Host-owned save slot.</summary>
internal sealed record WorldRpgPersistedSaveSlot(string Key, string Label, DateTime SavedAtUtc, string Ruleset, byte[]? Payload, ulong Revision)
{
    internal WorldRpgSaveSlotEntry ToEntry() => new(Key, Label, SavedAtUtc, Ruleset, Revision);

    internal GameSaveEnvelope ToEnvelope()
    {
        if (Payload is null || Payload.Length == 0)
        {
            throw new WorldRpgSaveFormatException($"Save slot '{Key}' has no current-state payload.");
        }

        return new GameSaveEnvelope(new RulesetSavePayload(new RulesetId(Ruleset), Payload.ToArray()));
    }
}
