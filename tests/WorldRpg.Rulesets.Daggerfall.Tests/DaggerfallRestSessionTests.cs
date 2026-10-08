using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRestSessionTests
{
    [Fact]
    public void Exterior_rest_selects_location_and_wilderness_night_encounters_from_the_live_cell()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSkyMedia sky = DaggerfallSkyMedia.Read(DaggerfallSkyMediaTests.Fixture().Content);
        foreach (bool wilderness in new[] { false, true })
        {
            List<string> releases = [];
            DaggerfallSiteProfile exterior = ExteriorContentAt(source, source.ProfileKey.Site,
                wilderness ? "rest-wilderness" : "rest-location");
            ContentFake content = new(releases);
            PopulateContent(content, exterior);
            PopulateTerrainContent(content, exterior);
            SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
            EngineContextFake engine = EngineContextFake.Create(content, spatial.Service,
                new AppearanceFake(releases), random: RandomMaximum.Create());
            using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, DaggerfallTuning.Defaults) {Sky = sky});
            if (wilderness)
                session.State.PlayerControl.MoveTo(new WorldPoint(DaggerfallExteriorCellResidency.CellSize + 1f, 1f, 1f).ToVector());

            DaggerfallExteriorCellId cell = session.Sites.CurrentExteriorCell();
            int climate = definitions.Grids.ClimateAtWorldPixel(cell.X, cell.Y).Value;
            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));

            Assert.Equal(3600, session.RestView.ElapsedSeconds);
            DaggerfallEncounterResolution[] selected = DaggerfallSavePayload.Read(session.CaptureSave()).Encounters.Resolved;
            Assert.NotEmpty(selected);
            Assert.All(selected, value =>
            {
                Assert.Equal(wilderness ? DaggerfallEncounterContext.WildernessNight : DaggerfallEncounterContext.LocationNight,
                    value.Request.Context);
                Assert.Equal(climate, value.Request.Climate);
            });
        }
    }

    [Fact]
    public void Selected_rest_encounter_interrupts_at_its_minute_and_restores_the_queued_choice()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile inputs = ExteriorContentAt(source, source.ProfileKey.Site, "rest-selected-encounter");
        DaggerfallSkyMedia sky = DaggerfallSkyMedia.Read(DaggerfallSkyMediaTests.Fixture().Content);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        PopulateTerrainContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: RandomMinimum.Create());
        DaggerfallSavePayload saved;
        long elapsed;
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults) {Sky = sky}))
        {
            DaggerfallCalendarSave before = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));
            elapsed = session.RestView.ElapsedSeconds;
            Assert.InRange(elapsed, 1, 3599);
            Assert.Equal(DaggerfallRestInterruption.Encounter, session.RestView.Interruption);
            saved = DaggerfallSavePayload.Read(session.CaptureSave());
            Assert.Equal(CalendarSeconds(before) + elapsed, CalendarSeconds(saved.Calendar));
            DaggerfallEncounterResolution selected = Assert.Single(saved.Encounters.Resolved);
            Assert.NotNull(selected.Choice.MobileId);
            Assert.Null(selected.SpawnedActorId);
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        PopulateTerrainContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service,
            new AppearanceFake(releases), random: RandomMaximum.Create());
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root),
            new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context,
            new(definitions, inputs, DaggerfallTuning.Defaults, identity) {Sky = sky}, DaggerfallSavePayload.Encode(saved));
        DaggerfallSavePayload after = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Equal(saved.Calendar, after.Calendar);
        Assert.Equal(JsonSerializer.Serialize(saved.Encounters), JsonSerializer.Serialize(after.Encounters));
    }

    [Fact]
    public void A_quest_prompt_raised_during_rest_stops_the_rest_before_further_time_passes()
    {
        JsonObject root = TestPayload.Sections("questSources");
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"rest-prompt","displayName":"Rest prompt","sourceFile":"rest-prompt.txt","disposition":"compiled",
            "messages":[{"id":1010,"firstLine":1,"lines":["Will you help?"]}],
            "blocks":[{"kind":"clock","firstLine":2,"lines":["clock _ask_ 30"],"global":null},
            {"kind":"variable","firstLine":3,"lines":["variable _yes_"],"global":null},
            {"kind":"variable","firstLine":4,"lines":["variable _no_"],"global":null},
            {"kind":"task","firstLine":5,"lines":["_ask_ task:","prompt 1010 yes _yes_ no _no_"],"global":null},
            {"kind":"headless","firstLine":7,"lines":["start timer _ask_"],"global":null}],"diagnostics":[]}
            """));
        DaggerfallDefinitions definitions = TestPayload.WithQuestSections(root.AsObject());
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallSkyMedia sky = DaggerfallSkyMedia.Read(DaggerfallSkyMediaTests.Fixture().Content);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        PopulateTerrainContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults) { Sky = sky });
        session.State.PlayerControl.MoveTo(new WorldPoint(1000f, 1f, 1000f).ToVector());
        session.State.Quests.Start(new("rest-prompt", "rest-prompt.txt", "rest-prompt", DaggerfallQuestLifecycle.Active, null, [], []));
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Null(session.State.Quests.Messages.Pending);
        long before = CalendarSeconds(DaggerfallSavePayload.Read(session.CaptureSave()).Calendar);

        session.Update(new ProductUpdate(OuterUpdate(2), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":2}")]));

        Assert.NotNull(session.State.Quests.Messages.Pending);
        Assert.Equal(DaggerfallRestInterruption.Stopped, session.RestView.Interruption);
        long elapsed = CalendarSeconds(DaggerfallSavePayload.Read(session.CaptureSave()).Calendar) - before;
        Assert.InRange(elapsed, 29 * DaggerfallCalendar.SecondsPerMinute, 31 * DaggerfallCalendar.SecondsPerMinute);
    }

    [Fact]
    public void Rest_ui_action_advances_once_recovers_once_round_trips_and_refuses_town_camping()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallSkyMedia sky = DaggerfallSkyMedia.Read(DaggerfallSkyMediaTests.Fixture().Content);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        PopulateTerrainContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());

        DaggerfallSavePayload restedSave;
        int medicalBefore;
        double healthBefore;
        double staminaBefore;
        double magickaBefore;
        DaggerfallCalendarSave calendarBefore;
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults) {Sky = sky}))
        {
            DaggerfallCalendarSave initialCalendar = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));
            Assert.Equal(0, session.RestView.ElapsedSeconds);
            Assert.Contains("Enemies are too close", session.RestView.Message, StringComparison.Ordinal);
            Assert.Equal(initialCalendar, DaggerfallSavePayload.Read(session.CaptureSave()).Calendar);

            // Exercise an eligible camp away from the dungeon's live enemy group.
            session.State.PlayerControl.MoveTo(new WorldPoint(1000f, 1f, 1000f).ToVector());
            Track health = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
            Track stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
            Track magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
            health.SetCurrent(health.MaximumValue - 10d, clamp: true);
            stamina.SetCurrent(stamina.MaximumValue - 100d, clamp: true);
            magicka.SetCurrent(magicka.MaximumValue - 10d, clamp: true);
            healthBefore = health.Current;
            staminaBefore = stamina.Current;
            magickaBefore = magicka.Current;
            medicalBefore = session.State.Progression.SkillUses["medical"];
            calendarBefore = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;

            session.Update(new ProductUpdate(OuterUpdate(2), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));

            DaggerfallRestView view = session.RestView;
            Assert.True(view.HasResult);
            Assert.Equal("Timed", view.Mode);
            Assert.Equal(DaggerfallCalendar.SecondsPerMinute * DaggerfallCalendar.MinutesPerHour, view.ElapsedSeconds);
            Assert.Equal(1, view.RecoveryHours);
            Assert.Equal(DaggerfallRestInterruption.None, view.Interruption);
            Assert.True(view.HealthRecovered > 0);
            Assert.True(view.FatigueRecovered > 0);
            Assert.True(view.SpellPointsRecovered > 0);
            Assert.Equal(healthBefore + view.HealthRecovered, health.Current);
            Assert.Equal(staminaBefore + view.FatigueRecovered, stamina.Current);
            Assert.Equal(magickaBefore + view.SpellPointsRecovered, magicka.Current);
            Assert.Equal(medicalBefore + 1, session.State.Progression.SkillUses["medical"]);

            restedSave = DaggerfallSavePayload.Read(session.CaptureSave());
            Assert.Equal(CalendarSeconds(calendarBefore) + DaggerfallCalendar.SecondsPerMinute * DaggerfallCalendar.MinutesPerHour,
                CalendarSeconds(restedSave.Calendar));
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        PopulateTerrainContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using (DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context,
            new(definitions, inputs, DaggerfallTuning.Defaults, identity) {Sky = sky}, DaggerfallSavePayload.Encode(restedSave)))
        {
            DaggerfallSavePayload restoredSave = DaggerfallSavePayload.Read(restored.CaptureSave());
            Assert.Equal(restedSave.Calendar, restoredSave.Calendar);
            Assert.Equal(JsonSerializer.Serialize(restedSave.SkillUses), JsonSerializer.Serialize(restoredSave.SkillUses));
            Assert.Equal(JsonSerializer.Serialize(restedSave.Actors), JsonSerializer.Serialize(restoredSave.Actors));
        }

        DaggerfallSiteRecord town = definitions.Locations.Records.First(record => record.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteProfile townInputs = ExteriorContentAt(inputs, town.Id, "town-exterior");
        ContentFake townContent = new(releases);
        PopulateContent(townContent, townInputs);
        PopulateTerrainContent(townContent, townInputs);
        SpatialFake townSpatial = SpatialFake.Create(townInputs.SpatialArtifact.Sha256, releases);
        EngineContextFake townEngine = EngineContextFake.Create(townContent, townSpatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        using DaggerfallSession townSession = DaggerfallSession.StartNew(townEngine.Context, new(definitions, townInputs, DaggerfallTuning.Defaults) {Sky = sky});
        Track townHealth = townSession.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        townHealth.SetCurrent(townHealth.MaximumValue - 10d, clamp: true);
        DaggerfallSavePayload townBefore = DaggerfallSavePayload.Read(townSession.CaptureSave());
        townSession.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));
        DaggerfallSavePayload townAfter = DaggerfallSavePayload.Read(townSession.CaptureSave());

        Assert.Equal(0, townSession.RestView.ElapsedSeconds);
        Assert.Contains("Camping in a town", townSession.RestView.Message, StringComparison.Ordinal);
        Assert.Equal(townBefore.Calendar, townAfter.Calendar);
        Assert.Equal(JsonSerializer.Serialize(townBefore.SkillUses), JsonSerializer.Serialize(townAfter.SkillUses));
        Assert.Equal(JsonSerializer.Serialize(townBefore.Actors), JsonSerializer.Serialize(townAfter.Actors));
    }

    private static DaggerfallSiteProfile ExteriorContentAt(DaggerfallSiteProfile source, DaggerfallSiteId site,
        string logicalId) => new(
        new ProjectFacts(new WorldPoint(1f, 1f, 1f), source.Project.Actors),
        source.SpatialArtifact,
        source.StaticMesh,
        source.WorldAppearance,
        source.InitialLook,
        source.Materials,
        source.ActorSprites,
        source.MobileSprites,
        source.Audio,
        source.ClassicPresentation,
        site,
        [],
        DaggerfallWorldProfileKind.Exterior,
        logicalId,
        source.Portals,
        source.Anchors.Values.ToArray(),
        source.Lights,
        source.GroundContainerSprite,
        null,
        [],
        [],
        null,
        source.Music,
        source.AudioBundle,
        source.QuestMarkers,
        source.BillboardSprites,
        [],
        source.WaterVolumes,
        source.TerrainTextures,
        []);

    private static void PopulateTerrainContent(ContentFake content, DaggerfallSiteProfile profile)
    {
        foreach (NormalizedTerrainTexture texture in profile.TerrainTextures.Values)
            content.Add(texture.TexturePath, texture.TextureSha256);
    }

    private static long CalendarSeconds(DaggerfallCalendarSave save) =>
        new DaggerfallCalendar(save.Year, save.Month, save.Day, save.Hour, save.Minute, save.Second).ToAbsoluteSeconds();
}
