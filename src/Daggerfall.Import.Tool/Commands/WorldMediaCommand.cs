using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the product-wide world media every site draws from: the meshes, material textures, billboard
/// and actor atlases, terrain textures, classic sidecar, audio clips and world visuals, once.
/// </summary>
/// <remarks>
/// It reads the imported payload for the actors and flats the runtime may materialize anywhere, and for the
/// locations whose placed blocks it trims where placements overlap, so it runs after the payload's sections;
/// the classic media group is read so the classic images it already publishes are referenced rather than
/// published again. The world-blocks command then checks its placed meshes against this publication with
/// <c>--shared</c>.
/// </remarks>
internal static class WorldMediaCommand
{
    public static ToolCommand Command { get; } = new("world-media",
        [Options.Arena2, CommandOption.Staged(PublicationInputs.Output.Name, "OUTPUT_DIR", ImportLayout.WorldMedia), AuthoredUi.Manifest, AuthoredUi.Originals, Options.Pack,
            PublicationInputs.ClassicGroup], Run);

    private static int Run(CommandArguments args)
    {
        Arena2SiteSources sources = Arena2SiteSources.ForWorldMedia(Path.GetFullPath(args[Options.Arena2.Name]));
        Arena2WorldMediaPublication publication = Arena2SitePublication.WorldMedia(
            sources,
            PublicationInputs.Media(sources, AuthoredUi.Profile(args, required: true)),
            ClassicMediaGroup.ReadInventory(args[PublicationInputs.ClassicGroup.Name]),
            PayloadFiles.ReadSection<DaggerfallLocations>(args[Options.Pack.Name], "locations"));
        ImportPublicationPlan plan = publication.Plan.WithInvocation(PublicationInputs.Invocation(args));
        PublicationInputs.PrintComparison(ImportPublicationWriter.Write(plan, Path.GetFullPath(args[PublicationInputs.Output.Name])));
        Arena2WorldTextureSelection textures = publication.Textures;
        Console.WriteLine($"world media: {plan.Artifacts.Count} artifacts, {plan.Artifacts.Sum(artifact => (long)artifact.Bytes.Length)} bytes");
        Console.WriteLine($"  textures: {textures.Materials.Count} material records and {textures.Billboards.Count} billboard records from {textures.Archives.Count} leaves");
        foreach (IGrouping<string, Arena2WorldTextureRefusal> form in textures.Refused.GroupBy(refusal => refusal.Form).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {form.Count()} records carry no {form.Key}, for example {string.Join("; ", form.Take(3).Select(refusal => $"{refusal.Archive}/{refusal.Record}: {refusal.Reason}"))}");
        }

        Console.WriteLine($"  meshes: {publication.Geometry.Published} published of {publication.Geometry.Records} archive records, {publication.Geometry.Unresolvable} unresolvable, {publication.Geometry.Duplicate} duplicate numbers");
        Console.WriteLine($"  {publication.PlacementMeshes} block placements trimmed where an earlier placement covers their faces");
        return 0;
    }
}
