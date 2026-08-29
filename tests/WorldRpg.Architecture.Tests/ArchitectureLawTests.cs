using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace WorldRpg.Architecture.Tests;

public sealed class ArchitectureLawTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Kit_does_not_encode_reference_ruleset_vocabulary_or_references()
    {
        string kit = SourceDirectory("WorldRpg.Kit");
        string source = ReadSources(kit);

        foreach (string forbidden in new[] { "Daggerfall", "Arena2", "PrivateersHold", "DFUnity" })
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("WorldRpg.Rulesets.Daggerfall", ProjectFile("WorldRpg.Kit"), StringComparison.Ordinal);
    }

    [Fact]
    public void Canary_does_not_use_reference_ruleset_vocabulary_or_references()
    {
        string canary = SourceDirectory("WorldRpg.Rulesets.Canary.Tests");
        string source = ReadSources(canary);
        string project = ProjectFile("WorldRpg.Rulesets.Canary.Tests");

        foreach (string forbidden in new[] { "Daggerfall", "Arena2", "PrivateersHold", "DFUnity" })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, project, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("WorldRpg.Rulesets.Daggerfall", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_references_follow_the_worldrpg_dependency_graph()
    {
        AssertProjectReferences("WorldRpg.Kit", ["Rusty.Engine"]);
        AssertProjectReferences("WorldRpg.Rulesets.Daggerfall", ["Rusty.Engine", "WorldRpg.Kit"]);
        AssertProjectReferences("WorldRpg.Host", ["Rusty.Engine", "WorldRpg.Kit", "WorldRpg.Rulesets.Daggerfall"]);
        AssertProjectReferences("RustyDagger.NativeProduct", ["Rusty.Engine", "Rusty.Engine.ProductGenerator", "WorldRpg.Host"]);
        AssertProjectReferences("WorldRpg.Rulesets.Canary.Tests", ["WorldRpg.Host", "WorldRpg.Kit"]);
    }

    [Fact]
    public void Host_concrete_ruleset_references_stay_at_builtin_composition_seams()
    {
        string host = SourceDirectory("WorldRpg.Host");
        string[] concreteReferences = SourceFiles(host)
            .Where(path => File.ReadAllText(path).Contains("WorldRpg.Rulesets.Daggerfall", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["BuiltInRulesets.cs"], concreteReferences);
    }

    [Fact]
    public void Active_runtime_projects_reject_implicit_runtime_authorities()
    {
        (string Label, string Pattern)[] projectAndSourceForbidden =
        [
            ("reflection discovery", @"\b(System\.Reflection|Assembly\.(Load|GetAssemblies)|Type\.GetType|\.GetTypes\s*\()"),
            ("service locator", @"\b(IServiceProvider|ServiceProvider|GetRequiredService\s*\(|GetService\s*\()"),
            ("generic command dispatch", @"\b(ICommandBus|CommandBus|CommandDispatcher|DispatchCommand\s*\(|GenericCommand)"),
            ("runtime C# compilation", @"\b(CSharpCompilation|CodeDom|Microsoft\.CodeAnalysis|Roslyn)"),
            ("parallel update loop", @"\b(new\s+Thread\s*\(|Task\.Run\s*\(|PeriodicTimer\s*\(|System\.Threading\.Timer|System\.Timers\.Timer|while\s*\(\s*true\s*\))"),
        ];
        (string Label, string Pattern)[] safeCodeBoundaryEscapes =
        [
            ("unsafe code", @"\bunsafe\b"),
            ("handwritten native interop", @"\b(DllImport|LibraryImport|GCHandle|Native[A-Z]\w*)"),
        ];

        foreach (string project in ActiveRuntimeProjects())
        {
            foreach (string file in SourceFiles(SourceDirectory(project)))
            {
                string source = File.ReadAllText(file);
                AssertNoForbiddenPatterns(file, source, projectAndSourceForbidden);
                AssertNoForbiddenPatterns(file, source, safeCodeBoundaryEscapes);
            }

            string projectFile = ProjectFile(project);
            AssertNoForbiddenPatterns(projectFile, File.ReadAllText(projectFile), projectAndSourceForbidden);
        }
    }

    private static IEnumerable<string> ActiveRuntimeProjects() =>
    ["WorldRpg.Kit", "WorldRpg.Rulesets.Daggerfall", "WorldRpg.Host", "RustyDagger.NativeProduct"];

    private static void AssertProjectReferences(string project, IReadOnlyList<string> expected)
    {
        XDocument document = XDocument.Load(ProjectFile(project));
        string[] actual = document.Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .OfType<string>()
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => ProjectReferenceName(include))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
    }

    private static string ProjectReferenceName(string include)
    {
        string fileName = Path.GetFileNameWithoutExtension(include);
        return fileName;
    }

    private static string ProjectFile(string project) => project.EndsWith(".Tests", StringComparison.Ordinal)
        ? Path.Combine(RepositoryRoot, "tests", project, $"{project}.csproj")
        : Path.Combine(RepositoryRoot, "src", project, $"{project}.csproj");

    private static string SourceDirectory(string project) => project == "RustyDagger.NativeProduct"
        ? Path.Combine(RepositoryRoot, "src", project)
        : project.EndsWith(".Tests", StringComparison.Ordinal)
            ? Path.Combine(RepositoryRoot, "tests", project)
            : Path.Combine(RepositoryRoot, "src", project);

    private static string ReadSources(string path) => string.Join(
        Environment.NewLine,
        SourceFiles(path).Select(File.ReadAllText));

    private static void AssertNoForbiddenPatterns(string file, string source, IEnumerable<(string Label, string Pattern)> patterns)
    {
        foreach ((string label, string pattern) in patterns)
        {
            Assert.False(
                Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant),
                $"{file}: contains forbidden {label} pattern.");
        }
    }

    private static IEnumerable<string> SourceFiles(string path) => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
        .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
