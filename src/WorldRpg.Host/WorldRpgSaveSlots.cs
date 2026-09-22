using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Kit;

namespace WorldRpg.Host;

/// <summary>One save slot's metadata: what the slot list shows without opening the save.</summary>
/// <param name="Key">The slot key.</param>
/// <param name="Label">The player-visible label.</param>
/// <param name="SavedAtUtc">When the slot was written.</param>
/// <param name="Ruleset">The ruleset the save belongs to.</param>
/// <param name="Revision">The store revision the write produced.</param>
public sealed record WorldRpgSaveSlotEntry(string Key, string Label, DateTime SavedAtUtc, string Ruleset, ulong Revision);

/// <summary>Why a slot load did not produce a session.</summary>
/// <param name="Kind">missing, incompatible, corrupt or selection.</param>
/// <param name="Message">What the slot shows instead.</param>
public sealed record WorldRpgSlotLoadDiagnostic(string Kind, string Message);

/// <summary>
/// Save slots with metadata, overwrite and new/quit transitions over the existing save store.
/// The slot index lives beside the saves under a reserved key, so listing never opens a payload.
/// Loading admits before any session is touched, so a rejected load keeps the current session.
/// Deleting drops the slot from the index; the store offers no key removal, so the underlying
/// bytes remain until overwritten: the index, not the bytes, is what the slot list reads.
/// </summary>
public sealed class WorldRpgSaveSlots : IDisposable
{
    /// <summary>The reserved key the slot index lives under.</summary>
    public const string IndexKey = "slots/index";

    private readonly WorldRpgSaveStore _saves;
    private readonly ProductStateStore<string> _index;
    private bool _disposed;

    /// <summary>Creates slot workflows over a save scope.</summary>
    public WorldRpgSaveSlots(IEngineContext engine, string scope)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _saves = new WorldRpgSaveStore(engine, scope);
        _index = new ProductStateStore<string>(
            engine,
            scope + "/slots",
            new JsonProductStateCodec<string>(WorldRpgSaveJsonContext.Default.String));
    }

    /// <summary>Every indexed slot, newest first.</summary>
    public IReadOnlyList<WorldRpgSaveSlotEntry> List()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ReadIndex().OrderByDescending(entry => entry.SavedAtUtc).ToList();
    }

    /// <summary>
    /// Saves into a slot and indexes its metadata: overwriting a slot takes the revision guard,
    /// so a concurrent write is refused rather than silently lost.
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
        PersistenceSaveReceipt receipt = _saves.Save(key, value, guard, expectedRevision);
        WorldRpgSaveSlotEntry entry = new(key, label, DateTime.UtcNow, value.Payload.Ruleset.Value, receipt.Revision);
        List<WorldRpgSaveSlotEntry> index = ReadIndex();
        index.RemoveAll(existing => string.Equals(existing.Key, key, StringComparison.Ordinal));
        index.Add(entry);
        WriteIndex(index);
        return entry;
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
        if (!ReadIndex().Any(entry => string.Equals(entry.Key, key, StringComparison.Ordinal)))
        {
            return (null, new("missing", $"No save is indexed under '{key}'."));
        }

        ProductStateLoad<GameSaveEnvelope> loaded;
        try
        {
            loaded = _saves.Load(key);
        }
        catch (WorldRpgSaveFormatException error)
        {
            return (null, new("corrupt", error.Message));
        }

        if (!loaded.Present || loaded.State is null)
        {
            return (null, new("missing", $"No saved state exists for '{key}'."));
        }

        if (!string.Equals(loaded.State.Payload.Ruleset.Value, ruleset, StringComparison.Ordinal))
        {
            return (null, new("incompatible", $"Slot '{key}' belongs to ruleset '{loaded.State.Payload.Ruleset.Value}', not '{ruleset}'."));
        }

        return (loaded.State, null);
    }

    /// <summary>
    /// Deletes a slot from the index; the store offers no key removal, so indexed listing is what
    /// deletion means here and stale bytes stay unreachable until overwritten.
    /// </summary>
    public bool DeleteSlot(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        List<WorldRpgSaveSlotEntry> index = ReadIndex();
        int removed = index.RemoveAll(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
        if (removed > 0) WriteIndex(index);
        return removed > 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saves.Dispose();
        _index.Dispose();
    }

    private List<WorldRpgSaveSlotEntry> ReadIndex()
    {
        ProductStateLoad<string> loaded = _index.Load(IndexKey);
        if (!loaded.Present || string.IsNullOrEmpty(loaded.State)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<WorldRpgSaveSlotEntry>>(loaded.State!) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private void WriteIndex(List<WorldRpgSaveSlotEntry> index) =>
        _index.Save(IndexKey, System.Text.Json.JsonSerializer.Serialize(index), PersistenceRevisionGuard.Any);
}
