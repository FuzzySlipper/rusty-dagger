using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

/// <summary>The options and inputs the world media and per-block world publications share.</summary>
internal static class PublicationInputs
{
    public static readonly CommandOption Output = CommandOption.Required("--out", "OUTPUT_DIR");
    public static readonly CommandOption SourceManifest = CommandOption.Required("--source-manifest", "FILE");
    public static readonly CommandOption Shared = CommandOption.Staged("--shared", "WORLD_MEDIA_DIR", ImportLayout.WorldMedia);
    public static readonly CommandOption ClassicGroup = CommandOption.Staged("--classic-group", "CLASSIC_GROUP_DIR", ImportLayout.ClassicGroup);

    /// <summary>
    /// What the world media publishes beyond the meshes and texture leaves: the actors the imported mobile catalog
    /// says the runtime may spawn, the people, nature and terrain it may materialize anywhere, and the authored UI art.
    /// </summary>
    public static Arena2SiteMedia Media(CommandArguments args, Arena2ClassicMediaProfile classicMedia)
    {
        string imported = PayloadFiles.ReadGeneratedText(args[Options.Pack.Name]);
        return new(Arena2SitePublication.RuntimeActorResources(imported), classicMedia)
        {
            RuntimeNpcResources = Arena2SitePublication.RuntimeNpcResources(imported),
            RuntimeNatureResources = Arena2SitePublication.RuntimeNatureResources(),
            RuntimeTerrainResources = Arena2SitePublication.RuntimeTerrainResources(),
        };
    }

    /// <summary>The command line a publication manifest records: the tool's own arguments as the caller spelled them.</summary>
    public static ImportInvocation Invocation(CommandArguments args) => new(["daggerfall-import-tool", .. args.Raw]);

    /// <summary>
    /// Writes the publication's source manifest to the import records: the corpus scanned against the documented
    /// inventory, with every source the publication read recorded as imported. Nothing at runtime reads it, so it
    /// stays outside the runtime content root; the publication's recorded command names where it is.
    /// </summary>
    public static void WriteSourceManifest(CommandArguments args, ImportPublicationPlan plan)
    {
        string inventoryFile = args[Options.Inventory.Name];
        SourceManifest manifest = SourceManifestPublication.ForPublication(
            plan.Manifest.Sources.Select(source => source.Path), args[Options.Arena2.Name], Path.GetFileName(inventoryFile), File.ReadAllBytes(inventoryFile));
        // One publication's subset cannot reconcile the product-wide inventory. The final source-coverage
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
