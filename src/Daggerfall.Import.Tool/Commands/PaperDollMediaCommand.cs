using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the paper doll's worn-item images - every record an item template can reach, in every dye,
/// placed in the doll - and the <c>paperDoll</c> section the ruleset resolves a worn item through. It reads
/// the item template section, so it runs after the <c>items</c> command.
/// </summary>
internal static class PaperDollMediaCommand
{
    public static ToolCommand Command { get; } = new("paper-doll-media",
        [Options.Arena2, Options.Pack, Options.ContentRoot, Options.Group, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        string packFile = args["--pack"];
        Dictionary<int, ReadOnlyMemory<byte>> archives = [];
        foreach (int number in PaperDollItemPublication.Archives)
        {
            string path = Path.Combine(arena2, $"TEXTURE.{number:000}");
            if (File.Exists(path)) archives[number] = File.ReadAllBytes(path);
        }

        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(Path.Combine(arena2, CharacterMediaReferences.ArtPalette)), $"arena2/{CharacterMediaReferences.ArtPalette}");
        var templates = PaperDollItemPublication.ReadTemplates(PayloadFiles.ReadGenerated(packFile)["itemTemplates"]
            ?? throw new InvalidOperationException($"'{packFile}' carries no itemTemplates section: run the items command first."));
        PaperDollItemPass pass = PaperDollItemPublication.Publish(archives, palette, templates);
        Console.WriteLine($"paper doll: {templates.Count} worn-item templates reach {pass.Images.Count} records over {pass.Sources.Count} archives; {pass.Artifacts.Count} images, {pass.Images.Count(image => image.MaskMediaId is not null)} with a mask, {pass.Artifacts.Sum(artifact => (long)artifact.Bytes.Length) / 1024} KiB");
        if (!args.Switch("--update")) return Options.ReportOnly("these artifacts and the paperDoll section");

        string outRoot = Path.Combine(args["--out"], args["--group"]);
        string indexFile = Path.Combine(outRoot, PaperDollItemPublication.IndexRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string directory = Path.GetDirectoryName(indexFile)!;
        // This refuses rather than deleting, as the character media command does: a file nothing here wrote
        // is not this command's to remove.
        HashSet<string> published = [.. pass.Artifacts.Select(artifact => Path.GetFileName(artifact.RelativePath)), Path.GetFileName(indexFile)];
        string[] stale = Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory).Select(Path.GetFileName).Where(name => !published.Contains(name!)).Order(StringComparer.Ordinal)!] : [];
        if (stale.Length != 0)
            throw new InvalidOperationException($"{stale.Length} file(s) under {directory} are not artifacts this run publishes: {string.Join(", ", stale.Take(8))}. Remove them, or republish from the corpus that produced them.");

        foreach (PaperDollArtifact artifact in pass.Artifacts)
            PayloadFiles.WriteFile(Path.Combine(outRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)), artifact.Bytes);
        PayloadFiles.WriteFile(indexFile, PaperDollItemPublication.WriteIndex(pass, args["--group"]));
        PayloadFiles.WriteSections(packFile, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["paperDoll"] = PaperDollItemPublication.Section(pass).ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
        });
        return 0;
    }
}
