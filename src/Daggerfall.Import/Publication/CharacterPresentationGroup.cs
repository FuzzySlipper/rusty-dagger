using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>
/// The character media group and the presentation section built from it: every character canvas the corpus
/// supplies is published and indexed, and the section states which of them the character sheet binds.
/// </summary>
/// <remarks>
/// The publication runs before the presentation is built, because the presentation states whether a
/// reference binds: <see cref="MediaBinding.Admitted"/> means a published consumer binds the file, so the
/// files the character sheet resolves are enumerated as bound and the references are rewritten with that
/// fact rather than every reference staying pending beside its own published artifact.
/// </remarks>
public sealed class CharacterPresentationGroup
{
    private CharacterPresentationGroup(
        CharacterMediaInventory characters,
        CharacterMediaPassResult pass,
        CharacterMediaReferenceSet references,
        DaggerfallCharacterPresentation presentation,
        IReadOnlyList<string> undocumented)
    {
        Characters = characters;
        Pass = pass;
        References = references;
        Presentation = presentation;
        Undocumented = undocumented;
    }

    public CharacterMediaInventory Characters { get; }

    public CharacterMediaPassResult Pass { get; }

    /// <summary>The references with the character sheet's bindings applied, which is what the index states.</summary>
    public CharacterMediaReferenceSet References { get; }

    public DaggerfallCharacterPresentation Presentation { get; }

    /// <summary>Corpus files the documented inventory does not name; they are published and reported.</summary>
    public IReadOnlyList<string> Undocumented { get; }

    /// <summary>
    /// Publishes the supplied character media and builds the presentation section from it.
    /// </summary>
    /// <param name="sources">The corpus files of the documented character families, by file name.</param>
    /// <param name="palettes">The corpus palettes, by file name.</param>
    /// <param name="inventoryCsv">The documented inventory, checked against the corpus in both directions.</param>
    /// <param name="importedPayloadJson">The imported payload whose catalogs name the races and careers.</param>
    /// <param name="corpusLabel">The corpus directory name the inventory's source records cite.</param>
    public static CharacterPresentationGroup Create(
        IReadOnlyList<(string Path, ReadOnlyMemory<byte> Bytes)> sources,
        IReadOnlyDictionary<string, Arena2Palette> palettes,
        ReadOnlySpan<byte> inventoryCsv,
        string importedPayloadJson,
        string corpusLabel)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(palettes);
        // A documented file the corpus lacks is a source gap, and a corpus file the inventory does not
        // document is one this publication would emit without a record.
        string[] undocumented = [.. CharacterMediaPublisher.ReconcileDocumentedInventory(inventoryCsv, sources.Select(entry => entry.Path))];
        (IReadOnlyList<DaggerfallRaceKey> races, IReadOnlyDictionary<string, string> careers) = CatalogKeys(importedPayloadJson);

        // The publication pass runs first over the corpus as it stands, so the artifacts exist before any
        // reference claims one. The consumer is then derived from the pack's own identity: the character
        // sheet resolves the race the player's actor declares and a portrait per career, so those files are
        // the ones a published consumer binds and every other file stays required-pending with its artifact
        // written and indexed.
        CharacterMediaInventory unbound = CharacterMediaInventory.Enumerate(sources, new HashSet<string>(StringComparer.Ordinal), corpusLabel);
        IReadOnlySet<string> suppliedPalettes = palettes.Keys.ToHashSet(StringComparer.Ordinal);
        Dictionary<string, ReadOnlyMemory<byte>> corpus = sources.ToDictionary(entry => entry.Path, entry => entry.Bytes, StringComparer.Ordinal);
        CharacterMediaReferenceSet derived = CharacterMediaReferences.Derive(unbound, suppliedPalettes, corpus);
        CharacterMediaPassResult pass = CharacterMediaPublisher.PublishAll(derived, corpus, palettes, unbound);
        IReadOnlySet<string> bound = CharacterMediaReferences.FilesBoundByCharacterSheet(unbound);
        // The references already state the palette each file is painted in - the derivation reads a
        // container's own - so the rewrite only has to state the binding.
        CharacterMediaReferenceSet referenced = CharacterMediaReferences.WithBoundFiles(derived, bound, CharacterMediaReferences.CharacterSheetConsumer);

        // A reference a consumer binds has to resolve to a published canvas: binding a source whose artifact
        // the pass could not emit would publish a reference to nothing. A source whose format nothing here
        // reads is the same failure, so both halves are checked rather than only the enumerated canvases.
        string[] unboundSources = [.. referenced.Canvases
            .Where(canvas => canvas.Binding == MediaBinding.Admitted && !pass.PublishedMediaIds.Contains(canvas.MediaId))
            .Select(canvas => Path.GetFileName(canvas.Path))
            .Concat(referenced.Unavailable
                .Where(file => file.Binding == MediaBinding.Admitted)
                .Select(file => Path.GetFileName(file.Path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        if (unboundSources.Length != 0)
        {
            throw new InvalidOperationException(
                $"{CharacterMediaReferences.CharacterSheetConsumer} binds {unboundSources.Length} supplied file(s) this pass published no canvas from: {string.Join(", ", unboundSources)}.");
        }

        CharacterMediaInventory characters = CharacterMediaInventory.Enumerate(sources, bound, CharacterMediaReferences.CharacterSheetConsumer, corpusLabel);
        DaggerfallCharacterPresentation presentation = DaggerfallCharacterPresentationBuilder.Build(characters, suppliedPalettes, races, careers, pass.UnpublishableFiles, corpus);
        presentation.Validate(pass.PublishedMediaIds);
        return new(characters, pass, referenced, presentation, undocumented);
    }

    /// <summary>The races the catalogs publish, which the layers are keyed by, and each career's name, which names its portrait.</summary>
    private static (IReadOnlyList<DaggerfallRaceKey> Races, IReadOnlyDictionary<string, string> Careers) CatalogKeys(string importedPayloadJson)
    {
        JsonNode catalogs = JsonNode.Parse(importedPayloadJson)!["catalogs"]
            ?? throw new InvalidOperationException("The imported payload carries no catalogs section, which names the races and careers: run the catalogs command first.");
        DaggerfallRaceKey[] races = [.. catalogs["races"]!.AsArray().Select(value =>
        {
            JsonObject race = value!.AsObject();
            JsonObject source = race["source"]!.AsObject();
            return new DaggerfallRaceKey(
                race["id"]!.GetValue<string>(),
                race["donorRaceId"]!.GetValue<int>(),
                new DaggerfallCatalogSource(source["recordId"]!.GetValue<string>(), source["path"]!.GetValue<string>()));
        })];
        Dictionary<string, string> careers = catalogs["careers"]!.AsArray().ToDictionary(
            value => value!["id"]!.GetValue<string>(),
            value => value!["name"]!.GetValue<string>(),
            StringComparer.Ordinal);
        return (races, careers);
    }

    /// <summary>
    /// The files already under the character media directory that this group's index does not name. A
    /// rebuild writes what it published and does not delete what an earlier run published, so a file whose
    /// identity changed would otherwise sit in the tree unindexed while the index looked complete.
    /// </summary>
    public IEnumerable<string> StaleFiles(string characterDirectory, string indexFileName)
    {
        if (!Directory.Exists(characterDirectory)) yield break;
        HashSet<string> published = [.. Pass.Artifacts.Select(artifact => Path.GetFileName(artifact.RelativePath)), indexFileName];
        foreach (string path in Directory.EnumerateFiles(characterDirectory).Order(StringComparer.Ordinal))
        {
            if (!published.Contains(Path.GetFileName(path))) yield return Path.GetFileName(path);
        }
    }
}
