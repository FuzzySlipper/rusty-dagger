using System.Text.RegularExpressions;
using Xunit;

namespace WorldRpg.Architecture.Tests;

/// <summary>
/// The ruleset's domain order as a law: a type may name types in its own folder and in folders before it in the order,
/// never in a folder after it. The order is authored once, as the numbered list in the ruleset's AGENTS.md, and read
/// from there; the references the breakup left against it are <see cref="Exceptions"/>, each with its reason.
/// </summary>
public sealed class DomainOrderLawTests
{
    private static readonly string Ruleset = Path.Combine(TestData.RepositoryRoot, "src", "WorldRpg.Rulesets.Daggerfall");
    private static readonly string OrderFile = Path.Combine(Ruleset, "AGENTS.md");

    /// <summary>References against the order that stay by decision, by the type that makes them.</summary>
    private static readonly AcceptedReference[] Exceptions =
    [
        new("DaggerfallNpcServiceFacts",
            ["DaggerfallConcreteGuildCatalog", "DaggerfallConcreteGuildDefinition", "DaggerfallConcreteGuildService",
             "DaggerfallConcreteGuildServiceDefinition", "DaggerfallConcreteGuildServiceRuntime"],
            "Who an NPC is and serves reads Society's guild and service definitions, and Actors, Interiors, Economy, Quests "
            + "and Content's static placement reader all ask it, before Society; the catalog also holds membership, rank "
            + "and invitation policy, which is Society's (#10793)."),
        new("DaggerfallBaseContent", ["DaggerfallControlSettings"],
            "The help reader checks each {action} a page names against the controls catalog while the base content is "
            + "read, and about 80 callers read the base content (#10793)."),
        new("DaggerfallStatModifiers", ["DaggerfallPlayerVitals"],
            "RefreshPlayerDerivedMaxima forwards to the vitals' refresh so Character, before Condition, can refresh the "
            + "maxima a stat change moves without naming Condition (#10793)."),
    ];

    [Fact]
    public void Ruleset_types_name_no_type_in_a_folder_after_their_own()
    {
        IReadOnlyList<string> order = Order();
        Dictionary<string, int> rank = order.Select((folder, index) => (folder, index)).ToDictionary(item => item.folder, item => item.index);
        Declarations declarations = Declare();
        Assert.True(declarations.Smeared.Count == 0,
            "Ownership violation: a type is declared in more than one folder, so it has no one owner: "
            + string.Join("; ", declarations.Smeared));

        List<string> violations = [];
        HashSet<(string From, string To)> excused = [];
        foreach (Reference reference in References(declarations.Folders))
        {
            if (!rank.TryGetValue(reference.FromFolder, out int from) || !rank.TryGetValue(reference.ToFolder, out int to) || to <= from)
                continue;
            if (Exceptions.Any(exception => exception.From == reference.From && exception.To.Contains(reference.To)))
            {
                excused.Add((reference.From, reference.To));
                continue;
            }
            violations.Add($"{reference.From} ({reference.FromFolder}/) names {reference.To} ({reference.ToFolder}/)");
        }
        string[] stale = [.. Exceptions.SelectMany(exception => exception.To
            .Where(to => !excused.Contains((exception.From, to)))
            .Select(to => $"{exception.From} no longer names {to}"))];

        Assert.True(violations.Count == 0,
            "Ownership violation: a type names a type in a folder after its own in the order of "
            + "src/WorldRpg.Rulesets.Daggerfall/AGENTS.md, so the later domain's rules leak into the earlier one. Pass what it "
            + "needs as an input, raise a typed fact, declare an interface beside it that a later owner implements, or move "
            + "the type to the folder of the domain it serves:\n" + string.Join("\n", violations.Distinct()));
        Assert.True(stale.Length == 0,
            "An exception in DomainOrderLawTests.Exceptions is no longer needed; remove it: " + string.Join("; ", stale));
    }

    [Fact]
    public void Every_ruleset_folder_has_a_place_in_the_order()
    {
        IReadOnlyList<string> order = Order();
        string[] folders = [.. Directory.GetDirectories(Ruleset).Select(Path.GetFileName).OfType<string>()
            .Where(folder => folder is not ("bin" or "obj"))];

        string[] unplaced = [.. folders.Except(order)];
        string[] missing = [.. order.Except(folders)];
        Assert.True(unplaced.Length == 0,
            "A ruleset folder has no place in the order, so nothing checks what it names or what names it; add it to the "
            + "list in src/WorldRpg.Rulesets.Daggerfall/AGENTS.md: " + string.Join(", ", unplaced));
        Assert.True(missing.Length == 0,
            "src/WorldRpg.Rulesets.Daggerfall/AGENTS.md orders a folder the ruleset does not have: " + string.Join(", ", missing));
    }

    /// <summary>The folders in order: every backticked name of the numbered list that follows the order's bullet.</summary>
    private static IReadOnlyList<string> Order()
    {
        string[] lines = File.ReadAllLines(OrderFile);
        int start = Array.FindIndex(lines, line => line.Contains("References point down this order", StringComparison.Ordinal));
        Assert.True(start >= 0, $"{OrderFile} no longer has the bullet 'References point down this order' the law reads.");
        List<string> order = [];
        for (int index = start + 1; index < lines.Length; index++)
        {
            Match item = Regex.Match(lines[index], @"^\s+\d+\.\s+(.*)$");
            if (!item.Success) break;
            order.AddRange(Regex.Matches(item.Groups[1].Value, "`([A-Za-z]+)`").Select(name => name.Groups[1].Value));
        }
        Assert.True(order.Count > 0, $"{OrderFile} has no numbered list after its order bullet.");
        return order;
    }

    /// <summary>Which folder declares each top-level type (file-scoped namespaces put them at the start of a line).</summary>
    private static Declarations Declare()
    {
        Dictionary<string, HashSet<string>> folders = [];
        foreach (string file in SourceFiles())
            foreach (Match declaration in TopLevelDeclaration.Matches(ArchitectureLawTests.ExecutableText(File.ReadAllText(file))))
            {
                string name = declaration.Groups["name"].Value;
                if (!folders.TryGetValue(name, out HashSet<string>? declared)) folders[name] = declared = [];
                declared.Add(FolderOf(file));
            }
        return new(
            folders.Where(pair => pair.Value.Count == 1).ToDictionary(pair => pair.Key, pair => pair.Value.Single()),
            [.. folders.Where(pair => pair.Value.Count > 1).Select(pair => $"{pair.Key} in {string.Join(" and ", pair.Value.Order())}")]);
    }

    /// <summary>Every name of a type from another folder, with the top-level type of this file whose text it is in.</summary>
    private static IEnumerable<Reference> References(IReadOnlyDictionary<string, string> folders)
    {
        foreach (string file in SourceFiles())
        {
            string folder = FolderOf(file);
            string text = ArchitectureLawTests.ExecutableText(File.ReadAllText(file));
            Match[] declarations = [.. TopLevelDeclaration.Matches(text).Cast<Match>()];
            if (declarations.Length == 0) continue;
            foreach (Match name in Regex.Matches(text, @"\b[A-Z][A-Za-z0-9_]*\b"))
            {
                if (!folders.TryGetValue(name.Value, out string? declared) || declared == folder) continue;
                Match within = declarations.LastOrDefault(declaration => declaration.Index <= name.Index) ?? declarations[0];
                yield return new(within.Groups["name"].Value, folder, name.Value, declared);
            }
        }
    }

    private static readonly Regex TopLevelDeclaration = new(
        @"^(?:(?:public|internal|sealed|static|abstract|partial|readonly|file|ref)\s+)*"
        + @"(?:(?:class|struct|interface|enum|record(?:\s+class|\s+struct)?)\s+|delegate\s+[\w<>\[\],.?\s]+?\s+)(?<name>[A-Za-z_]\w*)\b",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static string FolderOf(string file) => Path.GetRelativePath(Ruleset, file).Split(Path.DirectorySeparatorChar)[0];

    private static IEnumerable<string> SourceFiles() => Directory.GetFiles(Ruleset, "*.cs", SearchOption.AllDirectories)
        .Where(file => !Path.GetRelativePath(Ruleset, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private sealed record AcceptedReference(string From, string[] To, string Reason);

    private sealed record Declarations(IReadOnlyDictionary<string, string> Folders, IReadOnlyList<string> Smeared);

    private sealed record Reference(string From, string FromFolder, string To, string ToFolder);
}
