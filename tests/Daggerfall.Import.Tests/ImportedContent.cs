namespace Daggerfall.Import.Tests;

/// <summary>
/// What a real import wrote, as these tests read it: generated content from the data container the product
/// opens, and the import records beside it.
/// </summary>
/// <remarks>
/// An importer API that reads a directory from disk is given the staging tree instead. The same run wrote it and
/// packed the container from it, and packing checks that the two carry the same files with the same bytes.
/// </remarks>
internal static class ImportedContent
{
    /// <summary>One generated file's bytes, by content-root-relative path (<c>worldrpg/...</c>), from the data container.</summary>
    public static byte[] Read(string path) => TestDataContainers.Read(TestDataContainers.Data, path);

    /// <summary>One import record's bytes, by record-relative path.</summary>
    public static byte[] Record(string path) => File.ReadAllBytes(RecordPath(path));

    /// <summary>One import record's path.</summary>
    public static string RecordPath(string path) => Path.Combine(TestData.ImportRecords, path);

    /// <summary>A content-root-relative path in the staging tree the data container was packed from.</summary>
    public static string Staged(string path) => Path.Combine(TestData.RepositoryRoot, "local", "import", "staging", path);
}
