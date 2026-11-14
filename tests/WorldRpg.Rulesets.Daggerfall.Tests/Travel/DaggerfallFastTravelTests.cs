using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Fast travel's own rules, built from its inputs without a session.</summary>
public sealed class DaggerfallFastTravelTests
{
    private static readonly DaggerfallSiteId Wayrest = new(0, 0);
    private static readonly DaggerfallSiteId Hidden = new(0, 1);
    private static readonly DaggerfallSiteId Capital = new(0, 2);

    [Fact]
    public void A_search_puts_the_place_it_names_before_its_regions_places_and_says_when_nothing_matches()
    {
        Stage stage = new();

        stage.Travel.Search(" daggerfall ");
        DaggerfallTravelPresentation found = stage.Travel.Read();
        Assert.Equal([Capital, Wayrest], found.Destinations.Select(place => place.Id));
        Assert.Null(found.Message);

        stage.Travel.Search("nowhere");
        DaggerfallTravelPresentation missed = stage.Travel.Read();
        Assert.Empty(missed.Destinations);
        Assert.Equal("No discovered destination matches that place or region.", missed.Message);
        Assert.Null(stage.Travel.PlaceRank("Wayrest Hall", 0, "Hall Keep"));
        Assert.Equal(1, stage.Travel.PlaceRank("Wayrest Hall", 0, "way"));
    }

    [Fact]
    public void A_selected_destination_is_quoted_and_refused_in_the_donors_order()
    {
        Stage stage = new();
        Assert.Throws<ArgumentException>(() => stage.Travel.Select(0, null, true, false, false));
        stage.Travel.Select(Capital.Region, Capital.Index, cautious: true, inn: false, ship: false);
        DaggerfallTravelQuote quote = Assert.IsType<DaggerfallTravelQuote>(stage.Travel.Quote());
        Assert.Equal(Capital, quote.Destination.Id);

        stage.Vampire = true;
        stage.Defeated = true;
        Assert.Equal("Vampires cannot start fast travel during daylight.", stage.Travel.Refusal(quote, out _));
        stage.Vampire = false;
        Assert.Equal("You cannot travel while defeated.", stage.Travel.Refusal(quote, out _));
        stage.Defeated = false;
        stage.Active = DaggerfallWorldProfileIds.Dungeon(Wayrest);
        Assert.Equal("Leave the building or dungeon before travelling.", stage.Travel.Refusal(quote, out _));
        stage.Active = DaggerfallWorldProfileIds.Exterior(Wayrest);
        stage.Pending = true;
        Assert.Equal("Something approaches. Move or turn to let the encounter arrive before travelling.", stage.Travel.Refusal(quote, out _));
        Assert.Equal(stage.Active.LogicalId, stage.AskedPending);
        stage.Pending = false;
        stage.EnemyNear = true;
        Assert.Equal("Nearby enemies prevent travel.", stage.Travel.Refusal(quote, out _));
        stage.EnemyNear = false;
        Assert.Equal("You cannot travel to Daggerfall from here.", stage.Travel.Refusal(quote, out DaggerfallSiteProfile? destination));
        Assert.Null(destination);
    }

    [Fact]
    public void Accepting_needs_the_quote_the_player_read_and_departing_retires_the_selection_and_the_map()
    {
        Stage stage = new();
        Assert.Null(stage.Travel.Accept("none", 0));
        Assert.Equal("Preview a current travel route before starting.", stage.Travel.Message);

        stage.Travel.Select(Capital.Region, Capital.Index, cautious: true, inn: false, ship: false);
        DaggerfallTravelQuote quote = stage.Travel.Quote()!;
        Assert.Null(stage.Travel.Accept(quote.Identity + "-changed", (ulong)quote.TotalCost));
        Assert.Equal("The route or carried funds changed. Preview the journey again.", stage.Travel.Message);

        stage.Travel.OpenMap();
        Assert.NotNull(stage.Travel.Read().Map);
        stage.Travel.Depart();
        Assert.Null(stage.Travel.Quote());
        Assert.Null(stage.Travel.Read().Map);
        Assert.Null(stage.Travel.Message);
    }

    [Fact]
    public void A_guild_teleport_is_free_and_refused_once_the_guild_no_longer_offers_it()
    {
        Stage stage = new();
        bool offered = true;
        Assert.Equal("Select a destination for free guild teleportation.", stage.Travel.OfferGuildTeleport(42, () => offered));
        Assert.True(stage.Travel.GuildTeleport);
        Assert.NotNull(stage.Travel.Read().Map);
        stage.Travel.Select(Capital.Region, Capital.Index, cautious: true, inn: false, ship: false);
        DaggerfallTravelQuote quote = stage.Travel.Quote()!;
        Assert.Equal(0, quote.TotalCost);
        Assert.Equal($"guild-teleport:42:{stage.Active}:{Capital}", quote.Identity);

        offered = false;
        Assert.Equal("The guild teleportation service is no longer available.", stage.Travel.Refusal(quote, out _));
        Assert.Equal("The destination could not be reached.", stage.Travel.Teleported(false, quote));
        stage.Travel.CloseMap();
        Assert.False(stage.Travel.GuildTeleport);
    }

    [Fact]
    public void The_road_passes_time_a_minute_at_a_time_and_ends_on_defeat_relocation_or_a_refused_advance()
    {
        Stage stage = new();
        DaggerfallWorldProfileKey origin = stage.Active;
        long start = stage.Calendar.ToAbsoluteSeconds();
        Assert.Equal(DaggerfallTravelOutcome.Arrived, stage.Travel.Road(150, origin));
        Assert.Equal(150, stage.Calendar.ToAbsoluteSeconds() - start);
        Assert.All(stage.Slices, slice => Assert.InRange(slice, 1, DaggerfallCalendar.SecondsPerMinute));

        stage.DefeatAfter = 2;
        Assert.Equal(DaggerfallTravelOutcome.Defeated, stage.Travel.Road(600, origin));
        stage.Defeated = false;
        stage.DefeatAfter = null;

        stage.RelocateAfter = 1;
        Assert.Equal(DaggerfallTravelOutcome.Relocated, stage.Travel.Road(600, origin));
        stage.Active = origin;
        stage.RelocateAfter = null;

        stage.RefuseAdvance = true;
        Assert.Equal(DaggerfallTravelOutcome.Stopped, stage.Travel.Road(600, origin));
    }

    [Fact]
    public void A_completed_journey_records_its_result_and_reads_where_it_ended()
    {
        Stage stage = new();
        stage.Travel.Select(Capital.Region, Capital.Index, cautious: true, inn: false, ship: false);
        DaggerfallTravelQuote quote = stage.Travel.Quote()!;
        long started = stage.Calendar.ToAbsoluteSeconds();
        stage.Travel.Begin(Wayrest, quote, started);
        stage.Calendar = DaggerfallCalendar.FromAbsoluteSeconds(started + quote.TravelSeconds);
        stage.Cell = new(3, 1);
        stage.Active = DaggerfallWorldProfileIds.Exterior(Capital);
        DaggerfallJourney journey = new(quote, null!, started, new(1, 1), DaggerfallTravelOutcome.Arrived);

        DaggerfallTravelResult result = stage.Travel.Complete(journey, DaggerfallTravelOutcome.Arrived);

        Assert.Equal(Capital, result.ActualSite);
        Assert.Equal(new DaggerfallTravelMapPixel(3, 1), result.ActualPixel);
        Assert.StartsWith("Arrived at Daggerfall. Paid", result.Message);
        Assert.Same(result, stage.Travel.Read().LastResult);
        Assert.Equal("You were defeated on the road.", DaggerfallFastTravel.InterruptionText(DaggerfallTravelOutcome.Defeated));
    }

    [Fact]
    public void The_world_map_position_is_the_exterior_cell_outdoors_and_the_sites_pixel_inside()
    {
        Stage stage = new();
        stage.Cell = new(2, 1);
        Assert.Equal(new DaggerfallTravelMapPixel(2, 1), stage.Travel.Position());
        stage.Active = DaggerfallWorldProfileIds.Dungeon(Wayrest);
        Assert.Equal(new DaggerfallTravelMapPixel(1, 1), stage.Travel.Position());
    }

    [Fact]
    public void A_map_reveals_one_unknown_location_of_the_region_and_then_has_nothing_left()
    {
        Stage stage = new();
        DaggerfallMapReveal first = stage.Travel.RevealFromMap("unique:7");
        Assert.Equal(new DaggerfallMapReveal("Hidden", null), first);
        Assert.True(stage.Sites.IsDiscovered(Hidden));
        KeyedRngRequest draw = Assert.Single(stage.Random.Requests);
        Assert.Equal("daggerfall.inventory.map-use.v1", draw.Scope);
        Assert.Equal("region:0:unique:7", draw.Key);

        Assert.Equal(new DaggerfallMapReveal(null, "You have already discovered every location in this region."),
            stage.Travel.RevealFromMap("unique:7"));
        Stage nowhere = new(atSite: false);
        Assert.Equal(new DaggerfallMapReveal(null, "A map can only reveal a location while you are in a region."),
            nowhere.Travel.RevealFromMap("unique:7"));
    }

    private sealed class Stage
    {
        internal DaggerfallSiteContext Sites { get; }
        internal KeyedRandomFake Random { get; } = KeyedRandomFake.Create(0);
        internal DaggerfallFastTravel Travel { get; }
        internal DaggerfallCalendar Calendar { get; set; } = new(405, 5, 0, 12, 0, 0);
        internal DaggerfallWorldProfileKey Active { get; set; } = DaggerfallWorldProfileIds.Exterior(Wayrest);
        internal DaggerfallExteriorCellId Cell { get; set; } = new(1, 1);
        internal bool Defeated { get; set; }
        internal bool Vampire { get; set; }
        internal bool Pending { get; set; }
        internal string? AskedPending { get; private set; }
        internal bool EnemyNear { get; set; }
        internal bool RefuseAdvance { get; set; }
        internal int? DefeatAfter { get; set; }
        internal int? RelocateAfter { get; set; }
        internal List<long> Slices { get; } = [];

        internal Stage(bool atSite = true)
        {
            DaggerfallSiteRecord wayrest = Site(Wayrest, "Wayrest Hall", 1, 1, discovered: true);
            DaggerfallSiteRecord hidden = Site(Hidden, "Hidden", 4, 1, discovered: false);
            DaggerfallSiteRecord capital = Site(Capital, "Daggerfall", 3, 1, discovered: true);
            DaggerfallLocationSet locations = new([(0, 0), (0, 1), (0, 2)], [wayrest, hidden, capital], Dungeons: 0, RegionGaps: 0, Regions: 1);
            Sites = new(locations, atSite ? Wayrest : null, null, []);
            byte[] cells = Enumerable.Repeat((byte)231, 8 * 3).ToArray();
            DaggerfallWorldGridsSet grids = new(
                new DaggerfallClimateGridDefinition(8, 3, cells, [new(223, "Ocean", DaggerfallClimateDisposition.Named), new(231, "Woodlands", DaggerfallClimateDisposition.Named)]),
                new DaggerfallPoliticGridDefinition(1, 1, [128], [new(128, 0, DaggerfallPoliticDisposition.Region)]));
            Travel = new(new DaggerfallTravelPolicy(Sites, grids, DaggerfallTuning.Defaults.Transport), new DaggerfallTravelState(),
                new DaggerfallTransportPolicy(DaggerfallTuning.Defaults.Transport), Sites, Random.Service, ["Daggerfall"],
                DaggerfallWorldNames.None, new DaggerfallExteriorWorldBounds(8, 3), atlasTiles: null,
                calendar: () => Calendar,
                passTime: PassTime,
                activeProfile: () => Active,
                exteriorCell: () => Cell,
                playerDefeated: () => Defeated,
                isVampire: () => Vampire,
                encounterPending: id => { AskedPending = id; return Pending; },
                enemyNearby: () => EnemyNear,
                profiles: () => null,
                options: selected => new(selected.Cautious, selected.Inn, selected.Ship, HasHorse: false, HasCart: false, HasShip: false,
                    AvailableGold: 1000, AvailableGoldPieces: 1000),
                classicArt: () => true,
                yaw: () => 0f);
        }

        private DaggerfallCalendarAdvance PassTime(long slice)
        {
            Slices.Add(slice);
            if (DefeatAfter is int defeat && Slices.Count >= defeat) Defeated = true;
            if (RelocateAfter is int relocate && Slices.Count >= relocate) Active = DaggerfallWorldProfileIds.Exterior(Capital);
            long applied = RefuseAdvance ? 0 : slice;
            Calendar = DaggerfallCalendar.FromAbsoluteSeconds(Calendar.ToAbsoluteSeconds() + applied);
            return new(Calendar, applied, slice - applied, DaggerfallCalendar.NoConsequence, default);
        }
    }

    private static DaggerfallSiteRecord Site(DaggerfallSiteId id, string name, int mapPixelX, int mapPixelY, bool discovered) => new(
        id, name, MapId: 0, Longitude: mapPixelX * 128, Latitude: (499 - mapPixelY) * 128, DungeonType: 0,
        Kind: DaggerfallSiteKind.TownCity, Discovered: discovered,
        Exterior: new DaggerfallSiteExterior(mapPixelX, mapPixelY, 1, 1, 0, 0, false, 0, 0, 1, 0, 1));
}
