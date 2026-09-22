using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Kit;

namespace WorldRpg.Host;

/// <summary>Host-owned Engine persistence composition for compiled WorldRpg rulesets.</summary>
public sealed class WorldRpgSaveStore : IDisposable
{
    private readonly ProductStateStore<PersistedEnvelope> _state;

    public WorldRpgSaveStore(IEngineContext engine, string scope)
    {
        _state = new ProductStateStore<PersistedEnvelope>(
            engine,
            scope,
            new JsonProductStateCodec<PersistedEnvelope>(WorldRpgSaveJsonContext.Default.PersistedEnvelope));
    }

    public ProductStateLoad<GameSaveEnvelope> Load(string key)
    {
        try
        {
            ProductStateLoad<PersistedEnvelope> loaded = _state.Load(key);
            return loaded.Present
                ? new ProductStateLoad<GameSaveEnvelope>(true, loaded.Revision, loaded.State?.ToEnvelope())
                : new ProductStateLoad<GameSaveEnvelope>(false, loaded.Revision, null);
        }
        catch (JsonException error)
        {
            throw new WorldRpgSaveFormatException("The persisted WorldRpg save is malformed.", error);
        }
        catch (ArgumentException error)
        {
            throw new WorldRpgSaveFormatException("The persisted WorldRpg save contains invalid current-state data.", error);
        }
        catch (InvalidOperationException error) when (error is not WorldRpgSaveFormatException)
        {
            throw new WorldRpgSaveFormatException("The persisted WorldRpg save contains invalid current-state data.", error);
        }
    }

    public PersistenceSaveReceipt Save(string key, GameSaveEnvelope value, PersistenceRevisionGuard guard = PersistenceRevisionGuard.Any, ulong expectedRevision = 0) =>
        _state.Save(key, PersistedEnvelope.From(value ?? throw new ArgumentNullException(nameof(value))), guard, expectedRevision);

    public void Dispose() => _state.Dispose();

    internal sealed record PersistedEnvelope(string Ruleset, byte[] Payload)
    {
        internal static PersistedEnvelope From(GameSaveEnvelope value) =>
            new(value.Payload.Ruleset.Value, value.Payload.Bytes.ToArray());

        internal GameSaveEnvelope ToEnvelope()
        {
            if (string.IsNullOrWhiteSpace(Ruleset))
                throw new ArgumentException("The save ruleset is required.");
            if (Payload is null || Payload.Length == 0)
                throw new ArgumentException("The save payload is required.");
            return new GameSaveEnvelope(new RulesetSavePayload(new RulesetId(Ruleset), Payload));
        }
    }
}

/// <summary>Corrupt current-state product save data rejected before a session is created.</summary>
public sealed class WorldRpgSaveFormatException : InvalidOperationException
{
    public WorldRpgSaveFormatException(string message) : base(message) { }
    public WorldRpgSaveFormatException(string message, Exception innerException) : base(message, innerException) { }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(WorldRpgSaveStore.PersistedEnvelope))]
[JsonSerializable(typeof(List<WorldRpgSaveSlotEntry>))]
internal partial class WorldRpgSaveJsonContext : JsonSerializerContext;
