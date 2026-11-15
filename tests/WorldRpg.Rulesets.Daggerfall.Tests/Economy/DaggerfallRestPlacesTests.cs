using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Property;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Resting inside a building, with no session: a let room, the tavern's seconds and the Fighters hall.</summary>
public sealed class DaggerfallRestPlacesTests
{
    private static readonly DaggerfallSiteId Site = new(17, 3);

    [Fact]
    public void A_room_let_here_lets_the_player_rest_and_a_tavern_says_how_long_it_has_left()
    {
        DaggerfallLodgingState lodging = new();
        DaggerfallRestPlaces places = new(lodging, new DaggerfallPropertyState(DaggerfallTuning.Defaults.Property), Guild(admitted: false));
        DaggerfallInteriorBuilding tavern = new(2, 3, new("test-block", 4), DaggerfallLodgingState.TavernBuildingType, 0);
        const long now = 1_000;

        Assert.False(places.InteriorPrivilege(Site, tavern, now, 0));
        Assert.Equal(0, places.TavernRoomSeconds(Site, tavern, now));
        Assert.True(lodging.Book(Site, new(2, 3, 4), 1, now, 0, null!));
        Assert.True(places.InteriorPrivilege(Site, tavern, now, 0));
        Assert.Equal(DaggerfallCalendar.SecondsPerDay, places.TavernRoomSeconds(Site, tavern, now));
        Assert.Null(places.TavernRoomSeconds(Site, tavern with { BuildingType = 2 }, now));
        Assert.Null(places.TavernRoomSeconds(Site, null, now));
        Assert.False(places.InteriorPrivilege(Site, null, now, 0));
    }

    [Fact]
    public void A_member_rests_in_the_fighters_hall()
    {
        DaggerfallInteriorBuilding hall = new(2, 3, new("test-block", 0), 11, 41);
        DaggerfallPropertyState property = new(DaggerfallTuning.Defaults.Property);

        Assert.False(new DaggerfallRestPlaces(new(), property, Guild(admitted: false)).InteriorPrivilege(Site, hall, 0, 0));
        Assert.True(new DaggerfallRestPlaces(new(), property, Guild(admitted: true)).InteriorPrivilege(Site, hall, 0, 0));
    }

    private static DaggerfallGuildMembershipPolicy Guild(bool admitted)
    {
        DaggerfallFactionsSet factions = new(
            new Dictionary<int, DaggerfallFactionDefinition> { [41] = new(
                41, 41, "The Fighters Guild", 0, null, [], 2, "Group", -1, 0, 0, 0,
                [], DaggerfallFactionLinkDisposition.Resolved, [], DaggerfallFactionLinkDisposition.Resolved,
                [], -1, -1, 0, "Commoners", 11, "Guild 11", 0, -1, 0, 0, 0, 0) },
            new Dictionary<int, DaggerfallRegionFactionDefinition>(),
            new Dictionary<string, int>(StringComparer.Ordinal));
        DaggerfallGuildMembershipPolicy guild = new(new DaggerfallSocialState(factions),
            skill => skill switch { "archery" => 22, "axe" => 4, _ => 0 },
            [DaggerfallConcreteGuildCatalog.ForFaction(41).ToMembershipPolicyDefinition()]);
        if (admitted) Assert.True(guild.Admit(41, 0).Applied);
        return guild;
    }
}
