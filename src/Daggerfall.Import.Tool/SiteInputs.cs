using System.Globalization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

/// <summary>The options and inputs every site closure command shares.</summary>
internal static class SiteInputs
{
    public static readonly CommandOption Region = CommandOption.Required("--region", "0..999");
    public static readonly CommandOption Location = CommandOption.Required("--location", "NAME");
    public static readonly CommandOption Output = CommandOption.Required("--out", "OUTPUT_DIR");
    public static readonly CommandOption SourceManifest = CommandOption.Required("--source-manifest", "FILE");
    public static readonly CommandOption Shared = CommandOption.Staged("--shared", "WORLD_MEDIA_DIR", ImportLayout.WorldMedia);
    public static readonly CommandOption ClassicGroup = CommandOption.Staged("--classic-group", "CLASSIC_GROUP_DIR", ImportLayout.ClassicGroup);

    /// <summary>The options every site closure takes, before its own.</summary>
    public static IReadOnlyList<CommandOption> Common =>
    [
        Options.Arena2, Output, Region, Location, AuthoredUi.Manifest, AuthoredUi.Originals,
        Options.Inventory, Options.Pack, SourceManifest, Shared, ClassicGroup,
    ];

    /// <summary>
    /// The product-wide media a site closure references: the world media publication the world-media command
    /// wrote, the classic media group the classic-media command wrote, and the tracked authored UI art the
    /// group lists.
    /// </summary>
    public static ProductWorldMedia ProductMedia(CommandArguments args) =>
        ProductWorldMedia.Read(args[Shared.Name], args[ClassicGroup.Name], args[AuthoredUi.Originals.Name]);

    /// <summary>
    /// The site closure as it is written: what is its own, with every shared body, entry and classic sidecar
    /// referenced from the product-wide media instead of copied.
    /// </summary>
    public static ImportPublicationPlan Reference(CommandArguments args, ImportPublicationPlan complete)
    {
        ImportPublicationPlan plan = ProductMedia(args).Partition(complete);
        Console.WriteLine($"site closure: {plan.Artifacts.Count} of {complete.Artifacts.Count} artifacts written, the rest referenced from the product-wide media");
        return plan;
    }

    public static int ParseRegion(CommandArguments args) =>
        int.TryParse(args[Region.Name], NumberStyles.None, CultureInfo.InvariantCulture, out int region) && region is >= 0 and <= 999
            ? region
            : throw args.Invalid("--region must be an integer in 0..999.");

    public static string ParseLocation(CommandArguments args) =>
        string.IsNullOrWhiteSpace(args[Location.Name]) ? throw args.Invalid("--location must be non-empty.") : args[Location.Name];

    /// <summary>
    /// What the site publishes beyond its spatial selection: the actors the imported mobile catalog says the
    /// runtime may spawn, the authored UI art and any dungeon sprite overlays.
    /// </summary>
    public static Arena2SiteMedia Media(CommandArguments args, Arena2ClassicMediaProfile classicMedia, IReadOnlyList<AuthoredMediaOverlay>? dungeonOverlays = null)
    {
        string imported = PayloadFiles.ReadGeneratedText(args[Options.Pack.Name]);
        return new(Arena2SitePublication.RuntimeActorResources(imported), classicMedia, dungeonOverlays ?? [])
        {
            RuntimeNpcResources = Arena2SitePublication.RuntimeNpcResources(imported),
            RuntimeNatureResources = Arena2SitePublication.RuntimeNatureResources(),
            RuntimeTerrainResources = Arena2SitePublication.RuntimeTerrainResources(),
        };
    }

    /// <summary>
    /// The command line a publication manifest records: the tool's own arguments as the caller spelled them.
    /// A plan or a determinism check describes the publication a write produces, so it is recorded under the
    /// write verb and a plan of an unchanged tree compares equal to the tree.
    /// </summary>
    public static ImportInvocation Invocation(CommandArguments args, IReadOnlyList<PublishedSource>? authoredOverlays = null) => new(
        ["daggerfall-import-tool", .. args.Raw.Select((argument, index) => index == 0 && argument is "plan" or "verify-real-data" ? "write" : argument)],
        authoredOverlays ?? []);

    /// <summary>
    /// Writes the site's source manifest to the import records: the corpus scanned against the documented
    /// inventory, with every source the closure read recorded as imported. Nothing at runtime reads it, so it
    /// stays outside the runtime content root; the closure's recorded command names where it is.
    /// </summary>
    public static void WriteSourceManifest(CommandArguments args, ImportPublicationPlan plan)
    {
        string inventoryFile = args[Options.Inventory.Name];
        SourceManifest manifest = SourceManifestPublication.ForPublication(
            plan.Manifest.Sources.Select(source => source.Path), args[Options.Arena2.Name], Path.GetFileName(inventoryFile), File.ReadAllBytes(inventoryFile));
        // A site's subset cannot reconcile the product-wide inventory. The final source-coverage
        // command compares that inventory with all published content and offline records together.
        PayloadFiles.WriteFile(args[SourceManifest.Name], SourceManifestSerializer.Serialize(manifest));
        Console.WriteLine($"source manifest: {manifest.Records.Count} records written to {args[SourceManifest.Name]}");
    }

    /// <summary>Prints what a plan comparison found, naming the artifacts: a count alone cannot say what moved.</summary>
    public static void PrintComparison(ImportPublicationComparison comparison)
    {
        if (comparison.IsNoOp)
        {
            Console.WriteLine("publication is current");
            return;
        }

        Console.WriteLine($"publication changes: missing={comparison.Missing.Count}, changed={comparison.Changed.Count}, unexpected={comparison.Unexpected.Count}");
        foreach ((string kind, IReadOnlyList<string> paths) in new[] { ("missing", comparison.Missing), ("changed", comparison.Changed), ("unexpected", comparison.Unexpected) })
        {
            foreach (string path in paths.Take(20)) Console.WriteLine($"  {kind}: {path}");
            if (paths.Count > 20) Console.WriteLine($"  {kind}: ... and {paths.Count - 20} more");
        }
    }
}
