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
        // Prose is not the boundary: Kit must not name reference-ruleset vocabulary in what it carries —
        // code or a hardcoded string — but a comment explaining that a mechanism is generic is not a
        // violation, and must not fail the build.
        string source = WithoutComments(ReadSources(kit));

        foreach (string forbidden in new[] { "Daggerfall", "Arena2", "PrivateersHold", "DFUnity" })
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("WorldRpg.Rulesets.Daggerfall", ProjectFile("WorldRpg.Kit"), StringComparison.Ordinal);
    }

    [Fact]
    public void Canary_does_not_use_reference_ruleset_vocabulary_or_references()
    {
        string canary = SourceDirectory("WorldRpg.Rulesets.Canary.Tests");
        string source = WithoutComments(ReadSources(canary));
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
        AssertProjectReferences("Daggerfall.Import", []);
        AssertProjectReferences("WorldRpg.Kit", []);
        AssertProjectReferences("WorldRpg.Rulesets.Daggerfall", ["WorldRpg.Kit"]);
        AssertProjectReferences("WorldRpg.Host", ["WorldRpg.Kit", "WorldRpg.Rulesets.Daggerfall"]);
        AssertProjectReferences("WorldRpg.Rulesets.Canary.Tests", ["WorldRpg.Host", "WorldRpg.Kit"]);

        foreach (string project in ActiveRuntimeProjects()) AssertPackageReference(project, "Rusty.Engine");
        AssertPackageReference("WorldRpg.SpriteWorkbench", "Rusty.Engine");
    }

    [Fact]
    public void Offline_importer_is_not_a_runtime_dependency()
    {
        XDocument importer = XDocument.Load(ProjectFile("Daggerfall.Import"));
        Assert.Empty(importer.Descendants("PackageReference"));

        foreach (string project in ActiveRuntimeProjects())
            Assert.DoesNotContain("Daggerfall.Import", File.ReadAllText(ProjectFile(project)), StringComparison.Ordinal);
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

        // The boundary is that concrete Daggerfall references live only in the Host's ruleset composition
        // seam, which the seam's own file name declares. Renaming that file is not a violation; naming a
        // concrete ruleset anywhere else is.
        Assert.NotEmpty(concreteReferences);
        Assert.All(concreteReferences, name => Assert.EndsWith("Rulesets.cs", name, StringComparison.Ordinal));
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
                string source = ExecutableText(File.ReadAllText(file));
                AssertNoForbiddenPatterns(file, source, projectAndSourceForbidden);
                AssertNoForbiddenPatterns(file, source, safeCodeBoundaryEscapes);
            }

            string projectFile = ProjectFile(project);
            AssertNoForbiddenPatterns(projectFile, File.ReadAllText(projectFile), projectAndSourceForbidden);
        }
    }

    /// <summary>
    /// A source file's executable text: comments and literals removed, so these scans flag a construct the
    /// code uses rather than one a comment or a user-facing message merely mentions. Text inside an
    /// interpolation hole is kept, because that text still runs.
    /// </summary>
    internal static string ExecutableText(string source) => Stripped(source, stripLiterals: true);

    /// <summary>
    /// A source file's text without comments, keeping literals: for a law about the vocabulary a project
    /// may name, where a hardcoded reference-ruleset string is as much a violation as a type reference,
    /// while a comment explaining the boundary is not.
    /// </summary>
    internal static string WithoutComments(string source) => Stripped(source, stripLiterals: false);

    private static string Stripped(string source, bool stripLiterals)
    {
        System.Text.StringBuilder text = new(source.Length);
        for (int index = 0; index < source.Length; index++)
        {
            char value = source[index];
            if (value == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\n') index++;
                text.Append('\n');
            }
            else if (value == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < source.Length && !(source[index] == '*' && source[index + 1] == '/')) index++;
                index++;
            }
            else if (!stripLiterals)
            {
                text.Append(value);
            }
            else if (value is '"' or '\'' || (value == '$' && index + 1 < source.Length && source[index + 1] == '"'))
            {
                bool interpolated = value == '$';
                if (interpolated) index++;
                char quote = source[index];
                index++;
                while (index < source.Length && source[index] != quote)
                {
                    if (source[index] == '\\') index++;
                    else if (interpolated && source[index] == '{' && index + 1 < source.Length && source[index + 1] != '{')
                    {
                        int depth = 0;
                        while (index < source.Length)
                        {
                            if (source[index] == '{') depth++;
                            else if (source[index] == '}')
                            {
                                depth--;
                                if (depth == 0) { text.Append(' '); break; }
                            }
                            else text.Append(source[index]);
                            index++;
                        }
                    }
                    index++;
                }
            }
            else text.Append(value);
        }
        return text.ToString();
    }

    [Fact]
    public void Forbidden_construct_scans_read_code_rather_than_prose_or_messages()
    {
        const string source = "// Assembly.Load( named in prose\nvar message = \"while (true) loops\";\n/* DllImport mention */\nvar real = Assembly.Load();\nvar hole = $\"x {Assembly.Load()}\";\n";

        string text = ExecutableText(source);

        Assert.DoesNotContain("named in prose", text, StringComparison.Ordinal);
        Assert.DoesNotContain("while (true)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DllImport mention", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split("Assembly.Load(").Length - 1);
        // The vocabulary law strips comments but keeps literals, so a hardcoded reference-ruleset string
        // is still a violation while the comment above it is not.
        Assert.DoesNotContain("named in prose", WithoutComments(source), StringComparison.Ordinal);
        Assert.Contains("Daggerfall", WithoutComments("// Daggerfall\nvar key = \"Daggerfall\";"), StringComparison.Ordinal);
        Assert.DoesNotContain("Daggerfall", ExecutableText("// Daggerfall\nvar key = \"Daggerfall\";"), StringComparison.Ordinal);
    }

    private static IEnumerable<string> ActiveRuntimeProjects() =>
    ["WorldRpg.Kit", "WorldRpg.Rulesets.Daggerfall", "WorldRpg.Host"];

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

    private static void AssertPackageReference(string project, string expected)
    {
        XDocument document = XDocument.Load(ProjectFile(project));
        string[] actual = document.Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .OfType<string>()
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .OrderBy(include => include, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(expected, actual);
        Assert.DoesNotContain(document.Descendants("ProjectReference"), reference =>
            ((string?)reference.Attribute("Include"))?.Contains("rusty-engine", StringComparison.OrdinalIgnoreCase) == true);

        Assert.Equal(
            "$(RustyEnginePackageVersion)",
            document.Descendants("PackageReference")
                .Single(reference => (string?)reference.Attribute("Include") == expected)
                .Attribute("Version")?.Value);
    }

    private static string ProjectFile(string project) => project.EndsWith(".Tests", StringComparison.Ordinal)
        ? Path.Combine(RepositoryRoot, "tests", project, $"{project}.csproj")
        : Path.Combine(RepositoryRoot, "src", project, $"{project}.csproj");

    private static string SourceDirectory(string project) => project.EndsWith(".Tests", StringComparison.Ordinal)
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
