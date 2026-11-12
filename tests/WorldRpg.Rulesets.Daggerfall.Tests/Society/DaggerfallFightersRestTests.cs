using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallFightersRestTests
{
    [Fact]
    public void Exact_fighters_hall_rest_follows_canonical_membership_through_expulsion_and_restore()
    {
        DaggerfallFactionsSet factions = new(
            new Dictionary<int, DaggerfallFactionDefinition> { [41] = new(
                41, 41, "The Fighters Guild", 0, null, [], 2, "Group", -1, 0, 0, 0,
                [], DaggerfallFactionLinkDisposition.Resolved, [], DaggerfallFactionLinkDisposition.Resolved,
                [], -1, -1, 0, "Commoners", 11, "Guild 11", 0, -1, 0, 0, 0, 0) },
            new Dictionary<int, DaggerfallRegionFactionDefinition>(),
            new Dictionary<string, int>(StringComparer.Ordinal));
        DaggerfallSocialState social = new(factions);
        DaggerfallGuildMembershipPolicy guild = new(social,
            skill => skill switch { "archery" => 22, "axe" => 4, _ => 0 },
            [DaggerfallConcreteGuildCatalog.ForFaction(41).ToMembershipPolicyDefinition()]);
        DaggerfallInteriorBuilding hall = new(2, 3, new("test-block", 0), 11, 41);
        DaggerfallInteriorBuilding otherHall = hall with { BlockX = 4, FactionId = 40 };

        Assert.False(DaggerfallSession.FightersGuildRestAllowed(hall, guild, 17, 0));
        Assert.True(guild.Admit(41, 0).Applied);
        Assert.True(DaggerfallSession.FightersGuildRestAllowed(hall, guild, 17, 0));
        Assert.False(DaggerfallSession.FightersGuildRestAllowed(otherHall, guild, 17, 0));

        DaggerfallSocialSave saved = social.Capture();
        Assert.True(guild.Expel(41, 1).Applied);
        Assert.False(DaggerfallSession.FightersGuildRestAllowed(hall, guild, 17, 1));

        DaggerfallSocialState restored = new(factions);
        restored.Restore(saved);
        DaggerfallGuildMembershipPolicy restoredGuild = new(restored,
            skill => skill switch { "archery" => 22, "axe" => 4, _ => 0 },
            [DaggerfallConcreteGuildCatalog.ForFaction(41).ToMembershipPolicyDefinition()]);
        Assert.True(DaggerfallSession.FightersGuildRestAllowed(hall, restoredGuild, 17, 1));
    }
}
