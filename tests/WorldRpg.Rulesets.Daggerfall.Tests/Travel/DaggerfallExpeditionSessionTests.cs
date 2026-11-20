using Rusty.Engine.Debugging;
using System.Numerics;
using System.Text.Json;
using System.Reflection;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallExpeditionSessionTests
{
    [Fact]
    public void The_map_opens_outdoors_on_the_players_pixel_and_publishes_the_party_and_its_places_once_per_revision()
    {
        using Fixture fixture = new();
        Assert.Null(fixture.Expedition());
        fixture.Open();
        Assert.True(fixture.Session.Expedition.Open);
        Assert.Equal(fixture.OriginPixel, fixture.Session.Expedition.Party);
        Dictionary<string, object?> expedition = fixture.Expedition()!;
        Dictionary<string, object?> party = Assert.IsType<Dictionary<string, object?>>(expedition["party"]);
        Assert.Equal((fixture.OriginPixel.X + .5, fixture.OriginPixel.Y + .5), (Convert.ToDouble(party["x"]), Convert.ToDouble(party["y"])));
        Assert.Equal(("cautious", 0, 3), ((string?)expedition["mode"], Convert.ToInt32(expedition["speed"]), Convert.ToInt32(expedition["speeds"])));
        Assert.True((bool)expedition["canLeave"]!);
        Assert.Equal($"Enter {fixture.Session.Site.ActiveSite!.Name}", expedition["enterLabel"]);

        // The places travel once for each revision, and again when the page asks for them.
        Dictionary<string, object?> Map() => Assert.IsType<Dictionary<string, object?>>(fixture.Expedition()!["map"]);
        object?[] places = Assert.IsType<object?[]>(Map()["places"]);
        Assert.NotEmpty(places);
        fixture.Step();
        Assert.False(Map().ContainsKey("places"));
        fixture.Submit(new { action = "expedition-places" });
        Assert.Equal(places.Length, Assert.IsType<object?[]>(Map()["places"]).Length);

        // Leaving with the party where the body stands just closes the map.
        fixture.Submit(new { action = "expedition-leave" });
        Assert.False(fixture.Session.Expedition.Open);
        Assert.Null(fixture.Expedition());

        // The entry screen and death close the map; a host pause keeps it.
        foreach (ProductMode leaving in new[] { ProductMode.Title, ProductMode.Dead })
        {
            fixture.Open();
            fixture.Session.ApplyProductMode(ProductMode.Paused);
            Assert.True(fixture.Session.Expedition.Open);
            fixture.Session.ApplyProductMode(leaving);
            Assert.False(fixture.Session.Expedition.Open);
            fixture.Session.ApplyProductMode(ProductMode.Playing);
        }
    }

    [Fact]
    public void A_march_passes_its_true_time_through_the_calendar_and_spends_fatigue_minute_by_minute_with_no_recovery_before_leaving()
    {
        using Fixture fixture = new();
        StatsComponent stats = fixture.Session.State.Actors.Player.Stats;
        Track fatigue = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value));
        Track health = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        fatigue.SetCurrent(fatigue.MaximumValue * .9, clamp: true);
        health.SetCurrent(health.MaximumValue * .5, clamp: true);
        double fatigueBefore = fatigue.Current, healthBefore = health.Current;
        fixture.Open();
        long before = fixture.Now;
        fixture.Go(fixture.Step1);
        fixture.RunUntilStopped();

        Assert.Equal(fixture.Step1, fixture.Session.Expedition.Party);
        Assert.Equal("The party reaches your destination.", fixture.Session.Expedition.Message);
        long minutes = (fixture.Now - before) / 60;
        // One pixel on foot: the donor's minutes over its terrain, slowed at most by the night and the worst weather.
        int pixel = fixture.Session.Expedition.Policy.PixelMinutes(fixture.Step1, DaggerfallTransportMode.Foot, aboard: false);
        Assert.InRange(minutes, pixel, (long)Math.Ceiling(pixel * 1.25 * 1.5) + 1);
        // Each minute marched pays the idle rate (times the weather's toll), as walking anywhere does; nothing was
        // recovered before setting out.
        double Toll() => DaggerfallExpeditionTuning.Default.WeatherFatigue[fixture.Session.LocalWeather.State.ForClimate(
            fixture.Session.Expedition.Policy.Climate(fixture.Session.Expedition.Party))];
        Assert.Equal(fatigueBefore - minutes * (int)(DaggerfallLocomotionTuning.Classic.IdleFatiguePerGameMinute * Toll()), fatigue.Current, 1);
        Assert.Equal(healthBefore, health.Current);

        // A reckless march spends twice the fatigue a minute.
        fatigueBefore = fatigue.Current;
        before = fixture.Now;
        fixture.Submit(new { action = "expedition-mode", mode = "reckless" });
        fixture.Go(fixture.Step2);
        fixture.RunUntilStopped();
        minutes = (fixture.Now - before) / 60;
        Assert.Equal(fatigueBefore - minutes * (int)(DaggerfallLocomotionTuning.Classic.IdleFatiguePerGameMinute * 2 * Toll()), fatigue.Current, 1);
    }

    [Fact]
    public void A_camp_in_the_wild_recovers_by_the_rest_hour_a_halt_waits_untired_and_lodging_in_a_town_costs_gold()
    {
        using Fixture fixture = new();
        fixture.AddGold(100);
        StatsComponent stats = fixture.Session.State.Actors.Player.Stats;
        Track fatigue = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value));
        fixture.Open();
        fatigue.SetCurrent(fatigue.MaximumValue * .3, clamp: true);
        fixture.Submit(new { action = "expedition-mode", mode = "camp" });
        fixture.Submit(new { action = "expedition-speed", speed = 3 });
        fixture.RunGameMinutes(150);
        ulong paid = 100 - fixture.Session.State.Currency.Read().Gold;
        Assert.Equal((ulong)DaggerfallExpeditionTuning.Default.Lodging[fixture.Session.Site.ActiveSite!.Kind], paid);
        double lodged = fatigue.Current;
        Assert.True(lodged > fatigue.MaximumValue * .3);

        // In the wild a camp is free and recovers the rough ground's share; a halt there waits without tiring.
        fixture.Submit(new { action = "expedition-mode", mode = "cautious" });
        fixture.Go(fixture.Wild);
        fixture.RunUntilStopped();
        double halted = fatigue.Current;
        fixture.Submit(new { action = "expedition-mode", mode = "halt" });
        fixture.Submit(new { action = "expedition-speed", speed = 3 });
        fixture.RunGameMinutes(120);
        Assert.Equal(halted, fatigue.Current);
        ulong gold = fixture.Session.State.Currency.Read().Gold;
        fatigue.SetCurrent(fatigue.MaximumValue * .3, clamp: true);
        fixture.Submit(new { action = "expedition-mode", mode = "camp" });
        fixture.Submit(new { action = "expedition-speed", speed = 3 });
        fixture.RunGameMinutes(150);
        Assert.Equal(gold, fixture.Session.State.Currency.Read().Gold);
        double wild = fatigue.Current - fatigue.MaximumValue * .3;
        Assert.True(wild > 0 && wild < lodged - fatigue.MaximumValue * .3, $"A wild camp recovered {wild}, a lodging {lodged - fatigue.MaximumValue * .3}.");
    }

    [Fact]
    public void Entering_at_a_place_lands_at_its_exterior_and_entering_in_the_wilds_stands_over_the_partys_pixel()
    {
        using Fixture fixture = new();
        // The walk is long enough to camp on the way, perhaps in a village that asks for lodging.
        fixture.AddGold(1000);
        fixture.Open();
        fixture.Go(fixture.DestinationPixel, fixture.Destination.Site);
        fixture.RunUntilStopped(allowWarnings: true);
        Assert.Equal(fixture.DestinationPixel, fixture.Session.Expedition.Party);
        Assert.False((bool)fixture.Expedition()!["canLeave"]!);
        fixture.Submit(new { action = "expedition-leave" });
        Assert.True(fixture.Session.Expedition.Open);
        fixture.Submit(new { action = "expedition-enter" });
        Assert.False(fixture.Session.Expedition.Open);
        Assert.Equal(fixture.Destination.Site!.Value, fixture.Session.Site.Active);
        Assert.Equal("resume", fixture.Engine.PublishedNested("panelRequest", "panel"));
        Assert.Equal(new DaggerfallExteriorCellId(fixture.DestinationPixel.X, fixture.DestinationPixel.Y), fixture.Session.Exterior.CurrentExteriorCell());

        // Back on the map, a step into the wilds by the destination enters that town's exterior over the party's pixel.
        fixture.Open();
        DaggerfallTravelMapPixel wild = fixture.WildBeside(fixture.DestinationPixel, fixture.Destination.Site!.Value);
        fixture.Go(wild);
        fixture.RunUntilStopped(allowWarnings: true);
        fixture.Submit(new { action = "expedition-enter" });
        Assert.False(fixture.Session.Expedition.Open);
        Assert.Equal(fixture.Destination.Site!.Value, fixture.Session.Site.Active);
        Assert.Equal(new DaggerfallExteriorCellId(wild.X, wild.Y), fixture.Session.Exterior.CurrentExteriorCell());
        Assert.Equal("You set out on foot.", fixture.Engine.PublishedNested("hud", "message") ?? "You set out on foot.");
    }

    [Fact]
    public void An_hours_encounter_stops_the_map_until_the_player_enters_where_it_is_placed()
    {
        using Fixture fixture = new(encounter: true);
        fixture.Open();
        fixture.Go(fixture.Wild);
        fixture.RunUntilStopped();
        Assert.True(fixture.Session.Expedition.EncounterWaiting);
        Assert.True((bool)fixture.Expedition()!["encounter"]!);
        Assert.Equal("Something approaches! Enter to meet it.", fixture.Session.Expedition.Message);
        DaggerfallEncounterResolution pending = Assert.Single(DaggerfallSavePayload.Read(fixture.Session.CaptureSave()).Encounters.Resolved);
        Assert.NotNull(pending.ActorDefinition);
        DaggerfallSiteRecord host = fixture.Session.Expedition.HostAt(fixture.Session.Expedition.Party);
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(host.Id).LogicalId, pending.ProfileId);

        // Nothing goes on until the encounter is met.
        fixture.Submit(new { action = "expedition-speed", speed = 1 });
        Assert.Equal(0, fixture.Session.Expedition.Speed);
        fixture.Submit(new { action = "expedition-leave" });
        Assert.True(fixture.Session.Expedition.Open);

        // A save while it waits reopens the map on the same encounter, saying so.
        using (DaggerfallSession reloaded = fixture.Restore(fixture.Session.CaptureSave()))
            Assert.Equal((true, "Something approaches! Enter to meet it."), (reloaded.Expedition.EncounterWaiting, reloaded.Expedition.Message));

        fixture.Submit(new { action = "expedition-enter" });
        Assert.False(fixture.Session.Expedition.Open);
        Assert.Equal(host.Id, fixture.Session.Site.Active);
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(host.Id), fixture.Session.Sites.ActiveProfile);
    }

    [Fact]
    public void A_band_that_catches_the_party_is_queued_head_by_head_and_placed_beside_its_first_when_entered()
    {
        using Fixture fixture = new(bands: true);
        fixture.Open();
        fixture.Go(fixture.Wild);
        fixture.RunUntilStopped();
        // The developer stages it and reads it back: a wanderer close by that has not yet seen the party.
        DaggerfallExpeditionBandReadout readout = new(fixture.Session.Expedition);
        Assert.False(readout.StageBand(.3, 90, "sideways").Succeeded);
        DebugCommandResult staged = readout.StageBand(.3, 90, "wander");
        Assert.True(staged.Succeeded, staged.Message);
        using (JsonDocument bands = JsonDocument.Parse(staged.Message))
        {
            // Nearest first: the walk out may have staged another farther off.
            JsonElement only = bands.RootElement.GetProperty("bands")[0];
            Assert.Equal((false, false, true), (only.GetProperty("camp").GetBoolean(), only.GetProperty("spotted").GetBoolean(), only.GetProperty("seen").GetBoolean()));
            Assert.Equal(.3, only.GetProperty("distance").GetDouble(), 2);
        }
        fixture.Submit(new { action = "expedition-speed", speed = 3 });
        fixture.RunUntilStopped();
        Assert.EndsWith("Enter to fight.", fixture.Session.Expedition.Message);
        Assert.True(fixture.Session.Expedition.EncounterWaiting);
        DaggerfallEncounterResolution[] band = [.. DaggerfallSavePayload.Read(fixture.Session.CaptureSave()).Encounters.Resolved
            .Where(resolution => resolution.Band is not null)];
        Assert.NotEmpty(band);
        Assert.All(band, member => Assert.Equal(DaggerfallEncounterStaging.Caught, member.Band!.Staging));
        Assert.Single(band.Select(member => member.Band!.Id).Distinct());
        DaggerfallSiteRecord host = fixture.Session.Expedition.HostAt(fixture.Session.Expedition.Party);
        Assert.All(band, member => Assert.Equal(DaggerfallWorldProfileIds.Exterior(host.Id).LogicalId, member.ProfileId));

        // A save while it waits keeps the band's members and how they were staged.
        using (DaggerfallSession reloaded = fixture.Restore(fixture.Session.CaptureSave()))
            Assert.Equal(band.Select(member => member.Band), DaggerfallSavePayload.Read(reloaded.CaptureSave()).Encounters.Resolved
                .Where(member => member.Band is not null).Select(member => member.Band));

        fixture.Submit(new { action = "expedition-enter" });
        Assert.False(fixture.Session.Expedition.Open);
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(host.Id), fixture.Session.Sites.ActiveProfile);
    }

    [Fact]
    public void A_save_on_the_map_reopens_it_paused_with_the_party_its_leg_mode_and_pin()
    {
        using Fixture fixture = new();
        fixture.Open();
        fixture.Submit(new { action = "expedition-mode", mode = "reckless" });
        fixture.Go(fixture.DestinationPixel, fixture.Destination.Site);
        fixture.Submit(new { action = "expedition-pin", x = fixture.DestinationPixel.X, y = fixture.DestinationPixel.Y,
            region = fixture.Destination.Site!.Value.Region, index = fixture.Destination.Site!.Value.Index });
        fixture.Submit(new { action = "expedition-speed", speed = 3 });
        for (int update = 0; update < 40 && fixture.Session.Expedition.Party == fixture.OriginPixel; update++) fixture.Step();
        DaggerfallExpedition live = fixture.Session.Expedition;
        Assert.NotEqual(fixture.OriginPixel, live.Party);

        using DaggerfallSession restored = fixture.Restore(fixture.Session.CaptureSave());
        Assert.True(restored.Expedition.Open);
        Assert.Equal(0, restored.Expedition.Speed);
        Assert.Equal((live.Party, live.Position, live.Destination, live.Mode, live.Pin),
            (restored.Expedition.Party, restored.Expedition.Position, restored.Expedition.Destination, restored.Expedition.Mode, restored.Expedition.Pin));
        // The player's body stayed where the map was opened, so leaving needs an entry.
        Assert.False(restored.Expedition.CanLeave);
    }

    [Fact]
    public void The_game_menu_over_a_running_map_holds_its_time_fatigue_and_party_until_it_closes()
    {
        using Fixture fixture = new();
        fixture.Open();
        fixture.Go(fixture.Step2);
        fixture.Step();
        fixture.Submit(new { action = "menu", open = true });
        long held = fixture.Now;
        (double, double) position = fixture.Session.Expedition.Position;
        double fatigue = fixture.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value)).Current;
        for (int update = 0; update < 30; update++) fixture.Step();
        Assert.Equal(held, fixture.Now);
        Assert.Equal(position, fixture.Session.Expedition.Position);
        Assert.Equal(fatigue, fixture.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value)).Current);
        Assert.True(fixture.Session.Expedition.Running);

        fixture.Submit(new { action = "menu", open = false });
        for (int update = 0; update < 5; update++) fixture.Step();
        Assert.True(fixture.Now > held);
        Assert.NotEqual(position, fixture.Session.Expedition.Position);
    }

    [Fact]
    public void A_quest_popup_delivered_on_a_running_map_holds_it_until_it_is_dismissed()
    {
        using Fixture fixture = new(definitions: DeadlineDefinitions.Value);
        fixture.Session.State.Quests.Start(new("travel-deadline", "travel-deadline.txt", "travel-deadline",
            DaggerfallQuestLifecycle.Active, null, [], []));
        fixture.Open();
        fixture.Go(fixture.Step2);
        fixture.Step();
        DaggerfallQuestInstanceSave quest = Assert.Single(fixture.Session.State.Quests.All);
        var source = DeadlineDefinitions.Value.QuestSources.Quests[quest.SourceFile];
        fixture.Session.State.Quests.Messages.Popup(new DaggerfallQuestRuntimeInstance(quest, DaggerfallQuestTaskCompiler.Compile(source)), 10);
        long held = fixture.Now;
        (double, double) position = fixture.Session.Expedition.Position;
        double fatigue = fixture.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value)).Current;
        for (int update = 0; update < 30; update++) fixture.Step();
        Assert.Equal((held, position, fatigue), (fixture.Now, fixture.Session.Expedition.Position,
            fixture.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value)).Current));

        var popup = fixture.Session.State.Quests.Messages.Deliveries.Single(delivery => delivery.Delivery == DaggerfallQuestMessageDelivery.Popup);
        fixture.Submit(new { action = "quest-dismiss", questInstance = popup.InstanceId, questDelivery = popup.EntryId });
        Assert.False(fixture.Session.State.Quests.Messages.AwaitsReading);
        for (int update = 0; update < 5; update++) fixture.Step();
        Assert.True(fixture.Now > held);
        Assert.NotEqual(position, fixture.Session.Expedition.Position);
    }

    [Fact]
    public void Knightly_rank_six_grants_free_passage_on_a_hired_ship()
    {
        using Fixture fixture = new();
        fixture.Open();
        Assert.Equal(DaggerfallExpeditionTuning.Default.ShipHire, Convert.ToInt32(fixture.Expedition()!["shipFare"]));
        fixture.Session.State.Social.JoinGuild(368, 0);
        for (int day = 1; day <= 6; day++) fixture.Session.State.Social.PromoteGuild(368, day);
        fixture.Step();
        Assert.Equal(0, Convert.ToInt32(fixture.Expedition()!["shipFare"]));
    }

    [Fact]
    public void A_day_in_camp_on_the_map_expires_a_live_quest_clock_as_any_passing_time()
    {
        using Fixture fixture = new(definitions: DeadlineDefinitions.Value);
        fixture.AddGold(1000);
        fixture.Session.State.Quests.Start(new("travel-deadline", "travel-deadline.txt", "travel-deadline",
            DaggerfallQuestLifecycle.Active, null, [], []));
        fixture.Session.State.Quests.Advance(fixture.Session.State.Variables, DaggerfallCalendar.Start);
        Assert.True(Assert.Single(Assert.Single(fixture.Session.State.Quests.All).Clocks).Enabled);
        fixture.Open();
        fixture.Submit(new { action = "expedition-mode", mode = "camp" });
        fixture.Submit(new { action = "expedition-speed", speed = 3 });
        fixture.RunGameMinutes(3 * 24 * 60);
        Assert.DoesNotContain(fixture.Session.State.Quests.All, quest => quest.Lifecycle == DaggerfallQuestLifecycle.Active);
        var journal = fixture.Session.State.Quests.ReadPresentation(_ => throw new Exception("Ended quest must retain its own text."), DaggerfallCalendar.Start).Journal.Entries;
        Assert.Contains(journal, entry => entry.Text == "The deadline expired during travel.");
    }

    [Fact]
    public void A_guilds_teleport_chooses_a_known_place_on_the_map_and_lands_there()
    {
        using Fixture fixture = new();
        DaggerfallExpedition expedition = fixture.Session.Expedition;
        expedition.OfferGuildTeleport(7, () => true, fixture.OriginPixel);
        fixture.Step();
        Assert.True(expedition.Teleporting);
        Assert.Equal("Teleport", fixture.Expedition()!["enterLabel"]);
        Assert.False((bool)fixture.Expedition()!["canEnter"]!);
        fixture.Go(fixture.DestinationPixel, fixture.Destination.Site);
        Assert.Equal(fixture.Destination.Site, expedition.TeleportPlace);
        fixture.Submit(new { action = "expedition-enter" });
        Assert.False(expedition.Open);
        Assert.Equal(fixture.Destination.Site!.Value, fixture.Session.Site.Active);
    }

    private static long Absolute(RulesetSavePayload save)
    {
        var date = DaggerfallSavePayload.Read(save).Calendar;
        return new DaggerfallCalendar(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second).ToAbsoluteSeconds();
    }
    private static readonly SharedFixture<DaggerfallDefinitions> DeadlineDefinitions = new(() =>
    {
        JsonArray root = TestPayload.QuestRecords();
        root.Add(JsonNode.Parse("""
            {"name":"travel-deadline","displayName":"Travel deadline","sourceFile":"travel-deadline.txt","messages":[{"id":10,"lines":["The deadline expired during travel."]}],
            "blocks":[{"kind":"clock","firstLine":2,"lines":["clock _deadline_ 1"],"global":null},
            {"kind":"task","firstLine":3,"lines":["_deadline_ task:","log 10 step 1","end quest"],"global":null},
            {"kind":"headless","firstLine":6,"lines":["start timer _deadline_"],"global":null}]}
            """));
        return TestPayload.WithQuests(root);
    });
    private static readonly SharedFixture<(DaggerfallSiteProfile Profile, ResolvedCompositionIdentity Identity)> Inputs = new(() =>
        (ReadInputs(TestData.RepositoryRoot), TestSessions.Resolve(FullContent(TestData.RepositoryRoot),
            new GameBundleId("daggerfall.classic")).RequireComposition().Identity));

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallDefinitions definitions;
        private readonly List<string> releases = [];
        private readonly DaggerfallSiteProfiles profiles;
        private readonly bool encounter, bands;
        private ulong step = 1;
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; private set; } = null!;
        internal EngineContextFake Engine { get; private set; } = null!;
        internal DaggerfallSiteProfile Origin { get; }
        internal DaggerfallSiteProfile Destination { get; }
        internal long Now => Absolute(Session.CaptureSave());
        internal DaggerfallTravelMapPixel OriginPixel { get; }
        internal DaggerfallTravelMapPixel DestinationPixel { get; }

        /// <summary>The first and second pixels of the walk toward the destination, over land.</summary>
        internal DaggerfallTravelMapPixel Step1 { get; }
        internal DaggerfallTravelMapPixel Step2 { get; }

        /// <summary>A land pixel beside the origin with no location of its own, whose nearest location is the origin.</summary>
        internal DaggerfallTravelMapPixel Wild { get; }

        /// <param name="bands">The map's bands draw their lowest values (a published foe), as the hour's unseen encounters never come.</param>
        internal Fixture(bool encounter = false, DaggerfallDefinitions? definitions = null, bool bands = false)
        {
            this.definitions = definitions ?? TestPayload.Definitions; this.encounter = encounter; this.bands = bands;
            var source = Inputs.Value.Profile;
            var sites = this.definitions.Locations.Records.Where(site => site.Exterior is not null && site.Kind == DaggerfallSiteKind.TownCity
                && this.definitions.Grids.ClimateAtWorldPixel(site.MapPixelX, site.MapPixelY).Value is >= 224 and <= 232).ToArray();
            var origin = sites.First(site => Path(site, sites).Count >= 3 && Path(site, sites).All(Land));
            var destination = Nearest(origin, sites);
            OriginPixel = new(origin.MapPixelX, origin.MapPixelY);
            DestinationPixel = new(destination.MapPixelX, destination.MapPixelY);
            IReadOnlyList<DaggerfallTravelMapPixel> path = DaggerfallTravelPolicy.Path(OriginPixel, DestinationPixel);
            (Step1, Step2) = (path[0], path[1]);
            Wild = WildBeside(OriginPixel, origin.Id);
            Origin = Profile(origin.Id, "travel-origin"); Destination = Profile(destination.Id, "travel-arrival");
            profiles = new([Origin, Destination]);
            Session = Create(null); Session.Site.Discover(destination.Id);
            if (Session.State.Currency.Read().Gold > 0) Assert.True(Session.State.Currency.TrySpendCarried(Session.State.Currency.Read().Gold));
            DaggerfallSiteProfile Profile(DaggerfallSiteId id, string name) => new(
                new ProjectFacts(new WorldPoint(name == "travel-origin" ? 1 : 3, 1, 1), new Dictionary<long, AuthoredActor>()), source.Geometry, source.WorldAppearance, source.InitialLook, source.Materials, new Dictionary<long, NormalizedActorSprite>(),
                source.MobileSprites, source.Audio, source.ClassicPresentation, id,
                profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: DaggerfallWorldProfileIds.Exterior(id).LogicalId,
                terrainTextures: source.TerrainTextures, billboardSprites: source.BillboardSprites);
        }
        private DaggerfallSession Create(RulesetSavePayload? save)
        {
            ContentFake content = new(releases);
            PopulateContent(content, Origin); PopulateContent(content, Destination);
            PopulateTerrainContent(content, Origin); PopulateTerrainContent(content, Destination);
            Spatial = SpatialFake.Create(Origin.Geometry.Spatial[0].Sha256, releases);
            var engine = Engine = EngineContextFake.Create(content, Spatial.Service,
                new AppearanceFake(releases), random: encounter ? TravelEncounterRandom.CreateEncounter() : bands ? TravelBandRandom.CreateBands() : LodgingRandom.Create());
            DaggerfallSkyMedia sky = DaggerfallSkyMedia.Read(TestSessions.Data(FullContent(TestData.RepositoryRoot)));
            DaggerfallSessionComposition composition = new(definitions, Origin, DaggerfallTuning.Defaults, Inputs.Value.Identity)
                { Profiles = profiles, Sky = sky };
            return save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
        }
        internal DaggerfallSession Restore(RulesetSavePayload save) => Create(save);
        internal void Step() => Session.Update(new ProductUpdate(OuterUpdate(step++), []));
        internal void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(step++), [Ui(JsonSerializer.Serialize(action))]));
        internal void Open() => Submit(new { action = "expedition-open" });

        /// <summary>Heads the party for a pixel, or the place there, and sets the map running at its fastest.</summary>
        internal void Go(DaggerfallTravelMapPixel pixel, DaggerfallSiteId? place = null)
        {
            Submit(place is { } id ? new { action = "expedition-go", x = pixel.X, y = pixel.Y, region = id.Region, index = id.Index }
                : (object)new { action = "expedition-go", x = pixel.X, y = pixel.Y });
            Submit(new { action = "expedition-speed", speed = 3 });
        }

        /// <summary>
        /// Runs the map until it pauses. With <paramref name="allowWarnings"/>, a party warned of its fatigue camps for
        /// eight hours and walks on, as a player would, until it arrives or meets something.
        /// </summary>
        internal void RunUntilStopped(bool allowWarnings = false)
        {
            for (int update = 0; update < 50_000; update++)
            {
                if (!Session.Expedition.Running)
                {
                    if (!allowWarnings || Session.Expedition.Destination is null || Session.Expedition.EncounterWaiting) return;
                    Submit(new { action = "expedition-mode", mode = "camp" });
                    Submit(new { action = "expedition-speed", speed = 3 });
                    RunGameMinutes(8 * 60);
                    Submit(new { action = "expedition-mode", mode = "cautious" });
                    Submit(new { action = "expedition-speed", speed = 3 });
                }
                Step();
            }
            throw new InvalidOperationException("The map never stopped.");
        }

        /// <summary>Runs the map until at least this many game minutes have passed.</summary>
        internal void RunGameMinutes(long minutes)
        {
            long end = Now + minutes * 60;
            for (int update = 0; update < 50_000 && Now < end && Session.Expedition.Running; update++) Step();
            Assert.True(Now >= end, "The map stopped before its time had passed.");
        }

        /// <summary>The expedition the snapshot publishes, or null while the map is closed.</summary>
        internal Dictionary<string, object?>? Expedition() =>
            Engine.Published() is Dictionary<string, object?> snapshot ? snapshot["expedition"] as Dictionary<string, object?> : null;

        /// <summary>A land pixel beside a location, with none of its own, whose nearest location is that one.</summary>
        internal DaggerfallTravelMapPixel WildBeside(DaggerfallTravelMapPixel at, DaggerfallSiteId site)
        {
            DaggerfallExpedition expedition = Session?.Expedition ?? new DaggerfallExpedition(
                new DaggerfallTravelPolicy(new DaggerfallSiteContext(definitions.Locations), definitions.Grids, DaggerfallTuning.Defaults.Transport),
                new DaggerfallSiteContext(definitions.Locations), DaggerfallExpeditionTuning.Default, new NoWorld());
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) })
            {
                DaggerfallTravelMapPixel pixel = new(at.X + dx, at.Y + dy);
                if (Land(pixel) && expedition.LocationAt(pixel) is null && expedition.HostAt(pixel).Id == site) return pixel;
            }
            throw new InvalidOperationException($"No wild land pixel beside {at} is nearest to it.");
        }

        private bool Land(DaggerfallTravelMapPixel pixel) => definitions.Grids.ClimateAtWorldPixel(pixel.X, pixel.Y).Value is >= 224 and <= 232;

        private static DaggerfallSiteRecord Nearest(DaggerfallSiteRecord origin, IEnumerable<DaggerfallSiteRecord> sites) => sites
            .Where(site => site.MapPixelX != origin.MapPixelX || site.MapPixelY != origin.MapPixelY)
            .OrderBy(site => Math.Abs(site.MapPixelX - origin.MapPixelX) + Math.Abs(site.MapPixelY - origin.MapPixelY)).First();

        private IReadOnlyList<DaggerfallTravelMapPixel> Path(DaggerfallSiteRecord origin, IEnumerable<DaggerfallSiteRecord> sites)
        {
            DaggerfallSiteRecord destination = Nearest(origin, sites);
            return DaggerfallTravelPolicy.Path(new(origin.MapPixelX, origin.MapPixelY), new(destination.MapPixelX, destination.MapPixelY));
        }
        internal void AddGold(ulong quantity)
        {
            var item = definitions.RequireItem(new DaggerfallItemId("gold-piece"));
            var stack = InventoryStackId.Parse("travel.gold");
            Session.State.Inventory.Grant(new(new InventoryItemId(item.Id.Value), stack, quantity));
            if (!Session.State.ItemInstances.ContainsStack(DaggerfallItemOwner.Player, stack))
                Session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, stack, DaggerfallItemInstanceMetadata.Default(item, DaggerfallItemOwner.Player));
        }
        public void Dispose() => Session.Dispose();

        private static void PopulateTerrainContent(ContentFake content, DaggerfallSiteProfile profile)
        {
            foreach (NormalizedTerrainTexture texture in profile.TerrainTextures.Values)
                content.Add(texture.TexturePath, texture.TextureSha256);
        }
    }
}

/// <summary>Surroundings that answer nothing, for reading the map's places without a session.</summary>
internal sealed class NoWorld : IDaggerfallExpeditionSurroundings
{
    public DaggerfallCalendar Calendar => DaggerfallCalendar.Start;
    public DaggerfallWeatherKind Weather(int climate) => DaggerfallWeatherKind.Sunny;
    public DaggerfallTransportMode Transport => DaggerfallTransportMode.Foot;
    public bool OwnsShip => false;
    public bool FreeShipPassage => false;
    public bool TrySpendGold(int amount) => false;
    public double FatigueShare => 1;
    public bool Exhausted => false;
    public bool Defeated => false;
    public bool HeldElsewhere => false;
    public int Roll(string key, int minimum, int maximum) => minimum;
    public int PlayerLevel => 1;
    public double Scouting => 0;
    public double Concealment => 0;
    public void MeetBand(DaggerfallExpeditionBand band, DaggerfallTravelMapPixel pixel, DaggerfallEncounterStaging staging) { }
    public long Pass(DaggerfallExpeditionStretch stretch) => 0;
    public void RecoverCampHour(bool sheltered) { }
    public void Collapse(bool inWater) { }
    public bool EncounterArrives(DaggerfallTravelMapPixel pixel, bool atLocation, long minute) => false;
}

internal class TravelBandRandom : LodgingRandom
{
    internal static IRandomService CreateBands() => DispatchProxy.Create<IRandomService, TravelBandRandom>();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) =>
        method?.Name == nameof(IRandomService.DrawKeyed) && arguments![0] is KeyedRngRequest { Scope: "daggerfall.expedition.bands" } request
            ? new KeyedRngReceipt(request.Minimum) : base.Invoke(method, arguments);
}

internal class TravelEncounterRandom : LodgingRandom
{
    internal static IRandomService CreateEncounter() => DispatchProxy.Create<IRandomService, TravelEncounterRandom>();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
        ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum) : base.Invoke(method, arguments);
}
