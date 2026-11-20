using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
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
                () => Profile, () => Site, () => Cell, () => AtLocation, () => Generation, (_, _, _) => throw new InvalidOperationException("No spawn here."),
                _ => null, _ => { });
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
            () => false, () => false, () => stage.Profile, () => stage.Site, () => stage.Cell, () => false, () => null, (_, _, _) => 0, _ => null, _ => { });
        Assert.Throws<InvalidOperationException>(() => nowhere.Queue(stage.Encounters.Request(Night, resting: false)!));
        Assert.Null(nowhere.Place(first, 1));
    }

    [Fact]
    public void A_bands_members_stand_beside_its_first_or_come_as_any_encounter_where_there_is_no_room_there()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        using ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), TestSessions.HealthyMechanics(), "health",
            DaggerfallActorFactory.PlayerCapabilities);
        _ = CombatCollaborators.Targeting(actors, new Dictionary<long, DaggerfallActorDefinition>(), out SpatialMovementSystem spatial, out SpatialFake fake);
        using SpatialMovementSystem movement = spatial;
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        DaggerfallActorPlacement placement = new(actors, player, _ => true, new Dictionary<long, DaggerfallActorDefinition>(), _ => null,
            _ => false, _ => true, _ => false, _ => false, () => false, spatial, PerceptionFake.Create().Service, KeyedRandomFake.Create(3).Service,
            () => default, detectionDistance: 10d, fieldOfViewYDegrees: 60f);
        // The ground is floor everywhere; in the second band a ray from where its first member came to stand starts
        // inside the ground, as it can in the wilds.
        Vector3? solid = null;
        bool solidBesideFirst = false;
        fake.FloorHit = request => solid is { } at && Vector3.Distance(request.Origin, at) < .01f
            ? new SpatialHit { Present = true, StartSolid = true }
            : request.Direction.Y < -.5f
                ? new SpatialHit { Present = true, Kind = SpatialHitKind.Entity, Point = request.Origin with { Y = -1f }, Normal = Vector3.UnitY, Distance = 1 }
                : default;
        Dictionary<long, ActorPose> spawned = [];
        HashSet<long> provoked = [];
        DaggerfallEncounterRuntime runtime = new(definitions, RandomMinimum.Create());
        DaggerfallSiteRecord site = definitions.Locations.Records.First(site => site.Exterior is not null);
        DaggerfallWorldEncounters encounters = new(runtime, placement, definitions, player, () => 1, () => false, () => false,
            () => new(site.Id, DaggerfallWorldProfileKind.Exterior, "test"), () => site, () => (site.MapPixelX, site.MapPixelY), () => false, () => 1,
            (_, pose, _) =>
            {
                long id = 100 + spawned.Count;
                spawned[id] = pose;
                if (solidBesideFirst) solid ??= pose.Position.ToVector();
                return id;
            },
            id => spawned.TryGetValue(id, out ActorPose pose) ? pose : null, id => provoked.Add(id));
        DaggerfallEncounterRequest request = new(DaggerfallEncounterContext.WildernessDay, 1, Climate: 231);
        DaggerfallEncounterChoice choice = DaggerfallEncounterPolicy.Pick(definitions.Encounters, request, (_, minimum, _) => minimum);

        encounters.QueueBand(request, choice, new DaggerfallEncounterBand(1, DaggerfallEncounterStaging.Caught, 0f), 2, "test");
        Assert.Equal([100L, 101L], runtime.MaterializePending("test", pending => encounters.Place(pending, 1)));
        Assert.InRange(Vector3.Distance(spawned[100].Position.ToVector(), spawned[101].Position.ToVector()), 1.4f, 3.6f);
        Assert.Equal([100L, 101L], provoked);

        spawned.Clear();
        solidBesideFirst = true;
        encounters.QueueBand(request, choice, new DaggerfallEncounterBand(2, DaggerfallEncounterStaging.Camp, 1f), 2, "test");
        Assert.Equal([100L, 101L], runtime.MaterializePending("test", pending => encounters.Place(pending, 2)));
        Assert.Equal([100L, 101L], provoked);
    }
}
