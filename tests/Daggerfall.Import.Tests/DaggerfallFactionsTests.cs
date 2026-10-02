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
        Assert.Throws<InvalidOperationException>(() => DaggerfallFactionsBuilder.Build("#1\nname: X\n", "arena2/FACTION.TXT", [1], []));
    }

    [CorpusFact("FACTION.TXT")]
    public void Reads_all_supplied_factions_end_to_end()
    {
        string arena2 = TestData.CorpusRoot;

        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "data/content-source-manifest.csv")));
        DaggerfallFactions factions = DaggerfallFactionsBuilder.Build(
            File.ReadAllText(Path.Combine(arena2, "FACTION.TXT")),
            "arena2/FACTION.TXT",
            File.ReadAllBytes(Path.Combine(arena2, "FACTION.TXT")),
            inventory);
        factions.Validate();

        Assert.Equal(366, factions.Factions.Count);
        foreach (var faction in factions.Factions)
        {
            Assert.Equal(faction.Flats.Count, faction.FlatVisuals.Count);
            for (int i = 0; i < faction.Flats.Count; i++)
            {
                var visual = faction.FlatVisuals[i];
                Assert.Equal(faction.Flats[i], visual.Id);
                Assert.Equal(visual.Id, (visual.Archive << 7) | visual.Record);
                Assert.InRange(visual.Record, 0, 127);
            }
        }
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

    [DonorFact("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Flats.csv")]
    public void Publishes_the_actual_localized_flat_captions_with_offline_addresses_and_provenance()
    {
        const string relative = "Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Flats.csv";
        byte[] bytes = File.ReadAllBytes(TestData.Donor(relative));
        var baseline = DaggerfallFactionsBuilder.Build("#1\nname: X\n", "arena2/FACTION.TXT", [1], Inventory());
        var catalog = DaggerfallFactionsBuilder.WithNpcCaptions(baseline, bytes, PublishedSourcePath.Donor(relative));
        Assert.Equal(226, catalog.NpcCaptions.Count);
        Assert.Equal(PublishedSourcePath.Donor(relative), catalog.NpcCaptionSource!.Path);
        Assert.Equal("beautiful maiden", catalog.NpcCaptions.Single(value => value.Archive == 175 && value.Record == 0).Caption);
        Assert.Equal(InternalStringsReader.Read(bytes, relative).Records.Select(value => value.Value), catalog.NpcCaptions.Select(value => value.Caption));
    }

    [Theory]
    [InlineData("Key,Value\nnot-a-flat,caption\n")]
    [InlineData("Key,Value\n22400,caption\n22400,other\n")]
    [InlineData("Key,Value\n22400,\n")]
    public void Malformed_caption_data_is_reported_without_silently_dropping_entries(string csv)
    {
        var baseline = DaggerfallFactionsBuilder.Build("#1\nname: X\n", "arena2/FACTION.TXT", [1], Inventory());
        Assert.Throws<InvalidOperationException>(() => DaggerfallFactionsBuilder.WithNpcCaptions(baseline,
            System.Text.Encoding.UTF8.GetBytes(csv), "fixture/Internal_Flats.csv"));
    }

    private static IReadOnlyList<SourceInventoryRow> Inventory() =>
    [
        new SourceInventoryRow("CNT-013", "family", "CNT-013", "factions", "arena2/FACTION.TXT", string.Empty, "pending-import", string.Empty),
    ];
}
