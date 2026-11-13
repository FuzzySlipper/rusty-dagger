namespace WorldRpg.Tests.Support;

/// <summary>
/// Where tests find the repository, our tables beside the importer, the operator-supplied Arena2 corpus a clean
/// checkout does not carry, and a Daggerfall Unity checkout, which only the tests that compare with the donor read.
/// The corpus can be relocated through its environment variable; the donor is named only by its own.
/// </summary>
internal static class TestData
{
    public const string CorpusVariable = "DAGGER_ARENA2";
    public const string DonorVariable = "DAGGER_DONOR_ROOT";

    private static readonly Lazy<string> FoundRoot = new(FindRepositoryRoot);

    /// <summary>The directory holding <c>AGENTS.md</c>, found by walking up from the test output.</summary>
    public static string RepositoryRoot => FoundRoot.Value;

    /// <summary>The Arena2 corpus: <c>DAGGER_ARENA2</c> when set, else <c>local/inputs/arena2</c> in the repository.</summary>
    public static string CorpusRoot => Configured(CorpusVariable) ?? Path.Combine(RepositoryRoot, "local", "inputs", "arena2");

    /// <summary>A Daggerfall Unity checkout, named by <c>DAGGER_DONOR_ROOT</c>; the repository carries none.</summary>
    public static string? DonorRoot => Configured(DonorVariable);

    /// <summary>One of our tables the import reads, under <c>src/Daggerfall.Import.Tool/inputs</c>.</summary>
    public static string Input(string relative) => Path.Combine(RepositoryRoot, "src", "Daggerfall.Import.Tool", "inputs", relative);

    public static string Corpus(string relative) => Path.Combine(CorpusRoot, relative);

    /// <summary>A tracked content pack's payload, <c>content/worldrpg/packs/PACK/payload.json</c>.</summary>
    public static string PackPayload(string pack) => Path.Combine(RepositoryRoot, "content", "worldrpg", "packs", pack, "payload.json");

    public static string Donor(string relative) => Path.Combine(DonorRoot ?? throw new InvalidOperationException($"Set {DonorVariable} to a Daggerfall Unity checkout."), relative);

    /// <summary>The one command that converts the operator's Arena2 files into the data containers and records.</summary>
    public const string RegenerationCommand = "dotnet run --project src/Daggerfall.Import.Tool -- import --music local/inputs/Sound";

    /// <summary>The data container a real import packed.</summary>
    public static string DataContainer => Path.Combine(RepositoryRoot, "local", "data", "daggerfall-data.rpak");

    /// <summary>The import records a real import wrote beside the containers; nothing at runtime reads them.</summary>
    public static string ImportRecords => Path.Combine(RepositoryRoot, "local", "build", "import", "records");

    /// <summary>
    /// Why a test reading generated content cannot run, or null when a real import's data container and import
    /// records are both present.
    /// </summary>
    internal static string? MissingGeneratedContent()
    {
        string[] missing = [.. new[] { DataContainer, ImportRecords }.Where(path => !File.Exists(path) && !Directory.Exists(path))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))];
        return missing.Length == 0 ? null : $"generated content is absent ({string.Join(", ", missing)}); run `{RegenerationCommand}` with local/inputs/arena2 supplied.";
    }

    /// <summary>
    /// Why a test converting cinematics cannot run, or null when ffmpeg and ffprobe are on PATH. An import converts
    /// them only when asked (import --cinematics), so no other machine needs FFmpeg.
    /// </summary>
    internal static string? MissingFfmpeg()
    {
        string[] missing = [.. new[] { "ffmpeg", "ffprobe" }.Where(tool => !(Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, tool)) || File.Exists(Path.Combine(directory, tool + ".exe"))))];
        return missing.Length == 0 ? null : $"{string.Join(" and ", missing)} not on PATH; the cinematic conversion needs FFmpeg.";
    }

    /// <summary>Why a test needing these corpus entries cannot run, or null when they are all present.</summary>
    internal static string? MissingCorpus(string[] entries) => Missing("Arena2 corpus", CorpusRoot, CorpusVariable, entries);

    /// <summary>Why a test needing these donor entries cannot run, or null when they are all present.</summary>
    internal static string? MissingDonor(string[] entries) => DonorRoot is { } root
        ? Missing("Daggerfall Unity checkout", root, DonorVariable, entries)
        : $"no Daggerfall Unity checkout is named; set {DonorVariable} to one to compare our tables with it.";

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
