using System.Globalization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>Publishes one selected RMB exterior or building interior static-mesh/collision/navigation closure.</summary>
internal static class RmbSpatialCommand
{
    private static readonly CommandOption LocationIndex = CommandOption.Optional("--location-index", "INDEX");
    private static readonly CommandOption Profile = CommandOption.Required("--profile", "exterior|interior");
    private static readonly CommandOption BlockX = CommandOption.Optional("--block-x", "X");
    private static readonly CommandOption BlockY = CommandOption.Optional("--block-y", "Y");
    private static readonly CommandOption Building = CommandOption.Optional("--building", "INDEX");

    public static ToolCommand Command { get; } = new("rmb-spatial", [.. SiteInputs.Common, LocationIndex, Profile, BlockX, BlockY, Building], Run);

    private static int Run(CommandArguments args)
    {
        bool interior = args[Profile.Name] switch
        {
            "exterior" => false,
            "interior" => true,
            _ => throw args.Invalid("--profile must be exterior or interior."),
        };
        bool selectsBuilding = args.Has(BlockX.Name) || args.Has(BlockY.Name) || args.Has(Building.Name);
        if (selectsBuilding != interior) throw args.Invalid("--block-x, --block-y and --building select an interior's building and are required for it.");
        RmbBuildingSelection? building = null;
        if (interior)
        {
            if (!args.Has(BlockX.Name) || !args.Has(BlockY.Name) || !args.Has(Building.Name)
                || !byte.TryParse(args[BlockX.Name], NumberStyles.None, CultureInfo.InvariantCulture, out byte blockX)
                || !byte.TryParse(args[BlockY.Name], NumberStyles.None, CultureInfo.InvariantCulture, out byte blockY)
                || !int.TryParse(args[Building.Name], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                throw args.Invalid("--block-x, --block-y and --building must be non-negative integers.");
            }

            building = new(blockX, blockY, index);
        }

        int? locationIndex = null;
        if (args.Has(LocationIndex.Name))
            locationIndex = int.TryParse(args[LocationIndex.Name], NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index >= 0
                ? index : throw args.Invalid("--location-index must be non-negative.");
        (ImportPublicationPlan plan, RmbExteriorNormalizationResult result) = Arena2SitePublication.Rmb(
            Arena2SiteSources.ForSite(args[Options.Arena2.Name]),
            SiteInputs.ParseRegion(args),
            SiteInputs.ParseLocation(args),
            building,
            SiteInputs.Media(args, AuthoredUi.Profile(args, required: true)), locationIndex);
        plan = plan.WithInvocation(SiteInputs.Invocation(args));
        ImportPublicationWriter.Write(plan, Path.GetFullPath(args[SiteInputs.Output.Name]));
        SiteInputs.WriteSourceManifest(args, plan);
        Console.WriteLine($"rmb spatial: {result.Layout.LocationName} {args[Profile.Name]}, {result.Document.Meshes.Count} meshes, {result.SpatialPublication.Navigation.Cells.Count} navigation cells, {plan.Artifacts.Count} closure artifacts");
        return 0;
    }
}
