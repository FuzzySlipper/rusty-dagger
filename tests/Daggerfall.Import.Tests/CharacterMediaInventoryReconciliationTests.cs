using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The documented character-media inventory against the supplied corpus. The inventory is the independent
/// record of what the family is supposed to hold, so it is what makes the corpus checkable rather than
/// self-describing - and it is a reconciliation the tool runs, so it is checked here rather than only by a
/// command nobody runs in a suite.
/// </summary>
public sealed class CharacterMediaInventoryReconciliationTests
{
    [Fact]
    public void ReconcilesTheRealCorpusWithNoUndocumentedFile()
    {
        string[] supplied = [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "local", "arena2"))
            .Select(Path.GetFileName)
            .Where(name => name is { Length: > 0 } && CharacterMediaInventory.IsDocumentedFamily(name))
            .Select(name => name!)];

        IReadOnlyList<string> undocumented = CharacterMediaPublisher.ReconcileDocumentedInventory(InventoryCsv(), supplied);

        Assert.Empty(undocumented);
        Assert.Equal(87, supplied.Length);
    }

    [Fact]
    public void RefusesACorpusThatIsMissingADocumentedFile()
    {
        string[] supplied = [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "local", "arena2"))
            .Select(Path.GetFileName)
            .Where(name => name is { Length: > 0 } && CharacterMediaInventory.IsDocumentedFamily(name))
            .Select(name => name!)
            .Where(name => name != "MAGE.CEL")];

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => CharacterMediaPublisher.ReconcileDocumentedInventory(InventoryCsv(), supplied));
        Assert.Contains("MAGE.CEL", error.Message, StringComparison.Ordinal);
        Assert.Contains("are not in the supplied corpus", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsASuppliedFileTheInventoryDoesNotDocument()
    {
        string[] supplied = [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "local", "arena2"))
            .Select(Path.GetFileName)
            .Where(name => name is { Length: > 0 } && CharacterMediaInventory.IsDocumentedFamily(name))
            .Select(name => name!)
            .Append("BODY18I0.IMG")];

        IReadOnlyList<string> undocumented = CharacterMediaPublisher.ReconcileDocumentedInventory(InventoryCsv(), supplied);

        // Reported rather than refused: a supplied file with no row is content this publication would emit
        // without a record, and the strict direction is the documented file the corpus lacks.
        Assert.Equal(["BODY18I0.IMG"], undocumented);
    }

    [Fact]
    public void RefusesAnInventoryThatDocumentsNoCharacterMediaFile()
    {
        const string Empty = "id,row_type,family_id,kind,path_or_pattern,available_count,byte_size,record_or_stem,current_scope,disposition,notes\n";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => CharacterMediaPublisher.ReconcileDocumentedInventory(System.Text.Encoding.UTF8.GetBytes(Empty), ["BODY00I0.IMG"]));
        Assert.Contains("documents no CNT-021", error.Message, StringComparison.Ordinal);
    }

    private static byte[] InventoryCsv() =>
        File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv"));

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
