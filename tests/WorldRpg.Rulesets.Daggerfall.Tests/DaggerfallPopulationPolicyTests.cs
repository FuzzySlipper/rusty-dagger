using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallPopulationPolicyTests
{
    [Theory]
    [InlineData(0, 12, 12)]
    [InlineData(15, 40, 24)]
    [InlineData(16, 40, 24)]
    [InlineData(64, 200, 96)]
    [InlineData(128, 200, 96)]
    public void Population_capacity_bounds_real_source_placements_by_exterior_block_count(int blockCount, int sourceCount, int expected)
    {
        DaggerfallSiteRecord site = Site(blockCount);

        Assert.Equal(expected, DaggerfallSession.PopulationCapacity(site, sourceCount));
    }

    [Fact]
    public void Knightly_guard_source_facts_select_the_guard_role()
    {
        DaggerfallFactionDefinition commoner = TestPayload.Definitions.Factions.Factions.Values.First(faction => faction.Type != 10);
        // The reduced test payload intentionally has no filed Type=10 faction. Exercise the
        // source policy with the same type/name facts without admitting a fake actor.
        DaggerfallFactionDefinition guard = commoner with { Type = 10, TypeName = "KnightlyGuard" };

        Assert.Equal("guard", DaggerfallSession.PopulationRole(guard));
        Assert.Equal("civilian", DaggerfallSession.PopulationRole(commoner));
        Assert.Equal("civilian", DaggerfallSession.PopulationRole(null));
    }

    [Fact]
    public void Source_merchant_building_and_merchant_faction_publish_economy_services()
    {
        DaggerfallFactionDefinition merchant = TestPayload.Definitions.Factions.Factions.Values
            .First(faction => faction.SocialGroup == 1);

        (string role, IReadOnlyList<string> services) = DaggerfallNpcServiceFacts.Resolve(
            TestPayload.Definitions, merchant, sourceBuildingType: 9, sourceBuildingFaction: 0, "person");

        Assert.Equal("merchant", role);
        Assert.Contains("talk", services);
        Assert.Contains("shop", services);
        Assert.Contains("buy-items", services);
        Assert.Contains("sell-items", services);
        Assert.Contains("repair", services);
        Assert.DoesNotContain("identify", services);
    }

    /// <summary>
    /// A hall's own privileges (the Mages Guild library, a knightly order's free tavern rooms) have no NPC
    /// provider, so an unaffiliated person standing in the hall offers none of them, and none of the hall's
    /// provided services either.
    /// </summary>
    [Theory]
    [InlineData("Mages")]
    [InlineData("KnightlyOrder")]
    public void An_unaffiliated_person_in_a_guild_hall_provides_no_hall_service(string kind)
    {
        int hall = Guilds.DaggerfallConcreteGuildCatalog.All.First(guild => guild.Kind.ToString() == kind).FactionId;

        (_, IReadOnlyList<string> services) = DaggerfallNpcServiceFacts.Resolve(
            TestPayload.Definitions, sourceFaction: null, sourceBuildingType: 11, sourceBuildingFaction: hall, "person");

        Assert.Equal(["talk"], services);
    }

    private static DaggerfallSiteRecord Site(int blockCount)
    {
        DaggerfallSiteExterior exterior = new(0, 0, 1, 1, 0, 0, false, 2, 0, 1, 0, 1)
        {
            Blocks = Enumerable.Range(0, blockCount)
                .Select(index => new DaggerfallSiteBlock($"block-{index}", index, 0))
                .ToArray(),
        };
        return new DaggerfallSiteRecord(new DaggerfallSiteId(17, 4), "Charing", 1, 0, 0, 0,
            DaggerfallSiteKind.TownCity, true, exterior);
    }
}
