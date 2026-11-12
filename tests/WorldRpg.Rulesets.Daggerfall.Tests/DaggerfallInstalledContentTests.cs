using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Whether the data container is current for this build: one whole import by this build's importer revision. A
/// repack over an earlier import names the revision too, so its recorded departures are what say it is stale.
/// </summary>
public sealed class DaggerfallInstalledContentTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "dagger-installed-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Only_a_whole_import_by_this_builds_revision_is_current()
    {
        Assert.Null(Check(DaggerfallImporterRevision.Value, "[]"));

        string repacked = Check(DaggerfallImporterRevision.Value, """["repacked by import --only locations over files an earlier run staged; run the full import"]""")!;
        Assert.Contains("is not one whole import of this build's importer revision", repacked, StringComparison.Ordinal);
        Assert.Contains("import --only locations", repacked, StringComparison.Ordinal);

        string other = Check(new string('0', 64), "[]")!;
        Assert.Contains("was imported by importer revision 000000000000", other, StringComparison.Ordinal);

        // A description that records no departures is not one this build reads, so it is named as malformed.
        string malformed = Check(DaggerfallImporterRevision.Value, null)!;
        Assert.Contains("'departures'", malformed, StringComparison.Ordinal);
    }

    private string? Check(string revision, string? departures)
    {
        string staged = Directory.CreateDirectory(Path.Combine(_root, "staged-" + Guid.NewGuid().ToString("N"))).FullName;
        string described = departures is null ? "" : $"\"departures\":{departures},";
        Directory.CreateDirectory(Path.Combine(staged, "worldrpg"));
        File.WriteAllText(Path.Combine(staged, InstalledContent.DescriptionPath),
            $$"""{"kind":"worldrpg.data-container","importerRevision":"{{revision}}",{{described}}"contentPacks":[]}""");
        string container = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".rpak");
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        return host.Call(engine =>
        {
            ProductContentBundle.PackContainer(engine.Content, staged, container, compress: false);
            using ProductContentBundle opened = ProductContentBundle.OpenContainer(engine.Content, container);
            return new DaggerfallRuleset().CheckInstalledContent(new InstalledContent(DaggerfallData.DataContainerName, opened));
        });
    }
}
