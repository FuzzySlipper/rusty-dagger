using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestWorldTablesTests
{
    [Fact]
    public void Published_place_aliases_resolve_canonically_without_losing_source_parameters()
    {
        DaggerfallQuestPlaces places = Tables().Places;
        Assert.Equal(130, places.Rows.Count);
        DaggerfallQuestPlace canonical = places.Resolve("MantellanCrux");
        Assert.Same(canonical, places.Resolve("Mantellan_Crux"));
        Assert.Equal(0xc350, canonical.P1);
        Assert.Equal(0xc350fa01u, canonical.LocationKey);
        Assert.Equal((byte)1, canonical.TeleportTransfer);
        Assert.Equal(0xc352, places.Rows.Single(row => row.Name == "Mantellan_Crux").P1);
        Assert.Equal(40, places.Resolve("magery").P3);
    }

    [Fact]
    public void Sound_symbols_are_source_indices_not_playability_claims()
    {
        DaggerfallQuestTable sounds = Tables().Sounds;
        Assert.Equal(295, sounds.Rows.Count);
        Assert.Equal(8, sounds.Lookup["empty"]);
        Assert.Equal(92, sounds.Lookup["storm_1"]);
        Assert.Equal(93, sounds.Lookup["storm_2"]);
        Assert.Equal(94, sounds.Lookup["storm_3"]);
        Assert.Equal("Tables/Quests-Sounds.txt", sounds.SourcePath);
    }

    internal static DaggerfallQuestTables Tables() => Content().QuestSources.Tables;

    internal static DaggerfallDefinitions Content()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        return TestPayload.Definitions;
    }
}
