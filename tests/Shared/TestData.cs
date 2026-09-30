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
