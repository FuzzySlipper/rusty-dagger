using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Kit;

public readonly record struct RulesetId(string Value);
public readonly record struct GameBundleId(string Value);
public readonly record struct ContentPackId(string Value);
public readonly record struct TuningProfileId(string Value);

/// <summary>A compiled bundle declaration admitted with the product content.</summary>
public sealed record GameBundle(
    GameBundleId Id,
    int Version,
    RulesetId Ruleset,
    IReadOnlyList<ContentPackReference> ContentPacks,
    TuningProfileReference Tuning);

public sealed record ContentPackReference(ContentPackId Id, int Version);
public sealed record TuningProfileReference(TuningProfileId Id, int Version);

/// <summary>A dependency-ordered content pack with its selected opaque payload.</summary>
public sealed class ContentPack
{
    private readonly byte[] _payload;

    internal ContentPack(ContentPackId id, int version, IReadOnlyList<ContentPackReference> dependencies, byte[] payload)
    {
        Id = id;
        Version = version;
        Dependencies = dependencies;
        _payload = payload;
    }

    public ContentPackId Id { get; }
    public int Version { get; }
    public IReadOnlyList<ContentPackReference> Dependencies { get; }
    public ReadOnlyMemory<byte> Payload => _payload;
}

/// <summary>A ruleset-scoped tuning profile with an opaque selected payload.</summary>
public sealed class TuningProfile
{
    private readonly byte[] _payload;

    internal TuningProfile(TuningProfileId id, int version, RulesetId ruleset, byte[] payload)
    {
        Id = id;
        Version = version;
        Ruleset = ruleset;
        _payload = payload;
    }

    public TuningProfileId Id { get; }
    public int Version { get; }
    public RulesetId Ruleset { get; }
    public ReadOnlyMemory<byte> Payload => _payload;
}

/// <summary>The validated composition passed to one compiled ruleset session.</summary>
public sealed class ResolvedGameComposition
{
    private readonly ProductContent _content;

    internal ResolvedGameComposition(GameBundle bundle, IReadOnlyList<ContentPack> contentPacks, TuningProfile tuning, ProductContent content, string fingerprint)
    {
        Bundle = bundle;
        ContentPacks = contentPacks;
        Tuning = tuning;
        _content = content;
        Fingerprint = fingerprint;
    }

    public GameBundle Bundle { get; }
    public RulesetId Ruleset => Bundle.Ruleset;
    public IReadOnlyList<ContentPack> ContentPacks { get; }
    public TuningProfile Tuning { get; }
    public string Fingerprint { get; }

    /// <summary>All copied Engine-admitted files. Rulesets may interpret only their selected payloads.</summary>
    public ProductContent Content => _content;

    public ContentPack RequireContentPack(ContentPackId id) => ContentPacks.SingleOrDefault(pack => pack.Id == id)
        ?? throw new InvalidOperationException($"Resolved composition does not contain content pack '{id.Value}'.");
}

public sealed record CompositionDiagnostic(string Code, string Message);

public sealed class GameCompositionResolution
{
    internal GameCompositionResolution(ResolvedGameComposition? composition, IReadOnlyList<CompositionDiagnostic> diagnostics)
    {
        Composition = composition;
        Diagnostics = diagnostics;
    }

    public ResolvedGameComposition? Composition { get; }
    public IReadOnlyList<CompositionDiagnostic> Diagnostics { get; }
    public bool IsResolved => Composition is not null && Diagnostics.All(diagnostic => diagnostic.Code != "error");

    public ResolvedGameComposition RequireComposition()
    {
        if (Composition is not null && Diagnostics.All(diagnostic => diagnostic.Code != "error")) return Composition;
        string message = Diagnostics.Count == 0
            ? "Game composition could not be resolved."
            : string.Join(" ", Diagnostics.Select(diagnostic => diagnostic.Message));
        throw new InvalidOperationException(message);
    }
}

/// <summary>Resolves the compact generic bundle descriptor set from immutable Engine-admitted content.</summary>
public static class GameCompositionResolver
{
    private const int DiagnosticLimit = 32;
    private const string BundleKind = "worldrpg.game-bundle";
    private const string PackKind = "worldrpg.content-pack";
    private const string TuningKind = "worldrpg.tuning-profile";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static GameCompositionResolution Resolve(ProductContent content, GameBundleId requestedBundle)
    {
        ArgumentNullException.ThrowIfNull(content);
        List<CompositionDiagnostic> diagnostics = [];
        SortedDictionary<string, byte[]> files = CopyAdmittedFiles(content, diagnostics);
        if (!IsIdentifier(requestedBundle.Value)) AddError(diagnostics, $"Requested bundle id '{requestedBundle.Value}' is invalid.");

        Dictionary<GameBundleId, GameBundleDescriptor> bundles = [];
        Dictionary<ContentPackId, ContentPackDescriptor> packs = [];
        Dictionary<TuningProfileId, TuningDescriptor> tunings = [];
        foreach ((string path, byte[] bytes) in files)
            TryReadDescriptor(path, bytes, bundles, packs, tunings, diagnostics);

        if (!bundles.TryGetValue(requestedBundle, out GameBundleDescriptor? selected))
        {
            AddError(diagnostics, $"Requested game bundle '{requestedBundle.Value}' was not admitted.");
            return new GameCompositionResolution(null, diagnostics);
        }

        List<ContentPack> orderedPacks = [];
        Dictionary<ContentPackId, VisitState> visits = [];
        foreach (ContentPackReference reference in selected.ContentPacks.OrderBy(reference => reference.Id.Value, StringComparer.Ordinal))
            ResolvePack(reference, packs, files, visits, orderedPacks, diagnostics);

        TuningProfile? tuning = ResolveTuning(selected, tunings, files, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Code == "error") || tuning is null)
            return new GameCompositionResolution(null, diagnostics);

        GameBundle bundle = new(selected.Id, selected.Version, selected.Ruleset, selected.ContentPacks, selected.Tuning);
        ProductContent copiedContent = new(files.Select(file => new ProductContentFile(Encoding.UTF8.GetBytes(file.Key), file.Value.ToArray())).ToArray());
        string fingerprint = Fingerprint(bundle, orderedPacks, tuning);
        return new GameCompositionResolution(new ResolvedGameComposition(bundle, orderedPacks, tuning, copiedContent, fingerprint), diagnostics);
    }

    private static SortedDictionary<string, byte[]> CopyAdmittedFiles(ProductContent content, List<CompositionDiagnostic> diagnostics)
    {
        SortedDictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (ProductContentFile file in content.Files.Span)
        {
            string path;
            try { path = StrictUtf8.GetString(file.Path.Span); }
            catch (DecoderFallbackException) { AddError(diagnostics, "An admitted content path is not valid UTF-8."); continue; }
            if (!IsPath(path)) { AddError(diagnostics, $"Admitted content path '{path}' is invalid."); continue; }
            if (!files.TryAdd(path, file.Bytes.ToArray())) AddError(diagnostics, $"Admitted content contains duplicate path '{path}'.");
        }
        return files;
    }

    private static void TryReadDescriptor(
        string path,
        byte[] bytes,
        Dictionary<GameBundleId, GameBundleDescriptor> bundles,
        Dictionary<ContentPackId, ContentPackDescriptor> packs,
        Dictionary<TuningProfileId, TuningDescriptor> tunings,
        List<CompositionDiagnostic> diagnostics)
    {
        if (!path.EndsWith(".bundle.json", StringComparison.Ordinal)
            && !path.EndsWith(".pack.json", StringComparison.Ordinal)
            && !path.EndsWith(".tuning.json", StringComparison.Ordinal)) return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() is not { } type) return;
            switch (type)
            {
                case BundleKind:
                    GameBundleDescriptor bundle = ReadBundle(root);
                    if (!bundles.TryAdd(bundle.Id, bundle)) AddError(diagnostics, $"Duplicate game bundle '{bundle.Id.Value}'.");
                    break;
                case PackKind:
                    ContentPackDescriptor pack = ReadPack(root);
                    if (!packs.TryAdd(pack.Id, pack)) AddError(diagnostics, $"Duplicate content pack '{pack.Id.Value}'.");
                    break;
                case TuningKind:
                    TuningDescriptor tuning = ReadTuning(root);
                    if (!tunings.TryAdd(tuning.Id, tuning)) AddError(diagnostics, $"Duplicate tuning profile '{tuning.Id.Value}'.");
                    break;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            AddError(diagnostics, $"Invalid composition descriptor '{path}': {exception.Message}");
        }
    }

    private static GameBundleDescriptor ReadBundle(JsonElement root)
    {
        GameBundleId id = new(ReadIdentifier(root, "id"));
        int version = ReadVersion(root);
        RulesetId ruleset = new(ReadIdentifier(root, "ruleset"));
        List<ContentPackReference> references = [];
        foreach (JsonElement element in root.GetProperty("contentPacks").EnumerateArray())
            references.Add(new ContentPackReference(new ContentPackId(ReadIdentifier(element, "id")), ReadVersion(element)));
        if (references.Count == 0) throw new InvalidOperationException("Game bundle must select at least one content pack.");
        if (references.GroupBy(reference => reference.Id).Any(group => group.Count() != 1)) throw new InvalidOperationException("Game bundle selects a content pack more than once.");
        JsonElement tuning = root.GetProperty("tuning");
        return new GameBundleDescriptor(id, version, ruleset, references.OrderBy(reference => reference.Id.Value, StringComparer.Ordinal).ToArray(), new TuningProfileReference(new TuningProfileId(ReadIdentifier(tuning, "id")), ReadVersion(tuning)));
    }

    private static ContentPackDescriptor ReadPack(JsonElement root)
    {
        ContentPackId id = new(ReadIdentifier(root, "id"));
        int version = ReadVersion(root);
        List<ContentPackReference> dependencies = [];
        if (root.TryGetProperty("dependencies", out JsonElement dependenciesValue))
            foreach (JsonElement element in dependenciesValue.EnumerateArray())
                dependencies.Add(new ContentPackReference(new ContentPackId(ReadIdentifier(element, "id")), ReadVersion(element)));
        if (dependencies.GroupBy(reference => reference.Id).Any(group => group.Count() != 1)) throw new InvalidOperationException("Content pack declares a dependency more than once.");
        return new ContentPackDescriptor(id, version, dependencies.OrderBy(reference => reference.Id.Value, StringComparer.Ordinal).ToArray(), ReadPath(root, "payload"));
    }

    private static TuningDescriptor ReadTuning(JsonElement root) => new(
        new TuningProfileId(ReadIdentifier(root, "id")),
        ReadVersion(root),
        new RulesetId(ReadIdentifier(root, "ruleset")),
        ReadPath(root, "payload"));

    private static void ResolvePack(
        ContentPackReference reference,
        IReadOnlyDictionary<ContentPackId, ContentPackDescriptor> descriptors,
        IReadOnlyDictionary<string, byte[]> files,
        Dictionary<ContentPackId, VisitState> visits,
        List<ContentPack> ordered,
        List<CompositionDiagnostic> diagnostics)
    {
        if (!descriptors.TryGetValue(reference.Id, out ContentPackDescriptor? descriptor)) { AddError(diagnostics, $"Content pack '{reference.Id.Value}' is missing."); return; }
        if (descriptor.Version != reference.Version) { AddError(diagnostics, $"Content pack '{reference.Id.Value}' requires version {reference.Version}, but admitted version is {descriptor.Version}."); return; }
        if (visits.TryGetValue(reference.Id, out VisitState state))
        {
            if (state == VisitState.Visiting) AddError(diagnostics, $"Content pack dependency cycle includes '{reference.Id.Value}'.");
            return;
        }
        visits[reference.Id] = VisitState.Visiting;
        foreach (ContentPackReference dependency in descriptor.Dependencies)
            ResolvePack(dependency, descriptors, files, visits, ordered, diagnostics);
        visits[reference.Id] = VisitState.Done;
        if (!files.TryGetValue(descriptor.PayloadPath, out byte[]? payload)) { AddError(diagnostics, $"Content pack '{reference.Id.Value}' payload '{descriptor.PayloadPath}' is missing."); return; }
        ordered.Add(new ContentPack(descriptor.Id, descriptor.Version, descriptor.Dependencies, payload.ToArray()));
    }

    private static TuningProfile? ResolveTuning(GameBundleDescriptor bundle, IReadOnlyDictionary<TuningProfileId, TuningDescriptor> descriptors, IReadOnlyDictionary<string, byte[]> files, List<CompositionDiagnostic> diagnostics)
    {
        if (!descriptors.TryGetValue(bundle.Tuning.Id, out TuningDescriptor? descriptor)) { AddError(diagnostics, $"Tuning profile '{bundle.Tuning.Id.Value}' is missing."); return null; }
        if (descriptor.Version != bundle.Tuning.Version) { AddError(diagnostics, $"Tuning profile '{descriptor.Id.Value}' requires version {bundle.Tuning.Version}, but admitted version is {descriptor.Version}."); return null; }
        if (descriptor.Ruleset != bundle.Ruleset) { AddError(diagnostics, $"Tuning profile '{descriptor.Id.Value}' belongs to ruleset '{descriptor.Ruleset.Value}', not '{bundle.Ruleset.Value}'."); return null; }
        if (!files.TryGetValue(descriptor.PayloadPath, out byte[]? payload)) { AddError(diagnostics, $"Tuning profile '{descriptor.Id.Value}' payload '{descriptor.PayloadPath}' is missing."); return null; }
        return new TuningProfile(descriptor.Id, descriptor.Version, descriptor.Ruleset, payload.ToArray());
    }

    private static string Fingerprint(GameBundle bundle, IReadOnlyList<ContentPack> packs, TuningProfile tuning)
    {
        StringBuilder canonical = new();
        canonical.Append(bundle.Id.Value).Append('@').Append(bundle.Version).Append('|').Append(bundle.Ruleset.Value).Append('|');
        foreach (ContentPack pack in packs)
            canonical.Append(pack.Id.Value).Append('@').Append(pack.Version).Append(':').Append(Convert.ToHexString(SHA256.HashData(pack.Payload.Span))).Append('|');
        canonical.Append(tuning.Id.Value).Append('@').Append(tuning.Version).Append(':').Append(Convert.ToHexString(SHA256.HashData(tuning.Payload.Span)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static string ReadIdentifier(JsonElement root, string property)
    {
        string value = root.GetProperty(property).GetString() ?? throw new InvalidOperationException($"'{property}' must be a string.");
        if (!IsIdentifier(value)) throw new InvalidOperationException($"'{property}' value '{value}' is invalid.");
        return value;
    }

    private static int ReadVersion(JsonElement root)
    {
        if (!root.TryGetProperty("version", out JsonElement version) || !version.TryGetInt32(out int value) || value < 1)
            throw new InvalidOperationException("'version' must be a positive integer.");
        return value;
    }

    private static string ReadPath(JsonElement root, string property)
    {
        string value = root.GetProperty(property).GetString() ?? throw new InvalidOperationException($"'{property}' must be a string.");
        if (!IsPath(value)) throw new InvalidOperationException($"'{property}' value '{value}' is invalid.");
        return value;
    }

    private static bool IsIdentifier(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 96
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool IsPath(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 192
        && !value.StartsWith("/", StringComparison.Ordinal)
        && !value.Split('/').Any(segment => segment is "" or "." or "..")
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '/');

    private static void AddError(List<CompositionDiagnostic> diagnostics, string message)
    {
        if (diagnostics.Count < DiagnosticLimit) diagnostics.Add(new CompositionDiagnostic("error", message));
        else if (diagnostics.Count == DiagnosticLimit) diagnostics.Add(new CompositionDiagnostic("error", "Composition diagnostics were truncated."));
    }

    private enum VisitState { Visiting, Done }
    private sealed record GameBundleDescriptor(GameBundleId Id, int Version, RulesetId Ruleset, IReadOnlyList<ContentPackReference> ContentPacks, TuningProfileReference Tuning);
    private sealed record ContentPackDescriptor(ContentPackId Id, int Version, IReadOnlyList<ContentPackReference> Dependencies, string PayloadPath);
    private sealed record TuningDescriptor(TuningProfileId Id, int Version, RulesetId Ruleset, string PayloadPath);
}

public sealed class GameSessionContext(IEngineContext engine, ResolvedGameComposition composition)
{
    public IEngineContext Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
    public ResolvedGameComposition Composition { get; } = composition ?? throw new ArgumentNullException(nameof(composition));
}

public interface IGameRuleset
{
    RulesetId Id { get; }
    IGameSession CreateSession(GameSessionContext context);
}

public interface IGameSession : IDisposable
{
    void PublishInitial();
    ProductTurnRequest Update(ProductUpdate update);
}
