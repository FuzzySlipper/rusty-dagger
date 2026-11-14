using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Which minutes offer an encounter, queueing one at the player and placing it, built without the session.</summary>
public sealed class DaggerfallWorldEncountersTests
{
    // (minute / 12) % 12 == 0: minute 0 is midnight and 1,584 (26:24, so 02:24 the next day) is the next eligible one at night;
    // 576 (09:36) is an eligible day minute.
    private const long Night = 1_584, Day = 576, Ineligible = 12;

    private sealed class Stage
    {
        internal Stage()
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            Site = definitions.Locations.Records.First(site => site.Exterior is not null
                && definitions.Grids.ClimateAtWorldPixel(site.MapPixelX, site.MapPixelY).Value is >= 224 and <= 232);
            Profile = new(Site.Id, DaggerfallWorldProfileKind.Exterior, "test");
            Cell = (Site.MapPixelX, Site.MapPixelY);
            Runtime = new DaggerfallEncounterRuntime(definitions, RandomMinimum.Create());
            Encounters = new DaggerfallWorldEncounters(Runtime, null!, definitions, Player, () => 4, () => Swimming, () => false,
                () => Profile, () => Site, () => Cell, () => AtLocation, () => Generation, (_, _, _) => throw new InvalidOperationException("No spawn here."));
        }

        internal DaggerfallSiteRecord Site { get; }
        internal PlayerControlState Player { get; } = new(new WorldPoint(1f, 0f, 2f), .5f, 0f);
        internal DaggerfallWorldProfileKey Profile { get; set; }
        internal (int X, int Y) Cell { get; set; }
        internal bool Swimming { get; set; }
        internal bool AtLocation { get; set; }
        internal ulong? Generation { get; set; }
        internal DaggerfallEncounterRuntime Runtime { get; }
        internal DaggerfallWorldEncounters Encounters { get; }
    }

    [Fact]
    public void An_eligible_minute_outdoors_offers_the_wilderness_by_day_and_night_and_a_location_only_by_night()
    {
        Stage stage = new();
        Assert.Equal(DaggerfallEncounterContext.WildernessDay, stage.Encounters.Request(Day, resting: false)!.Context);
        Assert.Equal(DaggerfallEncounterContext.WildernessNight, stage.Encounters.Request(Night, resting: false)!.Context);
        Assert.Equal(4, stage.Encounters.Request(Night, resting: false)!.PlayerLevel);
        Assert.Null(stage.Encounters.Request(Ineligible, resting: false));

        stage.AtLocation = true;
        Assert.Null(stage.Encounters.Request(Day, resting: false));
        Assert.Equal(DaggerfallEncounterContext.LocationNight, stage.Encounters.Request(Night, resting: false)!.Context);

        stage.Swimming = true;
        Assert.Null(stage.Encounters.Request(Night, resting: false));
    }

    [Fact]
    public void A_dungeon_offers_one_only_to_a_rest_and_another_kind_of_place_none()
    {
        Stage stage = new();
        stage.Profile = new(stage.Site.Id, DaggerfallWorldProfileKind.Dungeon, "test");
        Assert.Null(stage.Encounters.Request(Night, resting: false));
        DaggerfallEncounterRequest resting = stage.Encounters.Request(Night, resting: true)!;
        Assert.Equal(DaggerfallEncounterContext.Dungeon, resting.Context);
        Assert.True(resting.EnemyAlert);
        Assert.Equal(stage.Site.DungeonType, resting.DungeonType);

        stage.Profile = new(stage.Site.Id, DaggerfallWorldProfileKind.Interior, "test");
        Assert.Null(stage.Encounters.Request(Night, resting: true));
    }

    [Fact]
    public void A_queued_encounter_waits_at_the_player_in_the_active_profile_under_the_latest_generation()
    {
        Stage stage = new();
        DaggerfallEncounterResolution first = stage.Encounters.Queue(stage.Encounters.Request(Night, resting: false)!);
        Assert.Equal(1UL, first.Generation);
        Assert.Equal("test", first.ProfileId);
        Assert.Equal(new ActorPose(stage.Player.Position!.Value, .5f), first.Pose);
        Assert.Equal(first.ActorDefinition is not null, stage.Runtime.HasPending("test"));

        stage.Generation = 9;
        Assert.Equal(9UL, stage.Encounters.Queue(stage.Encounters.Request(Night, resting: false)!).Generation);

        // With nowhere to stand, nothing is queued or placed.
        DaggerfallWorldEncounters nowhere = new(stage.Runtime, null!, TestPayload.Definitions, new PlayerControlState(null, 0f, 0f), () => 4,
            () => false, () => false, () => stage.Profile, () => stage.Site, () => stage.Cell, () => false, () => null, (_, _, _) => 0);
        Assert.Throws<InvalidOperationException>(() => nowhere.Queue(stage.Encounters.Request(Night, resting: false)!));
        Assert.Null(nowhere.Place(first, 1));
    }
}
