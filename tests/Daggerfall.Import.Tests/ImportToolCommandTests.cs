using Daggerfall.Import.Tool;
using Xunit;
using ToolProgram = Daggerfall.Import.Tool.Program;

namespace Daggerfall.Import.Tests;

[CollectionDefinition("Import tool console", DisableParallelization = true)]
public sealed class ImportToolConsoleCollection;

internal sealed class ImportToolDataTheoryAttribute : TheoryAttribute
{
    public ImportToolDataTheoryAttribute() => Skip = ImportToolDataFactAttribute.MissingInputs();
}

internal sealed class ImportToolDataFactAttribute : FactAttribute
{
    public ImportToolDataFactAttribute() => Skip = MissingInputs();
    internal static string? MissingInputs() => TestData.MissingGeneratedContent() ?? TestData.MissingCorpus([])
        ?? TestData.MissingDonor([]) ?? (Directory.Exists(Path.Combine(TestData.RepositoryRoot, "local/Sound"))
            ? null : "Operator music folder local/Sound is absent; supply the donor songs to exercise publication.");
}

/// <summary>Exercises the actual compiled dispatch, parser, commands and file publications.</summary>
[Collection("Import tool console")]
public sealed class ImportToolCommandTests
{
    public static IEnumerable<object[]> Commands => ToolProgram.Commands.Keys.Select(name => new object[] { name });
    public static IEnumerable<object[]> UpdateCommands => ToolProgram.Commands.Values
        .Where(command => command.Options.Any(option => option.Name == "--update"))
        .Select(command => new object[] { command.Name });

    [Theory]
    [MemberData(nameof(Commands))]
    public void Every_command_refuses_missing_unknown_repeated_and_valueless_options_with_its_usage(string name)
    {
        ToolCommand command = ToolProgram.Commands[name];
        string[] valid = [name, .. command.Options.Where(option => option.IsRequired)
            .SelectMany(option => new[] { option.Name, "value" })];
        Assert.NotNull(CommandArguments.Parse(valid, command));
        Refuses([.. valid, "--unknown"]);
        foreach (CommandOption option in command.Options)
        {
            string[] without = [name, .. command.Options.Where(other => other.IsRequired && other != option)
                .SelectMany(other => new[] { other.Name, "value" })];
            if (option.IsRequired) Refuses(without);
            string[] one = option.IsSwitch ? [option.Name] : [option.Name, "value"];
            Refuses([.. without, .. one, .. one]);
            if (!option.IsSwitch)
            {
                Refuses([.. without, option.Name]);
                Refuses([.. without, option.Name, ""]);
                Refuses([.. without, option.Name, "--unknown"]);
            }
        }
        Assert.Equal(1, Run([name]).Code);
        Assert.Contains(command.Usage, Run([name]).Error, StringComparison.Ordinal);

        void Refuses(string[] args)
        {
            Assert.Equal(command.Usage, Assert.Throws<ArgumentException>(() => command.Invoke(args)).Message);
            var result = Run(args);
            Assert.Equal(1, result.Code);
            Assert.Equal($"daggerfall-import-tool: {command.Usage}{Environment.NewLine}", result.Error);
            Assert.Empty(result.Output);
        }
    }

    [Fact]
    public void Missing_or_unknown_verbs_exit_one_with_top_level_usage()
    {
        foreach (string[] args in new[] { Array.Empty<string>(), new[] { "unknown" } })
        {
            var result = Run(args);
            Assert.Equal(1, result.Code);
            Assert.StartsWith("daggerfall-import-tool: usage:", result.Error, StringComparison.Ordinal);
            Assert.Empty(result.Output);
        }
    }

    [Theory]
    [InlineData("cinematic-media", "--kind", "unknown")]
    [InlineData("rmb-spatial", "--profile", "unknown")]
    [InlineData("write", "--region", "-1")]
    [InlineData("music-media", "--require-all", "")]
    public void Invalid_command_choices_exit_one_with_usage_before_any_publication(string name, string option, string value)
    {
        var command = ToolProgram.Commands[name];
        List<string> args = [name];
        foreach (var required in command.Options.Where(entry => entry.IsRequired && entry.Name != option))
            args.AddRange([required.Name, "unused"]);
        args.Add(option);
        if (value.Length > 0) args.Add(value);
        var result = Run([.. args]);
        Assert.Equal(1, result.Code);
        Assert.Contains(command.Usage, result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    [ImportToolDataTheory]
    [MemberData(nameof(UpdateCommands))]
    public void Report_writes_nothing_and_update_writes_exact_builder_output(string name)
    {
        using ImportToolFixture fixture = new();
        string[] args = fixture.Arguments(name);
        var expected = fixture.Expected(name);
        Assert.NotEmpty(expected);
        var before = fixture.Snapshot();
        var report = Run(args);
        Assert.True(report.Code == 0, report.Error);
        Assert.NotEmpty(report.Output);
        fixture.AssertUnchanged(before);

        var updated = Run([.. args, "--update"]);
        Assert.True(updated.Code == 0, updated.Error);
        fixture.AssertPublication(before, expected);
    }

    [ImportToolDataFact]
    public void Text_report_guard_and_publication_write_are_observable() =>
        Report_writes_nothing_and_update_writes_exact_builder_output("text");

    [ImportToolDataTheory]
    [InlineData("mobile-ledger")]
    [InlineData("monster-archive")]
    [InlineData("quest-sources")]
    [InlineData("texture-leaves")]
    [InlineData("residual-paths")]
    [InlineData("map-art")]
    [InlineData("sprite-list")]
    [InlineData("sprite-show")]
    [InlineData("sprite-overlay-validate")]
    [InlineData("plan")]
    [InlineData("verify-real-data")]
    public void Report_only_verbs_leave_all_supplied_outputs_untouched(string name)
    {
        using ImportToolFixture fixture = new();
        string[] args = fixture.Arguments(name);
        var before = fixture.Snapshot();
        var result = Run(args);
        Assert.True(result.Code == 0, result.Error);
        Assert.NotEmpty(result.Output);
        if (name == "verify-real-data")
            Assert.Contains($"verified deterministic publication ({fixture.Expected("write").Count - 1} artifacts)", result.Output, StringComparison.Ordinal);
        fixture.AssertUnchanged(before);
    }

    [ImportToolDataTheory]
    [InlineData("map-art")]
    [InlineData("quest-sources")]
    [InlineData("texture-leaves")]
    public void Report_inventory_reconciliation_is_executed_without_writing(string name)
    {
        using ImportToolFixture fixture = new();
        var before = fixture.Snapshot();
        var result = Run([.. fixture.Arguments(name), "--inventory", fixture.InventoryFile]);
        Assert.True(result.Code == 0, result.Error);
        Assert.Contains("documented", result.Output, StringComparison.Ordinal);
        fixture.AssertUnchanged(before);
    }

    [ImportToolDataFact]
    public void Flc_report_writes_nothing_and_update_publishes_the_selected_kind()
    {
        using ImportToolFixture fixture = new() { CinematicKind = Daggerfall.Import.Normalized.DaggerfallCinematicKind.Flc };
        var expected = fixture.Expected("cinematic-media");
        var before = fixture.Snapshot();
        var report = Run(fixture.Arguments("cinematic-media"));
        Assert.True(report.Code == 0, report.Error);
        fixture.AssertUnchanged(before);
        var update = Run([.. fixture.Arguments("cinematic-media"), "--update"]);
        Assert.True(update.Code == 0, update.Error);
        fixture.AssertPublication(before, expected);
    }

    [ImportToolDataFact]
    public void Interior_publication_uses_the_selected_real_building()
    {
        using ImportToolFixture fixture = new() { Interior = true };
        var expected = fixture.Expected("rmb-spatial");
        var before = fixture.Snapshot();
        var result = Run(fixture.Arguments("rmb-spatial"));
        Assert.True(result.Code == 0, result.Error);
        fixture.AssertPublication(before, expected);
    }

    [ImportToolDataFact]
    public void Source_manifest_reports_inventory_drift_then_updates_only_when_requested()
    {
        using ImportToolFixture fixture = new();
        fixture.PrepareInventoryDrift();
        byte[] original = File.ReadAllBytes(fixture.InventoryFile);
        var report = Run(fixture.Arguments("source-manifest"));
        Assert.True(report.Code == 0, report.Error);
        Assert.Contains("disagreements to record", report.Error, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(fixture.InventoryFile));
        var expected = fixture.Expected("source-manifest");
        expected.Add(fixture.InventoryFile, fixture.ExpectedReconciledInventory());
        var before = fixture.Snapshot();
        var updated = Run([.. fixture.Arguments("source-manifest"), "--update-inventory"]);
        Assert.True(updated.Code == 0, updated.Error);
        Assert.Contains("dispositions updated", updated.Error, StringComparison.Ordinal);
        fixture.AssertPublication(before, expected);
    }

    [ImportToolDataFact]
    public void Source_manifest_refused_inventory_update_exits_one_and_preserves_the_inventory()
    {
        using ImportToolFixture fixture = new();
        fixture.PrepareInventoryDrift();
        File.AppendAllText(fixture.InventoryFile,
            "CNT-001.file.NEVER-PRESENT,file,CNT-001,source-file,arena2/NEVER-PRESENT-8279*.ZZZ,0,,,test,uninspected,unmatched pattern\n");
        var expected = fixture.Expected("source-manifest");
        var before = fixture.Snapshot();
        var refused = Run([.. fixture.Arguments("source-manifest"), "--update-inventory"]);
        Assert.Equal(1, refused.Code);
        Assert.Contains("NOT rewritten", refused.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("dispositions updated", refused.Error, StringComparison.Ordinal);
        fixture.AssertPublication(before, expected);
    }

    [ImportToolDataTheory]
    [InlineData("fighters-quest-corpus")]
    [InlineData("classic-quest-corpora")]
    [InlineData("source-manifest")]
    [InlineData("source-coverage")]
    [InlineData("sprite-inspection")]
    [InlineData("sprite-overlay-write")]
    [InlineData("write")]
    [InlineData("rmb-spatial")]
    public void Explicit_writer_verbs_publish_exact_builder_output(string name)
    {
        using ImportToolFixture fixture = new();
        string[] args = fixture.Arguments(name);
        var expected = fixture.Expected(name);
        Assert.NotEmpty(expected);
        var before = fixture.Snapshot();
        var result = Run(args);
        Assert.True(result.Code == 0, result.Error);
        fixture.AssertPublication(before, expected);
    }

    [ImportToolDataFact]
    public void Overlay_discard_preserves_bytes_at_the_recovery_path_and_repeated_discard_is_quiet()
    {
        using ImportToolFixture fixture = new();
        string original = fixture.OverlayFile;
        byte[] bytes = File.ReadAllBytes(original);
        Assert.Equal(0, Run(fixture.Arguments("sprite-overlay-discard")).Code);
        Assert.False(File.Exists(original));
        Assert.Equal(bytes, File.ReadAllBytes(original + ".discarded"));
        var before = fixture.Snapshot();
        var again = Run(fixture.Arguments("sprite-overlay-discard"));
        Assert.Equal(0, again.Code);
        Assert.Contains("was not present", again.Output, StringComparison.Ordinal);
        fixture.AssertUnchanged(before);
    }

    internal static (int Code, string Output, string Error) Run(string[] args)
    {
        TextWriter output = Console.Out, error = Console.Error;
        using StringWriter capturedOutput = new(), capturedError = new();
        try
        {
            Console.SetOut(capturedOutput);
            Console.SetError(capturedError);
            int code = ToolProgram.Main(args);
            return (code, capturedOutput.ToString(), capturedError.ToString());
        }
        finally { Console.SetOut(output); Console.SetError(error); }
    }
}
