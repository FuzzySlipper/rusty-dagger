using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The classic factions: tab-hierarchy parents, donor-exact tag rules, duplicate handling, and
/// the region claims political cells resolve through. The donor re-rolls ruler seeds at load, so
/// those never enter the catalog.
/// </summary>
public sealed class DaggerfallFactionsTests
{
    [Fact]
    public void Reads_hierarchy_tags_and_duplicate_rules_from_fixtures()
    {
        string text = "; comment\n#1\ntype: 2\nname: Parent\nregion: 18\nflags: 1\nflags: 2\nally: 2\nenemy: 3\nflat: 4 5\nflat: 6 7\n\n\t#2\ntype: 4\nname: Child\nregion: -1\n\n#1\ntype: 2\nname: Child\n";
        IReadOnlyList<ClassicFaction> factions = FactionReader.Read(text, "faction");
        Assert.Equal(3, factions.Count);
        ClassicFaction parent = factions[0];
        Assert.Equal(1, parent.Id);
        Assert.Equal(1, parent.FiledId);
        Assert.Equal(0, parent.Parent);
        Assert.Equal(17, parent.Region);
        Assert.Equal(3, parent.Flags);
        Assert.Equal([2], parent.Allies);
        Assert.Equal([3], parent.Enemies);
        Assert.Equal([(4 << 7) + 5, (6 << 7) + 7], parent.Flats);
        ClassicFaction child = factions[1];
        Assert.Equal(1, child.Parent);
        Assert.Equal(-1, child.Region);
        Assert.Equal([2], parent.Children);
        // The repeated identity is reassigned past the resolver, keeping its filed identity.
        ClassicFaction duplicate = factions[2];
        Assert.Equal(FactionReader.DuplicateResolverStart, duplicate.Id);
        Assert.Equal(1, duplicate.FiledId);
        Assert.Equal(0, duplicate.Parent);
    }

    [Fact]
    public void Refuses_malformed_tags_and_overfull_relations()
    {
        Assert.Throws<Arena2FormatException>(() => FactionReader.Read("#1\ntype 2 3\n", "faction"));
        Assert.Throws<Arena2FormatException>(() => FactionReader.Read("#1\nbogus: 1\n", "faction"));
        Assert.Throws<Arena2FormatException>(() => FactionReader.Read("#1\nally: 1\nally: 2\nally: 3\nally: 4\n", "faction"));
        Assert.Throws<Arena2FormatException>(() => FactionReader.Read("#1\nface: *A\n", "faction"));
        Assert.Throws<InvalidOperationException>(() => DaggerfallFactionsBuilder.Build("#1\nname: X\n", "local/arena2/FACTION.TXT", [1], []));
    }

    [Fact]
    public void Reads_all_supplied_factions_end_to_end()
    {
        string arena2 = Arena2Directory();
        if (!File.Exists(Path.Combine(arena2, "FACTION.TXT"))) return;

        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));
        DaggerfallFactions factions = DaggerfallFactionsBuilder.Build(
            File.ReadAllText(Path.Combine(arena2, "FACTION.TXT")),
            "local/arena2/FACTION.TXT",
            File.ReadAllBytes(Path.Combine(arena2, "FACTION.TXT")),
            inventory);
        factions.Validate();

        Assert.Equal(366, factions.Factions.Count);
        // Every relation the file states resolves; nothing is missing.
        Assert.DoesNotContain(factions.Factions, faction => faction.ParentDisposition == DaggerfallFactionLinkDisposition.Unresolved);
        Assert.DoesNotContain(factions.Factions, faction => faction.AllyDisposition == DaggerfallFactionLinkDisposition.Unresolved);
        Assert.DoesNotContain(factions.Factions, faction => faction.EnemyDisposition == DaggerfallFactionLinkDisposition.Unresolved);
        // Region claims cover the settled regions; the empty ones stay explicitly unclaimed.
        Assert.Equal(62, factions.Regions.Count);
        Assert.Equal(48, factions.Regions.Count(region => region.Disposition == DaggerfallRegionFactionDisposition.Claimed));
        Assert.Equal([2, 3, 4, 7, 8, 10, 12, 14, 15, 25, 28, 29, 30, 31],
            factions.Regions.Where(region => region.Disposition == DaggerfallRegionFactionDisposition.Unclaimed).Select(region => region.Region));
        // The donor keeps the first identity for a repeated name.
        DaggerfallFactionNameAlias alias = Assert.Single(factions.DuplicateNames);
        Assert.Equal("The Master of Initiates", alias.Name);
        Assert.Equal([76, 18], alias.Ids);
        Assert.Equal(76, alias.ResolvedId);
        // Vampire clans file regions past the classic range; the conversion is preserved and
        // claims no region rather than clamped into one.
        Assert.Equal([62, 63, 64, 65, 66, 67, 68, 69, 70],
            factions.Factions.Where(faction => faction.Id is >= 150 and <= 158).Select(faction => faction.Region));
        Assert.DoesNotContain(factions.Regions.SelectMany(region => region.FactionIds), id => id is >= 150 and <= 158);
    }

    private static IReadOnlyList<SourceInventoryRow> Inventory() =>
    [
        new SourceInventoryRow("CNT-013", "family", "CNT-013", "factions", "local/arena2/FACTION.TXT", string.Empty, "pending-import", string.Empty),
    ];

    private static string Arena2Directory() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../local/arena2"));

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
