using System.Diagnostics;
using System.Text.Json;
using Daggerfall.Import.Tool;
using Daggerfall.Import.Tool.Commands;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The importer revision (<c>src/ImporterRevision.targets</c>) is what says whether a data container is current. An
/// input it misses lets a stale container read as current, which nothing downstream would notice; an input it
/// needlessly holds makes every machine's data stale on an edit that cannot change it. These run the real target.
/// </summary>
public sealed class ImporterRevisionTests
{
    private static string Root => TestData.RepositoryRoot;

    [Fact]
    public void An_edit_the_import_can_read_changes_the_revision_and_any_other_edit_leaves_it()
    {
        string tree = Path.Combine(Path.GetTempPath(), "importer-revision-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tree, "src", "Probe"));
            File.Copy(Path.Combine(Root, "src", "ImporterRevision.targets"), Path.Combine(tree, "src", "ImporterRevision.targets"));
            File.WriteAllText(Path.Combine(tree, "src", "Probe", "Probe.proj"), """
                <Project>
                  <PropertyGroup>
                    <ImporterRevisionNamespace>Probe</ImporterRevisionNamespace>
                    <ImporterRevisionClass>Probe</ImporterRevisionClass>
                    <IntermediateOutputPath>obj/</IntermediateOutputPath>
                  </PropertyGroup>
                  <Import Project="../ImporterRevision.targets" />
                </Project>
                """);
            string[] read =
            [
                "src/Daggerfall.Import/Normalization/RmbExteriorNormalizer.cs",
                "src/Daggerfall.Import/Publication/WorldBlocksPublication.cs",
                "src/Daggerfall.Import.Tool/Commands/ImportCommand.cs",
                "src/Daggerfall.Import.Tool/Daggerfall.Import.Tool.csproj",
                "data/content-source-manifest.csv",
                "data/ui-authored-assets.json",
                "data/daggerfall-unity/Assets/Resources/ItemTemplates.txt",
                "content/worldrpg/packs/daggerfall.art-replacements/authored-ui/ui-stone-title-v2.png",
                "content/worldrpg/packs/daggerfall.base/payload.json",
            ];
            string[] unread =
            [
                "src/Daggerfall.Source.Tool/ReferenceExports/ItemReferenceExport.cs",
                "src/Daggerfall.Source.Tool/Commands/MapArtCommand.cs",
                "src/Daggerfall.Source.Tool/Program.cs",
                "data/README.md",
                "content/worldrpg/packs/daggerfall.art-replacements/payload.json",
                "content/worldrpg/packs/daggerfall.art-replacements/media/ui/screens--title.webp",
                "content/worldrpg/packs/daggerfall.art-replacements/first-person/daggerfall.held.json",
                "content/worldrpg/packs/daggerfall.base/pack.json",
                "src/WorldRpg.Rulesets.Daggerfall/DaggerfallRuleset.cs",
                "src/Daggerfall.Import/obj/Debug/net10.0/ImporterRevision.g.cs",
                "src/Daggerfall.Import.Tool/bin/Debug/net10.0/Daggerfall.Import.Tool.dll",
            ];
            foreach (string path in read.Concat(unread)) Write(tree, path, "original");
            string baseline = Revision(Path.Combine(tree, "src", "Probe", "Probe.proj"));
            Assert.Matches("^[0-9a-f]{64}$", baseline);

            foreach (string path in unread)
            {
                Write(tree, path, "edited");
                Assert.True(baseline == Revision(Path.Combine(tree, "src", "Probe", "Probe.proj")), $"editing '{path}' changed the importer revision");
                Write(tree, path, "original");
            }

            foreach (string path in read)
            {
                Write(tree, path, "edited");
                Assert.True(baseline != Revision(Path.Combine(tree, "src", "Probe", "Probe.proj")), $"editing '{path}' left the importer revision unchanged");
                Write(tree, path, "original");
            }

            // A Windows checkout of the same commit names the same revision.
            Write(tree, read[0], "original\r\n");
            string lf = Revision(Path.Combine(tree, "src", "Probe", "Probe.proj"));
            Write(tree, read[0], "original\n");
            Assert.Equal(lf, Revision(Path.Combine(tree, "src", "Probe", "Probe.proj")));
        }
        finally
        {
            if (Directory.Exists(tree)) Directory.Delete(tree, recursive: true);
        }
    }

    [Fact]
    public void Every_source_file_the_import_tool_compiles_is_a_revision_input()
    {
        HashSet<string> inputs = RevisionInputs();
        List<string> outside = [];
        HashSet<string> projects = new(StringComparer.Ordinal);
        Queue<string> pending = new([Path.Combine(Root, "src", "Daggerfall.Import.Tool", "Daggerfall.Import.Tool.csproj")]);
        while (pending.TryDequeue(out string? project))
        {
            if (!projects.Add(Path.GetFullPath(project))) continue;
            if (!inputs.Contains(Path.GetFullPath(project))) outside.Add(Relative(project));
            JsonElement items = Evaluate(project, "-getItem:Compile", "-getItem:ProjectReference").GetProperty("Items");
            foreach (JsonElement item in items.GetProperty("Compile").EnumerateArray())
            {
                string file = item.GetProperty("FullPath").GetString()!;
                if (!inputs.Contains(file)) outside.Add(Relative(file));
            }

            foreach (JsonElement reference in items.GetProperty("ProjectReference").EnumerateArray())
                pending.Enqueue(reference.GetProperty("FullPath").GetString()!);
        }

        Assert.True(outside.Count == 0, $"the import tool compiles files outside the importer revision: {string.Join(", ", outside)}");
        Assert.Equal(["Daggerfall.Import", "Daggerfall.Import.Tool"], projects.Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_repository_path_an_import_step_reads_is_a_revision_input()
    {
        HashSet<string> inputs = RevisionInputs();
        List<string> outside = [];
        HashSet<string> checkedPaths = new(StringComparer.Ordinal);
        foreach (ImportCommand.Step step in ImportCommand.Steps(new("local/inputs/arena2", "local/inputs/Sound", null)))
        {
            ToolCommand command = Program.Commands[step.Arguments[0]];
            // A staged option the step leaves out reads its default, so the default is checked too.
            IEnumerable<string> defaults = command.Options
                .Where(option => option.Default is not null && !step.Arguments.Contains(option.Name))
                .Select(option => option.Default!);
            foreach (string value in step.Arguments.Skip(1).Concat(defaults))
            {
                // local/ holds the operator's own files and what the import writes. source-coverage reads the whole
                // repository only to reconcile the inventory: it writes its report to the records, never the container.
                if (value.StartsWith("local/", StringComparison.Ordinal) || (value == "." && step.Label == "source-coverage")) continue;
                string full = Path.GetFullPath(Path.Combine(Root, value));
                IEnumerable<string> files = File.Exists(full) ? [full]
                    : Directory.Exists(full) ? Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                    : [];
                foreach (string file in files)
                {
                    checkedPaths.Add(Relative(file));
                    if (!inputs.Contains(file)) outside.Add($"{Relative(file)} (step '{step.Label}')");
                }
            }
        }

        Assert.True(outside.Count == 0, $"import steps read repository files outside the importer revision: {string.Join(", ", outside.Distinct().Take(20))}");
        Assert.Contains("content/worldrpg/packs/daggerfall.base/payload.json", checkedPaths);
        Assert.Contains("data/content-source-manifest.csv", checkedPaths);
        Assert.Contains("data/ui-authored-assets.json", checkedPaths);
        Assert.Contains(checkedPaths, path => path.StartsWith(AuthoredUi.ProductDirectory + "/", StringComparison.Ordinal));
        Assert.Contains(checkedPaths, path => path.StartsWith(ImportCommand.Donor + "/Assets/StreamingAssets/Quests/", StringComparison.Ordinal));
    }

    private static void Write(string tree, string path, string text)
    {
        string full = Path.Combine(tree, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    /// <summary>The files the build hashes, as <c>scripts/data-sync.sh</c> asks for the revision itself.</summary>
    private static HashSet<string> RevisionInputs() =>
        [.. Evaluate(Path.Combine(Root, "src", "Daggerfall.Import", "Daggerfall.Import.csproj"), "-t:StampImporterRevision", "-getItem:_ImporterRevisionInput")
            .GetProperty("Items").GetProperty("_ImporterRevisionInput").EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!)];

    private static string Revision(string project) => MsBuild(project, "-t:StampImporterRevision", "-getProperty:_ImporterRevision").Trim();

    private static JsonElement Evaluate(string project, params string[] arguments) => JsonDocument.Parse(MsBuild(project, arguments)).RootElement.Clone();

    private static string MsBuild(string project, params string[] arguments)
    {
        ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { "msbuild", project, "-nologo" }.Concat(arguments)) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("dotnet could not be started.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"dotnet msbuild {project} {string.Join(' ', arguments)} failed: {output}{error.Result}");
        return output;
    }
}
