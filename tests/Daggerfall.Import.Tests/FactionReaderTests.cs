using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The classic faction file the world blocks read: tab-hierarchy parents, the classic tag rules and duplicate handling.
/// </summary>
public sealed class FactionReaderTests
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
    }
}
