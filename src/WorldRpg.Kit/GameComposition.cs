using System.Text;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Kit;

public readonly record struct RulesetId(string Value);
public readonly record struct GameBundleId(string Value);
public readonly record struct ContentPackId(string Value);
/// <summary>A ruleset-interpreted content-pack role; opaque to the Kit.</summary>
public readonly record struct ContentPackRoleId(string Value);
public readonly record struct TuningProfileId(string Value);
/// <summary>A compiled rules variant a game bundle may select.</summary>
public readonly record struct RulesVariantId(string Value);
/// <summary>A ruleset-declared variation point a variant replaces as a whole owner; opaque to the Kit.</summary>
public readonly record struct RulesSeamId(string Value);

public sealed record GameBundle(GameBundleId Id, RulesetId Ruleset, IReadOnlyList<ContentPackReference> ContentPacks, TuningProfileReference Tuning,
    IReadOnlyList<RulesVariantId> Variants, string Title);

/// <summary>An admitted game bundle as a player chooses it: its id, the ruleset it composes, and its authored title.</summary>
public sealed record GameBundleSummary(GameBundleId Id, RulesetId Ruleset, string Title);

/// <summary>
/// What a compiled rules variant declares about itself: the ruleset it deviates from, the seams whose
/// owners it replaces, its own tuning profile, the content packs it needs, and whether it owns save state.
/// A ruleset publishes its compiled variants' manifests; a bundle selects them by id.
/// </summary>
public sealed record RulesVariantManifest
{
    public RulesVariantManifest(RulesVariantId id, RulesetId ruleset, IEnumerable<RulesSeamId> seams, TuningProfileId tuning,
        IEnumerable<ContentPackId> contentPacks, bool ownsSaveState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleset.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(tuning.Value);
        Id = id; Ruleset = ruleset; Tuning = tuning; OwnsSaveState = ownsSaveState;
        Seams = Array.AsReadOnly((seams ?? throw new ArgumentNullException(nameof(seams))).ToArray());
        ContentPacks = Array.AsReadOnly((contentPacks ?? throw new ArgumentNullException(nameof(contentPacks))).ToArray());
        if (Seams.Any(seam => string.IsNullOrWhiteSpace(seam.Value)) || Seams.Distinct().Count() != Seams.Count)
            throw new ArgumentException($"Rules variant '{id.Value}' must claim each seam once by a non-empty id.", nameof(seams));
        if (ContentPacks.Any(pack => string.IsNullOrWhiteSpace(pack.Value)) || ContentPacks.Distinct().Count() != ContentPacks.Count)
            throw new ArgumentException($"Rules variant '{id.Value}' must name each content pack once by a non-empty id.", nameof(contentPacks));
    }
    public RulesVariantId Id { get; }
    public RulesetId Ruleset { get; }
    public IReadOnlyList<RulesSeamId> Seams { get; }
    public TuningProfileId Tuning { get; }
    public IReadOnlyList<ContentPackId> ContentPacks { get; }
    public bool OwnsSaveState { get; }
}

/// <summary>A selected rules variant with its admitted tuning profile.</summary>
public sealed record ResolvedRulesVariant(RulesVariantManifest Manifest, TuningProfile Tuning)
{
    public RulesVariantId Id => Manifest.Id;
}
public sealed record ContentPackReference(ContentPackId Id);
public sealed record TuningProfileReference(TuningProfileId Id);

public sealed class ContentPack
{
    private readonly ReadOnlyMemory<byte> _payload;
    internal ContentPack(GameCompositionResolver.ContentPackDescriptor value, ReadOnlyMemory<byte> payload)
    {
        Id = value.Id; Ruleset = value.Ruleset; Role = value.Role;
        Dependencies = Freeze(value.Dependencies); PayloadPath = value.PayloadPath; _payload = payload;
    }
    public ContentPackId Id { get; }
    public RulesetId Ruleset { get; }
    /// <summary>
    /// What the pack is to its ruleset. The Kit only checks that it is a well-formed identifier; the
    /// ruleset alone interprets it and chooses the reader, so a bundle can select any number of packs
    /// without the ruleset naming any of them.
    /// </summary>
    public ContentPackRoleId Role { get; }
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
    internal ResolvedGameComposition(GameBundle bundle, IEnumerable<ContentPack> packs, TuningProfile tuning, IEnumerable<ResolvedRulesVariant> variants,
        ProductContent content)
    {
        Bundle = bundle; ContentPacks = Freeze(packs); Tuning = tuning; Variants = Freeze(variants);
        Content = content;
        Identity = new ResolvedCompositionIdentity(Bundle, ContentPacks, Tuning, Variants);
    }
    public GameBundle Bundle { get; }
    public RulesetId Ruleset => Bundle.Ruleset;
    public IReadOnlyList<ContentPack> ContentPacks { get; }
    public TuningProfile Tuning { get; }
    /// <summary>The bundle's selected rules variants, in bundle order; empty for the ruleset's own rules.</summary>
    public IReadOnlyList<ResolvedRulesVariant> Variants { get; }
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

    internal ResolvedCompositionIdentity(GameBundle bundle, IEnumerable<ContentPack> contentPacks, TuningProfile tuning, IEnumerable<ResolvedRulesVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        Variants = Array.AsReadOnly(variants.Select(variant => variant.Id).ToArray());
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
    /// <summary>The selected rules variants, which a save records and a load must match.</summary>
    public IReadOnlyList<RulesVariantId> Variants { get; }
}

/// <summary>Resolved content-pack identity for diagnostics and ruleset construction.</summary>
public readonly record struct ResolvedContentPackIdentity(ContentPackId Id);

/// <summary>One resolution finding. <see cref="Code"/> is its severity; <see cref="Name"/>, when present, names the finding.</summary>
public sealed record CompositionDiagnostic(string Code, string Message, string? Name = null);

/// <summary>The named resolution findings a caller may act on.</summary>
public static class CompositionDiagnosticNames
{
    public const string UnknownVariant = "variant.unknown";
    public const string VariantRulesetMismatch = "variant.ruleset-mismatch";
    public const string VariantSeamCollision = "variant.seam-collision";
    public const string DuplicateVariantManifest = "variant.duplicate-manifest";
}
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

    /// <summary>Resolves a bundle that may select no compiled rules variants.</summary>
    public static GameCompositionResolution Resolve(ProductContent content, GameBundleId requestedBundle) =>
        Resolve(content, requestedBundle, []);

    /// <summary>
    /// Resolves a bundle against admitted content and the compiled rules variants the product composes.
    /// An unknown variant, a variant for another ruleset, or two variants claiming one seam is an error;
    /// each selected variant's tuning profile and content packs resolve as the bundle's own do.
    /// </summary>
    public static GameCompositionResolution Resolve(ProductContent content, GameBundleId requestedBundle, IReadOnlyList<RulesVariantManifest> compiledVariants)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(compiledVariants);
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
        TuningProfile? tuning = ResolveTuning(selected.Tuning.Id, selected.Ruleset, tunings, files, diagnostics, content);
        List<ResolvedRulesVariant> variants = [];
        foreach (RulesVariantManifest manifest in SelectVariants(selected, compiledVariants, diagnostics))
        {
            // A variant's own packs join the bundle's after them; one the bundle already selects is not added twice.
            foreach (ContentPackId pack in manifest.ContentPacks) ResolvePack(new(pack), selected.Ruleset, packs, files, visits, ordered, diagnostics, content);
            if (ResolveTuning(manifest.Tuning, selected.Ruleset, tunings, files, diagnostics, content) is TuningProfile variantTuning)
                variants.Add(new(manifest, variantTuning));
        }
        if (diagnostics.Any(diagnostic => diagnostic.Code == "error") || tuning is null) return new(null, diagnostics);
        GameBundle bundle = new(selected.Id, selected.Ruleset, Freeze(selected.ContentPacks), selected.Tuning, Freeze(selected.Variants), selected.Title);
        return new(new ResolvedGameComposition(bundle, ordered, tuning, variants, content), diagnostics);
    }

    /// <summary>
    /// Every well-formed game bundle the content admits, by id, without resolving any: whether one composes
    /// is <see cref="Resolve(ProductContent, GameBundleId, IReadOnlyList{RulesVariantManifest})"/>'s answer.
    /// </summary>
    public static IReadOnlyList<GameBundleSummary> ListBundles(ProductContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        List<CompositionDiagnostic> diagnostics = [];
        Dictionary<GameBundleId, GameBundleDescriptor> bundles = [];
        Dictionary<ContentPackId, ContentPackDescriptor> packs = [];
        Dictionary<TuningProfileId, TuningDescriptor> tunings = [];
        foreach (string path in IndexFiles(content, diagnostics).Keys)
            if (path.EndsWith(".bundle.json", StringComparison.Ordinal)) ReadDescriptor(path, content.ReadBytes(path), bundles, packs, tunings, diagnostics);
        return [.. bundles.Values.OrderBy(bundle => bundle.Id.Value, StringComparer.Ordinal).Select(bundle => new GameBundleSummary(bundle.Id, bundle.Ruleset, bundle.Title))];
    }

    private static List<RulesVariantManifest> SelectVariants(GameBundleDescriptor bundle, IReadOnlyList<RulesVariantManifest> compiled, List<CompositionDiagnostic> diagnostics)
    {
        Dictionary<RulesVariantId, RulesVariantManifest> catalog = [];
        foreach (RulesVariantManifest manifest in compiled)
            if (!catalog.TryAdd(manifest.Id, manifest))
                Error(diagnostics, $"Two compiled rules variants declare id '{manifest.Id.Value}'.", CompositionDiagnosticNames.DuplicateVariantManifest);
        List<RulesVariantManifest> selected = [];
        Dictionary<RulesSeamId, RulesVariantId> claims = [];
        foreach (RulesVariantId id in bundle.Variants)
        {
            if (!catalog.TryGetValue(id, out RulesVariantManifest? manifest))
            { Error(diagnostics, $"Game bundle '{bundle.Id.Value}' selects rules variant '{id.Value}', which this product does not compile.", CompositionDiagnosticNames.UnknownVariant); continue; }
            if (manifest.Ruleset != bundle.Ruleset)
            { Error(diagnostics, $"Rules variant '{id.Value}' applies to ruleset '{manifest.Ruleset.Value}', not '{bundle.Ruleset.Value}'.", CompositionDiagnosticNames.VariantRulesetMismatch); continue; }
            bool collides = false;
            foreach (RulesSeamId seam in manifest.Seams)
            {
                if (claims.TryGetValue(seam, out RulesVariantId owner))
                { Error(diagnostics, $"Rules variants '{owner.Value}' and '{id.Value}' both claim seam '{seam.Value}'.", CompositionDiagnosticNames.VariantSeamCollision); collides = true; }
                else claims[seam] = id;
            }
            if (!collides) selected.Add(manifest);
        }
        return selected;
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
        List<RulesVariantId> variants = [];
        if (root.TryGetProperty("variants", out JsonElement selected))
        {
            if (selected.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("'variants' must be an array.");
            foreach (JsonElement element in selected.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } value || !Identifier(value))
                    throw new InvalidOperationException("A selected rules variant must be a valid identifier.");
                variants.Add(new(value));
            }
            if (variants.Distinct().Count() != variants.Count) throw new InvalidOperationException("Game bundle selects a rules variant more than once.");
        }
        string id = Id(root, "id");
        // A bundle's title is what a player chooses it by; one that authors none is named by its id.
        string title = id;
        if (root.TryGetProperty("title", out JsonElement authored))
            title = authored.ValueKind == JsonValueKind.String && authored.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
                ? text.Trim() : throw new InvalidOperationException("'title' must be a non-empty string.");
        return new(new(id), new(Id(root, "ruleset")), contentPacks.ToArray(), new(new(Id(tuning, "id"))), variants.ToArray(), title);
    }

    private static ContentPackDescriptor Pack(JsonElement root)
    {
        List<ContentPackReference> dependencies = References(Array(root, "dependencies"));
        if (dependencies.GroupBy(reference => reference.Id).Any(group => group.Count() > 1)) throw new InvalidOperationException("Content pack declares a dependency more than once.");
        return new(new(Id(root, "id")), new(Id(root, "ruleset")), new(Id(root, "role")), dependencies.ToArray(), FilePath(root, "payload"));
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

    private static TuningProfile? ResolveTuning(TuningProfileId id, RulesetId ruleset, IReadOnlyDictionary<TuningProfileId, TuningDescriptor> descriptors, IReadOnlyDictionary<string, byte> files, List<CompositionDiagnostic> diagnostics, ProductContent content)
    {
        if (!descriptors.TryGetValue(id, out TuningDescriptor? descriptor)) { Error(diagnostics, $"Tuning profile '{id.Value}' is missing."); return null; }
        if (descriptor.Ruleset != ruleset) { Error(diagnostics, $"Tuning profile '{descriptor.Id.Value}' belongs to ruleset '{descriptor.Ruleset.Value}', not '{ruleset.Value}'."); return null; }
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
    private static void Error(List<CompositionDiagnostic> diagnostics, string message, string? name = null) { if (diagnostics.Count < DiagnosticLimit) diagnostics.Add(new("error", message, name)); else if (diagnostics.Count == DiagnosticLimit) diagnostics.Add(new("error", "Composition diagnostics were truncated.")); }

    private enum VisitState { Visiting, Done }
    internal sealed record ContentPackDescriptor(ContentPackId Id, RulesetId Ruleset, ContentPackRoleId Role, IReadOnlyList<ContentPackReference> Dependencies, string PayloadPath);
    internal sealed record TuningDescriptor(TuningProfileId Id, RulesetId Ruleset, string PayloadPath);
    private sealed record GameBundleDescriptor(GameBundleId Id, RulesetId Ruleset, IReadOnlyList<ContentPackReference> ContentPacks, TuningProfileReference Tuning,
        IReadOnlyList<RulesVariantId> Variants, string Title);
}

public sealed class GameSessionContext(IEngineContext engine, ResolvedGameComposition composition, LoadingScreen? loading = null)
{
    public IEngineContext Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
    public ResolvedGameComposition Composition { get; } = composition ?? throw new ArgumentNullException(nameof(composition));
    public ResolvedCompositionIdentity CompositionIdentity => Composition.Identity;

    /// <summary>
    /// The host's loading screen, on which a session stages its own slow work (a journey, a site change) when
    /// it accepts one inside an admitted update. Null when the host shows none; the work then runs at once.
    /// </summary>
    public LoadingScreen? Loading { get; } = loading;
}
public interface IGameRuleset { RulesetId Id { get; } IGameSession CreateSession(GameSessionContext context); }

/// <summary>
/// Optional seam for a session whose projection carries the host's <see cref="LoadingScreen"/>: while staged work
/// runs the session does not update, so the host asks it to publish each change of the screen.
/// </summary>
public interface ILoadingScreenSession
{
    void PublishLoading();
}
/// <summary>
/// Optional ruleset seam publishing the manifests of the rules variants it compiles, so a bundle's
/// variant selection resolves against exactly what this ruleset can build.
/// </summary>
public interface IRulesVariantCatalog { IReadOnlyList<RulesVariantManifest> CompiledVariants { get; } }
public interface IGameSession : IDisposable { void PublishInitial(); ProductUpdateResult Update(ProductUpdate update); }

/// <summary>Optional entry flow that constructs a fresh session from committed character choices.</summary>
public interface ICharacterCreationSession
{
    bool RequiresCharacterInitialization { get; }

    /// <summary>Whether a committed character is ready for <see cref="CreateNewGame"/>.</summary>
    bool HasCommittedCharacter { get; }

    /// <summary>Opens the ruleset's character creation from the entry screen; it stays open when it already is.</summary>
    void OpenCharacterCreation();

    IGameSession CreateNewGame();
}

/// <summary>Optional ruleset seam for recognizing its own entry-screen action.</summary>
public interface IEntryScreenSession
{
    bool RequestsBegin(ReadOnlySpan<ProductInputEvent> input);
}

/// <summary>
/// Optional entry seam for a ruleset whose entry action starts work before ordinary play may begin.
/// The Host owns the eventual mode transition; the ruleset only says whether its entry work is
/// waiting, ready, or could not start.
/// </summary>
public interface IEntryScreenStartupSession
{
    EntryScreenStartupResult StartEntry();

    /// <summary>Consumes the one completion which permits the Host to leave its entry screen.</summary>
    bool TakeEntryReadyForPlay();
}

/// <summary>The result of requesting a ruleset-owned entry startup operation.</summary>
public enum EntryScreenStartupResult
{
    /// <summary>The Host may enter ordinary play now.</summary>
    ReadyForPlay,

    /// <summary>The ruleset accepted the request and is waiting on an Engine-admitted operation.</summary>
    Waiting,

    /// <summary>The ruleset refused or could not start its entry operation.</summary>
    Failed,
}

/// <summary>
/// The mode a product runs a game session under. The product decides the mode; a session decides
/// what the mode means for its own world, input and presentation.
/// </summary>
public enum ProductMode
{
    /// <summary>
    /// The entry screen owns the product: it shows the screen the product offers before a world
    /// starts. The product still forwards updates so presentation keeps publishing; a mode-aware
    /// session interprets no gameplay input and takes no world step until the product leaves this
    /// mode, and a session that does not observe modes runs as it did before the mode existed.
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

    /// <summary>
    /// Whether the pending request is the owned interaction closing itself. A resume does not
    /// close a modal interaction — only the interaction that owns input ends it — so the product
    /// can only tell a close from a resume when the session says which one this is. True is only
    /// meaningful alongside a <see cref="PendingModeRequest"/>; without a request it is ignored.
    /// </summary>
    bool PendingModeRequestClosesModal { get; }
}

/// <summary>
/// Optional session seam for the choices a player can make after defeat. The ruleset recognizes
/// its own action payload and asks; the Host owns session replacement, title selection, and save
/// storage. Keeping this seam ruleset-neutral lets the Host route new, load, and quit without
/// interpreting a product's UI vocabulary or reading its save payload.
/// </summary>
public interface IPlayerDefeatOutcomeSession : IGameSession
{
    /// <summary>Takes the one defeat outcome request admitted by the ruleset, if any.</summary>
    PlayerDefeatOutcomeRequest? TakePlayerDefeatOutcomeRequest();

    /// <summary>Presents a Host-owned new-game or title outcome on the current session.</summary>
    void ReportPlayerDefeatOutcome(string message);
}

/// <summary>The session replacement or persistence action requested by a defeated player.</summary>
public enum PlayerDefeatOutcome
{
    NewGame,
    Load,
    QuitToTitle,
}

/// <summary>One ruleset-neutral player-defeat choice, optionally naming a Host save slot.</summary>
public sealed record PlayerDefeatOutcomeRequest(PlayerDefeatOutcome Outcome, string? SaveKey = null);

/// <summary>
/// Optional session seam for ordinary named save-slot requests. The session interprets the
/// player-facing actions and asks, because only the ruleset knows whether an action means
/// anything in the current mode; the product owns catalog storage and any session replacement,
/// then reports the outcome back for the session to present. A session that does not implement
/// this ignores save-slot actions, which is why the seam is optional rather than part of
/// <see cref="IGameSession"/>.
/// </summary>
public interface ISaveRequestingGameSession : IGameSession
{
    /// <summary>Presents the product's save/load outcome after it honored a request.</summary>
    void ReportSaveOutcome(string message);

    /// <summary>
    /// Takes one named save-slot request.
    /// </summary>
    SaveSlotRequest? TakeSaveSlotRequest();

    /// <summary>
    /// Delivers the Host-owned catalog after listing or changing it. Rulesets only present this
    /// projection; they do not inspect storage or choose persistence behavior.
    /// </summary>
    void ReportSaveSlots(IReadOnlyList<SaveSlotSummary> slots, string? diagnostic);
}

/// <summary>
/// Optional session seam for choosing the game bundle at the entry screen. The Host offers the bundles that
/// compose and names the current one; the session presents them, takes the player's choice, and presents the
/// outcome. Only the Host switches: it resolves the chosen bundle and replaces the entry-screen session.
/// </summary>
public interface IGameBundleChoiceSession : IGameSession
{
    /// <summary>Delivers the bundles the player may choose and the one this session runs under.</summary>
    void ReportBundles(GameBundleChoices choices);

    /// <summary>Takes the bundle the player chose, if any, since the last take.</summary>
    GameBundleId? TakeBundleRequest();

    /// <summary>Presents why a requested bundle was not switched to.</summary>
    void ReportBundleOutcome(string message);
}

/// <summary>The bundles a player may choose at the entry screen, and the current one.</summary>
public sealed record GameBundleChoices(GameBundleId Current, IReadOnlyList<GameBundleSummary> Options);

/// <summary>The named operation a ruleset asks the Host's ordinary save-slot owner to perform.</summary>
public enum SaveSlotOperation
{
    List,
    Save,
    Load,
    Delete,
    /// <summary>Saves into the Host's one quick-save slot, replacing what it held.</summary>
    QuickSave,
    /// <summary>Loads the Host's quick-save slot.</summary>
    QuickLoad,
}

/// <summary>
/// A player-visible save-slot request. Save names new slots when <see cref="Key"/> is absent;
/// selecting an existing key requires <see cref="Confirm"/> before it can overwrite or delete.
/// </summary>
public sealed record SaveSlotRequest(SaveSlotOperation Operation, string? Key = null, string? Label = null, bool Confirm = false);

/// <summary>Host-owned metadata a ruleset may project without opening a save payload.</summary>
/// <param name="Description">What the ruleset said about the game when it was saved, if it says anything.</param>
public sealed record SaveSlotSummary(string Key, string Label, DateTime SavedAtUtc, string Ruleset, SaveDescription? Description = null);

/// <summary>
/// What a save slot list shows about a saved game without opening it: the character, their level, where
/// they were, the world's date and time, and how long the game has been played. Each is the ruleset's own
/// wording; any it has no meaning for stays null.
/// </summary>
public sealed record SaveDescription(string? Character, int? Level, string? Location, string? WorldTime, double PlaySeconds);

/// <summary>Optional session seam: describes the game for the slot a save is written to.</summary>
public interface ISaveDescribingGameSession : IGameSession
{
    SaveDescription DescribeSave();
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

/// <summary>
/// Ruleset-neutral envelope persisted by the Host; only the ruleset interprets Payload. It records the
/// rules variants the game ran under, which a load must match.
/// </summary>
public sealed class GameSaveEnvelope
{
    public GameSaveEnvelope(RulesetSavePayload payload, IEnumerable<RulesVariantId> variants)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Variants = Array.AsReadOnly((variants ?? throw new ArgumentNullException(nameof(variants))).ToArray());
    }
    public RulesetSavePayload Payload { get; }
    public IReadOnlyList<RulesVariantId> Variants { get; }
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
