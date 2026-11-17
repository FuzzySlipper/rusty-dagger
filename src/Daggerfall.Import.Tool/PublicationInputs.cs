using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

/// <summary>The options and inputs the world media and per-block world publications share.</summary>
internal static class PublicationInputs
{
    public static readonly CommandOption Output = CommandOption.Required("--out", "OUTPUT_DIR");
    public static readonly CommandOption Shared = CommandOption.Staged("--shared", "WORLD_MEDIA_DIR", ImportLayout.WorldMedia);
    public static readonly CommandOption ClassicGroup = CommandOption.Staged("--classic-group", "CLASSIC_GROUP_DIR", ImportLayout.ClassicGroup);

    /// <summary>
    /// What the world media publishes beyond the meshes and texture leaves: every mobile's sprite the runtime may spawn,
    /// the people, nature and terrain it may materialize anywhere, and the authored UI art.
    /// </summary>
    public static Arena2SiteMedia Media(Arena2SiteSources sources, Arena2ClassicMediaProfile classicMedia)
    {
        return new(Arena2SitePublication.RuntimeActorResources(), classicMedia)
        {
            RuntimeNpcResources = Arena2SitePublication.RuntimeNpcResources(sources),
            RuntimeNatureResources = Arena2SitePublication.RuntimeNatureResources(),
            RuntimeTerrainResources = Arena2SitePublication.RuntimeTerrainResources(),
        };
    }

    /// <summary>The command line a publication manifest records: the tool's own arguments as the caller spelled them.</summary>
    public static ImportInvocation Invocation(CommandArguments args) => new(["daggerfall-import-tool", .. args.Raw]);

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
