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

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed partial class NormalizedRuntimeSeamTests
{
    [Fact]
    public void Exterior_rest_selects_location_and_wilderness_night_encounters_from_the_live_cell()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs source = ReadInputs(root);
        foreach (bool wilderness in new[] { false, true })
        {
            List<string> releases = [];
            PrivateersHoldInputs exterior = SameContentAt(source, source.ProfileKey.Site,
                DaggerfallWorldProfileKind.Exterior, wilderness ? "rest-wilderness" : "rest-location");
            ContentFake content = new(releases);
            PopulateContent(content, exterior);
            SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
            EngineContextFake engine = EngineContextFake.Create(content, spatial.Service,
                new AppearanceFake(releases), random: RandomMaximum.Create());
            using DaggerfallSession session = new(engine.Context, definitions, exterior, DaggerfallTuning.Defaults);
            if (wilderness)
                session.State.PlayerControl.MoveTo(new WorldPoint(DaggerfallExteriorCellResidency.CellSize + 1f, 1f, 1f).ToVector());

            DaggerfallExteriorCellId cell = session.CurrentExteriorCell();
            int climate = definitions.Grids.Climate.GetCell(cell.X, cell.Y).Value;
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
    public void Rest_ui_action_advances_once_recovers_once_round_trips_and_refuses_town_camping()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());

        DaggerfallSavePayload restedSave;
        int medicalBefore;
        double healthBefore;
        double staminaBefore;
        double magickaBefore;
        DaggerfallCalendarSave calendarBefore;
        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
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
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using (DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs,
            DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(restedSave), RandomMaximum.Create()))
        {
            DaggerfallSavePayload restoredSave = DaggerfallSavePayload.Read(restored.CaptureSave());
            Assert.Equal(restedSave.Calendar, restoredSave.Calendar);
            Assert.Equal(JsonSerializer.Serialize(restedSave.SkillUses), JsonSerializer.Serialize(restoredSave.SkillUses));
            Assert.Equal(JsonSerializer.Serialize(restedSave.Actors), JsonSerializer.Serialize(restoredSave.Actors));
        }

        DaggerfallSiteRecord town = definitions.Locations.Records.First(record => record.Kind == DaggerfallSiteKind.TownCity);
        PrivateersHoldInputs townInputs = SameContentAt(inputs, town.Id, DaggerfallWorldProfileKind.Exterior, "town-exterior");
        ContentFake townContent = new(releases);
        PopulateContent(townContent, townInputs);
        SpatialFake townSpatial = SpatialFake.Create(townInputs.SpatialArtifact.Sha256, releases);
        EngineContextFake townEngine = EngineContextFake.Create(townContent, townSpatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        using DaggerfallSession townSession = new(townEngine.Context, definitions, townInputs, DaggerfallTuning.Defaults);
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

    private static PrivateersHoldInputs SameContentAt(PrivateersHoldInputs source, DaggerfallSiteId site, DaggerfallWorldProfileKind kind, string logicalId) => new(
        kind == DaggerfallWorldProfileKind.Exterior
            ? new ProjectFacts(new WorldPoint(1f, 1f, 1f), source.Project.Actors)
            : source.Project,
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
        kind == DaggerfallWorldProfileKind.Dungeon ? source.Doors : [],
        kind,
        logicalId,
        source.Portals,
        source.Anchors.Values.ToArray(),
        source.Lights,
        source.GroundContainerSprite,
        kind == DaggerfallWorldProfileKind.Dungeon ? source.DungeonMap : null,
        kind == DaggerfallWorldProfileKind.Dungeon ? source.DungeonActions : [],
        kind == DaggerfallWorldProfileKind.Dungeon ? source.DungeonActionModels : []);

    private static long CalendarSeconds(DaggerfallCalendarSave save) =>
        new DaggerfallCalendar(save.Year, save.Month, save.Day, save.Hour, save.Minute, save.Second).ToAbsoluteSeconds();
}
