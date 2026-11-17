using Daggerfall.Import.Tool;
using Xunit;
using ImportProgram = Daggerfall.Import.Tool.Program;
using SourceProgram = Daggerfall.Source.Tool.Program;

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

    /// <summary>The same, for a command that converts cinematics and so needs FFmpeg as well.</summary>
    public bool Cinematics
    {
        get => field;
        set { field = value; Skip = value ? MissingInputs() ?? TestData.MissingFfmpeg() : MissingInputs(); }
    }
    internal static string? MissingInputs() => TestData.MissingGeneratedContent() ?? MissingStaging() ?? TestData.MissingCorpus([]);

    // The commands read publications from disk, so these tests read the staging tree an import leaves beside the
    // containers; a machine given only the containers and the records has no staging tree to give them.
    private static string? MissingStaging() => Directory.Exists(ImportedContent.Staged(""))
        ? null
        : $"the import's staging tree (local/build/import/staging) is absent; run `{TestData.RegenerationCommand}` with local/inputs/arena2 supplied.";
}

/// <summary>Both tools' verbs, each run through the tool that owns it.</summary>
internal static class ToolPrograms
{
    internal static readonly IReadOnlyDictionary<string, ToolCommand> Commands =
        ImportProgram.Commands.Concat(SourceProgram.Commands).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    internal static int Dispatch(string[] args) => args.Length != 0 && SourceProgram.Commands.ContainsKey(args[0])
        ? SourceProgram.Main(args)
        : ImportProgram.Main(args);
}

/// <summary>Exercises the actual compiled dispatch, parser, commands and file publications.</summary>
[Collection("Import tool console")]
public sealed class ImportToolCommandTests
{
    public static IEnumerable<object[]> Commands => ToolPrograms.Commands.Keys.Select(name => new object[] { name });
    // The relief takes minutes over the real map, three times here: ReliefBakeTests covers the bake on a small one.
    public static IEnumerable<object[]> UpdateCommands => ToolPrograms.Commands.Values
        // The relief bake and the atlas drawn from it run on the whole map; AtlasBakeTests and ReliefBakeTests cover them.
        .Where(command => command.Options.Any(option => option.Name == "--update") && command.Name is not ("relief" or "atlas"))
        .Select(command => new object[] { command.Name });

    [Theory]
    [MemberData(nameof(Commands))]
    public void Every_command_refuses_missing_unknown_repeated_and_valueless_options_with_its_usage(string name)
    {
        ToolCommand command = ToolPrograms.Commands[name];
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
        // Reference exports have useful defaults and intentionally run with only the verb. Do not write
        // the operator's default export directories just to test the shared argument grammar.
        if (command.Options.Any(option => option.IsRequired))
        {
            Assert.Equal(1, Run([name]).Code);
            Assert.Contains(command.Usage, Run([name]).Error, StringComparison.Ordinal);
        }

        void Refuses(string[] args)
        {
            Assert.Equal(command.Usage, Assert.Throws<ArgumentException>(() => command.Invoke(args)).Message);
            var result = Run(args);
            Assert.Equal(1, result.Code);
            Assert.Equal($"{command.Tool}: {command.Usage}{Environment.NewLine}", result.Error);
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
    public void Invalid_command_choices_exit_one_with_usage_before_any_publication(string name, string option, string value)
    {
        var command = ToolPrograms.Commands[name];
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

    [ImportToolDataFact(Cinematics = true)]
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

    internal static (int Code, string Output, string Error) Run(string[] args)
    {
        TextWriter output = Console.Out, error = Console.Error;
        using StringWriter capturedOutput = new(), capturedError = new();
        try
        {
            Console.SetOut(capturedOutput);
            Console.SetError(capturedError);
            int code = ToolPrograms.Dispatch(args);
            return (code, capturedOutput.ToString(), capturedError.ToString());
        }
        finally { Console.SetOut(output); Console.SetError(error); }
    }
}
