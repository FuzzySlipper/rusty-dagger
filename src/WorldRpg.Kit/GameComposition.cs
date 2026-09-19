using System.Text;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Kit;

public readonly record struct RulesetId(string Value);
public readonly record struct GameBundleId(string Value);
public readonly record struct ContentPackId(string Value);
public readonly record struct TuningProfileId(string Value);

public sealed record GameBundle(GameBundleId Id, RulesetId Ruleset, IReadOnlyList<ContentPackReference> ContentPacks, TuningProfileReference Tuning);
public sealed record ContentPackReference(ContentPackId Id);
public sealed record TuningProfileReference(TuningProfileId Id);

public sealed class ContentPack
{
    private readonly ReadOnlyMemory<byte> _payload;
    internal ContentPack(GameCompositionResolver.ContentPackDescriptor value, ReadOnlyMemory<byte> payload)
    {
        Id = value.Id; Ruleset = value.Ruleset;
        Dependencies = Freeze(value.Dependencies); PayloadPath = value.PayloadPath; _payload = payload;
    }
    public ContentPackId Id { get; }
    public RulesetId Ruleset { get; }
    public IReadOnlyList<ContentPackReference> Dependencies { get; }
    public string PayloadPath { get; }
    /// <summary>The immutable payload admitted with this composition.</summary>
    public ReadOnlyMemory<byte> Payload => _payload;
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

public sealed class TuningProfile
{
    private readonly ReadOnlyMemory<byte> _payload;
    internal TuningProfile(GameCompositionResolver.TuningDescriptor value, ReadOnlyMemory<byte> payload)
    {
        Id = value.Id; Ruleset = value.Ruleset;
        PayloadPath = value.PayloadPath; _payload = payload;
    }
    public TuningProfileId Id { get; }
    public RulesetId Ruleset { get; }
    public string PayloadPath { get; }
    /// <summary>The immutable payload admitted with this composition.</summary>
    public ReadOnlyMemory<byte> Payload => _payload;
}

public sealed class ResolvedGameComposition
{
    internal ResolvedGameComposition(GameBundle bundle, IEnumerable<ContentPack> packs, TuningProfile tuning, ProductContent content)
    {
        Bundle = bundle; ContentPacks = Freeze(packs); Tuning = tuning;
        Content = content;
        Identity = new ResolvedCompositionIdentity(Bundle, ContentPacks, Tuning);
    }
    public GameBundle Bundle { get; }
    public RulesetId Ruleset => Bundle.Ruleset;
    public IReadOnlyList<ContentPack> ContentPacks { get; }
    public TuningProfile Tuning { get; }
    /// <summary>The resolved selection, retained for product diagnostics.</summary>
    public ResolvedCompositionIdentity Identity { get; }
    /// <summary>The original immutable Engine content snapshot selected by this composition.</summary>
    public ProductContent Content { get; }
    public ContentPack RequireContentPack(ContentPackId id) => ContentPacks.SingleOrDefault(pack => pack.Id == id) ?? throw new InvalidOperationException($"Resolved composition does not contain content pack '{id.Value}'.");
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

/// <summary>Immutable, ruleset-neutral identity of one resolved product composition.</summary>
public sealed class ResolvedCompositionIdentity
{
    private readonly IReadOnlyList<ContentPackId> _contentPacks;
    private readonly IReadOnlyList<ResolvedContentPackIdentity> _contentPackIdentities;

    internal ResolvedCompositionIdentity(GameBundle bundle, IEnumerable<ContentPack> contentPacks, TuningProfile tuning)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(contentPacks);
        ArgumentNullException.ThrowIfNull(tuning);
        Bundle = bundle.Id;
        Ruleset = bundle.Ruleset;
        _contentPackIdentities = Array.AsReadOnly(contentPacks.Select(pack => new ResolvedContentPackIdentity(pack.Id)).ToArray());
        _contentPacks = Array.AsReadOnly(_contentPackIdentities.Select(pack => pack.Id).ToArray());
        Tuning = tuning.Id;
    }

    public GameBundleId Bundle { get; }
    public RulesetId Ruleset { get; }
    public IReadOnlyList<ContentPackId> ContentPacks => _contentPacks;
    public IReadOnlyList<ResolvedContentPackIdentity> ContentPackIdentities => _contentPackIdentities;
    public TuningProfileId Tuning { get; }
}

/// <summary>Resolved content-pack identity for diagnostics and ruleset construction.</summary>
public readonly record struct ResolvedContentPackIdentity(ContentPackId Id);

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
        SortedDictionary<string, byte> files = IndexFiles(content, diagnostics);
        if (!Identifier(requestedBundle.Value)) Error(diagnostics, $"Requested bundle id '{requestedBundle.Value}' is invalid.");
        Dictionary<GameBundleId, GameBundleDescriptor> bundles = [];
        Dictionary<ContentPackId, ContentPackDescriptor> packs = [];
        Dictionary<TuningProfileId, TuningDescriptor> tunings = [];
        foreach (string path in files.Keys) ReadDescriptor(path, content.ReadBytes(path), bundles, packs, tunings, diagnostics);
        if (!bundles.TryGetValue(requestedBundle, out GameBundleDescriptor? selected))
        {
            Error(diagnostics, $"Requested game bundle '{requestedBundle.Value}' was not admitted.");
            return new(null, diagnostics);
        }
        List<ContentPack> ordered = [];
        Dictionary<ContentPackId, VisitState> visits = [];
        foreach (ContentPackReference reference in selected.ContentPacks) ResolvePack(reference, selected.Ruleset, packs, files, visits, ordered, diagnostics, content);
        TuningProfile? tuning = ResolveTuning(selected, tunings, files, diagnostics, content);
        if (diagnostics.Any(diagnostic => diagnostic.Code == "error") || tuning is null) return new(null, diagnostics);
        GameBundle bundle = new(selected.Id, selected.Ruleset, Freeze(selected.ContentPacks), selected.Tuning);
        return new(new ResolvedGameComposition(bundle, ordered, tuning, content), diagnostics);
    }

    private static SortedDictionary<string, byte> IndexFiles(ProductContent content, List<CompositionDiagnostic> diagnostics)
    {
        SortedDictionary<string, byte> result = new(StringComparer.Ordinal);
        foreach (ProductContentFile file in content.Files.Span)
        {
            string path;
            try { path = StrictUtf8.GetString(file.Path.Span); }
            catch (DecoderFallbackException) { Error(diagnostics, "An admitted content path is not valid UTF-8."); continue; }
            if (!Path(path)) { Error(diagnostics, $"Admitted content path '{path}' is invalid."); continue; }
            if (!result.TryAdd(path, (byte)0)) Error(diagnostics, $"Admitted content contains duplicate path '{path}'.");
        }
        return result;
    }

    private static void ReadDescriptor(string path, ReadOnlyMemory<byte> bytes, Dictionary<GameBundleId, GameBundleDescriptor> bundles, Dictionary<ContentPackId, ContentPackDescriptor> packs, Dictionary<TuningProfileId, TuningDescriptor> tunings, List<CompositionDiagnostic> diagnostics)
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
        return new(new(Id(root, "id")), new(Id(root, "ruleset")), contentPacks.ToArray(), new(new(Id(tuning, "id"))));
    }

    private static ContentPackDescriptor Pack(JsonElement root)
    {
        List<ContentPackReference> dependencies = References(Array(root, "dependencies"));
        if (dependencies.GroupBy(reference => reference.Id).Any(group => group.Count() > 1)) throw new InvalidOperationException("Content pack declares a dependency more than once.");
        return new(new(Id(root, "id")), new(Id(root, "ruleset")), dependencies.ToArray(), FilePath(root, "payload"));
    }

    private static TuningDescriptor Tuning(JsonElement root) => new(new(Id(root, "id")), new(Id(root, "ruleset")), FilePath(root, "payload"));
    private static List<ContentPackReference> References(JsonElement elements)
    {
        List<ContentPackReference> result = [];
        foreach (JsonElement element in elements.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Content pack reference must be an object.");
            result.Add(new(new(Id(element, "id"))));
        }
        return result;
    }

    private static void ResolvePack(ContentPackReference reference, RulesetId ruleset, IReadOnlyDictionary<ContentPackId, ContentPackDescriptor> descriptors, IReadOnlyDictionary<string, byte> files, Dictionary<ContentPackId, VisitState> visits, List<ContentPack> ordered, List<CompositionDiagnostic> diagnostics, ProductContent content)
    {
        if (!descriptors.TryGetValue(reference.Id, out ContentPackDescriptor? descriptor)) { Error(diagnostics, $"Content pack '{reference.Id.Value}' is missing."); return; }
        if (descriptor.Ruleset != ruleset) { Error(diagnostics, $"Content pack '{reference.Id.Value}' belongs to ruleset '{descriptor.Ruleset.Value}', not '{ruleset.Value}'."); return; }
        if (visits.TryGetValue(reference.Id, out VisitState state)) { if (state == VisitState.Visiting) Error(diagnostics, $"Content pack dependency cycle includes '{reference.Id.Value}'."); return; }
        visits[reference.Id] = VisitState.Visiting;
        foreach (ContentPackReference dependency in descriptor.Dependencies) ResolvePack(dependency, ruleset, descriptors, files, visits, ordered, diagnostics, content);
        visits[reference.Id] = VisitState.Done;
        if (!files.ContainsKey(descriptor.PayloadPath)) { Error(diagnostics, $"Content pack '{reference.Id.Value}' payload '{descriptor.PayloadPath}' is missing."); return; }
        ordered.Add(new(descriptor, content.ReadBytes(descriptor.PayloadPath)));
    }

    private static TuningProfile? ResolveTuning(GameBundleDescriptor bundle, IReadOnlyDictionary<TuningProfileId, TuningDescriptor> descriptors, IReadOnlyDictionary<string, byte> files, List<CompositionDiagnostic> diagnostics, ProductContent content)
    {
        if (!descriptors.TryGetValue(bundle.Tuning.Id, out TuningDescriptor? descriptor)) { Error(diagnostics, $"Tuning profile '{bundle.Tuning.Id.Value}' is missing."); return null; }
        if (descriptor.Ruleset != bundle.Ruleset) { Error(diagnostics, $"Tuning profile '{descriptor.Id.Value}' belongs to ruleset '{descriptor.Ruleset.Value}', not '{bundle.Ruleset.Value}'."); return null; }
        if (!files.ContainsKey(descriptor.PayloadPath)) { Error(diagnostics, $"Tuning profile '{descriptor.Id.Value}' payload '{descriptor.PayloadPath}' is missing."); return null; }
        return new(descriptor, content.ReadBytes(descriptor.PayloadPath));
    }

    private static JsonElement Required(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out JsonElement value)) throw new InvalidOperationException($"Required property '{property}' is missing.");
        return value;
    }
    private static JsonElement Object(JsonElement root, string property) { JsonElement value = Required(root, property); return value.ValueKind == JsonValueKind.Object ? value : throw new InvalidOperationException($"'{property}' must be an object."); }
    private static JsonElement Array(JsonElement root, string property) { JsonElement value = Required(root, property); return value.ValueKind == JsonValueKind.Array ? value : throw new InvalidOperationException($"'{property}' must be an array."); }
    private static string String(JsonElement root, string property) { JsonElement value = Required(root, property); return value.ValueKind == JsonValueKind.String && value.GetString() is { } text ? text : throw new InvalidOperationException($"'{property}' must be a string."); }
    private static string Id(JsonElement root, string property) { string value = String(root, property); return Identifier(value) ? value : throw new InvalidOperationException($"'{property}' value '{value}' is invalid."); }
    private static string FilePath(JsonElement root, string property) { string value = String(root, property); return Path(value) ? value : throw new InvalidOperationException($"'{property}' value '{value}' is invalid."); }
    private static bool Identifier(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 96 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    private static bool Path(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 192 && !value.StartsWith("/", StringComparison.Ordinal) && !value.Split('/').Any(segment => segment is "" or "." or "..") && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '/');
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => System.Array.AsReadOnly(values.ToArray());
    private static void Error(List<CompositionDiagnostic> diagnostics, string message) { if (diagnostics.Count < DiagnosticLimit) diagnostics.Add(new("error", message)); else if (diagnostics.Count == DiagnosticLimit) diagnostics.Add(new("error", "Composition diagnostics were truncated.")); }

    private enum VisitState { Visiting, Done }
    internal sealed record ContentPackDescriptor(ContentPackId Id, RulesetId Ruleset, IReadOnlyList<ContentPackReference> Dependencies, string PayloadPath);
    internal sealed record TuningDescriptor(TuningProfileId Id, RulesetId Ruleset, string PayloadPath);
    private sealed record GameBundleDescriptor(GameBundleId Id, RulesetId Ruleset, IReadOnlyList<ContentPackReference> ContentPacks, TuningProfileReference Tuning);
}

public sealed class GameSessionContext(IEngineContext engine, ResolvedGameComposition composition)
{
    public IEngineContext Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
    public ResolvedGameComposition Composition { get; } = composition ?? throw new ArgumentNullException(nameof(composition));
    public ResolvedCompositionIdentity CompositionIdentity => Composition.Identity;
}
public interface IGameRuleset { RulesetId Id { get; } IGameSession CreateSession(GameSessionContext context); }
public interface IGameSession : IDisposable { void PublishInitial(); ProductUpdateResult Update(ProductUpdate update); }

/// <summary>Optional ruleset seam for recognizing its own entry-screen action.</summary>
public interface IEntryScreenSession
{
    bool RequestsBegin(ReadOnlySpan<ProductInputEvent> input);
}

/// <summary>
/// The mode a product runs a game session under. The product decides the mode; a session decides
/// what the mode means for its own world, input and presentation.
/// </summary>
public enum ProductMode
{
    /// <summary>
    /// The entry screen owns the product: it shows the screen the product offers before a world
    /// starts, so neither gameplay input nor world time reaches the session until the product leaves
    /// this mode. A product that never enters it behaves exactly as it did before the mode existed.
    /// </summary>
    Title,

    /// <summary>Ordinary play: gameplay input is interpreted and world time advances.</summary>
    Playing,

    /// <summary>Paused: no update reaches the world, so neither input nor time advances.</summary>
    Paused,

    /// <summary>A modal interaction owns input: presentation stays live and the world holds still.</summary>
    Modal,

    /// <summary>The player is dead: presentation stays live and gameplay input and time do not.</summary>
    Dead,
}

/// <summary>
/// Optional session seam for a ruleset whose world behaves differently per product mode. A session
/// that does not implement this keeps its playing behaviour, which is why the seam is optional
/// rather than part of <see cref="IGameSession"/>.
/// </summary>
public interface IModeAwareGameSession
{
    /// <summary>Applies the mode the product has decided. Called on change, not once per update.</summary>
    void ApplyProductMode(ProductMode mode);

    /// <summary>
    /// The mode the session asks the product to enter, or null when it asks for nothing. The
    /// session asks because it can open an interaction the product cannot see, such as a loot
    /// window triggered by a gameplay key. The product still decides: it may refuse the request,
    /// which is what keeps one authority over focus, pause and death.
    /// </summary>
    ProductMode? PendingModeRequest { get; }
}


/// <summary>Opaque current-state save bytes plus the compiled ruleset that interprets them.</summary>
public sealed class RulesetSavePayload
{
    private readonly byte[] _bytes;
    public RulesetSavePayload(RulesetId ruleset, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleset.Value);
        Ruleset = ruleset;
        _bytes = bytes.ToArray();
    }
    public RulesetId Ruleset { get; }
    public ReadOnlyMemory<byte> Bytes => _bytes.ToArray();
}

/// <summary>Ruleset-neutral envelope persisted by the Host; only the ruleset interprets Payload.</summary>
public sealed class GameSaveEnvelope
{
    public GameSaveEnvelope(RulesetSavePayload payload)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }
    public RulesetSavePayload Payload { get; }
}

/// <summary>Optional compiled-ruleset persistence seam. A resume payload is supplied after its ruleset identity is checked.</summary>
public interface ISaveableGameRuleset : IGameRuleset
{
    IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved);
}

/// <summary>Optional session seam for capturing only ruleset-owned durable meaning.</summary>
public interface ISaveableGameSession : IGameSession
{
    RulesetSavePayload CaptureSave();
}
