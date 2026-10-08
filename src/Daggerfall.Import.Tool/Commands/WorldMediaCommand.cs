using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the product-wide world media every site closure references: the meshes, material textures,
/// billboard and actor atlases, terrain textures, classic sidecar, audio clips and world visuals, once,
/// beside the site closures rather than copied into each of them.
/// </summary>
/// <remarks>
/// It reads the imported payload for the actors and flats the runtime may materialize anywhere and the
/// published music manifest for the cues every site names, so it runs after both; the classic media group is
/// read so the classic images it already publishes are referenced rather than published again. The site
/// commands then reference this publication with <c>--shared</c>.
/// </remarks>
internal static class WorldMediaCommand
{
    public static ToolCommand Command { get; } = new("world-media",
        [Options.Arena2, SiteInputs.Output, AuthoredUi.Manifest, AuthoredUi.Originals, Options.Inventory, Options.Pack,
            SiteInputs.SourceManifest, SiteInputs.MusicManifest, SiteInputs.ClassicGroup], Run);

    private static int Run(CommandArguments args)
    {
        Arena2WorldMediaPublication publication = Arena2SitePublication.WorldMedia(
            Arena2SiteSources.ForWorldMedia(Path.GetFullPath(args[Options.Arena2.Name])),
            SiteInputs.Media(args, AuthoredUi.Profile(args, required: true)),
            ProductWorldMedia.ReadClassicGroup(args[SiteInputs.ClassicGroup.Name]));
        ImportPublicationPlan plan = publication.Plan.WithInvocation(SiteInputs.Invocation(args));
        SiteInputs.PrintComparison(ImportPublicationWriter.Write(plan, Path.GetFullPath(args[SiteInputs.Output.Name])));
        SiteInputs.WriteSourceManifest(args, plan);
        Arena2WorldTextureSelection textures = publication.Textures;
        Console.WriteLine($"world media: {plan.Artifacts.Count} artifacts, {plan.Artifacts.Sum(artifact => (long)artifact.Bytes.Length)} bytes");
        Console.WriteLine($"  textures: {textures.Materials.Count} material records and {textures.Billboards.Count} billboard records from {textures.Archives.Count} leaves");
        foreach (IGrouping<string, Arena2WorldTextureRefusal> form in textures.Refused.GroupBy(refusal => refusal.Form).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {form.Count()} records carry no {form.Key}, for example {string.Join("; ", form.Take(3).Select(refusal => $"{refusal.Archive}/{refusal.Record}: {refusal.Reason}"))}");
        }

        Console.WriteLine($"  meshes: {publication.Geometry.Published} published of {publication.Geometry.Records} archive records, {publication.Geometry.Unresolvable} unresolvable, {publication.Geometry.Duplicate} duplicate numbers");
        return 0;
    }
}
