using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Kit;

public readonly record struct RulesetId(string Value);
public readonly record struct GameBundleId(string Value);
public readonly record struct ContentPackId(string Value);
public readonly record struct TuningProfileId(string Value);

public sealed record GameBundle(GameBundleId Id, int SchemaVersion, int Version, RulesetId Ruleset, IReadOnlyList<ContentPackReference> ContentPacks, TuningProfileReference Tuning);
public sealed record ContentPackReference(ContentPackId Id, int Version);
public sealed record TuningProfileReference(TuningProfileId Id, int Version);

public sealed class ContentPack
{
    private readonly byte[] _payload;
    internal ContentPack(GameCompositionResolver.ContentPackDescriptor value, byte[] payload)
    {
        Id = value.Id; SchemaVersion = value.SchemaVersion; Version = value.Version; Ruleset = value.Ruleset;
        Dependencies = Freeze(value.Dependencies); PayloadPath = value.PayloadPath; _payload = payload.ToArray();
    }
    public ContentPackId Id { get; }
    public int SchemaVersion { get; }
    public int Version { get; }
    public RulesetId Ruleset { get; }
    public IReadOnlyList<ContentPackReference> Dependencies { get; }
    public string PayloadPath { get; }
    public ReadOnlyMemory<byte> Payload => _payload.ToArray();
    internal ReadOnlySpan<byte> PayloadBytes => _payload;
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

public sealed class TuningProfile
{
    private readonly byte[] _payload;
    internal TuningProfile(GameCompositionResolver.TuningDescriptor value, byte[] payload)
    {
        Id = value.Id; SchemaVersion = value.SchemaVersion; Version = value.Version; Ruleset = value.Ruleset;
        PayloadPath = value.PayloadPath; _payload = payload.ToArray();
    }
    public TuningProfileId Id { get; }
    public int SchemaVersion { get; }
    public int Version { get; }
    public RulesetId Ruleset { get; }
    public string PayloadPath { get; }
    public ReadOnlyMemory<byte> Payload => _payload.ToArray();
    internal ReadOnlySpan<byte> PayloadBytes => _payload;
}

public sealed class ResolvedGameComposition
{
    private readonly StoredFile[] _files;
    internal ResolvedGameComposition(GameBundle bundle, IEnumerable<ContentPack> packs, TuningProfile tuning, IEnumerable<KeyValuePair<string, byte[]>> files, string fingerprint, string contentFingerprint, string tuningFingerprint)
    {
        Bundle = bundle; ContentPacks = Freeze(packs); Tuning = tuning;
        _files = files.Select(file => new StoredFile(file.Key, file.Value.ToArray())).ToArray();
        Fingerprint = fingerprint; ContentFingerprint = contentFingerprint; TuningFingerprint = tuningFingerprint;
        Identity = new ResolvedCompositionIdentity(Bundle, ContentPacks, Tuning, Fingerprint, ContentFingerprint, TuningFingerprint);
    }
    public GameBundle Bundle { get; }
    public RulesetId Ruleset => Bundle.Ruleset;
    public IReadOnlyList<ContentPack> ContentPacks { get; }
    public TuningProfile Tuning { get; }
    public string Fingerprint { get; }
    public string ContentFingerprint { get; }
    public string TuningFingerprint { get; }
    /// <summary>The resolved selection and fingerprints, retained for product diagnostics and future save identity.</summary>
    public ResolvedCompositionIdentity Identity { get; }
    public ProductContent Content => new(_files.Select(file => new ProductContentFile(Encoding.UTF8.GetBytes(file.Path), file.Bytes.ToArray())).ToArray());
    public ContentPack RequireContentPack(ContentPackId id) => ContentPacks.SingleOrDefault(pack => pack.Id == id) ?? throw new InvalidOperationException($"Resolved composition does not contain content pack '{id.Value}'.");
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
    private sealed record StoredFile(string Path, byte[] Bytes);
}

/// <summary>Immutable, ruleset-neutral identity of one resolved product composition.</summary>
public sealed class ResolvedCompositionIdentity
{
    private readonly IReadOnlyList<ContentPackId> _contentPacks;

    internal ResolvedCompositionIdentity(GameBundle bundle, IEnumerable<ContentPack> contentPacks, TuningProfile tuning, string fingerprint, string contentFingerprint, string tuningFingerprint)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(contentPacks);
        ArgumentNullException.ThrowIfNull(tuning);
        Bundle = bundle.Id;
        Ruleset = bundle.Ruleset;
        _contentPacks = Array.AsReadOnly(contentPacks.Select(pack => pack.Id).ToArray());
        Tuning = tuning.Id;
        Fingerprint = fingerprint;
        ContentFingerprint = contentFingerprint;
        TuningFingerprint = tuningFingerprint;
    }

    public GameBundleId Bundle { get; }
    public RulesetId Ruleset { get; }
    public IReadOnlyList<ContentPackId> ContentPacks => _contentPacks;
    public TuningProfileId Tuning { get; }
    public string Fingerprint { get; }
    public string ContentFingerprint { get; }
    public string TuningFingerprint { get; }
}

public sealed record CompositionDiagnostic(string Code, string Message);
public sealed class GameCompositionResolution
{
    internal GameCompositionResolution(ResolvedGameComposition? composition, IEnumerable<CompositionDiagnostic> diagnostics) { Composition = composition; Diagnostics = Array.AsReadOnly(diagnostics.ToArray()); }
    public ResolvedGameComposition? Composition { get; }
    public IReadOnlyList<CompositionDiagnostic> Diagnostics { get; }
    public bool IsResolved => Composition is not null && Diagnostics.All(diagnostic => diagnostic.Code != "error");
    public ResolvedGameComposition RequireComposition()
    {
        if (IsResolved) return Composition!;
        throw new InvalidOperationException(Diagnostics.Count == 0 ? "Game composition could not be resolved." : string.Join(" ", Diagnostics.Select(diagnostic => diagnostic.Message)));
    }
}

public static class GameCompositionResolver
{
    private const int DiagnosticLimit = 32;
    private const string BundleKind = "worldrpg.game-bundle", PackKind = "worldrpg.content-pack", TuningKind = "worldrpg.tuning-profile";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static GameCompositionResolution Resolve(ProductContent content, GameBundleId requestedBundle)
    {
        ArgumentNullException.ThrowIfNull(content);
        List<CompositionDiagnostic> diagnostics = [];
        SortedDictionary<string, byte[]> files = CopyFiles(content, diagnostics);
        if (!Identifier(requestedBundle.Value)) Error(diagnostics, $"Requested bundle id '{requestedBundle.Value}' is invalid.");
        Dictionary<GameBundleId, GameBundleDescriptor> bundles = [];
        Dictionary<ContentPackId, ContentPackDescriptor> packs = [];
        Dictionary<TuningProfileId, TuningDescriptor> tunings = [];
        foreach ((string path, byte[] bytes) in files) ReadDescriptor(path, bytes, bundles, packs, tunings, diagnostics);
        if (!bundles.TryGetValue(requestedBundle, out GameBundleDescriptor? selected))
        {
            Error(diagnostics, $"Requested game bundle '{requestedBundle.Value}' was not admitted.");
            return new(null, diagnostics);
        }
        List<ContentPack> ordered = [];
        Dictionary<ContentPackId, VisitState> visits = [];
        foreach (ContentPackReference reference in selected.ContentPacks) ResolvePack(reference, selected.Ruleset, packs, files, visits, ordered, diagnostics);
        TuningProfile? tuning = ResolveTuning(selected, tunings, files, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Code == "error") || tuning is null) return new(null, diagnostics);
        GameBundle bundle = new(selected.Id, selected.SchemaVersion, selected.Version, selected.Ruleset, Freeze(selected.ContentPacks), selected.Tuning);
        string contentFingerprint = ContentHash(bundle, ordered), tuningFingerprint = TuningHash(tuning);
        return new(new ResolvedGameComposition(bundle, ordered, tuning, files, Hash($"content={contentFingerprint}|tuning={tuningFingerprint}"), contentFingerprint, tuningFingerprint), diagnostics);
    }

    private static SortedDictionary<string, byte[]> CopyFiles(ProductContent content, List<CompositionDiagnostic> diagnostics)
    {
        SortedDictionary<string, byte[]> result = new(StringComparer.Ordinal);
        foreach (ProductContentFile file in content.Files.Span)
        {
            string path;
            try { path = StrictUtf8.GetString(file.Path.Span); }
            catch (DecoderFallbackException) { Error(diagnostics, "An admitted content path is not valid UTF-8."); continue; }
            if (!Path(path)) { Error(diagnostics, $"Admitted content path '{path}' is invalid."); continue; }
            if (!result.TryAdd(path, file.Bytes.ToArray())) Error(diagnostics, $"Admitted content contains duplicate path '{path}'.");
        }
        return result;
    }

    private static void ReadDescriptor(string path, byte[] bytes, Dictionary<GameBundleId, GameBundleDescriptor> bundles, Dictionary<ContentPackId, ContentPackDescriptor> packs, Dictionary<TuningProfileId, TuningDescriptor> tunings, List<CompositionDiagnostic> diagnostics)
    {
        if (!path.EndsWith(".bundle.json", StringComparison.Ordinal) && !path.EndsWith(".pack.json", StringComparison.Ordinal) && !path.EndsWith(".tuning.json", StringComparison.Ordinal)) return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            switch (String(document.RootElement, "kind"))
            {
                case BundleKind:
                    GameBundleDescriptor bundle = Bundle(document.RootElement);
                    if (!bundles.TryAdd(bundle.Id, bundle)) Error(diagnostics, $"Duplicate game bundle '{bundle.Id.Value}'.");
                    break;
                case PackKind:
                    ContentPackDescriptor pack = Pack(document.RootElement);
                    if (!packs.TryAdd(pack.Id, pack)) Error(diagnostics, $"Duplicate content pack '{pack.Id.Value}'.");
                    break;
                case TuningKind:
                    TuningDescriptor tuning = Tuning(document.RootElement);
                    if (!tunings.TryAdd(tuning.Id, tuning)) Error(diagnostics, $"Duplicate tuning profile '{tuning.Id.Value}'.");
                    break;
                default: throw new InvalidOperationException("'kind' is not a supported composition descriptor.");
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException)
        { Error(diagnostics, $"Invalid composition descriptor '{path}': {exception.Message}"); }
    }

    private static GameBundleDescriptor Bundle(JsonElement root)
    {
        List<ContentPackReference> contentPacks = References(Array(root, "contentPacks"));
        if (contentPacks.Count == 0) throw new InvalidOperationException("Game bundle must select at least one content pack.");
        if (contentPacks.GroupBy(reference => reference.Id).Any(group => group.Count() > 1)) throw new InvalidOperationException("Game bundle selects a content pack more than once.");
        JsonElement tuning = Object(root, "tuning");
        return new(new(Id(root, "id")), Version(root, "schemaVersion"), Version(root, "version"), new(Id(root, "ruleset")), contentPacks.ToArray(), new(new(Id(tuning, "id")), Version(tuning, "version")));
    }

    private static ContentPackDescriptor Pack(JsonElement root)
    {
        List<ContentPackReference> dependencies = References(Array(root, "dependencies"));
        if (dependencies.GroupBy(reference => reference.Id).Any(group => group.Count() > 1)) throw new InvalidOperationException("Content pack declares a dependency more than once.");
        return new(new(Id(root, "id")), Version(root, "schemaVersion"), Version(root, "version"), new(Id(root, "ruleset")), dependencies.ToArray(), FilePath(root, "payload"));
    }

    private static TuningDescriptor Tuning(JsonElement root) => new(new(Id(root, "id")), Version(root, "schemaVersion"), Version(root, "version"), new(Id(root, "ruleset")), FilePath(root, "payload"));
    private static List<ContentPackReference> References(JsonElement elements)
    {
        List<ContentPackReference> result = [];
        foreach (JsonElement element in elements.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Content pack reference must be an object.");
            result.Add(new(new(Id(element, "id")), Version(element, "version")));
        }
        return result;
    }

    private static void ResolvePack(ContentPackReference reference, RulesetId ruleset, IReadOnlyDictionary<ContentPackId, ContentPackDescriptor> descriptors, IReadOnlyDictionary<string, byte[]> files, Dictionary<ContentPackId, VisitState> visits, List<ContentPack> ordered, List<CompositionDiagnostic> diagnostics)
    {
        if (!descriptors.TryGetValue(reference.Id, out ContentPackDescriptor? descriptor)) { Error(diagnostics, $"Content pack '{reference.Id.Value}' is missing."); return; }
        if (descriptor.Version != reference.Version) { Error(diagnostics, $"Content pack '{reference.Id.Value}' requires version {reference.Version}, but admitted version is {descriptor.Version}."); return; }
        if (descriptor.Ruleset != ruleset) { Error(diagnostics, $"Content pack '{reference.Id.Value}' belongs to ruleset '{descriptor.Ruleset.Value}', not '{ruleset.Value}'."); return; }
        if (visits.TryGetValue(reference.Id, out VisitState state)) { if (state == VisitState.Visiting) Error(diagnostics, $"Content pack dependency cycle includes '{reference.Id.Value}'."); return; }
        visits[reference.Id] = VisitState.Visiting;
        foreach (ContentPackReference dependency in descriptor.Dependencies) ResolvePack(dependency, ruleset, descriptors, files, visits, ordered, diagnostics);
        visits[reference.Id] = VisitState.Done;
        if (!files.TryGetValue(descriptor.PayloadPath, out byte[]? payload)) { Error(diagnostics, $"Content pack '{reference.Id.Value}' payload '{descriptor.PayloadPath}' is missing."); return; }
        ordered.Add(new(descriptor, payload));
    }

    private static TuningProfile? ResolveTuning(GameBundleDescriptor bundle, IReadOnlyDictionary<TuningProfileId, TuningDescriptor> descriptors, IReadOnlyDictionary<string, byte[]> files, List<CompositionDiagnostic> diagnostics)
    {
        if (!descriptors.TryGetValue(bundle.Tuning.Id, out TuningDescriptor? descriptor)) { Error(diagnostics, $"Tuning profile '{bundle.Tuning.Id.Value}' is missing."); return null; }
        if (descriptor.Version != bundle.Tuning.Version) { Error(diagnostics, $"Tuning profile '{descriptor.Id.Value}' requires version {bundle.Tuning.Version}, but admitted version is {descriptor.Version}."); return null; }
        if (descriptor.Ruleset != bundle.Ruleset) { Error(diagnostics, $"Tuning profile '{descriptor.Id.Value}' belongs to ruleset '{descriptor.Ruleset.Value}', not '{bundle.Ruleset.Value}'."); return null; }
        if (!files.TryGetValue(descriptor.PayloadPath, out byte[]? payload)) { Error(diagnostics, $"Tuning profile '{descriptor.Id.Value}' payload '{descriptor.PayloadPath}' is missing."); return null; }
        return new(descriptor, payload);
    }

    private static string ContentHash(GameBundle bundle, IReadOnlyList<ContentPack> packs)
    {
        StringBuilder value = new($"bundle:{bundle.SchemaVersion}:{bundle.Id.Value}@{bundle.Version}:{bundle.Ruleset.Value}|");
        foreach (ContentPackReference selected in bundle.ContentPacks) value.Append($"selected:{selected.Id.Value}@{selected.Version}|");
        foreach (ContentPack pack in packs)
        {
            value.Append($"pack:{pack.SchemaVersion}:{pack.Id.Value}@{pack.Version}:{pack.Ruleset.Value}:{pack.PayloadPath}:{Convert.ToHexString(SHA256.HashData(pack.PayloadBytes))}|");
            foreach (ContentPackReference dependency in pack.Dependencies) value.Append($"depends:{dependency.Id.Value}@{dependency.Version}|");
        }
        return Hash(value.ToString());
    }
    private static string TuningHash(TuningProfile tuning) => Hash($"tuning:{tuning.SchemaVersion}:{tuning.Id.Value}@{tuning.Version}:{tuning.Ruleset.Value}:{tuning.PayloadPath}:{Convert.ToHexString(SHA256.HashData(tuning.PayloadBytes))}");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static JsonElement Required(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out JsonElement value)) throw new InvalidOperationException($"Required property '{property}' is missing.");
        return value;
    }
    private static JsonElement Object(JsonElement root, string property) { JsonElement value = Required(root, property); return value.ValueKind == JsonValueKind.Object ? value : throw new InvalidOperationException($"'{property}' must be an object."); }
    private static JsonElement Array(JsonElement root, string property) { JsonElement value = Required(root, property); return value.ValueKind == JsonValueKind.Array ? value : throw new InvalidOperationException($"'{property}' must be an array."); }
    private static string String(JsonElement root, string property) { JsonElement value = Required(root, property); return value.ValueKind == JsonValueKind.String && value.GetString() is { } text ? text : throw new InvalidOperationException($"'{property}' must be a string."); }
    private static string Id(JsonElement root, string property) { string value = String(root, property); return Identifier(value) ? value : throw new InvalidOperationException($"'{property}' value '{value}' is invalid."); }
    private static int Version(JsonElement root, string property) { JsonElement value = Required(root, property); return value.TryGetInt32(out int result) && result > 0 ? result : throw new InvalidOperationException($"'{property}' must be a positive integer."); }
    private static string FilePath(JsonElement root, string property) { string value = String(root, property); return Path(value) ? value : throw new InvalidOperationException($"'{property}' value '{value}' is invalid."); }
    private static bool Identifier(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 96 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    private static bool Path(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 192 && !value.StartsWith("/", StringComparison.Ordinal) && !value.Split('/').Any(segment => segment is "" or "." or "..") && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '/');
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => System.Array.AsReadOnly(values.ToArray());
    private static void Error(List<CompositionDiagnostic> diagnostics, string message) { if (diagnostics.Count < DiagnosticLimit) diagnostics.Add(new("error", message)); else if (diagnostics.Count == DiagnosticLimit) diagnostics.Add(new("error", "Composition diagnostics were truncated.")); }

    private enum VisitState { Visiting, Done }
    internal sealed record ContentPackDescriptor(ContentPackId Id, int SchemaVersion, int Version, RulesetId Ruleset, IReadOnlyList<ContentPackReference> Dependencies, string PayloadPath);
    internal sealed record TuningDescriptor(TuningProfileId Id, int SchemaVersion, int Version, RulesetId Ruleset, string PayloadPath);
    private sealed record GameBundleDescriptor(GameBundleId Id, int SchemaVersion, int Version, RulesetId Ruleset, IReadOnlyList<ContentPackReference> ContentPacks, TuningProfileReference Tuning);
}

public sealed class GameSessionContext(IEngineContext engine, ResolvedGameComposition composition)
{
    public IEngineContext Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
    public ResolvedGameComposition Composition { get; } = composition ?? throw new ArgumentNullException(nameof(composition));
    public ResolvedCompositionIdentity CompositionIdentity => Composition.Identity;
}
public interface IGameRuleset { RulesetId Id { get; } IGameSession CreateSession(GameSessionContext context); }
public interface IGameSession : IDisposable { void PublishInitial(); ProductTurnRequest Update(ProductUpdate update); }
