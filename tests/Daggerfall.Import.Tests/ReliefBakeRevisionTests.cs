using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Daggerfall.Import.Relief;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The relief bake's revision (<c>StampReliefBakeRevision</c> in <c>src/ImporterRevision.targets</c>) is what says whether
/// a kept terrain bake is still the bake this importer would make. Bake code it misses would let an import reuse a bake
/// its code no longer makes, silently; an input it needlessly holds rebakes the terrain on an edit that cannot change it.
/// These run the real target.
/// </summary>
public sealed class ReliefBakeRevisionTests
{
    private static string Root => TestData.RepositoryRoot;

    [Fact]
    public void The_bake_compiles_from_its_revision_inputs_alone()
    {
        string[] inputs = BakeInputs();
        Assert.Contains(inputs, file => file.EndsWith("/Relief/ReliefBake.cs", StringComparison.Ordinal));
        string probe = Path.Combine(Path.GetTempPath(), "relief-bake-alone-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(probe);
            // The bake's revision inputs and nothing else: a call into importer code outside them fails this build, naming it.
            File.WriteAllText(Path.Combine(probe, "Probe.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                    <AssemblyName>Probe</AssemblyName>
                    <OutputPath>out/</OutputPath>
                    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                  </PropertyGroup>
                  <ItemGroup>
                {string.Concat(inputs.Where(file => file.EndsWith(".cs", StringComparison.Ordinal)).Select(file => $"    <Compile Include=\"{file}\" />\n"))}  </ItemGroup>
                </Project>
                """);
            ImporterRevisionTests.MsBuild(Path.Combine(probe, "Probe.csproj"), "-restore", "-t:Build", "-v:q");

            // Every bake type the importer builds is one the probe built, so none is declared outside the inputs.
            using PEReader pe = new(File.OpenRead(Path.Combine(probe, "out", "Probe.dll")));
            MetadataReader metadata = pe.GetMetadataReader();
            HashSet<string> alone = [.. metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
                .Where(type => type.GetDeclaringType().IsNil)
                .Select(type => metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name))];
            string[] bake = [.. typeof(ReliefBake).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(ReliefBake).Namespace && !type.IsNested)
                .Select(type => type.FullName!)];
            Assert.Contains(typeof(DaggerfallRelief).FullName!, bake);
            Assert.All(bake, type => Assert.True(alone.Contains(type), $"{type} is declared outside the relief bake's revision inputs"));
        }
        finally
        {
            if (Directory.Exists(probe)) Directory.Delete(probe, recursive: true);
        }
    }

    [Fact]
    public void An_edit_to_the_bake_changes_its_revision_and_any_other_importer_edit_leaves_it()
    {
        string tree = Path.Combine(Path.GetTempPath(), "relief-bake-revision-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tree, "src", "Probe"));
            File.Copy(Path.Combine(Root, "src", "ImporterRevision.targets"), Path.Combine(tree, "src", "ImporterRevision.targets"));
            string project = Path.Combine(tree, "src", "Probe", "Probe.proj");
            File.WriteAllText(project, """
                <Project>
                  <PropertyGroup>
                    <ImporterRevisionNamespace>Probe</ImporterRevisionNamespace>
                    <ImporterRevisionClass>Probe</ImporterRevisionClass>
                    <StampReliefBakeRevision>true</StampReliefBakeRevision>
                    <IntermediateOutputPath>obj/</IntermediateOutputPath>
                  </PropertyGroup>
                  <Import Project="../ImporterRevision.targets" />
                </Project>
                """);
            string[] bake = ["src/Daggerfall.Import/Relief/ReliefBake.cs", "src/Daggerfall.Import/Relief/ReliefRivers.cs"];
            string[] elsewhere =
            [
                // What the bake is baked from is recorded as data, so how the input is read is not the bake.
                "src/Daggerfall.Import/Normalization/ReliefInputs.cs",
                "src/Daggerfall.Import/Normalization/AtlasBake.cs",
                "src/Daggerfall.Import/ImporterBuild.cs",
                "src/Daggerfall.Import/Daggerfall.Import.csproj",
                "src/Daggerfall.Import.Tool/Commands/ReliefCommand.cs",
                "src/Daggerfall.Import.Tool/KeptRelief.cs",
                "src/Daggerfall.Import.Tool/inputs/content-source-manifest.csv",
                "src/Daggerfall.Import.Tool/inputs/relief/record.json",
            ];
            foreach (string path in bake.Concat(elsewhere)) Write(tree, path, "original");
            string baseline = Revision(project);
            Assert.Matches("^[0-9a-f]{64}$", baseline);

            foreach (string path in elsewhere)
            {
                Write(tree, path, "edited");
                Assert.True(baseline == Revision(project), $"editing '{path}' changed the relief bake's revision");
                Write(tree, path, "original");
            }

            foreach (string path in bake)
            {
                Write(tree, path, "edited");
                Assert.True(baseline != Revision(project), $"editing '{path}' left the relief bake's revision unchanged");
                Write(tree, path, "original");
            }

            Write(tree, "src/Daggerfall.Import/Relief/ReliefStages.cs", "new");
            Assert.True(baseline != Revision(project), "a new file beside the bake left its revision unchanged");
        }
        finally
        {
            if (Directory.Exists(tree)) Directory.Delete(tree, recursive: true);
        }
    }

    [Fact]
    public void The_stamped_revision_is_the_one_the_target_computes()
    {
        string computed = ImporterRevisionTests.MsBuild(Path.Combine(Root, "src", "Daggerfall.Import", "Daggerfall.Import.csproj"),
            "-t:StampReliefBakeRevision", "-getProperty:_ReliefBakeRevision").Trim();
        Assert.Equal(computed, ImporterBuild.ReliefBakeRevision);
        Assert.NotEqual(ImporterBuild.Revision, ImporterBuild.ReliefBakeRevision);
    }

    private static string[] BakeInputs() =>
        [.. ImporterRevisionTests.Evaluate(Path.Combine(Root, "src", "Daggerfall.Import", "Daggerfall.Import.csproj"), "-t:StampReliefBakeRevision", "-getItem:_ReliefBakeInput")
            .GetProperty("Items").GetProperty("_ReliefBakeInput").EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!.Replace('\\', '/'))];

    private static string Revision(string project) =>
        ImporterRevisionTests.MsBuild(project, "-t:StampReliefBakeRevision", "-getProperty:_ReliefBakeRevision").Trim();

    private static void Write(string tree, string path, string text)
    {
        string full = Path.Combine(tree, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }
}
