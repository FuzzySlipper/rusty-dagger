using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes Daggerfall Unity's managed localization table into the existing shared text section. The table
/// supplements TEXT.RSC; it is a focused import so it does not need an Arena2 directory merely to update one
/// donor-owned CSV source.
/// </summary>
internal static class InternalStringsCommand
{
    public static ToolCommand Command { get; } = new("internal-strings",
        [CommandOption.Required("--source", "Internal_Strings.csv"), CommandOption.Required("--label", "LOGICAL_PATH"), Options.Pack, CommandOption.Required("--language", "LANG"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        DaggerfallText existing = PayloadFiles.ReadSection<DaggerfallText>(args["--pack"], "text");
        DaggerfallText merged = DaggerfallInternalStringsBuilder.Merge(existing, File.ReadAllBytes(args["--source"]), args["--label"], args["--language"]);
        Console.WriteLine($"internal strings: {merged.Records.Count(record => record.Key.Kind == DaggerfallTextKind.Internal)} records from {args["--label"]}");
        if (!args.Switch("--update")) return Options.ReportOnly("internal strings");
        PayloadFiles.WriteSection(args["--pack"], "text", merged);
        return 0;
    }
}
