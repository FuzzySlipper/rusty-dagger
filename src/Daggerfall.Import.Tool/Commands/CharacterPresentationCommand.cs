using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the character media the corpus supplies and builds the presentation section from it, writing
/// the artifacts under the content root and the references into the imported payload when asked, so a
/// character or social consumer resolves a race's layers by identity instead of reconstructing file names.
/// </summary>
internal static class CharacterPresentationCommand
{
    public static ToolCommand Command { get; } = new("character-presentation",
        [Options.Arena2, Options.Inventory, Options.Pack, Options.ContentRoot, Options.Group, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        string packFile = args["--pack"];
        // The inventory's own family rule, not a prefix filter: a class portrait or story sprite is named by
        // its extension, and a prefix test alone drops those files silently.
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [.. Directory
            .EnumerateFiles(arena2)
            .Select(path => Path.GetFileName(path))
            .Where(CharacterMediaInventory.IsDocumentedFamily)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (name, (ReadOnlyMemory<byte>)File.ReadAllBytes(Path.Combine(arena2, name))))];
        Dictionary<string, Arena2Palette> palettes = Directory.EnumerateFiles(arena2, "*.COL")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => Path.GetFileName(path), path => PaletteDecoder.Decode(File.ReadAllBytes(path), $"arena2/{Path.GetFileName(path)}"), StringComparer.Ordinal);
        CharacterPresentationGroup group = CharacterPresentationGroup.Create(
            sources, palettes, File.ReadAllBytes(args["--inventory"]), PayloadFiles.ReadGeneratedText(packFile), Options.CorpusLabel(arena2));
        Report(group);
        if (!args.Switch("--update")) return Options.ReportOnly("these artifacts and references");

        // The group is part of the path the artifacts and their index are written under, which is the
        // naming the product's admitted content carries.
        string outRoot = Path.Combine(args["--out"], args["--group"]);
        string indexFile = Path.Combine(outRoot, CharacterMediaPublisher.IndexRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string characterRoot = Path.GetDirectoryName(indexFile)!;
        // This refuses rather than deleting, because the tree is the product's content and a file nothing
        // here wrote is not this command's to remove.
        string[] stale = [.. group.StaleFiles(characterRoot, Path.GetFileName(indexFile))];
        if (stale.Length != 0)
        {
            throw new InvalidOperationException(
                $"{stale.Length} file(s) under {characterRoot} are not artifacts this run publishes, so the delivered tree would carry content no index names: {string.Join(", ", stale)}. Remove them, or republish from the corpus that produced them.");
        }

        foreach (CharacterMediaArtifact artifact in group.Pass.Artifacts)
        {
            PayloadFiles.WriteFile(Path.Combine(outRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)), artifact.Bytes);
        }

        PayloadFiles.WriteFile(indexFile, CharacterMediaPublisher.WriteIndex(group.Pass, group.References, args["--group"]));
        Console.WriteLine($"content: {group.Pass.Artifacts.Count} character artifacts and their index written under {outRoot}");
        PayloadFiles.WriteSection(packFile, "characterPresentation", group.Presentation);
        return 0;
    }

    private static void Report(CharacterPresentationGroup group)
    {
        DaggerfallCharacterPresentation presentation = group.Presentation;
        CharacterMediaPassResult pass = group.Pass;
        // Every reference, not the layers and faces alone: a career portrait is bound too, and reporting only
        // part of the set understates what the pack claims a consumer draws.
        int pending = presentation.Layers.Count(layer => layer.Binding == MediaBinding.RequiredPending)
            + presentation.Faces.Count(face => face.Binding == MediaBinding.RequiredPending)
            + presentation.ChildFaces.Count(face => face.Binding == MediaBinding.RequiredPending)
            + presentation.Careers.Count(portrait => portrait.Binding == MediaBinding.RequiredPending);
        int admitted = presentation.Layers.Count + presentation.Faces.Count + presentation.ChildFaces.Count + presentation.Careers.Count - pending;
        string[] races = [.. presentation.Layers.Select(layer => layer.Race).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Console.WriteLine($"character presentation: {group.Characters.Files.Count} supplied files, {pass.Artifacts.Count} published canvases, {pass.Refusals.Count} refused, {presentation.Layers.Count} layers over {races.Length} races, {presentation.Faces.Count} faction faces, {presentation.ChildFaces.Count} child faces, {presentation.Careers.Count} career portraits, {presentation.CareersWithoutPortrait.Count} careers without one");
        Console.WriteLine($"  binding: {admitted} reference(s) bound by {CharacterMediaReferences.CharacterSheetConsumer}, {pending} required-pending; {pass.UnreadableFamilies.Count} unreadable family entry(ies)");
        foreach (string refusal in pass.Refusals.Take(2)) Console.WriteLine($"  refusal: {refusal}");
        Console.WriteLine($"  inventory: {group.Characters.Files.Count - group.Undocumented.Count} documented supplied file(s) reconciled with the corpus");
        foreach (string file in group.Undocumented) Console.WriteLine($"  warning: '{file}' is in the corpus and not in the documented inventory");
        foreach (string race in races) Console.WriteLine($"  {race}: {presentation.Layers.Count(layer => layer.Race == race)} layers");
        foreach (DaggerfallRaceWithoutMedia gap in presentation.RacesWithoutMedia.Take(4)) Console.WriteLine($"  gap {gap.Race}: {gap.Reason}");
    }
}
