namespace WorldRpg.Tests.Support;

/// <summary>
/// Where tests find the repository and the operator-supplied data a clean checkout does not carry:
/// the Arena2 corpus and the donor checkout. Each root can be relocated through its environment variable.
/// </summary>
internal static class TestData
{
    public const string CorpusVariable = "DAGGER_ARENA2";
    public const string DonorVariable = "DAGGER_DONOR_ROOT";

    private static readonly Lazy<string> FoundRoot = new(FindRepositoryRoot);

    /// <summary>The directory holding <c>AGENTS.md</c>, found by walking up from the test output.</summary>
    public static string RepositoryRoot => FoundRoot.Value;

    /// <summary>The Arena2 corpus: <c>DAGGER_ARENA2</c> when set, else <c>local/arena2</c> in the repository.</summary>
    public static string CorpusRoot => Configured(CorpusVariable) ?? Path.Combine(RepositoryRoot, "local", "arena2");

    /// <summary>The donor checkout: <c>DAGGER_DONOR_ROOT</c> when set, else the research checkout.</summary>
    public static string DonorRoot => Configured(DonorVariable) ?? "/home/research/daggerfall-unity";

    public static string Corpus(string relative) => Path.Combine(CorpusRoot, relative);

    public static string Donor(string relative) => Path.Combine(DonorRoot, relative);

    /// <summary>The one command that writes the generated content from the operator's Arena2 files.</summary>
    public const string RegenerationCommand = "scripts/regenerate-content.sh";

    private static readonly Lazy<string?> GeneratedContentGap = new(FindGeneratedContentGap);

    /// <summary>
    /// Why a test reading generated content cannot run, or null when every generated path is present.
    /// The paths are the ones <c>scripts/generated-content-paths.txt</c> lists, the same list the
    /// regeneration script removes and rewrites and <c>.gitignore</c> keeps out of the repository.
    /// </summary>
    internal static string? MissingGeneratedContent() => GeneratedContentGap.Value;

    /// <summary>Why a test needing these corpus entries cannot run, or null when they are all present.</summary>
    internal static string? MissingCorpus(string[] entries) => Missing("Arena2 corpus", CorpusRoot, CorpusVariable, entries);

    /// <summary>Why a test needing these donor entries cannot run, or null when they are all present.</summary>
    internal static string? MissingDonor(string[] entries) => Missing("donor checkout", DonorRoot, DonorVariable, entries);

    private static string? Missing(string label, string root, string variable, string[] entries)
    {
        if (!Directory.Exists(root)) return $"{label} not found at {root}; set {variable} to relocate it.";

        string[] missing = [.. entries.Where(entry => !File.Exists(Path.Combine(root, entry)) && !Directory.Exists(Path.Combine(root, entry)))];
        return missing.Length == 0
            ? null
            : $"{label} at {root} lacks {string.Join(", ", missing)}; set {variable} to relocate it.";
    }

    private static string? FindGeneratedContentGap()
    {
        string list = Path.Combine(RepositoryRoot, "scripts", "generated-content-paths.txt");
        List<string> missing = [];
        foreach (string line in File.ReadLines(list))
        {
            string entry = line.Trim();
            if (entry.Length == 0 || entry.StartsWith('#')) continue;
            string relative = entry.TrimStart('/');
            string path = Path.Combine(RepositoryRoot, relative.TrimEnd('/'));
            bool present = relative.Contains('*', StringComparison.Ordinal)
                ? Directory.Exists(Path.GetDirectoryName(path)) && Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!, Path.GetFileName(path)).Any()
                : relative.EndsWith('/') ? Directory.Exists(path) : File.Exists(path);
            if (!present) missing.Add(relative);
        }

        return missing.Count == 0
            ? null
            : $"generated content is absent ({string.Join(", ", missing)}); run {RegenerationCommand} with local/arena2 supplied.";
    }

    private static string? Configured(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : null;

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        }

        throw new DirectoryNotFoundException($"No directory above {AppContext.BaseDirectory} holds AGENTS.md, so tests cannot locate repository files.");
    }
}
