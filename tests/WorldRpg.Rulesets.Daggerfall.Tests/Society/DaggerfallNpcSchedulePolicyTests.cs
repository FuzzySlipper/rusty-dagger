using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Data-driven NPC daily schedules and weather sheltering (#11085 / #11100):
/// schedule catalog validation, single-source opening hours, deterministic per-slot jitter,
/// role-staggered storm sheltering, late-night underworld pulse, custody/post exclusions,
/// and exterior population gating.
/// </summary>
public sealed class DaggerfallNpcSchedulePolicyTests
{
    private static readonly DaggerfallSiteId DaggerfallTown = new(17, 0);

    [Fact]
    public void Base_pack_publishes_all_eight_schedule_profiles_and_assigns_valid_schedules_to_town_and_cast_specs()
    {
        DaggerfallActorCatalog catalog = TestPayload.Definitions.ActorCatalog;
        string[] expectedProfiles =
        [
            "shopkeeper",
            "guild-member",
            "tavern-staff",
            "street-walker",
            "city-guard",
            "court-noble",
            "underworld-night",
            "always-stationed",
        ];

        foreach (string id in expectedProfiles)
            Assert.True(catalog.Schedules.ContainsKey(id), $"Missing schedule profile '{id}'.");

        Assert.All(
            catalog.Specs.Values.Where(s => s.Family is ActorSpawnerFamily.Town or ActorSpawnerFamily.Cast),
            spec =>
            {
                Assert.False(string.IsNullOrWhiteSpace(spec.Schedule), $"Spec '{spec.Id}' is missing a schedule.");
                Assert.True(catalog.Schedules.ContainsKey(spec.Schedule!), $"Spec '{spec.Id}' references unknown schedule '{spec.Schedule}'.");
            });
    }

    [Fact]
    public void Actor_catalog_validation_rejects_unknown_spec_schedule_and_schedule_on_template()
    {
        // 1. Unknown schedule ID on a spawner spec -> load-time validation error.
        JsonObject badSpecRoot = JsonNode.Parse(TestPayload.CombinedBytes)!.AsObject();
        JsonArray townsfolk = badSpecRoot["actorSpecsTownsfolk"]!["specs"]!.AsArray();
        townsfolk[0]!["schedule"] = "nonexistent-schedule";
        DaggerfallContentException badSpec = Assert.Throws<DaggerfallContentException>(
            () => DaggerfallBaseContent.Read(JsonSerializer.SerializeToUtf8Bytes(badSpecRoot), TestPayload.Data));
        Assert.Contains("unknown schedule 'nonexistent-schedule'", badSpec.Message, StringComparison.Ordinal);

        // 2. Schedule placed on an actor template -> forbidden instance-only field error.
        JsonObject badTemplateRoot = JsonNode.Parse(TestPayload.CombinedBytes)!.AsObject();
        JsonArray roles = badTemplateRoot["actorTemplateRoles"]!["templates"]!.AsArray();
        roles[0]!["schedule"] = "street-walker";
        DaggerfallContentException badTemplate = Assert.Throws<DaggerfallContentException>(
            () => DaggerfallBaseContent.Read(JsonSerializer.SerializeToUtf8Bytes(badTemplateRoot), TestPayload.Data));
        Assert.Contains("instance-only field 'schedule'", badTemplate.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]  // Alchemist (07:00-22:00)
    [InlineData(2)]  // Armorer (09:00-19:00)
    [InlineData(3)]  // Bank (08:00-15:00)
    [InlineData(5)]  // Bookseller (09:00-21:00)
    [InlineData(6)]  // ClothingStore (10:00-19:00)
    [InlineData(7)]  // FurnitureStore (10:00-20:00)
    [InlineData(8)]  // GemStore (09:00-18:00)
    [InlineData(9)]  // GeneralStore (06:00-23:00)
    [InlineData(10)] // Library (09:00-23:00)
    [InlineData(11)] // GuildHall (11:00-23:00)
    [InlineData(12)] // PawnShop (09:00-20:00)
    [InlineData(13)] // WeaponSmith (09:00-20:00)
    [InlineData(16)] // Palace (10:00-16:00)
    public void Shopkeeper_and_building_hours_align_exactly_with_DaggerfallBuildingEntryPolicy(int buildingType)
    {
        DaggerfallActorScheduleProfile profile = TestPayload.Definitions.ActorCatalog.Schedules["shopkeeper"];
        Assert.True(DaggerfallBuildingEntryPolicy.TryGetOpeningHours(buildingType, out _, out _));

        for (int hour = 0; hour < 24; hour++)
        {
            bool policyOpen = DaggerfallBuildingEntryPolicy.IsOpenHour(buildingType, hour);
            DaggerfallScheduleDecision decision = DaggerfallNpcSchedulePolicy.Evaluate(
                worldSocialSeed: 42UL,
                siteId: DaggerfallTown,
                slotKey: $"shop/slot/{buildingType}",
                profile: profile,
                minuteOfDay: hour * 60 + 15,
                weather: DaggerfallWeatherKind.Sunny,
                buildingType: buildingType);

            if (policyOpen)
            {
                Assert.Equal(DaggerfallSchedulePlacement.InteriorBuilding, decision.Placement);
                Assert.Equal(DaggerfallScheduleBuildingTarget.HomePost, decision.BuildingTarget);
                Assert.Equal("open-hours", decision.Reason);
            }
            else
            {
                Assert.NotEqual(DaggerfallScheduleBuildingTarget.HomePost, decision.BuildingTarget);
            }
        }

        // Sun's Rest closes shops (not palaces/guild halls).
        if (DaggerfallNpcServiceFacts.IsShop(buildingType))
        {
            DaggerfallScheduleDecision holiday = DaggerfallNpcSchedulePolicy.Evaluate(
                worldSocialSeed: 42UL,
                siteId: DaggerfallTown,
                slotKey: $"shop/slot/{buildingType}",
                profile: profile,
                minuteOfDay: 12 * 60,
                weather: DaggerfallWeatherKind.Sunny,
                buildingType: buildingType,
                holidayId: DaggerfallBuildingEntryPolicy.SunsRestHoliday);
            Assert.NotEqual(DaggerfallScheduleBuildingTarget.HomePost, holiday.BuildingTarget);
        }
    }

    [Fact]
    public void Street_walker_applies_deterministic_35m_jitter_so_dawn_and_dusk_transition_gradually()
    {
        DaggerfallActorScheduleProfile walker = TestPayload.Definitions.ActorCatalog.Schedules["street-walker"];
        HashSet<int> jitters = [];
        int outAt0545 = 0, outAt0615 = 0, outAt1200 = 0, outAt1745 = 0, outAt1815 = 0, outAt2000 = 0;
        int eveningTavernAt2000 = 0;

        for (int i = 0; i < 64; i++)
        {
            string slot = $"population/daggerfall/dynamic/{i:D3}";
            DaggerfallScheduleDecision at0545 = DaggerfallNpcSchedulePolicy.Evaluate(99UL, DaggerfallTown, slot, walker, 5 * 60 + 45, DaggerfallWeatherKind.Sunny);
            DaggerfallScheduleDecision at0615 = DaggerfallNpcSchedulePolicy.Evaluate(99UL, DaggerfallTown, slot, walker, 6 * 60 + 15, DaggerfallWeatherKind.Sunny);
            DaggerfallScheduleDecision at1200 = DaggerfallNpcSchedulePolicy.Evaluate(99UL, DaggerfallTown, slot, walker, 12 * 60, DaggerfallWeatherKind.Sunny);
            DaggerfallScheduleDecision at1745 = DaggerfallNpcSchedulePolicy.Evaluate(99UL, DaggerfallTown, slot, walker, 17 * 60 + 45, DaggerfallWeatherKind.Sunny);
            DaggerfallScheduleDecision at1815 = DaggerfallNpcSchedulePolicy.Evaluate(99UL, DaggerfallTown, slot, walker, 18 * 60 + 15, DaggerfallWeatherKind.Sunny);
            DaggerfallScheduleDecision at2000 = DaggerfallNpcSchedulePolicy.Evaluate(99UL, DaggerfallTown, slot, walker, 20 * 60, DaggerfallWeatherKind.Sunny);

            Assert.InRange(at1200.JitterMinutes, -35, 35);
            jitters.Add(at1200.JitterMinutes);
            if (at0545.Placement == DaggerfallSchedulePlacement.ExteriorStreet) outAt0545++;
            if (at0615.Placement == DaggerfallSchedulePlacement.ExteriorStreet) outAt0615++;
            if (at1200.Placement == DaggerfallSchedulePlacement.ExteriorStreet) outAt1200++;
            if (at1745.Placement == DaggerfallSchedulePlacement.ExteriorStreet) outAt1745++;
            if (at1815.Placement == DaggerfallSchedulePlacement.ExteriorStreet) outAt1815++;
            if (at2000.Placement == DaggerfallSchedulePlacement.ExteriorStreet) outAt2000++;
            if (at2000.Placement == DaggerfallSchedulePlacement.InteriorBuilding
                && at2000.BuildingTarget == DaggerfallScheduleBuildingTarget.Tavern)
                eveningTavernAt2000++;
        }

        Assert.True(jitters.Any(j => j < 0) && jitters.Any(j => j > 0), "Expected both negative and positive jitter offsets.");
        Assert.InRange(outAt0545, 1, 63);
        Assert.True(outAt0615 > outAt0545);
        Assert.Equal(64, outAt1200);
        Assert.InRange(outAt1745, 1, 63);
        Assert.True(outAt1815 < outAt1745);
        Assert.Equal(0, outAt2000);
        Assert.InRange(eveningTavernAt2000, 10, 50);
    }

    [Fact]
    public void Storm_sheltering_staggers_across_2_to_10_minutes_and_respects_role_hardiness()
    {
        DaggerfallActorScheduleProfile walker = TestPayload.Definitions.ActorCatalog.Schedules["street-walker"];
        DaggerfallActorScheduleProfile guard = TestPayload.Definitions.ActorCatalog.Schedules["city-guard"];
        DaggerfallActorScheduleProfile underworld = TestPayload.Definitions.ActorCatalog.Schedules["underworld-night"];

        int shelteredAt0 = 0, shelteredAt4 = 0, shelteredAt10 = 0;
        for (int i = 0; i < 64; i++)
        {
            string slot = $"population/storm/{i:D3}";
            DaggerfallScheduleDecision t0 = DaggerfallNpcSchedulePolicy.Evaluate(7UL, DaggerfallTown, slot, walker, 12 * 60, DaggerfallWeatherKind.Thunder, weatherElapsedMinutes: 0);
            DaggerfallScheduleDecision t4 = DaggerfallNpcSchedulePolicy.Evaluate(7UL, DaggerfallTown, slot, walker, 12 * 60, DaggerfallWeatherKind.Thunder, weatherElapsedMinutes: 4);
            DaggerfallScheduleDecision t10 = DaggerfallNpcSchedulePolicy.Evaluate(7UL, DaggerfallTown, slot, walker, 12 * 60, DaggerfallWeatherKind.Thunder, weatherElapsedMinutes: 10);

            Assert.InRange(t0.StormDelayMinutes, 2, 10);
            if (t0.Placement != DaggerfallSchedulePlacement.ExteriorStreet) shelteredAt0++;
            if (t4.Placement != DaggerfallSchedulePlacement.ExteriorStreet) shelteredAt4++;
            if (t10.Placement != DaggerfallSchedulePlacement.ExteriorStreet) shelteredAt10++;
        }

        Assert.Equal(0, shelteredAt0);
        Assert.InRange(shelteredAt4, 1, 63);
        Assert.Equal(64, shelteredAt10);

        // City guard stays on duty in thunder and at 02:00.
        DaggerfallScheduleDecision guardNightStorm = DaggerfallNpcSchedulePolicy.Evaluate(7UL, DaggerfallTown, "guard/000", guard, 2 * 60, DaggerfallWeatherKind.Thunder);
        Assert.Equal(DaggerfallSchedulePlacement.ExteriorStreet, guardNightStorm.Placement);

        // Underworld-night walks at 23:30 in rain, shelters in thunder after delay, and shelters at midday.
        DaggerfallScheduleDecision uwNightRain = DaggerfallNpcSchedulePolicy.Evaluate(7UL, DaggerfallTown, "uw/001", underworld, 23 * 60 + 30, DaggerfallWeatherKind.Rain);
        DaggerfallScheduleDecision uwNightThunder = DaggerfallNpcSchedulePolicy.Evaluate(7UL, DaggerfallTown, "uw/001", underworld, 23 * 60 + 30, DaggerfallWeatherKind.Thunder, weatherElapsedMinutes: 15);
        DaggerfallScheduleDecision uwDay = DaggerfallNpcSchedulePolicy.Evaluate(7UL, DaggerfallTown, "uw/001", underworld, 12 * 60, DaggerfallWeatherKind.Sunny);
        Assert.Equal(DaggerfallSchedulePlacement.ExteriorStreet, uwNightRain.Placement);
        Assert.NotEqual(DaggerfallSchedulePlacement.ExteriorStreet, uwNightThunder.Placement);
        Assert.Equal(DaggerfallSchedulePlacement.ShelteredInHome, uwDay.Placement);
    }

    [Fact]
    public void Questors_office_holders_party_members_and_overland_travellers_are_excluded_from_ambient_schedule_pulls()
    {
        DaggerfallActorScheduleProfile shopkeeper = TestPayload.Definitions.ActorCatalog.Schedules["shopkeeper"];
        DaggerfallActorScheduleProfile walker = TestPayload.Definitions.ActorCatalog.Schedules["street-walker"];

        // Pick a slot that would otherwise visit the tavern at 20:30 when GeneralStore (type 8, 09-18) is closed.
        string tavernSlot = Enumerable.Range(0, 64)
            .Select(i => $"merchant/slot/{i}")
            .First(slot => DaggerfallNpcSchedulePolicy.Evaluate(11UL, DaggerfallTown, slot, shopkeeper, 20 * 60 + 30, DaggerfallWeatherKind.Sunny, buildingType: 8).BuildingTarget == DaggerfallScheduleBuildingTarget.Tavern);

        DaggerfallScheduleDecision questor = DaggerfallNpcSchedulePolicy.Evaluate(11UL, DaggerfallTown, tavernSlot, shopkeeper, 20 * 60 + 30, DaggerfallWeatherKind.Sunny, buildingType: 8, isActiveQuestor: true);
        DaggerfallScheduleDecision officer = DaggerfallNpcSchedulePolicy.Evaluate(11UL, DaggerfallTown, tavernSlot, shopkeeper, 20 * 60 + 30, DaggerfallWeatherKind.Sunny, buildingType: 8, isOfficeHolder: true);
        Assert.True(questor.Pinned);
        Assert.Equal(DaggerfallScheduleBuildingTarget.HomePost, questor.BuildingTarget);
        Assert.Equal("active-questor", questor.Reason);
        Assert.True(officer.Pinned);
        Assert.Equal(DaggerfallScheduleBuildingTarget.HomePost, officer.BuildingTarget);
        Assert.Equal("office-holder", officer.Reason);

        // Party members and overland-band travellers are excluded even during midday sunshine.
        DaggerfallScheduleDecision withParty = DaggerfallNpcSchedulePolicy.Evaluate(11UL, DaggerfallTown, "walker/1", walker, 12 * 60, DaggerfallWeatherKind.Sunny, isWithParty: true);
        DaggerfallScheduleDecision onBand = DaggerfallNpcSchedulePolicy.Evaluate(11UL, DaggerfallTown, "walker/1", walker, 12 * 60, DaggerfallWeatherKind.Sunny, isOnOverlandBand: true);
        Assert.True(withParty.Pinned);
        Assert.Equal(DaggerfallSchedulePlacement.ShelteredInHome, withParty.Placement);
        Assert.Equal("with-party", withParty.Reason);
        Assert.True(onBand.Pinned);
        Assert.Equal(DaggerfallSchedulePlacement.ShelteredInHome, onBand.Placement);
        Assert.Equal("on-overland-band", onBand.Reason);
    }

    [Fact]
    public void Exterior_source_population_keeps_guards_and_underworld_pulse_at_night_and_shelters_walkers_in_storms()
    {
        DaggerfallSiteRecord town = TestPayload.Definitions.Locations.Records.Single(s => s.Id.Region == 17 && s.Name == "Daggerfall" && s.Exterior is not null);
        // 24 placements: index 0 is a guard, 1..23 are non-guard townsfolk (populated enough to seed underworld pulse).
        DaggerfallPopulationPlacement[] placements =
        [
            .. Enumerable.Range(0, 24).Select(i => new DaggerfallPopulationPlacement(
                $"population/daggerfall/dynamic/{i:D3}",
                new WorldPoint(i, 0f, 3f),
                210,
                4,
                0,
                (byte)(i % 2 == 1 ? 0x20 : 0),
                (ushort)(1000 + i * 19))
            {
                IsGuard = i == 0,
            }),
        ];

        using CivilianStage stage = new(town, placements);
        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        int minuteOfDay = 12 * 60;
        DaggerfallWeatherKind weather = DaggerfallWeatherKind.Sunny;
        int weatherElapsed = int.MaxValue;

        DaggerfallSourcePopulation population = new(
            stage.Npcs,
            stage.Actors,
            player,
            TestPayload.Definitions,
            DaggerfallCivilianWanderTuning.Classic,
            stage.Civilians,
            () => stage.Profile,
            () => stage.Profile.ProfileKey,
            () => stage.Appearance,
            () => true,
            () => true,
            p => p,
            p => p,
            _ => town,
            () => minuteOfDay is >= 360 and < 1080,
            () => false,
            _ => false,
            (_, _, _, _) => { },
            _ => { },
            isPlacementScheduledOutdoors: (siteId, placement, _, total) =>
                DaggerfallNpcSchedulePolicy.EvaluateExteriorPlacement(
                    0UL, siteId, placement.Id, placement.IsGuard, total,
                    TestPayload.Definitions.ActorCatalog, minuteOfDay, weather, weatherElapsed).Placement
                == DaggerfallSchedulePlacement.ExteriorStreet);

        // 1. Midday sunny: guard + all street-walkers are active; underworld-night slots stay sheltered.
        population.Reconcile();
        HashSet<string> middayActive = stage.Npcs.All
            .Where(n => DaggerfallCivilians.IsSourcePopulation(n) && n.Presence == DaggerfallNpcPresence.Active)
            .Select(n => n.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("population/daggerfall/dynamic/000", middayActive);
        Assert.True(middayActive.Count >= 18);

        // 2. Settled midday thunderstorm: guard remains outdoors; street-walkers shelter indoors.
        weather = DaggerfallWeatherKind.Thunder;
        weatherElapsed = 20;
        population.Reconcile();
        HashSet<string> stormActive = stage.Npcs.All
            .Where(n => DaggerfallCivilians.IsSourcePopulation(n) && n.Presence == DaggerfallNpcPresence.Active)
            .Select(n => n.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(["population/daggerfall/dynamic/000"], [.. stormActive]);

        // 3. Clear late night (23:30): guard stays out AND any seeded underworld-night slot appears while ordinary walkers sleep.
        weather = DaggerfallWeatherKind.Sunny;
        minuteOfDay = 23 * 60 + 30;
        population.Reconcile();
        HashSet<string> nightActive = stage.Npcs.All
            .Where(n => DaggerfallCivilians.IsSourcePopulation(n) && n.Presence == DaggerfallNpcPresence.Active)
            .Select(n => n.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("population/daggerfall/dynamic/000", nightActive);
        Assert.True(nightActive.Count < middayActive.Count);
        foreach (string key in nightActive)
        {
            string expectedSchedule = DaggerfallNpcSchedulePolicy.ResolveExteriorScheduleId(0UL, town.Id, key, key.EndsWith("000", StringComparison.Ordinal), placements.Length);
            Assert.True(expectedSchedule is DaggerfallNpcSchedulePolicy.CityGuardScheduleId or DaggerfallNpcSchedulePolicy.UnderworldNightScheduleId);
        }
    }

    [Fact]
    public void Sustained_rain_to_thunder_or_snow_staggers_underworld_while_keeping_rain_sheltered_walkers_indoors()
    {
        DaggerfallActorScheduleProfile walker = TestPayload.Definitions.ActorCatalog.Schedules["street-walker"];
        DaggerfallActorScheduleProfile underworld = TestPayload.Definitions.ActorCatalog.Schedules["underworld-night"];

        // 1. Midday escalation (Sunny -> Rain for 15m -> Thunder): rain-sensitive street-walkers are already sheltered
        //    and must stay sheltered on the first Thunder tick (no pop-back-out!).
        DaggerfallWeatherShelterClock middayClock = new();
        _ = middayClock.Observe(DaggerfallWeatherKind.Sunny, 720L);
        _ = middayClock.Observe(DaggerfallWeatherKind.Rain, 725L);
        DaggerfallWeatherShelterElapsed afterSustainedRain = middayClock.Observe(DaggerfallWeatherKind.Rain, 740L);
        Assert.Equal(15, afterSustainedRain.PrecipElapsedMinutes);
        Assert.Equal(0, afterSustainedRain.SevereElapsedMinutes);

        DaggerfallWeatherShelterElapsed thunderAtMidday = middayClock.Observe(DaggerfallWeatherKind.Thunder, 740L);
        Assert.Equal(15, thunderAtMidday.PrecipElapsedMinutes);
        Assert.Equal(0, thunderAtMidday.SevereElapsedMinutes);

        for (int i = 0; i < 32; i++)
        {
            DaggerfallScheduleDecision decision = DaggerfallNpcSchedulePolicy.Evaluate(
                7UL, DaggerfallTown, $"walker/midday/{i:D3}", walker, 12 * 60 + 20, DaggerfallWeatherKind.Thunder,
                weatherElapsedMinutes: thunderAtMidday.PrecipElapsedMinutes,
                severeWeatherElapsedMinutes: thunderAtMidday.SevereElapsedMinutes);
            Assert.NotEqual(DaggerfallSchedulePlacement.ExteriorStreet, decision.Placement);
        }

        // 2. Late-night escalation (arrival in Rain at 23:30, sustained 15m, then escalating to Thunder or Snow):
        //    underworld-night NPCs stay outdoors in Rain, then stagger indoors over 6-10 minutes when Thunder/Snow starts.
        foreach (DaggerfallWeatherKind severeKind in new[] { DaggerfallWeatherKind.Thunder, DaggerfallWeatherKind.Snow })
        {
            DaggerfallWeatherShelterClock nightClock = new();
            // Arrival in Rain settles PrecipElapsedMinutes = int.MaxValue while SevereElapsedMinutes = 0.
            DaggerfallWeatherShelterElapsed arrivalRain = nightClock.Observe(DaggerfallWeatherKind.Rain, 1410L);
            Assert.Equal(int.MaxValue, arrivalRain.PrecipElapsedMinutes);
            Assert.Equal(0, arrivalRain.SevereElapsedMinutes);

            // Sustained rain for 15 minutes still keeps underworld-night outdoors.
            DaggerfallWeatherShelterElapsed sustainedRain = nightClock.Observe(DaggerfallWeatherKind.Rain, 1425L);
            Assert.Equal(int.MaxValue, sustainedRain.PrecipElapsedMinutes);
            Assert.Equal(0, sustainedRain.SevereElapsedMinutes);

            // Escalation Rain -> Thunder/Snow starts the severe-weather clock at 0!
            DaggerfallWeatherShelterElapsed severeStart = nightClock.Observe(severeKind, 1425L);
            Assert.Equal(int.MaxValue, severeStart.PrecipElapsedMinutes);
            Assert.Equal(0, severeStart.SevereElapsedMinutes);

            DaggerfallWeatherShelterElapsed severeMid = nightClock.Observe(severeKind, 1433L); // +8 min
            Assert.Equal(8, severeMid.SevereElapsedMinutes);

            DaggerfallWeatherShelterElapsed severeSettled = nightClock.Observe(severeKind, 1436L); // +11 min
            Assert.Equal(11, severeSettled.SevereElapsedMinutes);

            int outAtStart = 0, outAtMid = 0, outAtSettled = 0;
            for (int i = 0; i < 48; i++)
            {
                string slot = $"uw/night/{i:D3}";
                DaggerfallScheduleDecision d0 = DaggerfallNpcSchedulePolicy.Evaluate(
                    7UL, DaggerfallTown, slot, underworld, 23 * 60 + 45, severeKind,
                    weatherElapsedMinutes: severeStart.PrecipElapsedMinutes,
                    severeWeatherElapsedMinutes: severeStart.SevereElapsedMinutes);
                DaggerfallScheduleDecision d8 = DaggerfallNpcSchedulePolicy.Evaluate(
                    7UL, DaggerfallTown, slot, underworld, 23 * 60 + 53, severeKind,
                    weatherElapsedMinutes: severeMid.PrecipElapsedMinutes,
                    severeWeatherElapsedMinutes: severeMid.SevereElapsedMinutes);
                DaggerfallScheduleDecision d11 = DaggerfallNpcSchedulePolicy.Evaluate(
                    7UL, DaggerfallTown, slot, underworld, 23 * 60 + 56, severeKind,
                    weatherElapsedMinutes: severeSettled.PrecipElapsedMinutes,
                    severeWeatherElapsedMinutes: severeSettled.SevereElapsedMinutes);

                if (d0.Placement == DaggerfallSchedulePlacement.ExteriorStreet) outAtStart++;
                if (d8.Placement == DaggerfallSchedulePlacement.ExteriorStreet) outAtMid++;
                if (d11.Placement == DaggerfallSchedulePlacement.ExteriorStreet) outAtSettled++;
            }

            Assert.Equal(48, outAtStart);
            Assert.InRange(outAtMid, 1, 47);
            Assert.Equal(0, outAtSettled);
        }
    }

    [Fact]
    public void Session_sustained_rain_to_thunder_staggers_underworld_while_preserving_sheltered_walkers()
    {
        List<string> releases = [];
        DaggerfallSiteProfile profile = ReadInputs(TestData.RepositoryRoot);
        ContentFake content = new(releases);
        PopulateContent(content, profile);
        SpatialFake spatial = SpatialFake.Create(profile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(
            content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service, random: RandomMinimum.Create());
        using DaggerfallSession session = DaggerfallSession.StartNew(
            engine.Context, new(TestPayload.Definitions, profile, DaggerfallTuning.Defaults));

        DaggerfallSiteId siteId = session.Site.ActiveSite!.Id;
        const int townPop = 24;
        string underworldId = Enumerable.Range(1, 200)
            .Select(i => $"population/session/dynamic/{i:D3}")
            .First(id => DaggerfallNpcSchedulePolicy.ResolveExteriorScheduleId(session.WorldSocialSeed, siteId, id, isGuard: false, townPop)
                == DaggerfallNpcSchedulePolicy.UnderworldNightScheduleId);
        string walkerId = Enumerable.Range(1, 200)
            .Select(i => $"population/session/dynamic/{i:D3}")
            .First(id => DaggerfallNpcSchedulePolicy.ResolveExteriorScheduleId(session.WorldSocialSeed, siteId, id, isGuard: false, townPop)
                == DaggerfallNpcSchedulePolicy.StreetWalkerScheduleId);

        DaggerfallPopulationPlacement underworldPlacement = new(underworldId, new WorldPoint(2f, 0f, 2f), 210, 4, 0, 0, 1001);
        DaggerfallPopulationPlacement walkerPlacement = new(walkerId, new WorldPoint(3f, 0f, 2f), 210, 4, 0, 0, 1002);

        // 1. Midday (12:00): Sunny -> Rain -> 15 min sustained Rain -> Thunder.
        //    Rain-sensitive walker shelters during sustained Rain and stays sheltered immediately on Rain -> Thunder.
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        session.AdvanceElapsedTime(12 * 60 * 60); // 12:00 (resets weather baseline)
        Assert.Equal(DaggerfallSchedulePlacement.ExteriorStreet,
            session.EvaluateExteriorPlacementSchedule(siteId, walkerPlacement, 9001L, townPop).Placement);

        session.LocalWeather.State.Force(DaggerfallWeatherKind.Rain);
        Assert.Equal(0, session.ObserveScheduleWeatherElapsed().PrecipElapsedMinutes);
        // Step ordinary play forward 15 minutes (in 5-minute increments so no >=10m jump reset triggers).
        for (ulong i = 0; i < 3; i++)
            session.Update(new ProductUpdate(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, i + 1, i + 1, 60, 1, 0, 25d), [])); // 25s real * 12x = 5 game min
        Assert.True(session.ObserveScheduleWeatherElapsed().PrecipElapsedMinutes >= 10);
        Assert.NotEqual(DaggerfallSchedulePlacement.ExteriorStreet,
            session.EvaluateExteriorPlacementSchedule(siteId, walkerPlacement, 9001L, townPop).Placement);

        // Escalate Rain -> Thunder: already-sheltered street-walker MUST remain sheltered immediately!
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Thunder);
        Assert.NotEqual(DaggerfallSchedulePlacement.ExteriorStreet,
            session.EvaluateExteriorPlacementSchedule(siteId, walkerPlacement, 9001L, townPop).Placement);

        // 2. Late night (23:30): settled in Rain, then escalate Rain -> Thunder during ordinary play.
        //    Underworld-night stays outdoors in Rain, lingers at Thunder +0m (SevereElapsed == 0 < 6..10m),
        //    then shelters indoors once 11+ in-game minutes of Thunder elapse.
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Rain);
        session.AdvanceElapsedTime(11 * 60 * 60 + 15 * 60); // 23:30 (settled in Rain)
        DaggerfallScheduleDecision uwInRain = session.EvaluateExteriorPlacementSchedule(siteId, underworldPlacement, 9002L, townPop);
        Assert.Equal(DaggerfallSchedulePlacement.ExteriorStreet, uwInRain.Placement);

        session.LocalWeather.State.Force(DaggerfallWeatherKind.Thunder);
        DaggerfallScheduleDecision uwAtThunderStart = session.EvaluateExteriorPlacementSchedule(siteId, underworldPlacement, 9002L, townPop);
        Assert.Equal(DaggerfallSchedulePlacement.ExteriorStreet, uwAtThunderStart.Placement);
        Assert.Equal(0, session.ObserveScheduleWeatherElapsed().SevereElapsedMinutes);

        // Advance ordinary play 11+ in-game minutes (3 steps of 20s real = 3 * 4 game min = 12 game min).
        for (ulong i = 0; i < 3; i++)
            session.Update(new ProductUpdate(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 10 + i, 10 + i, 60, 1, 0, 20d), []));
        DaggerfallScheduleDecision uwAfterThunderDelay = session.EvaluateExteriorPlacementSchedule(siteId, underworldPlacement, 9002L, townPop);
        Assert.NotEqual(DaggerfallSchedulePlacement.ExteriorStreet, uwAfterThunderDelay.Placement);
    }
}
