using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>Publishes the donor MapsFile regionRaces table used by classic %ef building-name expansion.</summary>
internal static class BuildingNameInputsCommand
{
    public static ToolCommand Command { get; } = new("building-name-inputs",
        [CommandOption.Required("--maps-file", "MapsFile.cs"), CommandOption.Required("--label", "LOGICAL_PATH"), Options.Pack, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        DaggerfallBuildingNameInputs inputs = DaggerfallBuildingNameInputsBuilder.Build(File.ReadAllBytes(args["--maps-file"]), args["--label"]);
        Console.WriteLine($"building-name inputs: {inputs.RegionNameBanks.Count} regions from {inputs.Source.Path}");
        if (!args.Switch("--update")) return Options.ReportOnly("building-name inputs");
        PayloadFiles.WriteSection(args["--pack"], "buildingNames", inputs);
        return 0;
    }
}
