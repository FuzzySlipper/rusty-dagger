using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Kit;

namespace WorldRpg.Host;

/// <summary>Host-owned Engine persistence composition for compiled WorldRpg rulesets.</summary>
public sealed class WorldRpgSaveStore : IDisposable
{
    public const uint SchemaVersion = 1;
    private readonly ProductStateStore<GameSaveEnvelope> _state;

    public WorldRpgSaveStore(IEngineContext engine, string scope)
    {
        _state = new ProductStateStore<GameSaveEnvelope>(engine, scope, new EnvelopeCodec());
    }

    public ProductStateLoad<GameSaveEnvelope> Load(string key)
    {
        try
        {
            return _state.Load(key);
        }
        catch (InvalidOperationException error) when (IsUnsupportedStorageSchema(error))
        {
            throw new WorldRpgSaveSchemaException("The persisted WorldRpg save uses an unsupported storage schema version.", error);
        }
    }
    public PersistenceSaveReceipt Save(string key, GameSaveEnvelope value, PersistenceRevisionGuard guard = PersistenceRevisionGuard.Any, ulong expectedRevision = 0) =>
        _state.Save(key, value ?? throw new ArgumentNullException(nameof(value)), guard, expectedRevision);
    public void Dispose() => _state.Dispose();

    private sealed class EnvelopeCodec : IProductStateCodec<GameSaveEnvelope>
    {
        public uint SchemaVersion => WorldRpgSaveStore.SchemaVersion;
        public void Encode(in GameSaveEnvelope state, IBufferWriter<byte> destination)
        {
            ArgumentNullException.ThrowIfNull(state);
            destination.Write(JsonSerializer.SerializeToUtf8Bytes(PersistedEnvelope.From(state), WorldRpgSaveJsonContext.Default.PersistedEnvelope));
        }
        public GameSaveEnvelope Decode(ReadOnlySpan<byte> payload)
        {
            try
            {
                PersistedEnvelope? parsed = JsonSerializer.Deserialize(payload, WorldRpgSaveJsonContext.Default.PersistedEnvelope);
                return parsed?.ToEnvelope() ?? throw new WorldRpgSaveFormatException("The persisted WorldRpg save envelope is empty.");
            }
            catch (JsonException error)
            {
                throw new WorldRpgSaveFormatException("The persisted WorldRpg save envelope is malformed.", error);
            }
            catch (ArgumentException error)
            {
                throw new WorldRpgSaveFormatException("The persisted WorldRpg save envelope contains invalid identity data.", error);
            }
        }
    }

    internal sealed record PersistedEnvelope(PersistedComposition Composition, string Ruleset, uint RulesetSchemaVersion, byte[] Payload)
    {
        internal static PersistedEnvelope From(GameSaveEnvelope value) => new(PersistedComposition.From(value.Composition), value.Payload.Ruleset.Value, value.Payload.SchemaVersion, value.Payload.Bytes.ToArray());
        internal GameSaveEnvelope ToEnvelope()
        {
            if (Composition is null) throw Invalid("Composition is required.");
            RequireString(Ruleset, "Payload.Ruleset");
            if (RulesetSchemaVersion == 0) throw Invalid("Payload.RulesetSchemaVersion must be positive.");
            if (Payload is null) throw Invalid("Payload is required.");
            return new(Composition.ToIdentity(), new RulesetSavePayload(new RulesetId(Ruleset), RulesetSchemaVersion, Payload));
        }
    }

    internal sealed record PersistedComposition(
        string Bundle, int BundleSchemaVersion, int BundleVersion,
        string Ruleset,
        PersistedPack[] ContentPacks,
        string Tuning, int TuningSchemaVersion, int TuningVersion,
        string Fingerprint, string ContentFingerprint, string TuningFingerprint)
    {
        internal static PersistedComposition From(SaveCompositionIdentity value) => new(
            value.Bundle.Id.Value, value.Bundle.SchemaVersion, value.Bundle.Version,
            value.Ruleset.Value,
            value.ContentPacks.Select(PersistedPack.From).ToArray(),
            value.Tuning.Id.Value, value.Tuning.SchemaVersion, value.Tuning.Version,
            value.Fingerprint, value.ContentFingerprint, value.TuningFingerprint);
        internal SaveCompositionIdentity ToIdentity()
        {
            RequireString(Bundle, "Composition.Bundle");
            RequirePositive(BundleSchemaVersion, "Composition.BundleSchemaVersion");
            RequirePositive(BundleVersion, "Composition.BundleVersion");
            RequireString(Ruleset, "Composition.Ruleset");
            if (ContentPacks is null) throw Invalid("Composition.ContentPacks is required.");
            RequireString(Tuning, "Composition.Tuning");
            RequirePositive(TuningSchemaVersion, "Composition.TuningSchemaVersion");
            RequirePositive(TuningVersion, "Composition.TuningVersion");
            RequireString(Fingerprint, "Composition.Fingerprint");
            RequireString(ContentFingerprint, "Composition.ContentFingerprint");
            RequireString(TuningFingerprint, "Composition.TuningFingerprint");
            ResolvedContentPackIdentity[] packs = ContentPacks.Select((pack, index) =>
            {
                if (pack is null) throw Invalid($"Composition.ContentPacks[{index}] is required.");
                return pack.ToIdentity();
            }).ToArray();
            return new(
                new ResolvedBundleIdentity(new GameBundleId(Bundle), BundleSchemaVersion, BundleVersion),
                new RulesetId(Ruleset),
                packs,
                new ResolvedTuningIdentity(new TuningProfileId(Tuning), TuningSchemaVersion, TuningVersion),
                Fingerprint, ContentFingerprint, TuningFingerprint);
        }
    }

    internal sealed record PersistedPack(string Id, int SchemaVersion, int Version)
    {
        internal static PersistedPack From(ResolvedContentPackIdentity value) => new(value.Id.Value, value.SchemaVersion, value.Version);
        internal ResolvedContentPackIdentity ToIdentity()
        {
            RequireString(Id, "Composition.ContentPacks[].Id");
            RequirePositive(SchemaVersion, "Composition.ContentPacks[].SchemaVersion");
            RequirePositive(Version, "Composition.ContentPacks[].Version");
            return new(new ContentPackId(Id), SchemaVersion, Version);
        }
    }

    private static void RequireString(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid($"{name} is required.");
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0) throw Invalid($"{name} must be positive.");
    }

    private static WorldRpgSaveFormatException Invalid(string message) => new($"The persisted WorldRpg save envelope is invalid: {message}");

    private static bool IsUnsupportedStorageSchema(InvalidOperationException error) =>
        error.Message.StartsWith("No finite migration path from schema ", StringComparison.Ordinal)
        || error.Message.StartsWith("No migration from schema ", StringComparison.Ordinal);
}

/// <summary>Corrupt or unsupported product envelope data rejected before a session is created.</summary>
public sealed class WorldRpgSaveFormatException : InvalidOperationException
{
    public WorldRpgSaveFormatException(string message) : base(message) { }
    public WorldRpgSaveFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>An Engine-persisted product schema with no declared migration path.</summary>
public sealed class WorldRpgSaveSchemaException : InvalidOperationException
{
    public WorldRpgSaveSchemaException(string message, Exception innerException) : base(message, innerException) { }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(WorldRpgSaveStore.PersistedEnvelope))]
internal partial class WorldRpgSaveJsonContext : JsonSerializerContext;
