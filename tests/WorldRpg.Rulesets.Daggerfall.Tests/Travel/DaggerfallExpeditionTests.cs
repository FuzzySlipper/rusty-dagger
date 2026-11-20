using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The expedition map without the session: a small world of woodland with the sea to the east and an island in it, a
/// city, a port village, a dungeon, a tavern and a hidden place, and surroundings that keep the calendar and record each
/// stretch the party passes.
/// </summary>
public sealed class DaggerfallExpeditionTests
{
    // Woodland (climate 231) costs the donor's 104 minutes a pixel on foot, 52 on a horse; the sea 255 swimming, 51 by ship.
    private const long FootPixel = 104 * 60;
    private const int Fastest = 3;

    [Fact]
    public void A_cautious_march_crosses_the_route_pixel_by_pixel_passing_its_true_minutes_and_stops_on_arrival()
    {
        (DaggerfallExpedition expedition, World world) = Create();
        expedition.Begin(new(1, 1), aboardOwnShip: false);

        expedition.SetDestination(new(4, 1), null);
        Assert.Equal(1, expedition.Speed);
        expedition.SetSpeed(Fastest);
        expedition.Advance(10);

        Assert.Equal(new DaggerfallTravelMapPixel(4, 1), expedition.Party);
        Assert.Null(expedition.Destination);
        Assert.Equal(0, expedition.Speed);
        Assert.Equal("The party reaches your destination.", expedition.Message);
        Assert.Equal(new DaggerfallTravelMapPixel(3, 1), Assert.IsType<DaggerfallExpeditionEntry>(expedition.Entry()).CameFrom);
        Assert.Equal(3 * FootPixel, world.Stretches.Sum(stretch => stretch.Seconds));
        Assert.All(world.Stretches, stretch => Assert.Equal((DaggerfallExpeditionActivity.Marching, 1d), (stretch.Activity, stretch.FatigueFactor)));
        // No stretch runs past an hour, ten minutes or a pixel, so every hour from 08:00 to 13:12 was rolled once.
        Assert.All(world.Stretches, stretch => Assert.InRange(stretch.Seconds, 1, 600));
        Assert.Equal([9L, 10L, 11L, 12L, 13L], world.Rolls.Select(roll => roll.Minute / 60 % 24));
    }

    [Fact]
    public void Reckless_halves_the_minutes_and_doubles_the_fatigue_while_a_horse_night_and_weather_change_the_pace()
    {
        (DaggerfallExpedition reckless, World fast) = Create();
        reckless.Begin(new(1, 1), false);
        reckless.SetMode(DaggerfallExpeditionMode.Reckless);
        reckless.SetDestination(new(3, 1), null);
        reckless.SetSpeed(Fastest);
        reckless.Advance(10);
        Assert.Equal(FootPixel, fast.Stretches.Sum(stretch => stretch.Seconds));
        Assert.All(fast.Stretches, stretch => Assert.Equal(2d, stretch.FatigueFactor));

        (DaggerfallExpedition horse, World riding) = Create(world => world.Transport = DaggerfallTransportMode.Horse);
        horse.Begin(new(1, 1), false);
        horse.SetDestination(new(3, 1), null);
        Assert.Equal(52d, horse.MinutesToNext());

        (DaggerfallExpedition night, World dark) = Create(world => world.Calendar = At(hour: 22));
        night.Begin(new(1, 1), false);
        night.SetDestination(new(3, 1), null);
        Assert.Equal(104 * 1.25, night.MinutesToNext());

        (DaggerfallExpedition storm, World wet) = Create(world => world.Current = DaggerfallWeatherKind.Thunder);
        storm.Begin(new(1, 1), false);
        storm.SetDestination(new(2, 1), null);
        Assert.Equal(104 * 1.35, storm.MinutesToNext());
        storm.SetSpeed(Fastest);
        storm.Advance(10);
        Assert.All(wet.Stretches, stretch => Assert.Equal(1.5, stretch.FatigueFactor));
        Assert.InRange(wet.Stretches.Sum(stretch => stretch.Seconds), 8424, 8425);
    }

    [Fact]
    public void A_halt_passes_time_standing_and_a_wild_camp_recovers_each_whole_hour_at_the_rough_ground_share()
    {
        (DaggerfallExpedition expedition, World world) = Create();
        expedition.Begin(new(2, 2), false);
        expedition.SetDestination(new(4, 2), null);
        expedition.SetMode(DaggerfallExpeditionMode.Halt);
        expedition.SetSpeed(Fastest);
        expedition.Advance(1d / 6);
        Assert.Equal(new DaggerfallTravelMapPixel(2, 2), expedition.Party);
        Assert.Equal(3600, world.Stretches.Sum(stretch => stretch.Seconds));
        Assert.All(world.Stretches, stretch => Assert.Equal(DaggerfallExpeditionActivity.Halted, stretch.Activity));
        Assert.NotNull(expedition.Destination);

        world.Stretches.Clear();
        expedition.SetMode(DaggerfallExpeditionMode.Camp);
        expedition.SetSpeed(Fastest);
        expedition.Advance(2.5d / 6);
        Assert.Equal(DaggerfallExpeditionActivity.Camping, Assert.Single(world.Stretches.Select(stretch => stretch.Activity).Distinct()));
        Assert.Equal([false, false], world.CampHours);
        Assert.Equal(1000, world.Gold);

        // Resuming the pace walks on along the halted leg.
        expedition.SetMode(DaggerfallExpeditionMode.Cautious);
        Assert.Equal(104d, expedition.MinutesToNext());
    }

    [Fact]
    public void Camping_in_a_settlement_takes_lodging_once_a_day_and_halts_a_party_that_cannot_pay()
    {
        (DaggerfallExpedition expedition, World world) = Create();
        expedition.Begin(new(1, 1), false);
        Assert.Equal(30, expedition.LodgingHere);
        expedition.SetMode(DaggerfallExpeditionMode.Camp);
        expedition.SetSpeed(Fastest);
        expedition.Advance(30d / 6);

        Assert.Equal(1000 - 30 - 30, world.Gold);
        Assert.Equal(30, world.CampHours.Count);
        Assert.All(world.CampHours, Assert.True);

        (DaggerfallExpedition poor, World broke) = Create(world => world.Gold = 5);
        poor.Begin(new(1, 1), false);
        poor.SetMode(DaggerfallExpeditionMode.Camp);
        poor.Advance(1);
        Assert.Equal(DaggerfallExpeditionMode.Halt, poor.Mode);
        Assert.Equal(0, poor.Speed);
        Assert.Equal("A night's lodging in Ashford costs 30 gold, which you do not carry.", poor.Message);
        Assert.Empty(broke.Stretches);
    }

    [Fact]
    public void Swimming_is_slow_and_tiring_cannot_camp_and_cannot_enter_while_a_hired_ship_sails_from_a_port_and_puts_ashore()
    {
        (DaggerfallExpedition swim, World water) = Create();
        swim.Begin(new(7, 1), false);
        swim.SetDestination(new(9, 1), null);
        swim.SetSpeed(Fastest);
        swim.Advance(10);
        Assert.Equal(2 * 255 * 60, water.Stretches.Sum(stretch => stretch.Seconds));
        Assert.Equal(DaggerfallExpeditionActivity.Swimming, Assert.Single(water.Stretches.Select(stretch => stretch.Activity).Distinct()));
        Assert.True(swim.Swimming);
        swim.SetMode(DaggerfallExpeditionMode.Camp);
        Assert.Equal("You cannot camp in the water.", swim.Message);
        Assert.Null(swim.Entry());
        // Only the hours spent wading out from the shore roll; the sea has no encounter table.
        Assert.All(water.Rolls, roll => Assert.Equal(new DaggerfallTravelMapPixel(7, 1), roll.Pixel));

        (DaggerfallExpedition inland, _) = Create();
        inland.Begin(new(2, 2), false);
        Assert.Equal("Ships sail from a port, and none is near enough.", inland.ToggleShip());

        (DaggerfallExpedition voyage, World sea) = Create();
        voyage.Begin(new(7, 2), false);
        Assert.Null(voyage.ToggleShip());
        Assert.Equal(DaggerfallExpeditionShip.Hired, voyage.Ship);
        Assert.Equal(900, sea.Gold);
        voyage.SetDestination(new(10, 2), null);
        voyage.SetSpeed(Fastest);
        voyage.Advance(10);
        Assert.Equal(new DaggerfallTravelMapPixel(10, 2), voyage.Party);
        Assert.Equal(DaggerfallExpeditionShip.None, voyage.Ship);
        Assert.Equal([DaggerfallExpeditionActivity.Sailing, DaggerfallExpeditionActivity.Marching],
            sea.Stretches.Select(stretch => stretch.Activity).Distinct());
        // Two sea pixels at the ship's 51 minutes, then the island's woodland on foot.
        Assert.Equal(2 * 51 * 60 + FootPixel, sea.Stretches.Sum(stretch => stretch.Seconds));
        Assert.Equal("Isle", Assert.IsType<DaggerfallExpeditionEntry>(voyage.Entry()).Location?.Name);

        (DaggerfallExpedition owner, World owned) = Create(world => world.OwnsShip = true);
        owner.Begin(new(7, 2), false);
        Assert.Null(owner.ToggleShip());
        Assert.Equal((DaggerfallExpeditionShip.Owned, 1000), (owner.Ship, owned.Gold));
    }

    [Fact]
    public void An_hour_that_draws_an_encounter_pauses_the_map_and_only_entering_goes_on()
    {
        (DaggerfallExpedition expedition, World world) = Create(world => world.Encounter = (_, _, minute) => minute / 60 % 24 == 10);
        expedition.Begin(new(1, 1), false);
        expedition.SetDestination(new(6, 1), null);
        expedition.SetSpeed(Fastest);
        expedition.Advance(10);

        Assert.True(expedition.EncounterWaiting);
        Assert.False(expedition.Running);
        Assert.Equal("Something approaches! Enter to meet it.", expedition.Message);
        Assert.Equal(At(hour: 10).ToAbsoluteSeconds(), world.Calendar.ToAbsoluteSeconds());
        Assert.Equal((new DaggerfallTravelMapPixel(2, 1), false), (world.Rolls[^1].Pixel, world.Rolls[^1].AtLocation));

        expedition.SetSpeed(1);
        Assert.Equal(0, expedition.Speed);
        expedition.SetDestination(new(1, 1), null);
        Assert.Equal(new DaggerfallTravelMapPixel(6, 1), expedition.Destination?.Pixel);
        Assert.Equal("Something approaches. Enter to meet it.", expedition.Leave());
        DaggerfallExpeditionEntry entry = Assert.IsType<DaggerfallExpeditionEntry>(expedition.Entry());
        Assert.Equal((new DaggerfallTravelMapPixel(2, 1), new DaggerfallTravelMapPixel(1, 1), null),
            (entry.Pixel, entry.CameFrom, entry.Location));
    }

    [Fact]
    public void The_low_fatigue_warning_pauses_once_until_fatigue_rises_and_exhaustion_drops_the_party()
    {
        (DaggerfallExpedition expedition, World world) = Create(world => world.DrainPerHour = .1);
        expedition.Begin(new(1, 1), false);
        expedition.SetDestination(new(9, 1), null);
        expedition.SetSpeed(Fastest);
        expedition.Advance(10);
        Assert.Equal("You are growing weary. Halt or make camp before you drop.", expedition.Message);
        Assert.InRange(world.FatigueShare, .1, .2);

        expedition.SetSpeed(Fastest);
        world.Exhausted = false;
        world.ExhaustAtZero = true;
        expedition.Advance(10);
        Assert.Equal([false], world.Collapses);
        Assert.Equal((DaggerfallExpeditionMode.Halt, 0), (expedition.Mode, expedition.Speed));
        Assert.Null(expedition.Destination);
        Assert.Equal("Exhausted, you drop where you stand.", expedition.Message);
    }

    [Fact]
    public void A_save_on_the_map_keeps_the_party_leg_mode_pin_ship_and_lodging_and_resumes_paused()
    {
        (DaggerfallExpedition expedition, World world) = Create();
        expedition.Begin(new(1, 1), false);
        expedition.SetPin(new(5, 3), new DaggerfallSiteId(0, 2));
        expedition.SetMode(DaggerfallExpeditionMode.Reckless);
        expedition.SetDestination(new(5, 3), new DaggerfallSiteId(0, 2));
        expedition.SetSpeed(Fastest);
        expedition.Advance(4000d / (360 * 60));
        (double X, double Y) position = expedition.Position;

        DaggerfallExpeditionSave saved = JsonSerializer.Deserialize<DaggerfallExpeditionSave>(JsonSerializer.Serialize(expedition.Capture()))!;
        DaggerfallExpedition restored = new(Policy(), Sites(), DaggerfallExpeditionTuning.Default, world, saved);

        Assert.True(restored.Open);
        Assert.Equal(0, restored.Speed);
        Assert.Equal(position, restored.Position);
        Assert.Equal(expedition.Party, restored.Party);
        Assert.Equal(new DaggerfallExpeditionMark(new(5, 3), new(0, 2)), restored.Destination);
        Assert.Equal(new DaggerfallExpeditionMark(new(5, 3), new(0, 2)), restored.Pin);
        Assert.Equal(DaggerfallExpeditionMode.Reckless, restored.Mode);
        Assert.Equal(expedition.RemainingSeconds(), restored.RemainingSeconds());

        DaggerfallExpeditionSave tampered = saved with { Party = new(9, 4) };
        Assert.Throws<ArgumentException>(() => new DaggerfallExpedition(Policy(), Sites(), DaggerfallExpeditionTuning.Default, world, tampered));
    }

    [Fact]
    public void Leaving_needs_the_party_where_the_body_stands_find_pins_a_known_place_and_a_guild_teleport_picks_one()
    {
        (DaggerfallExpedition expedition, World world) = Create();
        expedition.Begin(new(1, 1), false);
        expedition.SetMode(DaggerfallExpeditionMode.Camp);
        expedition.SetSpeed(Fastest);
        expedition.Advance(1d / 6);
        Assert.Null(expedition.Leave());
        Assert.False(expedition.Open);

        expedition.Begin(new(1, 1), false);
        expedition.SetDestination(new(2, 1), null);
        expedition.SetSpeed(Fastest);
        expedition.Advance(10);
        Assert.Equal("The party has moved on. Enter to go on foot from here.", expedition.Leave());
        Assert.True(expedition.Open);

        expedition.Find("dark", ["Testland"]);
        Assert.Equal(new DaggerfallExpeditionMark(new(5, 3), new(0, 2)), expedition.Pin);
        expedition.Find("hidden", ["Testland"]);
        Assert.Equal("No place you know matches that name or region.", expedition.Message);
        expedition.SetDestination(new(3, 0), new DaggerfallSiteId(0, 3));
        Assert.Equal("You do not know that place.", expedition.Message);

        (DaggerfallExpedition guild, _) = Create();
        bool available = true;
        guild.OfferGuildTeleport(7, () => available, new(1, 1));
        guild.SetDestination(new(4, 4), null);
        Assert.Equal("The guild teleports only to a place you know.", guild.Message);
        guild.SetDestination(new(5, 3), new DaggerfallSiteId(0, 2));
        Assert.Equal((7L, "Dark Hole"), guild.TeleportChoice() is { } choice ? (choice.NpcId, choice.Place.Name) : default);
        guild.SetSpeed(Fastest);
        Assert.False(guild.Running);
        Assert.Null(guild.Entry());
        available = false;
        Assert.Null(guild.TeleportChoice());
        Assert.Equal("The guild teleportation service is no longer available.", guild.Message);
    }

    [Fact]
    public void The_route_walk_steps_diagonally_as_the_donor_and_the_quest_clock_keeps_its_minutes()
    {
        // The donor's strict comparison never steps across a shallow diagonal; the party's path still ends where it heads.
        Assert.Equal([new(1, 1), new(2, 1), new(3, 1), new(4, 1)], DaggerfallTravelPolicy.Route(new(0, 1), new(4, 2)));
        Assert.Equal([new(1, 1), new(2, 1), new(3, 1), new(4, 2)], DaggerfallTravelPolicy.Path(new(0, 1), new(4, 2)));
        Assert.Equal([new(1, 0), new(2, 2)], DaggerfallTravelPolicy.Path(new(0, 0), new(2, 2)));
        Assert.Empty(DaggerfallTravelPolicy.Path(new(3, 3), new(3, 3)));
        // Three woodland pixels by cart, camping out: the donor's 270 minutes, under the quest clock's one-day minimum.
        Assert.Equal(24 * 60, Policy().CautiousQuestLegMinutes(new(1, 1), new(0, 2)));
    }

    private static DaggerfallCalendar At(int hour) =>
        DaggerfallCalendar.FromAbsoluteSeconds(DaggerfallCalendar.Start.ToAbsoluteSeconds() + hour * 3600L);

    private static (DaggerfallExpedition Expedition, World World) Create(Action<World>? setup = null)
    {
        World world = new();
        setup?.Invoke(world);
        return (new DaggerfallExpedition(Policy(), Sites(), DaggerfallExpeditionTuning.Default, world), world);
    }

    private static DaggerfallTravelPolicy Policy() => new(Sites(), Grids(), DaggerfallTuning.Defaults.Transport);

    private static DaggerfallWorldGridsSet Grids()
    {
        const int width = 13, height = 5;
        byte[] cells = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int pixel = x - 1;
                cells[(y * width) + x] = (byte)(pixel >= 8 && !(pixel == 10 && y == 2) ? 223 : 231);
            }
        return new(new DaggerfallClimateGridDefinition(width, height, cells,
            [new(223, "Ocean", DaggerfallClimateDisposition.Named), new(231, "Woodlands", DaggerfallClimateDisposition.Named)]),
            new DaggerfallPoliticGridDefinition(1, 1, [128], [new(128, 0, DaggerfallPoliticDisposition.Region)]));
    }

    private static DaggerfallSiteContext Sites()
    {
        DaggerfallSiteRecord[] records =
        [
            Site(new(0, 0), "Ashford", DaggerfallSiteKind.TownCity, 1, 1, true),
            Site(new(0, 1), "Seaport", DaggerfallSiteKind.TownVillage, 7, 2, true, port: true),
            Site(new(0, 2), "Dark Hole", DaggerfallSiteKind.DungeonRuin, 5, 3, true),
            Site(new(0, 3), "Hidden", DaggerfallSiteKind.DungeonRuin, 3, 0, false),
            Site(new(0, 4), "Isle", DaggerfallSiteKind.TownCity, 10, 2, true),
        ];
        DaggerfallLocationSet locations = new([.. records.Select(site => (site.Id.Region, site.Id.Index))], records,
            Dungeons: 0, RegionGaps: 0, Regions: 1);
        return new(locations, null, null, [.. records.Where(site => site.Discovered).Select(site => site.Id)]);
    }

    private static DaggerfallSiteRecord Site(DaggerfallSiteId id, string name, DaggerfallSiteKind kind, int x, int y, bool discovered,
        bool port = false) => new(id, name, MapId: 0, Longitude: x * 128, Latitude: (499 - y) * 128, DungeonType: 0, Kind: kind,
        Discovered: discovered, Exterior: new DaggerfallSiteExterior(x, y, 1, 1, 0, 0, false, 0, 0, 1, 0, 1) { PortTownAndUnknown = port ? 1 : 0 });

    private sealed class World : IDaggerfallExpeditionSurroundings
    {
        public DaggerfallCalendar Calendar { get; set; } = At(hour: 8);
        public DaggerfallWeatherKind Current { get; set; } = DaggerfallWeatherKind.Sunny;
        public DaggerfallTransportMode Transport { get; set; } = DaggerfallTransportMode.Foot;
        public bool OwnsShip { get; set; }
        public int Gold { get; set; } = 1000;
        public double FatigueShare { get; set; } = 1;
        public double DrainPerHour { get; set; }
        public bool ExhaustAtZero { get; set; }
        public bool Exhausted { get; set; }
        public bool Defeated { get; set; }
        public Func<DaggerfallTravelMapPixel, bool, long, bool> Encounter { get; set; } = (_, _, _) => false;
        public List<DaggerfallExpeditionStretch> Stretches { get; } = [];
        public List<bool> CampHours { get; } = [];
        public List<bool> Collapses { get; } = [];
        public List<(DaggerfallTravelMapPixel Pixel, bool AtLocation, long Minute)> Rolls { get; } = [];

        public DaggerfallWeatherKind Weather(int climate) => Current;

        public bool TrySpendGold(int amount)
        {
            if (Gold < amount) return false;
            Gold -= amount;
            return true;
        }

        public long Pass(DaggerfallExpeditionStretch stretch)
        {
            Stretches.Add(stretch);
            Calendar = DaggerfallCalendar.FromAbsoluteSeconds(Calendar.ToAbsoluteSeconds() + stretch.Seconds);
            if (stretch.Activity is DaggerfallExpeditionActivity.Marching or DaggerfallExpeditionActivity.Swimming)
                FatigueShare = Math.Max(0, FatigueShare - DrainPerHour * stretch.FatigueFactor * stretch.Seconds / 3600);
            if (ExhaustAtZero && FatigueShare <= 0) Exhausted = true;
            return stretch.Seconds;
        }

        public void RecoverCampHour(bool sheltered) => CampHours.Add(sheltered);

        public void Collapse(bool inWater)
        {
            Collapses.Add(inWater);
            Exhausted = false;
            FatigueShare = .1;
        }

        public bool EncounterArrives(DaggerfallTravelMapPixel pixel, bool atLocation, long minute)
        {
            Rolls.Add((pixel, atLocation, minute));
            return Encounter(pixel, atLocation, minute);
        }
    }
}
