using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the product-wide classic media group into a content root, so the artifacts are admitted content
/// a consumer reads by name rather than bytes that only exist inside this process, with the generated
/// inventory that indexes them.
/// </summary>
internal static class ClassicMediaCommand
{
    public static ToolCommand Command { get; } = new("classic-media",
        [Options.Arena2, Options.ContentRoot, Options.Group, CommandOption.Optional("--ui-authored-assets", "FILE"), CommandOption.Optional("--ui-original", "DIR"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        string group = args["--group"];
        // Paths are content-root relative, which is the naming the product's admitted content carries: the
        // group is part of the path, so a consumer holds one name for an artifact and the inventory uses it.
        string outRoot = Path.Combine(args["--out"], group);
        Console.WriteLine($"group: {group} under {args["--out"]}");
        // The authored UI art is part of the same group the DOM reads, so its identity, bytes and inventory
        // entry come from the one publication rather than a copy staged beside the UI.
        ClassicMediaGroup media = ClassicMediaGroup.Create(Arena2SiteSources.ForClassicMedia(arena2).ClassicMediaInputs, AuthoredUi.Profile(args, required: false));
        DaggerfallSoundCatalog catalog = media.SoundCatalog;
        int admitted = catalog.Clips.Count(clip => clip.Disposition == DaggerfallSoundClipDisposition.Admitted);
        int readable = catalog.Clips.Count(clip => clip.Disposition == DaggerfallSoundClipDisposition.ReadableNoConsumer);
        Console.WriteLine($"classic media: {media.Publication.Artifacts.Count} artifacts, {media.Publication.Sources.Count} sources, {media.Publication.UiImages.Count} UI images");
        Console.WriteLine($"sound catalog: {catalog.Clips.Count} clips, {admitted} admitted, {readable} readable with no consumer, {catalog.Clips.Count - admitted - readable} unsupported");
        if (!args.Switch("--update")) return Options.ReportOnly("these artifacts");

        PayloadFiles.WriteArtifacts(outRoot, media.Artifacts);
        PayloadFiles.WriteFile(
            Path.Combine(outRoot, ClassicMediaGroup.InventoryRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            media.WriteInventory(group, Directory.EnumerateFiles(arena2).Select(path => Path.GetFileName(path))));
        Console.WriteLine($"content: {media.Artifacts.Count} artifacts and their inventory written under {outRoot}");
        return 0;
    }
}
