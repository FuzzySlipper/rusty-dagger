using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// Cinematic provenance: seventeen VIDs with the opening bound, sixteen FLCs with Daedric hooks,
/// and no media bytes anywhere.
/// </summary>
public sealed class DaggerfallCinematicPackTests
{
    [Fact]
    public void Binds_the_opening_and_the_daedric_table()
    {
        List<(string, DaggerfallCinematicKind, long, string)> files = [];
        for (int index = 0; index < 16; index++)
        {
            files.Add(($"ANIM{index:0000}.VID", DaggerfallCinematicKind.Vid, 1000 + index, $"VID{index}"));
        }

        files.Add(("DAG2.VID", DaggerfallCinematicKind.Vid, 2000, "DAG2"));
        foreach (DaggerfallCinematicPackBuilder.DaedricHook hook in DaggerfallCinematicPackBuilder.DaedricHooks)
        {
            files.Add((hook.FileName, DaggerfallCinematicKind.Flc, 3000, hook.FileName));
        }

        DaggerfallCinematicPack pack = DaggerfallCinematicPackBuilder.Build(files, "local/arena2", Inventory());
        pack.Validate();
        Assert.Equal(33, pack.Cinematics.Count);
        Assert.Equal(DaggerfallCinematicBinding.Bound, pack.Cinematics.Single(record => record.FileName == "ANIM0000.VID").Binding);
        Assert.Equal(DaggerfallCinematicBinding.Bound, pack.Cinematics.Single(record => record.FileName == "DAG2.VID").Binding);
        Assert.Equal(DaggerfallCinematicBinding.Unresolved, pack.Cinematics.Single(record => record.FileName == "ANIM0001.VID").Binding);
        DaggerfallCinematicRecord azura = pack.Cinematics.Single(record => record.FileName == "AZURA.FLC");
        Assert.Equal(DaggerfallCinematicBinding.Bound, azura.Binding);
        Assert.Equal(16, azura.FactionId);
        Assert.Equal("T0C00Y00", azura.Quest);
    }

    [Fact]
    public void Refuses_missing_identities_and_contradictions()
    {
        List<(string, DaggerfallCinematicKind, long, string)> files = [];
        for (int index = 0; index < 16; index++)
        {
            files.Add(($"ANIM{index:0000}.VID", DaggerfallCinematicKind.Vid, 1000 + index, $"VID{index}"));
        }

        files.Add(("DAG2.VID", DaggerfallCinematicKind.Vid, 2000, "DAG2"));
        foreach (DaggerfallCinematicPackBuilder.DaedricHook hook in DaggerfallCinematicPackBuilder.DaedricHooks.Skip(1))
        {
            files.Add((hook.FileName, DaggerfallCinematicKind.Flc, 3000, hook.FileName));
        }

        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => DaggerfallCinematicPackBuilder.Build(files, "local/arena2", Inventory()));
        Assert.Contains("sixteen FLC", missing.Message, StringComparison.Ordinal);
        DaggerfallCinematicRecord contradiction = new("X.VID", DaggerfallCinematicKind.Vid, 1, "X", DaggerfallCinematicBinding.Unresolved, "caller", null, string.Empty);
        Assert.Throws<ArgumentException>(() => contradiction.Validate());
    }

    [Fact]
    public void Records_the_supplied_corpus()
    {
        const string arena2 = "local/arena2";
        string root = RepositoryRoot();
        if (!Directory.Exists(Path.Combine(root, arena2))) return;

        List<(string, DaggerfallCinematicKind, long, string)> files = [];
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, arena2)).Order(StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(path);
            DaggerfallCinematicKind? kind = fileName.EndsWith(".VID", StringComparison.OrdinalIgnoreCase)
                ? DaggerfallCinematicKind.Vid
                : fileName.EndsWith(".FLC", StringComparison.OrdinalIgnoreCase) ? DaggerfallCinematicKind.Flc : null;
            if (kind is null) continue;
            byte[] bytes = File.ReadAllBytes(path);
            files.Add((fileName, kind.Value, bytes.LongLength, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))));
        }

        DaggerfallCinematicPack pack = DaggerfallCinematicPackBuilder.Build(files, arena2, SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(root, "docs/coverage/content-source-manifest.csv"))));
        pack.Validate();
        Assert.Equal(33, pack.Cinematics.Count);
        Assert.Equal(19, pack.Cinematics.Count(record => record.Binding == DaggerfallCinematicBinding.Bound));
        Assert.All(pack.Cinematics, record => Assert.True(record.ByteLength > 0 && record.Digest.Length == 64));
    }

    private static IReadOnlyList<SourceInventoryRow> Inventory() =>
    [
        new SourceInventoryRow("CNT-025", "family", "CNT-025", "videos", "local/arena2", string.Empty, string.Empty, string.Empty),
        new SourceInventoryRow("CNT-026", "family", "CNT-026", "flc-cinematics", "local/arena2", string.Empty, string.Empty, string.Empty),
    ];

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found");
    }
}
